using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SolSystem.Client;

/// <summary>
/// The Earth's surface and atmosphere, drawn with <c>Planet.fx</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only the Earth uses this. The other bodies are still the fixed-function spheres in
/// <see cref="BodyRenderer"/>, which is honest: they have no night side worth lighting and no
/// atmosphere worth scattering. The Earth has both.
/// </para>
/// <para>
/// The vertex buffer is rebuilt on the CPU each frame in clip space. That is the consequence of the
/// effect compiler exposing matrices as scalars: the sphere is transformed here instead of in a
/// shader. At 2,145 vertices and one body it is a few microseconds, and it also keeps the
/// atmosphere's 2% shell consistent with the surface's limb.
/// </para>
/// </remarks>
internal sealed class PlanetRenderer : IDisposable
{
    private const float AtmosphereScale = 1.02f;

    private readonly Effect _effect;
    private readonly VertexPositionNormalTexture[] _sphere;
    private readonly IndexBuffer _indexBuffer;
    private readonly int _indexCount;

    private readonly PlanetVertex[] _surfaceVertices;
    private readonly PlanetVertex[] _atmosphereVertices;
    private readonly VertexBuffer _surfaceBuffer;
    private readonly VertexBuffer _atmosphereBuffer;

    private readonly Texture2D _black;
    private readonly Texture2D _transparent;

    internal PlanetRenderer(GraphicsDevice device, VertexPositionNormalTexture[] sphere, short[] indices)
    {
        _effect = EffectLoader.Load(device, "Planet");
        _sphere = sphere;

        _surfaceVertices = new PlanetVertex[sphere.Length];
        _atmosphereVertices = new PlanetVertex[sphere.Length];

        _surfaceBuffer = new VertexBuffer(
            device, PlanetVertex.Declaration, sphere.Length, BufferUsage.WriteOnly);
        _atmosphereBuffer = new VertexBuffer(
            device, PlanetVertex.Declaration, sphere.Length, BufferUsage.WriteOnly);

        _indexBuffer = new IndexBuffer(
            device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
        _indexBuffer.SetData(indices);
        _indexCount = indices.Length;

        _black = new Texture2D(device, 1, 1, false, SurfaceFormat.Color);
        _black.SetData(new[] { Color.Black });

        _transparent = new Texture2D(device, 1, 1, false, SurfaceFormat.Color);
        _transparent.SetData(new[] { new Color(0, 0, 0, 0) });
    }

    /// <summary>
    /// Draws the day map, the night lights and the inner rim.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="centre">The body's centre in render units.</param>
    /// <param name="radius">The body's radius in render units.</param>
    /// <param name="orientation">The body's pole and spin, in render space.</param>
    /// <param name="toSun">Unit direction from the body towards the Sun.</param>
    /// <param name="viewProjection">The camera's view times projection.</param>
    /// <param name="albedo">The day map.</param>
    /// <param name="night">The city-light map, or null to draw no lights.</param>
    /// <param name="clouds">The cloud coverage map, or null for a clear sky.</param>
    internal void DrawSurface(GraphicsDevice device, Vector3 centre, float radius,
        Matrix orientation, Vector3 toSun, Matrix viewProjection, Texture2D albedo,
        Texture2D? night, Texture2D? clouds)
    {
        Fill(_surfaceVertices, centre, radius, orientation, viewProjection);
        _surfaceBuffer.SetData(_surfaceVertices);

        _effect.Parameters["Albedo"]?.SetValue(albedo);
        _effect.Parameters["Night"]?.SetValue(night ?? _black);
        _effect.Parameters["Clouds"]?.SetValue(clouds ?? _transparent);
        _effect.Parameters["SunDirection"]?.SetValue(new Vector4(toSun, 0f));
        _effect.Parameters["AtmosphereColor"]?.SetValue(new Vector4(0.32f, 0.52f, 0.95f, 1f));
        _effect.Parameters["Tuning"]?.SetValue(new Vector4(2.6f, 1.0f, 0.45f, 0f));

        // The same texture slot and sampler the fixed-function spheres use. The effect's own sampler
        // state is declared in the shader; setting the device state too costs nothing and makes the
        // scaled UVs behave if the effect leaves the sampler at its point-sampled default.
        device.SamplerStates[0] = SamplerState.LinearWrap;
        device.SamplerStates[1] = SamplerState.LinearWrap;
        device.SamplerStates[2] = SamplerState.LinearWrap;

        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
        device.BlendState = BlendState.Opaque;

        _effect.CurrentTechnique = _effect.Techniques["Surface"];
        Draw(device, _surfaceBuffer);
    }

    /// <summary>
    /// Draws the outer atmosphere shell: a 2% larger sphere, additively blended.
    /// </summary>
    internal void DrawAtmosphere(GraphicsDevice device, Vector3 centre, float radius,
        Matrix orientation, Vector3 toSun, Matrix viewProjection)
    {
        Fill(_atmosphereVertices, centre, radius * AtmosphereScale, orientation, viewProjection);
        _atmosphereBuffer.SetData(_atmosphereVertices);

        _effect.Parameters["SunDirection"]?.SetValue(new Vector4(toSun, 0f));
        _effect.Parameters["AtmosphereColor"]?.SetValue(new Vector4(0.32f, 0.52f, 0.95f, 1f));
        _effect.Parameters["Tuning"]?.SetValue(new Vector4(2.6f, 1.0f, 0.45f, 0f));

        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
        device.BlendState = BlendState.Additive;

        _effect.CurrentTechnique = _effect.Techniques["Atmosphere"];
        Draw(device, _atmosphereBuffer);

        // The next body is drawn with the fixed-function sphere and expects an opaque pass.
        device.BlendState = BlendState.Opaque;
    }

    private void Draw(GraphicsDevice device, VertexBuffer buffer)
    {
        device.SetVertexBuffer(buffer);
        device.Indices = _indexBuffer;

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawIndexedPrimitives(
                PrimitiveType.TriangleList, 0, 0, _indexCount / 3);
        }
    }

    private void Fill(PlanetVertex[] target, Vector3 centre, float radius,
        Matrix orientation, Matrix viewProjection)
    {
        for (int i = 0; i < _sphere.Length; i++)
        {
            VertexPositionNormalTexture vertex = _sphere[i];

            Vector3 world = centre + Vector3.Transform(vertex.Position * radius, orientation);
            Vector3 normal = Vector3.TransformNormal(vertex.Normal, orientation);

            target[i] = new PlanetVertex
            {
                Position = Vector4.Transform(new Vector4(world, 1f), viewProjection),
                Normal = normal,
                World = world,
                UV = vertex.TextureCoordinate,
            };
        }
    }

    public void Dispose()
    {
        _surfaceBuffer.Dispose();
        _atmosphereBuffer.Dispose();
        _indexBuffer.Dispose();
        _black.Dispose();
        _transparent.Dispose();
        _effect.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PlanetVertex : IVertexType
    {
        internal Vector4 Position;
        internal Vector3 Normal;
        internal Vector3 World;
        internal Vector2 UV;

        internal static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.Position, 0),
            new VertexElement(16, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(28, VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(40, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1));

        VertexDeclaration IVertexType.VertexDeclaration => Declaration;
    }
}
