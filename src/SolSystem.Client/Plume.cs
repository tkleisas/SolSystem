using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SolSystem.Core.Numerics;

namespace SolSystem.Client;

/// <summary>
/// The engine plume: the only immediate feedback that the throttle did anything.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a sentence: <i>"I can see the earth but nothing happens"</i>. The ship was
/// accelerating correctly the whole time — at four milligee, which over ten seconds is four tenths
/// of a metre a second, and which from a chase camera a hundred and thirty metres back is
/// indistinguishable from sitting still. The physics was right and the game was unplayable, because
/// nothing on the screen said that a key had done anything.
/// </para>
/// <para>
/// So the plume is not decoration. It is the readout for the one control whose effect is otherwise
/// invisible at the timescale a person watches. A fusion torch at four milligee has no exhaust you
/// could see from outside anyway — what is drawn here is the radiator glow at the throat, scaled by
/// throttle, and it is a deliberate lie about brightness in service of a truth about state.
/// </para>
/// <para>
/// Drawn as sprites rather than geometry: a bright core, a soft halo, and a pair of lateral flares
/// from the magnetic nozzles. Sprites because the plume is a light source, lights do not have
/// silhouettes, and a mesh would only give it an edge to get wrong.
/// </para>
/// </remarks>
internal sealed class Plume : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _flare;

    internal Plume(GraphicsDevice device, SpriteBatch sprites)
    {
        _device = device;
        _sprites = sprites;
        _flare = MakeFlare(device, 128);
    }

    /// <summary>
    /// Draws the plume behind a hull.
    /// </summary>
    /// <param name="view">The near-pass view matrix, with the ship at the origin.</param>
    /// <param name="projection">The near-pass projection.</param>
    /// <param name="nose">Unit vector along the hull's nose, in the near pass's frame.</param>
    /// <param name="throttle">How hard the engine is running, 0 to 1.</param>
    /// <param name="lengthMetres">How far the plume reaches at full throttle.</param>
    internal void Draw(Matrix view, Matrix projection, Vector3 nose, double throttle,
        float lengthMetres)
    {
        if (throttle <= 0.001)
        {
            return;
        }

        // The plume's points, in the near pass's metre frame, behind the engine plane.
        float far = lengthMetres * (float)throttle;

        Span<(Vector3 Point, float Size, float Brightness)> knots = stackalloc[]
        {
            (nose * -4f, 22f, 1.00f),
            (nose * -(far * 0.25f), 40f, 0.72f),
            (nose * -(far * 0.55f), 62f, 0.40f),
            (nose * -(far * 0.85f), 86f, 0.18f),
            (nose * -far, 110f, 0.07f),
        };

        Matrix viewProjection = view * projection;
        float height = _device.Viewport.Height;
        float width = _device.Viewport.Width;

        foreach ((Vector3 point, float size, float brightness) in knots)
        {
            Vector4 clip = Vector4.Transform(new Vector4(point, 1f), viewProjection);
            if (clip.W <= 0.001f)
            {
                continue;
            }

            var screen = new Vector2(
                width * 0.5f * (1f + (clip.X / clip.W)),
                height * 0.5f * (1f - (clip.Y / clip.W)));

            // Scale with distance the way a light does: the sprite is a fixed angular size, so its
            // pixel size falls off with range.
            float pixels = size * (height * 0.5f) / clip.W;

            if (pixels < 1f)
            {
                continue;
            }

            // Blue-white at the throat shading to violet down the plume, which is what a hydrogen
            // plasma at a hundred million kelvin looks like when it has been through a nozzle.
            var colour = new Color(
                brightness, brightness * 0.86f, brightness * 0.72f) * brightness;

            _sprites.Draw(
                _flare,
                screen,
                null,
                colour,
                0f,
                new Vector2(_flare.Width * 0.5f),
                pixels * 2f / _flare.Width,
                SpriteEffects.None,
                0f);
        }
    }

    /// <summary>A soft radial flare: bright at the centre, nothing at the corners.</summary>
    private static Texture2D MakeFlare(GraphicsDevice device, int size)
    {
        var texture = new Texture2D(device, size, size);
        var pixels = new Color[size * size];
        float centre = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - centre) / centre;
                float dy = (y - centre) / centre;
                float d = MathF.Sqrt((dx * dx) + (dy * dy));

                // A steeper falloff than a star's, because a plume is a flare rather than a point.
                float alpha = d >= 1f ? 0f : MathF.Pow(1f - d, 3.2f);
                pixels[(y * size) + x] = new Color(alpha, alpha, alpha, alpha);
            }
        }

        texture.SetData(pixels);
        return texture;
    }

    public void Dispose() => _flare.Dispose();
}
