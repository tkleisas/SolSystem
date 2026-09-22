using System.Numerics;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Matrix = Microsoft.Xna.Framework.Matrix;
using Vector2 = Microsoft.Xna.Framework.Vector2;
using Vector4 = Microsoft.Xna.Framework.Vector4;
using Vector3 = Microsoft.Xna.Framework.Vector3;

namespace SolSystem.Client;

/// <summary>
/// A hull: the drawable form of a glTF model, kept in named parts.
/// </summary>
/// <remarks>
/// <para>
/// Parts are kept separate rather than merged, and the reason is the design rather than the loader.
/// A radiator that has to deploy, a nozzle that has to glow when the engine is lit, a cargo door
/// that has to open — these are nodes with names in the file, and merging them costs the game the
/// ability to move them. The cost of keeping them is one draw call each, on models that have fewer
/// than a dozen.
/// </para>
/// <para>
/// Geometry stays in the model's own space and the node transform is carried alongside. Baking a
/// part's transform into its vertices would move its pivot away from the part, and a part cannot
/// rotate about a pivot it no longer has.
/// </para>
/// </remarks>
internal sealed class Hull : IDisposable
{
    /// <summary>
    /// One drawable part and the material it wears.
    /// </summary>
    internal sealed class Part : IDisposable
    {
        internal required string Name { get; init; }

        internal required VertexBuffer Vertices { get; init; }

        internal required IndexBuffer Indices { get; init; }

        internal required int Triangles { get; init; }

        /// <summary>Where the part sits relative to its parent, from its glTF node.</summary>
        internal required Matrix LocalTransform { get; init; }

        /// <summary>The enclosing part, or -1 for a root part.</summary>
        internal required int ParentIndex { get; init; }

        internal required Material Material { get; init; }

        /// <summary>
        /// Where the part sits relative to the whole hull.
        /// </summary>
        /// <remarks>
        /// Accumulated once at load, because the hull is drawn as a rigid body and walking the
        /// parent chain per frame per part would be work done sixty times a second to produce an
        /// answer that does not change. A part that is *animated* uses
        /// <see cref="LocalTransform"/>.
        /// </remarks>
        internal Matrix WorldTransform { get; set; }

        public void Dispose()
        {
            Vertices.Dispose();
            Indices.Dispose();
            Material.Dispose();
        }
    }

    /// <summary>
    /// A glTF material, as much of one as a hull uses.
    /// </summary>
    /// <remarks>
    /// The names are the specification's, not this project's, so that a value read here can be
    /// checked against the file that produced it without a translation table in the way.
    /// </remarks>
    internal sealed class Material : IDisposable
    {
        internal string Name { get; init; } = string.Empty;

        internal Vector4 BaseColorFactor { get; init; } = Vector4.One;

        internal Texture2D? BaseColorTexture { get; init; }

        internal float Metallic { get; init; } = 1.0f;

        internal float Roughness { get; init; } = 1.0f;

        internal float? MetallicRoughnessTexture { get; init; } = null;

        /// <summary>
        /// The colour a surface emits with no light on it.
        /// </summary>
        /// <remarks>
        /// The one material property this project needs beyond plain shading, because a hull at
        /// three hundred kelvin in the outer system is lit by a Sun thirty times dimmer than the
        /// Earth's, and a radiator and a nozzle are the only things on it that are not black.
        /// </remarks>
        internal Vector3 EmissiveFactor { get; init; } = Vector3.Zero;

        internal Texture2D? EmissiveTexture { get; init; }

        /// <summary>OPAQUE, MASK or BLEND, as written.</summary>
        internal string AlphaMode { get; init; } = "OPAQUE";

        internal float AlphaCutoff { get; init; } = 0.5f;

        /// <summary>When set, back faces are drawn as well as front ones.</summary>
        internal bool DoubleSided { get; init; }

        internal bool Emits => EmissiveFactor.LengthSquared() > 1e-6f;

        public void Dispose()
        {
            BaseColorTexture?.Dispose();
            EmissiveTexture?.Dispose();
        }
    }

