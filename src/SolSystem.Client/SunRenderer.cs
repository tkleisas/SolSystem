using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SolSystem.Client;

/// <summary>
/// Draws the Sun as a limb-darkened disc inside a radiating corona.
/// </summary>
/// <remarks>
/// <para>
/// The Sun is the one object in the sky that cannot be drawn as a lit sphere, and the reason is that
/// it is not a surface — it is a source. A sphere shaded by a directional light gives a bright disc
/// with a hard edge, which is what a cue ball looks like, and the Sun does not look like that: it
/// has a darkening limb, and it has light coming off it in every direction for several of its own
/// radii.
/// </para>
/// <para>
/// So it is a camera-facing quad eight times the disc's radius, and the shader in
/// <c>Content/Sun.fx</c> does the rest. Three octaves of that are worth naming here because they are
/// what makes the difference between a star and a sticker: the Eddington limb-darkening law, which
/// costs one multiply and is what the eye reads as "sphere"; two exponential falloffs, a tight one
/// that hugs the limb and a wide one that carries the sky glow out to the quad's edge; and a warm
/// colour on the corona against a white disc, because the light that scatters out of the Sun has
/// been through more of its atmosphere than the light that comes straight at you.
/// </para>
/// <para>
/// The quad is sized from the Sun's real angular radius at the observer's real distance, so the disc
/// is half a degree across from the Earth and thirty times that from Mercury, without anything here
/// knowing which.
/// </para>
/// </remarks>
internal sealed class SunRenderer : IDisposable
{
    /// <summary>
    /// How far the corona reaches, in disc radii.
    /// </summary>
    /// <remarks>
    /// Twelve. The disc itself stays the real 0.53 degrees — at a sixty-degree field of view that is
    /// six pixels, which is correct and reads as a point — so the thing that has to say "star"
    /// rather than "pixel" is the glare around it. Eight disc radii was a two-degree halo and
    /// disappeared against the starfield; twelve carries it out to three degrees without turning the
    /// Sun into a lamp. The shader fades the glow to nothing at the quad's edge, so the square never
    /// shows.
    /// </remarks>
    private const float GlowExtent = 12.0f;

    private readonly GraphicsDevice _device;
    private readonly Effect _effect;
    private readonly VertexBuffer _quad;
    private readonly IndexBuffer _indices;
    private readonly SunVertex[] _vertices = new SunVertex[4];

    private double _seconds;

    /// <summary>
    /// One corner of the Sun's quad, already in clip space.
    /// </summary>
    /// <remarks>
    /// The position is a <see cref="Vector4"/> rather than the usual <see cref="Vector3"/> because the
    /// CPU does the world/view/projection multiply: the effect compiler exposed the matrix uniform as
    /// a scalar, so the shader never received it and the corona was never drawn. Four projected
    /// corners are also less work than a uniform that does not arrive.
    /// </remarks>
    private struct SunVertex : IVertexType
    {
        internal Vector4 Position;
        internal Vector3 Normal;
        internal Vector2 TexCoord;

        internal static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.Position, 0),
            new VertexElement(16, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

        VertexDeclaration IVertexType.VertexDeclaration => Declaration;
    }

