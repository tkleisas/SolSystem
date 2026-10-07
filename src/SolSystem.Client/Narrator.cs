using Microsoft.Xna.Framework.Audio;
using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using SolSystem.Speech;

namespace SolSystem.Client;

/// <summary>
/// The narrator: the voice that tells the player where they are and what it costs.
/// </summary>
/// <remarks>
/// <para>
/// Synthesis is MOSS-TTS-Nano's ONNX graphs, run in-process — no Python, no service. The
/// model loads on a worker thread because most of a gigabyte of weights takes seconds to
/// seat; until it is ready the queue simply holds the lines. Playback is a
/// <see cref="DynamicSoundEffectInstance"/> fed with the codec's PCM, which decodes faster
/// than real time, so the voice is always ahead of the ear.
/// </para>
/// <para>
/// Nothing here simulates anything. The lines come from <see cref="Narration"/>, which
/// reads the flight and the session and says what a pilot would want said; this class only
/// queues words and plays them. The narrator exists only in an interactive session — a
/// headless shot has no ear to speak to.
/// </para>
/// </remarks>
internal sealed class Narrator : IDisposable
{
    private readonly DynamicSoundEffectInstance _speaker;
    private readonly string _modelRoot;
    private readonly string _voiceName;
    private readonly string? _promptAudioPath;
    private readonly object _gate = new();
    private readonly Queue<string> _pending = new();
    private Thread? _voiceThread;
    private MossModel? _model;
    private MossSynthesizer? _synthesizer;
    private bool _ready;
    private bool _speaking;

    private readonly NarrationState _narration = new();

    private volatile bool _disposed;

    private Narrator(string modelRoot, LaunchOptions options)
    {
        _modelRoot = modelRoot;
        _voiceName = options.NarratorVoice;
        _promptAudioPath = options.NarratorPrompt.Length > 0
            ? options.NarratorPrompt
            : File.Exists(Narration.DefaultPromptPath) ? Narration.DefaultPromptPath : null;

        _speaker = new DynamicSoundEffectInstance(Narration.SampleRate, AudioChannels.Stereo)
        {
            Volume = 0.9f,
        };
        // Not played yet: the first line is buffered first, so the ear hears a whole
        // breath rather than the codec spinning up.
    }

    /// <summary>
    /// Starts the narrator for a session, or null when muted, when the run is a single
    /// instrument shot, or when the weights are simply not present — in which case the game
    /// is silent, not broken.
    /// </summary>
    /// <remarks>
    /// The default voice is the narrator's own recording, cloned from the prompt clip in
    /// <c>art/audio/narration/</c>; <c>--narrator-voice</c> names a built-in preset instead,
    /// and <c>--narrator-prompt</c> points at a different recording. A bounded
    /// <c>--frames</c> run is the loop a player gets, so it gets the voice; only the
    /// one-frame <c>--shot</c> path — the render-bench mode, with no loop behind it — stays
    /// silent.
    /// </remarks>
    internal static Narrator? Start(LaunchOptions options)
    {
        if (options.OneShot || options.Mute)
        {
            return null;
        }

        if (FlightSession.FindModelRoot() is not string modelRoot)
        {
            Console.WriteLine("  note: no MOSS-TTS-Nano weights under artifacts/moss-tts; the game runs silent. "
                + "Fetch them with tools/tts/fetch_models.sh.");
            return null;
        }

        var narrator = new Narrator(modelRoot, options);
        narrator.StartVoice();
        narrator.Say(Narration.Intro);
        return narrator;
    }

    private void StartVoice()
    {
        _voiceThread = new Thread(VoiceLoop)
        {
            Name = "narrator",
            IsBackground = true,
        };
        _voiceThread.Start();
    }

    /// <summary>Queues a line. Words spoken first, queue honoured in order.</summary>
    internal void Say(string line)
    {
        lock (_gate)
        {
            _pending.Enqueue(line);
        }
    }