    private readonly List<Part> _parts = new();

    internal IReadOnlyList<Part> Parts => _parts;

    /// <summary>
    /// The hull's own dimensions in its model space, in metres.
    /// </summary>
    /// <remarks>
    /// Measured from the geometry rather than declared. Blender exports in metres, so this is a
    /// length and not a scale factor — and a hull whose bounding box disagrees with the design
    /// document should be the thing that is wrong, not the camera.
    /// </remarks>
    internal Vector3 Size { get; private set; }

    internal string Source { get; private set; } = string.Empty;

    /// <summary>Loads a hull and uploads it.</summary>
    internal static Hull Load(GraphicsDevice device, string path)
    {
        Gltf.Document document = Gltf.Open(path);
        var hull = new Hull { Source = path };

        JsonElement root = document.Root;
        JsonElement nodes = document.Get("nodes");

        // Materials are built once per glTF material and shared by every primitive that names it,
        // so a hull with nine material slots does not upload nine copies of the same map.
        var materials = new Material?[
            root.TryGetProperty("materials", out JsonElement m) ? m.GetArrayLength() : 0];

        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = BuildMaterial(device, document, m[i], path);
        }

        int[] roots = SceneRoots(document, nodes);

        var minimum = new Vector3(float.MaxValue);
        var maximum = new Vector3(float.MinValue);

        foreach (int node in roots)
        {
            AppendNode(device, document, materials, node, Matrix.Identity, -1, 0, hull,
                ref minimum, ref maximum);
        }

        if (hull._parts.Count == 0)
        {
            throw new InvalidDataException($"'{path}' contains no renderable triangle geometry");
        }

