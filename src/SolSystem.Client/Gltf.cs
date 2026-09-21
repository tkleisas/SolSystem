using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace SolSystem.Client;

/// <summary>
/// A glTF 2.0 reader: container, scene graph, accessors, and PBR materials with their textures.
/// </summary>
/// <remarks>
/// <para>
/// This reads the format rather than the subset one exporter happens to emit today. The hulls are
/// going to gain texture maps, emissive radiators and nozzle glows, multi-material assemblies and
/// named sub-parts that move — and a loader written against the current files would have to be
/// rewritten for each of those in turn. It is cheaper to read the specification once.
/// </para>
/// <para>
/// <b>It is not a complete implementation and says so.</b> Skins and animations are not read, and
/// neither are morph targets, cameras, lights or the <c>KHR_</c> extension families. A hull has no
/// skeleton. If a file uses one of those, the geometry still loads and the animation does not, which
/// is the right way round for a failure to go.
/// </para>
/// <para>
/// The parts that are easy to get wrong, and are handled here on purpose:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Accessor strides.</b> An accessor may live inside a buffer view that interleaves several
/// attributes. Reading it as though the values were packed gives every vertex the wrong numbers, and
/// the symptom is not an error — it is a hull smeared along a diagonal.
/// </description></item>
/// <item><description>
/// <b>Normalised integer attributes.</b> Colours and texture coordinates are often stored as
/// unsigned bytes or shorts, and have to be divided by the type's maximum before use.
/// </description></item>
/// <item><description>
/// <b>Column-major matrices.</b> glTF stores a node matrix column-major and the graphics layer here
/// is row-major with row vectors. Forgetting the transpose turns every part inside out.
/// </description></item>
/// <item><description>
/// <b>The byte order.</b> Little-endian throughout, which the format requires and which every
/// machine this runs on is — but reading a big-endian file would otherwise give plausible garbage
/// rather than an error.
/// </description></item>
/// </list>
/// </remarks>
internal static class Gltf
{
    private const uint GlbMagic = 0x46546C67;
    private const uint ChunkJson = 0x4E4F534A;
    private const uint ChunkBinary = 0x004E4942;

    /// <summary>
    /// How deep the node graph may nest before the walk gives up.
    /// </summary>
    /// <remarks>
    /// glTF is a directed acyclic graph in principle and a tree in every file that ships. A graph
    /// deeper than this is a cycle or a hostile file, and a cycle with no guard is a
    /// <c>StackOverflowException</c>, which .NET cannot catch and therefore cannot turn into a
    /// fallback.
    /// </remarks>
    private const int MaxNodeDepth = 64;

    /// <summary>
    /// Ceiling on one accessor's element count.
    /// </summary>
    /// <remarks>
    /// Without a bound, <c>new float[count * components]</c> is sized by a number the file chooses.
    /// </remarks>
    private const int MaxAccessorElements = 4_000_000;

    // ---------------------------------------------------------------- document

    /// <summary>A parsed glTF file: its JSON, its binary buffers, and its resolved images.</summary>
    internal sealed class Document
    {
        internal required JsonElement Root { get; init; }

        /// <summary>One entry per glTF buffer, already resolved from the file system or a data URI.</summary>
        internal required byte[][] Buffers { get; init; }

        /// <summary>
        /// The decoded images, by glTF texture index, or null where a texture is absent.
        /// </summary>
        /// <remarks>
        /// Held as encoded bytes rather than as a decoded image because decoding needs the graphics
        /// device, and this layer deliberately does not have one: it is the part that can be tested
        /// without a window.
        /// </remarks>
        internal required byte[]?[] Images { get; init; }

        internal JsonElement Get(string name) =>
            Root.TryGetProperty(name, out JsonElement value) ? value : default;
    }

