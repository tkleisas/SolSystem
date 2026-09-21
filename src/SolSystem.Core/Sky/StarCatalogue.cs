using System.Buffers.Binary;
using SolSystem.Core.Numerics;

namespace SolSystem.Core.Sky;

/// <summary>
/// The stars, loaded from the packed catalogue.
/// </summary>
/// <remarks>
/// <para>
/// A flat array of fixed-size records, read once and never sorted again. 11 558 stars at sixteen
/// bytes each is 185 kB, which loads in a millisecond and carries the brightest thing in the sky
/// down to the faintest a person can see, plus everything within 25 parsecs whatever its magnitude.
/// </para>
/// <para>
/// Directions are converted from equatorial to ecliptic at load, so a renderer iterating the array
/// gets vectors in the same frame the planets are in and never has to think about the obliquity of
/// the ecliptic at all. That conversion is the one place a sign error would be catastrophic and
/// invisible, so it is done once, in one method, with a test on it.
/// </para>
/// <para>
/// The format is written by <c>tools/pack_stars.py</c> and documented there. It is deliberately
/// little-endian and fixed-width rather than a text format: this is the hottest read in the sky and
/// the coldest data in the project, and parsing 11 558 lines of CSV at startup would be neither.
/// </para>
/// </remarks>
internal sealed class StarCatalogue
{
    /// <summary>The eight bytes every packed catalogue begins with.</summary>
    private static readonly byte[] Magic = "SOLSTARS"u8.ToArray();

    private const int SupportedVersion = 1;
    private const int RecordBytes = 16;

    /// <summary>
    /// The divisor that turns a stored 2⁻³²-of-a-turn angle into the fixed-point turn the
    /// trigonometry uses.
    /// </summary>
    private const double TurnScale = 4294967296.0;

    private readonly Star[] _stars;

    private StarCatalogue(Star[] stars) => _stars = stars;

    /// <summary>Every star, brightest first.</summary>
    internal ReadOnlySpan<Star> Stars => _stars;

    internal int Count => _stars.Length;

    /// <summary>Loads a catalogue from a file.</summary>
    internal static StarCatalogue Load(string path) =>
        FromBytes(File.ReadAllBytes(path));

    /// <summary>
    /// Loads a catalogue from the packed bytes.
    /// </summary>
    /// <remarks>
    /// Every failure here is a <see cref="FormatException"/> with the byte offset in it, because the
    /// alternative is an exception from deep inside the bit reader that says nothing about which
    /// file was wrong or why.
    /// </remarks>
    internal static StarCatalogue FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Magic.Length + 8)
        {
            throw new FormatException(
                $"a star catalogue is at least {Magic.Length + 8} bytes; got {bytes.Length}");
        }

        if (!bytes[..Magic.Length].SequenceEqual(Magic))
        {
            throw new FormatException(
                "not a star catalogue: the first eight bytes should spell SOLSTARS");
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(bytes[Magic.Length..]);
        if (version != SupportedVersion)
        {
            throw new FormatException(
                $"star catalogue version {version}, and this build reads {SupportedVersion}");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[(Magic.Length + 4)..]);
        if (count < 0)
        {
            throw new FormatException($"star catalogue claims {count} stars");
        }

        int bodyAt = Magic.Length + 8;
        long bodyEnd = (long)bodyAt + ((long)count * RecordBytes);
        if (bytes.Length < bodyEnd + 2)
        {
            throw new FormatException(
                $"star catalogue says {count} stars, which needs {bodyEnd + 2} bytes; got {bytes.Length}");
        }

        int nameCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(int)bodyEnd..]);
        string[] names = ReadNames(bytes[(int)(bodyEnd + 2)..], nameCount);

        var stars = new Star[count];
        double obliquity = Frames.ObliquityJ2000Degrees;

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> record = bytes.Slice(bodyAt + (i * RecordBytes), RecordBytes);

            uint ra = BinaryPrimitives.ReadUInt32LittleEndian(record);
            int dec = BinaryPrimitives.ReadInt32LittleEndian(record[4..]);
            short magnitude = BinaryPrimitives.ReadInt16LittleEndian(record[8..]);
            short colour = BinaryPrimitives.ReadInt16LittleEndian(record[10..]);
            ushort distance = BinaryPrimitives.ReadUInt16LittleEndian(record[12..]);
            ushort nameIndex = BinaryPrimitives.ReadUInt16LittleEndian(record[14..]);

            // Declination is an angle, so a quarter turn either way; right ascension is a full
            // turn. Both were written as fractions of a turn and both are read the same way.
            double raDegrees = (ra / TurnScale) * 360.0;
            double decDegrees = (dec / TurnScale) * 360.0;

            Fix128Vec equatorial = Frames.FromEquatorialDegrees(raDegrees, decDegrees);
            Fix128Vec ecliptic = Frames.EquatorialToEcliptic(equatorial, obliquity);

            stars[i] = new Star(
                ecliptic.Normalized(),
                magnitude / 1000.0,
                colour == 0 ? double.NaN : colour / 1000.0,
                distance / 100.0,
                nameIndex > 0 && nameIndex <= names.Length ? names[nameIndex - 1] : string.Empty);
        }

        return new StarCatalogue(stars);
    }

    private static string[] ReadNames(ReadOnlySpan<byte> bytes, int count)
    {
        var names = new string[count];
        int at = 0;

        for (int i = 0; i < count; i++)
        {
            if (at >= bytes.Length)
            {
                throw new FormatException($"star catalogue ran out of bytes reading name {i + 1} of {count}");
            }

            int length = bytes[at++];
            if (at + length > bytes.Length)
            {
                throw new FormatException($"star name {i + 1} claims {length} bytes and has fewer");
            }

            names[i] = System.Text.Encoding.UTF8.GetString(bytes.Slice(at, length));
            at += length;
        }

        return names;
    }

    /// <summary>The first star with this name, or null. Exact match, case-sensitive.</summary>
    internal Star? Find(string name)
    {
        foreach (Star star in _stars)
        {
            if (star.Name == name)
            {
                return star;
            }
        }

        return null;
    }
}