        hull.Size = maximum - minimum;
        hull.ComputeWorldTransforms();
        return hull;
    }

    /// <summary>The nodes the default scene starts from, or every node if there is no scene.</summary>
    private static int[] SceneRoots(Gltf.Document document, JsonElement nodes)
    {
        JsonElement scenes = document.Get("scenes");

        if (scenes.ValueKind == JsonValueKind.Array && scenes.GetArrayLength() > 0)
        {
            int scene = document.Root.TryGetProperty("scene", out JsonElement s) ? s.GetInt32() : 0;
            scene = Math.Clamp(scene, 0, scenes.GetArrayLength() - 1);

            if (scenes[scene].TryGetProperty("nodes", out JsonElement list))
            {
                return list.EnumerateArray().Select(n => n.GetInt32()).ToArray();
            }
        }

        return Enumerable.Range(0, nodes.ValueKind == JsonValueKind.Array ? nodes.GetArrayLength() : 0)
            .ToArray();
    }

    /// <summary>
    /// Walks the scene graph, emitting one part per triangle primitive.
    /// </summary>
    /// <remarks>
    /// The depth guard is not decoration. A glTF node list is a graph, a file may contain a cycle,
    /// and the failure mode of an unguarded recursive walk on a cycle is a stack overflow — which
    /// .NET cannot catch, so it cannot be reported or recovered from either.
    /// </remarks>
    private static void AppendNode(
        GraphicsDevice device,
        Gltf.Document document,
        Material?[] materials,
        int nodeIndex,
        Matrix parent,
        int parentPart,
        int depth,
        Hull hull,
        ref Vector3 minimum,
        ref Vector3 maximum)
    {
        if (depth > Gltf.MaxDepth)
        {
            throw new InvalidDataException(
                $"the node graph in '{hull.Source}' nests deeper than {Gltf.MaxDepth}; it is a "
                + "cycle or a hostile file");
        }

        JsonElement nodes = document.Get("nodes");
        if (nodes.ValueKind != JsonValueKind.Array
            || nodeIndex < 0
            || nodeIndex >= nodes.GetArrayLength())
        {
            throw new InvalidDataException($"node {nodeIndex} is out of range");
        }

        JsonElement node = nodes[nodeIndex];
        Matrix transform = ToXna(Gltf.NodeTransform(node)) * parent;

        string name = node.TryGetProperty("name", out JsonElement n)
            ? n.GetString() ?? $"node{nodeIndex}"
            : $"node{nodeIndex}";

        int lastPart = parentPart;

        if (node.TryGetProperty("mesh", out JsonElement meshIndex))
        {
            lastPart = AppendMesh(device, document, materials, meshIndex.GetInt32(), name,
                transform, parentPart, hull, ref minimum, ref maximum);
        }

        if (node.TryGetProperty("children", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                AppendNode(device, document, materials, child.GetInt32(), transform, lastPart,
                    depth + 1, hull, ref minimum, ref maximum);
            }
        }
    }

    private static int AppendMesh(
        GraphicsDevice device,
        Gltf.Document document,
        Material?[] materials,
        int meshIndex,
        string nodeName,
        Matrix transform,
        int parentPart,
        Hull hull,
        ref Vector3 minimum,
        ref Vector3 maximum)
    {
        JsonElement meshes = document.Get("meshes");
        JsonElement mesh = meshes[meshIndex];

        if (!mesh.TryGetProperty("primitives", out JsonElement primitives))
        {
            return parentPart;
        }

        int last = parentPart;
        int index = 0;

        foreach (JsonElement primitive in primitives.EnumerateArray())
        {
            // Mode 4 is triangles and is the default. A hull with a strip or a fan in it would be a
            // file this project did not write, and drawing it as triangles would be worse than
            // skipping it.
            int mode = primitive.TryGetProperty("mode", out JsonElement mo) ? mo.GetInt32() : 4;
            if (mode != 4)
            {
                Console.WriteLine($"  note: {nodeName} primitive {index} has mode {mode}; skipped");
                index++;
                continue;
            }

            string partName = index == 0 ? nodeName : $"{nodeName}.{index}";
            (Part part, Vector3 lower, Vector3 upper) = BuildPart(device, document, materials,
                primitive, partName, transform, parentPart);

            hull._parts.Add(part);
            last = hull._parts.Count - 1;

            // All eight corners of the part's own box, transformed. The centre alone would give a
            // hull zero metres long, and a camera framed on that is inside the ship.
            foreach (Vector3 corner in BoxCorners(lower, upper, transform))
            {
                minimum = Vector3.Min(minimum, corner);
                maximum = Vector3.Max(maximum, corner);
            }

            index++;
        }

        return last;
    }

    private static (Part Part, Vector3 Lower, Vector3 Upper) BuildPart(
        GraphicsDevice device,
        Gltf.Document document,
        Material?[] materials,
        JsonElement primitive,
        string name,
        Matrix transform,
        int parentPart)
    {
        JsonElement attributes = primitive.GetProperty("attributes");
        float[] positions = Gltf.ReadFloats(document, attributes.GetProperty("POSITION").GetInt32(), 3);

        int vertexCount = positions.Length / 3;

        float[] normals = attributes.TryGetProperty("NORMAL", out JsonElement normalAccessor)
            ? Gltf.ReadFloats(document, normalAccessor.GetInt32(), 3)
            : new float[0];

        float[] texCoords = attributes.TryGetProperty("TEXCOORD_0", out JsonElement uvAccessor)
            ? Gltf.ReadFloats(document, uvAccessor.GetInt32(), 2)
            : new float[0];

        uint[] indices = primitive.TryGetProperty("indices", out JsonElement indexAccessor)
            ? Gltf.ReadIndices(document, indexAccessor.GetInt32())
            : Enumerable.Range(0, vertexCount).Select(i => (uint)i).ToArray();

        Material material = primitive.TryGetProperty("material", out JsonElement materialIndex)
            && materialIndex.GetInt32() < materials.Length
            && materials[materialIndex.GetInt32()] is Material found
                ? found
                : Default;

        var vertices = new VertexPositionNormalTexture[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            var position = new Vector3(positions[i * 3], positions[(i * 3) + 1], positions[(i * 3) + 2]);

            Vector3 normal;
            if (normals.Length >= (i * 3) + 3)
            {
                normal = new Vector3(normals[i * 3], normals[(i * 3) + 1], normals[(i * 3) + 2]);
            }
            else
            {
                normal = Vector3.Zero;
            }

            // A degenerate normal is a file that did not write one, not a surface with no
            // orientation. Up is arbitrary and visible; a zero normal shades as black.
            normal = normal.LengthSquared() < 1e-8f ? Vector3.Up : Vector3.Normalize(normal);

            var uv = texCoords.Length >= (i * 2) + 2
                ? new Vector2(texCoords[i * 2], texCoords[(i * 2) + 1])
                : Vector2.Zero;

            vertices[i] = new VertexPositionNormalTexture(position, normal, uv);
        }

        var lower = new Vector3(float.MaxValue);
        var upper = new Vector3(float.MinValue);

        foreach (VertexPositionNormalTexture vertex in vertices)
        {
            lower = Vector3.Min(lower, vertex.Position);
            upper = Vector3.Max(upper, vertex.Position);
        }

        var vertexBuffer = new VertexBuffer(device, VertexPositionNormalTexture.VertexDeclaration,
            vertexCount, BufferUsage.WriteOnly);
        vertexBuffer.SetData(vertices);

        (IndexBuffer indexBuffer, int triangles) = Upload(device, indices);

        return (new Part
        {
            Name = name,
            Vertices = vertexBuffer,
            Indices = indexBuffer,
            Triangles = triangles,
            LocalTransform = transform,
            ParentIndex = parentPart,
            Material = material,
            WorldTransform = transform,
        }, lower, upper);
    }

    /// <summary>The eight corners of a box, put through a transform.</summary>
    private static IEnumerable<Vector3> BoxCorners(Vector3 lower, Vector3 upper, Matrix transform)
    {
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? lower.X : upper.X,
                (i & 2) == 0 ? lower.Y : upper.Y,
                (i & 4) == 0 ? lower.Z : upper.Z);

            yield return Vector3.Transform(corner, transform);
        }
    }

    /// <summary>
    /// Uploads an index list, sixteen bit where it fits.
    /// </summary>
    /// <remarks>
    /// Thirty-two bit only past 65 535 vertices, because a narrower index is half the bandwidth and
    /// every hull here is far below the line. Choosing per mesh rather than globally means a detail
    /// part added later cannot silently start drawing triangles across the whole ship.
    /// </remarks>
    private static (IndexBuffer Buffer, int Triangles) Upload(GraphicsDevice device, uint[] indices)
    {
        int count = indices.Length - (indices.Length % 3);
        uint highest = 0;

        for (int i = 0; i < count; i++)
        {
            highest = Math.Max(highest, indices[i]);
        }

        // Sixteen bits is the value range, not the count: a mesh whose highest index is
        // exactly 65 535 does not fit, because the check used to read `>` and index 65 535
        // is itself a valid sixteen-bit value that the narrow cast then turns into -1.
        if (highest >= ushort.MaxValue)
        {
            var wide = new int[count];
            for (int i = 0; i < count; i++)
            {
                wide[i] = (int)indices[i];
            }

            var buffer = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, count,
                BufferUsage.WriteOnly);
            buffer.SetData(wide);
            return (buffer, count / 3);
        }

        var narrow = new short[count];
        for (int i = 0; i < count; i++)
        {
            narrow[i] = (short)indices[i];
        }

        var narrowBuffer = new IndexBuffer(device, IndexElementSize.SixteenBits, count,
            BufferUsage.WriteOnly);
        narrowBuffer.SetData(narrow);
        return (narrowBuffer, count / 3);
    }

    /// <summary>Builds a glTF material, textures and all.</summary>
    private static Material BuildMaterial(
        GraphicsDevice device, Gltf.Document document, JsonElement material, string path)
    {
        string name = material.TryGetProperty("name", out JsonElement n)
            ? n.GetString() ?? string.Empty
            : string.Empty;

        var baseColor = new Vector4(1f);
        float metallic = 1f;
        float roughness = 1f;
        Texture2D? baseColorTexture = null;

        if (material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr))
        {
            if (pbr.TryGetProperty("baseColorFactor", out JsonElement factor))
            {
                float[] v = Gltf.ReadNumbers(factor, 4);
                baseColor = new Vector4(v[0], v[1], v[2], v[3]);
            }

            if (pbr.TryGetProperty("metallicFactor", out JsonElement me))
            {
                metallic = (float)me.GetDouble();
            }

            if (pbr.TryGetProperty("roughnessFactor", out JsonElement ro))
            {
                roughness = (float)ro.GetDouble();
            }

            if (pbr.TryGetProperty("baseColorTexture", out JsonElement bct))
            {
                baseColorTexture = LoadTexture(device, document, bct, $"{name} base colour", path);
            }
        }

        var emissive = Vector3.Zero;
        Texture2D? emissiveTexture = null;

        if (material.TryGetProperty("emissiveFactor", out JsonElement em))
        {
            float[] v = Gltf.ReadNumbers(em, 3);
            emissive = new Vector3(v[0], v[1], v[2]);
        }

        if (material.TryGetProperty("emissiveTexture", out JsonElement et))
        {
            emissiveTexture = LoadTexture(device, document, et, $"{name} emissive", path);
        }

        string alphaMode = material.TryGetProperty("alphaMode", out JsonElement am)
            ? am.GetString() ?? "OPAQUE"
            : "OPAQUE";

        float alphaCutoff = material.TryGetProperty("alphaCutoff", out JsonElement ac)
            ? (float)ac.GetDouble()
            : 0.5f;

        bool doubleSided = material.TryGetProperty("doubleSided", out JsonElement ds)
            && ds.GetBoolean();

        return new Material
        {
            Name = name,
            BaseColorFactor = baseColor,
            BaseColorTexture = baseColorTexture,
            Metallic = metallic,
            Roughness = roughness,
            EmissiveFactor = emissive,
            EmissiveTexture = emissiveTexture,
            AlphaMode = alphaMode,
            AlphaCutoff = alphaCutoff,
            DoubleSided = doubleSided,
        };
    }

    private static Texture2D? LoadTexture(
        GraphicsDevice device, Gltf.Document document, JsonElement reference, string what, string path)
    {
        if (!reference.TryGetProperty("index", out JsonElement textureIndex))
        {
            return null;
        }

        int index = textureIndex.GetInt32();
        if (index < 0 || index >= document.Images.Length || document.Images[index] is not byte[] bytes)
        {
            // An external image file, referenced by uri rather than embedded.
            Console.WriteLine($"  note: {what} in {Path.GetFileName(path)} is an external image; "
                + "not loaded");
            return null;
        }

        using var stream = new MemoryStream(bytes, writable: false);
        return Texture2D.FromStream(device, stream);
    }

    /// <summary>
    /// Fills in each part's transform relative to the whole hull.
    /// </summary>
    /// <remarks>
    /// Done once, from the parent chain the walk recorded. A part whose parent is animated will need
    /// recomputing, which is a method on the part rather than a per-frame walk of the whole hull.
    /// </remarks>
    private void ComputeWorldTransforms()
    {
        for (int i = 0; i < _parts.Count; i++)
        {
            Part part = _parts[i];
            part.WorldTransform = part.ParentIndex >= 0 && part.ParentIndex < _parts.Count
                ? part.LocalTransform * _parts[part.ParentIndex].WorldTransform
                : part.LocalTransform;
        }
    }

    /// <summary>The material worn by a primitive that names none.</summary>
    internal static readonly Material Default = new()
    {
        Name = "(default)",
        BaseColorFactor = new Vector4(0.7f, 0.7f, 0.7f, 1f),
    };

    /// <summary>Microsoft's matrix type to the graphics layer's.</summary>
    private static Matrix ToXna(System.Numerics.Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);

    public void Dispose()
    {
        // Materials are shared between parts, so they are disposed once, by their own identity.
        var seen = new HashSet<Material>();

        foreach (Part part in _parts)
        {
            if (seen.Add(part.Material))
            {
                part.Material.Dispose();
            }

            part.Vertices.Dispose();
            part.Indices.Dispose();
        }

        _parts.Clear();
    }
}