    internal SunRenderer(GraphicsDevice device)
    {
        _device = device;

        _effect = EffectLoader.Load(device, "Sun");

        _quad = new VertexBuffer(device, SunVertex.Declaration, 4, BufferUsage.WriteOnly);

        _indices = new IndexBuffer(device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
        _indices.SetData(new short[] { 0, 1, 2, 0, 2, 3 });
    }

    /// <summary>Advances the shader's clock, which drives the granulation and the flicker.</summary>
    internal void Update(double seconds) => _seconds += seconds;

    /// <summary>
    /// Draws the Sun as a screen-space disc and corona.
    /// </summary>
    /// <param name="session">Where the observer is.</param>
    /// <param name="view">The view matrix, with the observer at the origin.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="scale">Render units per kilometre.</param>
    internal void Draw(FlightSession session, Matrix view, Matrix projection, float scale)
    {
        // The effect is a constructor requirement — EffectLoader.Load throws when the shader
        // is missing rather than handing back a null — so there is no "not ready" case here,
        // and none of the fallback-a-plain-disc kind the old Ready property promised.
        // The Sun is the origin of the heliocentric frame, so the direction to it is the reverse of
        // where the observer is — and in render space, where the camera is at the origin, its centre
        // is the observer's own position negated.
        var centre = new Vector3(
            (float)(-session.ObserverPosition.X.ToDouble() * scale),
            (float)(-session.ObserverPosition.Y.ToDouble() * scale),
            (float)(-session.ObserverPosition.Z.ToDouble() * scale));

        float distance = centre.Length();
        if (distance < 1e-6f)
        {
            return;
        }

        Vector3 toSun = centre / distance;

        float angularRadius = MathF.Asin(Math.Clamp(696_000f / (distance / scale), 0f, 1f));
        float reach = GlowExtent * angularRadius;

        // THE QUAD IS BUILT IN SCREEN SPACE, NOT ON A WORLD-SPACE BILLBOARD.
        //
        // A world-space quad has to face the camera to look like a disc. Facing it along the line
        // to the Sun is not the same thing: off-axis, that plane is tilted relative to the view and
        // projects as a trapezoid, which is why the disc and its corona only looked right when the
        // Sun was centred. Project the centre, offset in NDC by the angular radius, and the shape is
        // circular wherever it falls. The shader corrects its radial coordinate by the viewport
        // aspect so the disc is a circle in pixels, not in NDC units.
        Matrix viewProjection = view * projection;
        Vector4 centreClip = Vector4.Transform(new Vector4(centre, 1f), viewProjection);
        if (centreClip.W <= 1.0e-4f)
        {
            return;
        }

        Vector2 centreNdc = new(centreClip.X / centreClip.W, centreClip.Y / centreClip.W);

        // The projection's M22 is 1 / tan(vertical field of view / 2), so this is the quad's
        // half-height in NDC: the same angular size the Sun had on the sphere, at any off-axis
        // angle.
        float tanHalfFovY = 1.0f / projection.M22;
        float halfY = MathF.Tan(reach) / tanHalfFovY;
        float aspect = _device.Viewport.AspectRatio;
        float halfX = halfY * aspect;

        Vector2[] corners =
        [
            new(-halfX, -halfY),
            new(halfX, -halfY),
            new(halfX, halfY),
            new(-halfX, halfY),
        ];
        Vector2[] texCoords =
        [
            new(0f, 1f), new(1f, 1f), new(1f, 0f), new(0f, 0f),
        ];

        for (int i = 0; i < corners.Length; i++)
        {
            _vertices[i] = new SunVertex
            {
                Position = new Vector4(
                    centreNdc.X + corners[i].X, centreNdc.Y + corners[i].Y, 0f, 1f),
                Normal = -toSun,
                TexCoord = texCoords[i],
            };
        }

        _quad.SetData(_vertices);

        _effect.Parameters["DiscRadius"]?.SetValue(1.0f / GlowExtent);
        _effect.Parameters["Aspect"]?.SetValue(aspect);
        // Tuned by looking, and the numbers are these rather than something rounder because the
        // range between "invisible" and "a white square" turned out to be narrow. The tight falloff
        // hugs the limb and gives the flash; the wide one carries the glow out to the quad's edge,
        // where the corner mask takes it to nothing.
        _effect.Parameters["CoronaTightness"]?.SetValue(11.0f);
        _effect.Parameters["CoronaWidth"]?.SetValue(1.0f);
        _effect.Parameters["DiscIntensity"]?.SetValue(1.6f);
        _effect.Parameters["CoronaIntensity"]?.SetValue(14.0f);
        _effect.Parameters["FlickerAmount"]?.SetValue(0.05f);
        _effect.Parameters["Time"]?.SetValue((float)_seconds);


        // Additive, and no depth: the Sun is a source, so it adds to whatever is behind it and is
        // occluded by nothing. A planet passing in front of it does so in the draw order, which is
        // why the bodies are painted before this.
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.Additive;
        _device.Indices = _indices;
        _device.SetVertexBuffer(_quad);

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 2);
        }

        _device.BlendState = BlendState.Opaque;
    }

    public void Dispose()
    {
        _quad.Dispose();
        _indices.Dispose();
        _effect.Dispose();
    }
}