    /// <summary>Opens a .glb or .gltf file and resolves everything it refers to.</summary>
    internal static Document Open(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        byte[]? binary = null;
        JsonElement root;

        if (bytes.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == GlbMagic)
        {
            root = ReadContainer(bytes, path, out binary);
        }
        else
        {
            using JsonDocument parsed = JsonDocument.Parse(bytes);
            root = parsed.RootElement.Clone();
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        byte[][] buffers = ResolveBuffers(root, binary, directory);

        return new Document
        {
            Root = root,
            Buffers = buffers,
            Images = ResolveImages(root, buffers),
        };
    }

    private static JsonElement ReadContainer(byte[] bytes, string path, out byte[]? binary)
    {
        if (bytes.Length < 12)
        {
            throw new InvalidDataException($"{path} is too short to be a binary glTF");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        if (version != 2)
        {
            throw new InvalidDataException($"{path} is glTF version {version}; this reads 2");
        }

        binary = null;
        JsonElement? root = null;
        int at = 12;

        while (at + 8 <= bytes.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
            uint kind = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4));
            at += 8;

            if (length < 0 || at + length > bytes.Length)
            {
                throw new InvalidDataException(
                    $"{path} declares a {length}-byte chunk with {bytes.Length - at} bytes left");
            }

            if (kind == ChunkJson)
            {
                using JsonDocument parsed = JsonDocument.Parse(bytes.AsMemory(at, length));
                root = parsed.RootElement.Clone();
            }
            else if (kind == ChunkBinary)
            {
                binary = bytes[at..(at + length)];
            }

            at += length;
            at = (at + 3) & ~3;
        }

        if (root is null)
        {
            throw new InvalidDataException($"{path} has no JSON chunk");
        }

        if (root.Value.TryGetProperty("buffers", out JsonElement buffers)
            && buffers.GetArrayLength() > 0
            && !buffers[0].TryGetProperty("uri", out _)
            && binary is null)
        {
            throw new InvalidDataException($"{path} refers to its binary chunk but carries none");
        }

        return root.Value;
    }

    private static byte[][] ResolveBuffers(JsonElement root, byte[]? binary, string directory)
    {
        if (!root.TryGetProperty("buffers", out JsonElement buffers))
        {
            return [];
        }

        var resolved = new byte[buffers.GetArrayLength()][];

        for (int i = 0; i < resolved.Length; i++)
        {
            JsonElement buffer = buffers[i];

            if (!buffer.TryGetProperty("uri", out JsonElement uri))
            {
                // No URI means the GLB's binary chunk, and only the first buffer may do that.
                resolved[i] = binary ?? throw new InvalidDataException(
                    $"buffer {i} has no uri and there is no binary chunk");
                continue;
            }

            resolved[i] = ReadUri(uri.GetString() ?? string.Empty, directory, $"buffer {i}");
        }

        return resolved;
    }

    /// <summary>
    /// Decodes the image of every texture, so a caller can hand the bytes to a decoder.
    /// </summary>
    /// <remarks>
    /// Blender embeds images in the binary chunk, which is the case that matters: a hull file with a
    /// texture map on it is one file and not two.
    /// </remarks>
    private static byte[]?[] ResolveImages(JsonElement root, byte[][] buffers)
    {
        if (!root.TryGetProperty("textures", out JsonElement textures))
        {
            return [];
        }

        JsonElement images = root.TryGetProperty("images", out JsonElement im) ? im : default;
        var decoded = new byte[]?[textures.GetArrayLength()];

        for (int i = 0; i < decoded.Length; i++)
        {
            if (!textures[i].TryGetProperty("source", out JsonElement source))
            {
                continue;
            }

            int index = source.GetInt32();
            if (images.ValueKind != JsonValueKind.Array || index >= images.GetArrayLength())
            {
                continue;
            }

            JsonElement image = images[index];

            if (image.TryGetProperty("bufferView", out JsonElement view))
            {
                decoded[i] = ReadBufferViewBytes(root, buffers, view.GetInt32());
            }
            else if (image.TryGetProperty("uri", out JsonElement uri))
            {
                string raw = uri.GetString() ?? string.Empty;
                decoded[i] = raw.StartsWith("data:", StringComparison.Ordinal)
                    ? DecodeDataUri(raw)
                    : null;   // An external image file; the caller resolves it against the model.
            }
        }

        return decoded;
    }

    private static byte[] ReadUri(string uri, string directory, string what)
    {
        if (uri.StartsWith("data:", StringComparison.Ordinal))
        {
            return DecodeDataUri(uri);
        }

        string path = Path.Combine(directory, Uri.UnescapeDataString(uri));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{what} points at {path}, which is not there");
        }

