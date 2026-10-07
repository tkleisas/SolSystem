using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SolSystem.Speech;

/// <summary>
/// The voice cache: the mapping from a spoken line to its rendered WAV, and the WAVs.
/// </summary>
/// <remarks>
/// <para>
/// The database is the index and the files are the audio. A row holds the line — as given
/// and as normalised — the voice identity and the seed that produced it, and the path of a
/// WAV file in a subdirectory of its own, plus that file's SHA-256. A request first looks
/// up the line: a row whose file exists <em>and</em> hashes to what the row recorded is
/// played as-is, and no synthesis runs at all. Anything else — no row, a row whose file is
/// gone, or a file that no longer matches its recorded hash — is re-rendered, written to
/// disk, and the row written or updated to match.
/// </para>
/// <para>
/// The table is <c>cache_voice</c>, under the <c>cache_</c> prefix that marks what a table
/// is <em>for</em> rather than where a file lives — because the database file is the
/// project's general store and will carry tables of other kinds; only this task's audio
/// files live in the subdirectory, <c>artifacts/voice/</c>, beside the database at
/// <c>artifacts/solsystem.db</c>. Both are regenerable by design: gitignored, and deleting
/// them costs one synthesis per line ever spoken.
/// </para>
/// <para>
/// The cache is <b>validated, not trusted</b>, per this repository's rule for anything
/// loaded from storage: the hash in the row is checked on every load, so a truncated or
/// hand-edited file is refused and re-rendered rather than played.
/// </para>
/// </remarks>
internal sealed class SpeechCache : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _audioDirectory;
    private readonly object _gate = new();

    private SpeechCache(SqliteConnection connection, string audioDirectory)
    {
        _connection = connection;
        _audioDirectory = audioDirectory;
    }

    /// <summary>
    /// Opens the voice cache over the project's general database: the database at
    /// <paramref name="dbPath"/>, this task's WAVs beside it in a <c>voice/</c>
    /// subdirectory. Schema created on the spot.
    /// </summary>
    internal static SpeechCache? Open(string? dbPath)
    {
        if (dbPath is null)
        {
            return null;
        }

        string? directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string audioDirectory = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "voice");
        Directory.CreateDirectory(audioDirectory);

        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var schema = connection.CreateCommand();
        schema.CommandText = """
            CREATE TABLE IF NOT EXISTS cache_voice (
                key          TEXT PRIMARY KEY,
                text_raw     TEXT NOT NULL,
                text_norm    TEXT NOT NULL,
                voice        TEXT NOT NULL,
                voice_source TEXT NOT NULL,
                seed         INTEGER NOT NULL,
                greedy       INTEGER NOT NULL,
                sample_rate  INTEGER NOT NULL,
                channels     INTEGER NOT NULL,
                frames       INTEGER NOT NULL,
                file         TEXT NOT NULL,
                sha256       TEXT NOT NULL,
                created_utc  TEXT NOT NULL,
                updated_utc  TEXT NOT NULL
            );
            """;
        schema.ExecuteNonQuery();

        return new SpeechCache(connection, audioDirectory);
    }

    /// <summary>
    /// The database file's default place, found by walking up for the model deployment:
    /// <c>artifacts/solsystem.db</c>, with the voice files in <c>artifacts/voice/</c>.
    /// Null when the deployment is absent — the synthesizer then runs uncached.
    /// </summary>
    internal static string? DefaultDatabasePath()
    {
        if (MossModel.FindDeployment() is not string deployment)
        {
            return null;
        }

        string? artifacts = Path.GetDirectoryName(Path.GetFullPath(deployment));
        return artifacts is null ? null : Path.Combine(artifacts, "solsystem.db");
    }

    /// <summary>
    /// The cache key: everything that decides what the ear hears, hashed together. The
    /// voice is the user-selectable one and enters through its identity — a preset's name,
    /// or the hash of a clone's codes, so a re-cut prompt becomes a new key and never
    /// replays the old voice.
    /// </summary>
    internal static string Key(int sampleRate, int channels, string voiceSource, int seed, bool greedy, string normalisedText)
    {
        var canonical = new StringBuilder()
            .Append("v1|")
            .Append(sampleRate).Append('|').Append(channels)
            .Append('|').Append(voiceSource)
            .Append('|').Append(greedy ? "greedy" : "seed=").Append(seed)
            .Append('|').Append(normalisedText);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    /// <summary>
    /// Looks a line up. True only when the row exists, the file exists, and the file's
    /// bytes hash to what the row recorded; the audio comes back as 16-bit interleaved PCM.
    /// </summary>
    internal bool TryGet(
        string key, int sampleRate, int channels, out byte[] pcm, out int frames)
    {
        pcm = [];
        frames = 0;

        lock (_gate)
        {
            using var query = _connection.CreateCommand();
            query.CommandText = """
                SELECT file, sha256, frames FROM cache_voice
                WHERE key = $key AND sample_rate = $rate AND channels = $channels
                """;
            query.Parameters.AddWithValue("$key", key);
            query.Parameters.AddWithValue("$rate", sampleRate);
            query.Parameters.AddWithValue("$channels", channels);

            string file = "";
            string recordedHash = "";
            using (SqliteDataReader reader = query.ExecuteReader())
            {
                if (!reader.Read())
                {
                    return false;
                }

                file = (string)reader["file"];
                recordedHash = (string)reader["sha256"];
                frames = (int)(long)reader["frames"];
            }

            string fullPath = Path.Combine(_audioDirectory, file);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != recordedHash)
            {
                // The row names a file that is not what it recorded: re-render.
                return false;
            }

            if (!WavHeaderMatches(bytes, sampleRate, channels))
            {
                return false;
            }

            pcm = bytes[44..];
            return true;
        }
    }    /// <summary>
    /// Records a rendered line: the WAV written under its key's name, the row written or
    /// updated to name it. The WAV is IEEE float32 — the codec's native output — so the
    /// round-trip is byte-exact and a cached play is indistinguishable from the live one.
    /// Writing is atomic; the row's timestamps say when it was first made and last
    /// re-rendered.
    /// </summary>
    internal void Put(
        string key,
        string textRaw,
        string textNorm,
        string voice,
        string voiceSource,
        int seed,
        bool greedy,
        int sampleRate,
        int channels,
        int frames,
        ReadOnlySpan<float> interleaved)
    {
        lock (_gate)
        {
            string file = key + ".wav";
            string fullPath = Path.Combine(_audioDirectory, file);
            Wav.Write(fullPath + ".part", interleaved, interleaved.Length / channels, sampleRate, channels,
                WavEncoding.Float32);
            File.Move(fullPath + ".part", fullPath, overwrite: true);

            byte[] bytes = File.ReadAllBytes(fullPath);
            string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            string now = DateTime.UtcNow.ToString("o");

            using var upsert = _connection.CreateCommand();
            upsert.CommandText = """
                INSERT INTO cache_voice
                    (key, text_raw, text_norm, voice, voice_source, seed, greedy,
                     sample_rate, channels, frames, file, sha256, created_utc, updated_utc)
                VALUES
                    ($key, $textRaw, $textNorm, $voice, $voiceSource, $seed, $greedy,
                     $rate, $channels, $frames, $file, $sha256, $now, $now)
                ON CONFLICT(key) DO UPDATE SET
                    text_raw = $textRaw,
                    text_norm = $textNorm,
                    voice = $voice,
                    voice_source = $voiceSource,
                    seed = $seed,
                    greedy = $greedy,
                    sample_rate = $rate,
                    channels = $channels,
                    frames = $frames,
                    file = $file,
                    sha256 = $sha256,
                    updated_utc = $now
                """;
            upsert.Parameters.AddWithValue("$key", key);
            upsert.Parameters.AddWithValue("$textRaw", textRaw);
            upsert.Parameters.AddWithValue("$textNorm", textNorm);
            upsert.Parameters.AddWithValue("$voice", voice);
            upsert.Parameters.AddWithValue("$voiceSource", voiceSource);
            upsert.Parameters.AddWithValue("$seed", seed);
            upsert.Parameters.AddWithValue("$greedy", greedy ? 1L : 0L);
            upsert.Parameters.AddWithValue("$rate", sampleRate);
            upsert.Parameters.AddWithValue("$channels", channels);
            upsert.Parameters.AddWithValue("$frames", frames);
            upsert.Parameters.AddWithValue("$file", file);
            upsert.Parameters.AddWithValue("$sha256", hash);
            upsert.Parameters.AddWithValue("$now", now);
            upsert.ExecuteNonQuery();
        }
    }

    /// <summary>The canonical 44-byte WAV header check, before the data is trusted.</summary>
    private static bool WavHeaderMatches(byte[] bytes, int sampleRate, int channels)
    {
        if (bytes.Length < 44)
        {
            return false;
        }

        // The cache's WAVs are IEEE float (format tag 3), 32-bit.
        return BitConverter.ToUInt32(bytes, 0) == 0x46464952                    // RIFF
            && BitConverter.ToUInt32(bytes, 8) == 0x45564157                    // WAVE
            && BitConverter.ToUInt16(bytes, 20) == 3
            && BitConverter.ToUInt16(bytes, 22) == channels
            && BitConverter.ToInt32(bytes, 24) == sampleRate
            && BitConverter.ToUInt16(bytes, 34) == 32;                         // 32-bit
    }

    public void Dispose() => _connection.Dispose();
}
