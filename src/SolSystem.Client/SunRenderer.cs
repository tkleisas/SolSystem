using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SolSystem.Core.Numerics;

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
    /// Eight. The quad is this many times the disc's radius in each direction, and the wide falloff
    /// is tuned to be nearly gone by the corner — a glow that visibly stops at the edge of a square
    /// is worse than no glow at all.
    /// </remarks>
    private const float GlowExtent = 8.0f;

    private readonly GraphicsDevice _device;
    private readonly Effect _effect;
    private readonly VertexBuffer _quad;
    private readonly IndexBuffer _indices;
    private readonly VertexPositionNormalTexture[] _vertices = new VertexPositionNormalTexture[4];

    private double _seconds;

    internal SunRenderer(GraphicsDevice device, Microsoft.Xna.Framework.Content.ContentManager content)
    {
        _device = device;

        _effect = EffectLoader.Load(content, "Sun");

        _quad = new VertexBuffer(device, VertexPositionNormalTexture.VertexDeclaration, 4,
            BufferUsage.WriteOnly);

        _indices = new IndexBuffer(device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
        _indices.SetData(new short[] { 0, 1, 2, 0, 2, 3 });
    }

    /// <summary>Whether the shader loaded, so the caller can fall back to a plain disc.</summary>
    internal bool Ready => _effect is not null;

    /// <summary>Advances the shader's clock, which drives the granulation and the flicker.</summary>
    internal void Update(double seconds) => _seconds += seconds;

    /// <summary>
    /// Draws the Sun, if it is in front of the camera.
    /// </summary>
    /// <param name="session">Where the observer is and which way it is looking.</param>
    /// <param name="view">The view matrix, with the observer at the origin.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="scale">Render units per kilometre.</param>
    internal void Draw(FlightSession session, Matrix view, Matrix projection, float scale)
    {
        if (_effect is null)
        {
            return;
        }

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
        Vector3 forward = Unit(session.Forward);

        // Behind the camera, or far enough off the axis that the corona cannot reach the frame.
        float facing = Vector3.Dot(toSun, forward);
        float angularRadius = MathF.Asin(Math.Clamp(696_000f / (distance / scale), 0f, 1f));
        float reach = GlowExtent * angularRadius;

        if (facing < -0.2f || facing < MathF.Cos(MathF.Min(MathF.PI, reach + 1.0f)))
        {
            return;
        }

        // The quad is placed at the Sun's centre and sized so that its own radius is GlowExtent disc
        // radii as seen from the observer. It faces the camera, so its axes are the camera's.
        Vector3 right = Vector3.Normalize(
            Unit(FlightSession.Cross(session.Forward, session.Up)));

        Vector3 up = Vector3.Cross(toSun, right);
        if (up.LengthSquared() < 1e-8f)
        {
            // Looking straight down the Sun's direction with the camera's right parallel to it; any
            // up will do, and a NaN here would blank the frame.
            up = Vector3.Up;
        }

        up = Vector3.Normalize(up);
        right = Vector3.Normalize(Vector3.Cross(up, toSun));

        float half = MathF.Tan(reach) * distance;

        _vertices[0] = new VertexPositionNormalTexture(
            centre - (right * half) - (up * half), -toSun, new Vector2(0f, 1f));
        _vertices[1] = new VertexPositionNormalTexture(
            centre + (right * half) - (up * half), -toSun, new Vector2(1f, 1f));
        _vertices[2] = new VertexPositionNormalTexture(
            centre + (right * half) + (up * half), -toSun, new Vector2(1f, 0f));
        _vertices[3] = new VertexPositionNormalTexture(
            centre - (right * half) + (up * half), -toSun, new Vector2(0f, 0f));

        _quad.SetData(_vertices);

        _effect.Parameters["WorldViewProjection"]?.SetValue(view * projection);
        _effect.Parameters["DiscRadius"]?.SetValue(1.0f / GlowExtent);
        // Tuned by looking, and the numbers are these rather than something rounder because the
        // range between "invisible" and "a white square" turned out to be narrow. The tight falloff
        // hugs the limb and gives the flash; the wide one carries the glow out to the quad's edge,
        // where the corner mask takes it to nothing.
        _effect.Parameters["CoronaTightness"]?.SetValue(13.0f);
        _effect.Parameters["CoronaWidth"]?.SetValue(0.9f);
        _effect.Parameters["DiscIntensity"]?.SetValue(1.6f);
        _effect.Parameters["CoronaIntensity"]?.SetValue(1.5f);
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

    private static Vector3 Unit(Fix128Vec v) => new(
        (float)v.X.ToDouble(), (float)v.Y.ToDouble(), (float)v.Z.ToDouble());

    public void Dispose()
    {
        _quad.Dispose();
        _indices.Dispose();
        _effect?.Dispose();
    }
}