        return File.ReadAllBytes(path);
    }

    /// <summary>Decodes a <c>data:</c> URI's base64 payload.</summary>
    private static byte[] DecodeDataUri(string uri)
    {
        int comma = uri.IndexOf(',');
        if (comma < 0)
        {
            throw new InvalidDataException("a data: uri with no comma in it");
        }

        string header = uri[..comma];
        string payload = uri[(comma + 1)..];

        if (!header.Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
        }

        return Convert.FromBase64String(payload);
    }

    // ---------------------------------------------------------------- accessors

    /// <summary>
    /// Reads one accessor as floats, honouring its stride and its normalisation.
    /// </summary>
    /// <param name="document">The open document.</param>
    /// <param name="accessorIndex">Which accessor.</param>
    /// <param name="components">How many components the caller expects: 2, 3 or 4.</param>
    internal static float[] ReadFloats(Document document, int accessorIndex, int components)
    {
        JsonElement accessor = Accessor(document, accessorIndex);
        int count = ElementCount(accessor);

        (byte[] buffer, int start, int stride, int componentType, bool normalized) =
            Locate(document, accessor, components);

        var values = new float[count * components];

        for (int i = 0; i < count; i++)
        {
            int element = start + (i * stride);
            for (int c = 0; c < components; c++)
            {
                values[(i * components) + c] = ReadComponent(
                    buffer, element + (c * ComponentSize(componentType)), componentType, normalized);
            }
        }

        return values;
    }

    /// <summary>Reads one accessor as unsigned integers, for indices.</summary>
    internal static uint[] ReadIndices(Document document, int accessorIndex)
    {
        JsonElement accessor = Accessor(document, accessorIndex);
        int count = ElementCount(accessor);

        (byte[] buffer, int start, int stride, int componentType, _) =
            Locate(document, accessor, 1);

        // An index accessor is required to be unsigned and must not be normalised; the specification
        // says stride is ignored for indices, and a file that sets one is wrong rather than exotic.
        var indices = new uint[count];

        for (int i = 0; i < count; i++)
        {
            int at = start + (i * stride);
            indices[i] = componentType switch
            {
                5121 => buffer[at],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(at)),
                5125 => BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(at)),
                _ => throw new InvalidDataException(
                    $"index accessor {accessorIndex} has component type {componentType}, and an "
                    + "index must be unsigned byte, short or int"),
            };
        }

        return indices;
    }

    private static JsonElement Accessor(Document document, int index)
    {
        JsonElement accessors = document.Get("accessors");

        if (accessors.ValueKind != JsonValueKind.Array
            || index < 0
            || index >= accessors.GetArrayLength())
        {
            throw new InvalidDataException($"accessor {index} is out of range");
        }

        return accessors[index];
    }

    private static int ElementCount(JsonElement accessor)
    {
        int count = accessor.GetProperty("count").GetInt32();

        if (count < 0 || count > MaxAccessorElements)
        {
            throw new InvalidDataException($"an accessor claims {count} elements");
        }

        return count;
    }

    /// <summary>The buffer, byte offset and stride an accessor's values live at.</summary>
    private static (byte[] Buffer, int Start, int Stride, int ComponentType, bool Normalized) Locate(
        Document document, JsonElement accessor, int components)
    {
        if (!accessor.TryGetProperty("bufferView", out JsonElement viewIndex))
        {
            // A sparse-only accessor has no buffer view and reads as zeroes. Nothing this project
            // exports uses one, and a zeroed vertex is a visible failure rather than a silent one.
            throw new InvalidDataException("an accessor with no bufferView is not supported");
        }

        JsonElement views = document.Get("bufferViews");
        int index = viewIndex.GetInt32();

        if (views.ValueKind != JsonValueKind.Array || index < 0 || index >= views.GetArrayLength())
        {
            throw new InvalidDataException($"bufferView {index} is out of range");
        }

        JsonElement view = views[index];
        int bufferIndex = view.TryGetProperty("buffer", out JsonElement b) ? b.GetInt32() : 0;

        if (bufferIndex < 0 || bufferIndex >= document.Buffers.Length)
        {
            throw new InvalidDataException($"bufferView {index} names buffer {bufferIndex}");
        }

        int componentType = accessor.GetProperty("componentType").GetInt32();
        int size = ComponentSize(componentType);

        int viewOffset = view.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0;
        int accessorOffset = accessor.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0;

        int stride = view.TryGetProperty("byteStride", out JsonElement s) && s.GetInt32() > 0
            ? s.GetInt32()
            : components * size;

        bool normalized = accessor.TryGetProperty("normalized", out JsonElement n) && n.GetBoolean();

        byte[] buffer = document.Buffers[bufferIndex];
        int start = viewOffset + accessorOffset;

        // The last element only has to fit as far as its own components, not a full stride.
        int needed = start + ((ElementCount(accessor) - 1) * stride) + (components * size);
        if (needed > buffer.Length)
        {
            throw new InvalidDataException(
                $"an accessor reads to byte {needed} of a {buffer.Length}-byte buffer");
        }

        return (buffer, start, stride, componentType, normalized);
    }

    private static int ComponentSize(int componentType) => componentType switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5125 or 5126 => 4,
        _ => throw new InvalidDataException($"component type {componentType}"),
    };

    /// <summary>
    /// One component as a float, applying the normalisation the accessor asks for.
    /// </summary>
    /// <remarks>
    /// A normalised unsigned byte is <c>v / 255</c> and a signed one is
    /// <c>max(v / 127, -1)</c> — not <c>v / 128</c>, which is the off-by-one that makes every
    /// normalised normal very slightly too long.
    /// </remarks>
    private static float ReadComponent(byte[] buffer, int at, int componentType, bool normalized)
    {
        ReadOnlySpan<byte> span = buffer.AsSpan(at);

        return componentType switch
        {
            5126 => BinaryPrimitives.ReadSingleLittleEndian(span),
            5125 => BinaryPrimitives.ReadUInt32LittleEndian(span),
            5123 => normalized
                ? BinaryPrimitives.ReadUInt16LittleEndian(span) / 65535f
                : BinaryPrimitives.ReadUInt16LittleEndian(span),
            5121 => normalized ? span[0] / 255f : span[0],
            5122 => normalized
                ? MathF.Max(BinaryPrimitives.ReadInt16LittleEndian(span) / 32767f, -1f)
                : BinaryPrimitives.ReadInt16LittleEndian(span),
            5120 => normalized
                ? MathF.Max((sbyte)span[0] / 127f, -1f)
                : (sbyte)span[0],
            _ => throw new InvalidDataException($"component type {componentType}"),
        };
    }

    /// <summary>The raw bytes of a buffer view, for an embedded image.</summary>
    private static byte[] ReadBufferViewBytes(JsonElement root, byte[][] buffers, int index)
    {
        JsonElement views = root.GetProperty("bufferViews");
        JsonElement view = views[index];

        int bufferIndex = view.TryGetProperty("buffer", out JsonElement b) ? b.GetInt32() : 0;
        int offset = view.TryGetProperty("byteOffset", out JsonElement o) ? o.GetInt32() : 0;
        int length = view.GetProperty("byteLength").GetInt32();

        return buffers[bufferIndex].AsSpan(offset, length).ToArray();
    }

    // ---------------------------------------------------------------- transforms

    /// <summary>
    /// A node's own transform, from whichever of the two forms it uses.
    /// </summary>
    /// <remarks>
    /// glTF matrices are column-major and this graphics layer is row-major with row vectors, so the
    /// literal below is the transpose — which is the whole of the conversion, and forgetting it
    /// turns every part inside out.
    /// </remarks>
    internal static System.Numerics.Matrix4x4 NodeTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out JsonElement matrix))
        {
            float[] m = ReadNumbers(matrix, 16);

            return new System.Numerics.Matrix4x4(
                m[0], m[4], m[8], m[12],
                m[1], m[5], m[9], m[13],
                m[2], m[6], m[10], m[14],
                m[3], m[7], m[11], m[15]);
        }

        var scale = System.Numerics.Vector3.One;
        if (node.TryGetProperty("scale", out JsonElement s))
        {
            float[] v = ReadNumbers(s, 3);
            scale = new System.Numerics.Vector3(v[0], v[1], v[2]);
        }

        var translation = System.Numerics.Vector3.Zero;
        if (node.TryGetProperty("translation", out JsonElement t))
        {
            float[] v = ReadNumbers(t, 3);
            translation = new System.Numerics.Vector3(v[0], v[1], v[2]);
        }

        var rotation = System.Numerics.Quaternion.Identity;
        if (node.TryGetProperty("rotation", out JsonElement r))
        {
            // glTF stores x, y, z, w and the constructor here takes the same order.
            float[] v = ReadNumbers(r, 4);
            rotation = System.Numerics.Quaternion.Normalize(
                new System.Numerics.Quaternion(v[0], v[1], v[2], v[3]));
        }

        return System.Numerics.Matrix4x4.CreateScale(scale)
            * System.Numerics.Matrix4x4.CreateFromQuaternion(rotation)
            * System.Numerics.Matrix4x4.CreateTranslation(translation);
    }

    internal static float[] ReadNumbers(JsonElement array, int expected)
    {
        var values = new float[array.GetArrayLength()];

        int i = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            values[i++] = (float)element.GetDouble();
        }

        if (values.Length < expected)
        {
            throw new InvalidDataException(
                $"expected {expected} numbers and found {values.Length}");
        }

        return values;
    }

    internal static int MaxDepth => MaxNodeDepth;
}