    /// <summary>
    /// Fired on the narrator's thread when a line begins being spoken — the moment the
    /// terminal starts printing it.
    /// </summary>
    internal event Action<string>? LineSpoken;

    private void VoiceLoop()
    {
        try
        {
            _model = MossModel.Load(_modelRoot, threadCount: 4);
            SentencePieceModel tokenizer = SentencePieceModel.Load(Path.Combine(
                _modelRoot, MossModel.TtsDirectoryName, "tokenizer.model"));

            // The voice cache, over the project's general database: a line rendered once is
            // a file read afterwards. Null when the database's home is absent.
            using SpeechCache? cache = SpeechCache.Open(SpeechCache.DefaultDatabasePath());
            _synthesizer = new MossSynthesizer(_model, tokenizer, cache);
            _ready = true;
            Console.WriteLine("  narrator ready");

            while (!_disposed)
            {
                string? line = TakeNext();
                if (line is null)
                {
                    Thread.Sleep(50);
                    continue;
                }

                LineSpoken?.Invoke(line);
                Speak(line);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"  narrator stopped ({(_disposed ? "disposed" : "live")}): {exception.Message}");
        }
        finally
        {
            _model?.Dispose();
        }
    }

    private string? TakeNext()
    {
        lock (_gate)
        {
            return _pending.Count > 0 ? _pending.Dequeue() : null;
        }
    }

    private void Speak(string line)
    {
        var settings = new SynthesisSettings
        {
            Voice = _voiceName,
            PromptAudioPath = _promptAudioPath,
            Greedy = false,
            Seed = Narration.Seed,
        };

        _synthesizer!.Synthesize(line, settings, (chunk, samplesPerChannel) =>
        {
            Submit(chunk, samplesPerChannel);

            // Two seconds of lead before the speaker starts, and the decode keeps running
            // ahead after it: at this machine's synthesis rate (~1.2x realtime, overlapping
            // the codec on its own thread) a lead that deep covers any line the narrator
            // says without the queue ever catching the ear.
            if (!_speaking && _speaker.PendingBufferCount >= Narration.LeadBuffers)
            {
                _speaking = true;
                _speaker.Play();
            }
        });

        // A short breath between lines, so queued lines do not run together.
        SeatSilence((int)(Narration.SampleRate * 0.35));
    }

    private void Submit(ReadOnlySpan<float> interleaved, int samplesPerChannel)
    {
        if (_disposed || samplesPerChannel == 0)
        {
            return;
        }

        // The DynamicSoundEffectInstance takes 16-bit PCM bytes; the codec emits float32.
        // One pass, no clipping beyond the codec's own range.
        Span<byte> bytes = stackalloc byte[samplesPerChannel * 2 * 2];
        for (int index = 0; index < samplesPerChannel * 2; index++)
        {
            float clipped = Math.Clamp(interleaved[index], -1.0f, 1.0f);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(
                bytes[(index * 2)..], (short)MathF.Round(clipped * 32767.0f));
        }

        _speaker.SubmitBuffer(bytes.ToArray());
    }

    private void SeatSilence(int samples)
    {
        if (_disposed)
        {
            return;
        }

        _speaker.SubmitBuffer(new byte[samples * 2 * 2]);
    }

    /// <summary>
    /// Per-frame narration update: the triggers read the sim, the voice says what is new.
    /// </summary>
    internal void Update(Flight flight, FlightSession session)
    {
        if (!_ready || _disposed)
        {
            return;
        }

        foreach (string? line in Narration.Update(_narration, flight, session))
        {
            if (line is not null)
            {
                Say(line);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Stop first, then wait for the voice thread: a session that exits while the model
        // is still seating used to tear the weights out from under the in-flight ORT init
        // — the ugly bad_alloc / GetElementType native errors were that race, not bugs in
        // the graphs. The load completes, the loop sees the flag and exits, and the model
        // is disposed once, by the thread that owns it.
        _disposed = true;
        _voiceThread?.Join();
        _speaker.Stop();
        _speaker.Dispose();
    }
}
