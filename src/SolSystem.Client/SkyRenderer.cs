using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SolSystem.Core.Numerics;
using SolSystem.Core.Sky;

namespace SolSystem.Client;

/// <summary>
/// Draws the sky: the Milky Way as a shell of geometry, and the stars as points of light.
/// </summary>
/// <remarks>
/// <para>
/// The two are drawn by different means for a reason that took a wrong first version to find.
/// </para>
/// <para>
/// <b>The Milky Way is geometry</b>, because it is a smooth function of direction with a scale of a
/// few degrees and no texture: a sphere of a hundred and ninety-two by ninety-six vertices samples
/// it every 1.9 degrees, the brightness rides per vertex, and the hardware interpolates across each
/// triangle. That produces a band as smooth as a per-pixel shader would and needs no shader, no
/// compiled effect and no content pipeline.
/// </para>
/// <para>
/// <b>The stars are projected directly</b>, not drawn as camera-facing quads. A star is at infinity,
/// so its screen position depends only on the direction to it and the camera's orientation: two dot
/// products and two divides. The quad approach looks equivalent and is not — at the distance a star
/// must sit to be behind everything, the quad's own edges subtend an angle the rasteriser resolves
/// badly, and the first version of this renderer drew every star in the sky as a short diagonal
/// streak. Direct projection is also far cheaper: eleven thousand sprites, no vertex buffer, no
/// index buffer, and one dot product to reject the half of the sky behind the camera.
/// </para>
/// <para>
/// Colour is the measured B−V index taken through a black-body spectrum and the CIE
/// colour-matching functions, so Betelgeuse is orange and Rigel is blue because they are. Nothing
/// here is chosen by eye.
/// </para>
/// </remarks>
internal sealed class SkyRenderer : IDisposable
{
    /// <summary>
    /// Radius of the sky shell, in kilometres.
    /// </summary>
    /// <remarks>
    /// A hundred million kilometres, inside Mercury's orbit and further than anything else the
    /// client draws. A star is a direction and any distance will do, as long as nothing else is
    /// drawn there.
    /// </remarks>
    internal const float SkyRadius = 1.0e8f;

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly VertexBuffer _shell;
    private readonly IndexBuffer _shellIndices;
    private readonly int _shellIndexCount;
    private readonly Texture2D _starTexture;
    private readonly SpriteBatch _sprites;

    /// <summary>How many stars were drawn in the last frame, for the heads-up display.</summary>
    internal int StarsDrawn { get; private set; }

    internal SkyRenderer(GraphicsDevice device, SpriteBatch sprites)
    {
        _device = device;
        _sprites = sprites;

        _effect = new BasicEffect(device)
        {
            TextureEnabled = false,
            VertexColorEnabled = true,
            LightingEnabled = false,
            World = Matrix.Identity,
        };

        (_shell, _shellIndices, _shellIndexCount) = BuildShell(device);
        _starTexture = MakeDisc(device, 64);
    }

    /// <summary>
    /// The Milky Way as a vertex-coloured sphere.
    /// </summary>
    /// <remarks>
    /// The brightness is evaluated once per vertex at load and never again: the band does not move
    /// relative to the stars, only the observer moves inside it.
    /// </remarks>
    private static (VertexBuffer Buffer, IndexBuffer Indices, int IndexCount) BuildShell(
        GraphicsDevice device)
    {
        const int Longitudes = 192;
        const int Latitudes = 96;

        var vertices = new List<VertexPositionColor>(Longitudes * (Latitudes + 1));
        var indices = new List<short>();

        for (int lat = 0; lat <= Latitudes; lat++)
        {
            double phi = Math.PI * (lat / (double)Latitudes);
            float sinPhi = (float)Math.Sin(phi);
            float cosPhi = (float)Math.Cos(phi);

            for (int lon = 0; lon < Longitudes; lon++)
            {
                double theta = 2.0 * Math.PI * (lon / (double)Longitudes);
                var direction = new Vector3(
                    sinPhi * (float)Math.Cos(theta),
                    sinPhi * (float)Math.Sin(theta),
                    cosPhi);

                var ecliptic = new Fix128Vec(
                    Fix128.FromDouble(direction.X),
                    Fix128.FromDouble(direction.Y),
                    Fix128.FromDouble(direction.Z));

                float brightness = (float)MilkyWay.Brightness(ecliptic);

                // Warm white: the band is unresolved starlight, and unresolved stars are not blue.
                vertices.Add(new VertexPositionColor(
                    direction * SkyRadius,
                    new Color(0.80f * brightness, 0.76f * brightness, 0.70f * brightness)));
            }
        }

        for (int lat = 0; lat < Latitudes; lat++)
        {
            for (int lon = 0; lon < Longitudes; lon++)
            {
                int next = (lon + 1) % Longitudes;
                short a = (short)((lat * Longitudes) + lon);
                short b = (short)((lat * Longitudes) + next);
                short c = (short)(((lat + 1) * Longitudes) + next);
                short d = (short)(((lat + 1) * Longitudes) + lon);

                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(a); indices.Add(c); indices.Add(d);
            }
        }

        // A sixteen-bit index reaches 32 767 and this sphere has 18 624 vertices, so it fits — but
        // only just, and a resolution bump would wrap the indices silently and draw the far side of
        // the sky on top of the near side, which looks like a coordinate bug and is not one.
        if (vertices.Count > short.MaxValue)
        {
            throw new InvalidOperationException(
                $"the sky shell has {vertices.Count} vertices and a sixteen-bit index reaches "
                + $"{short.MaxValue}");
        }

        var buffer = new VertexBuffer(device, VertexPositionColor.VertexDeclaration,
            vertices.Count, BufferUsage.WriteOnly);
        buffer.SetData(vertices.ToArray());

        var indexBuffer = new IndexBuffer(device, IndexElementSize.SixteenBits,
            indices.Count, BufferUsage.WriteOnly);
        indexBuffer.SetData(indices.ToArray());

        return (buffer, indexBuffer, indices.Count);
    }

    /// <summary>
    /// Projects every visible star and queues its sprite.
    /// </summary>
    /// <remarks>
    /// The projection is the whole of it. For a direction <c>d</c> and a camera with basis
    /// <c>(f, r, u)</c> and vertical half-angle <c>θ</c>:
    /// <code>
    ///   z  = d·f                            (behind the camera if not positive)
    ///   sx = W/2 + (d·r)/z / tan(θx) · W/2
    ///   sy = H/2 - (d·u)/z / tan(θy) · H/2
    /// </code>
    /// and a star's angular radius is its linear radius over the shell radius, converted to pixels
    /// through the same <c>tan θ</c>.
    /// </remarks>
    /// <summary>
    /// Projects every visible star and queues its sprite, as seen by one camera.
    /// </summary>
    /// <param name="session">Where the observer is: parallax and aberration need the position.</param>
    /// <param name="forward">The direction the CAMERA is looking, not the direction the ship faces.</param>
    /// <param name="up">The camera's up.</param>
    /// <param name="fieldOfViewDegrees">Vertical field of view.</param>
    /// <remarks>
    /// <b>The camera's basis and the ship's are not the same thing, and passing the ship's was a
    /// bug.</b> A star is projected through the camera that is actually rendering, so when the view
    /// became a chase camera the stars kept being placed for a camera looking down the ship's nose —
    /// which is a different direction entirely. The result was a sky with its stars in the wrong
    /// place: mostly empty, with no way to tell that anything was wrong except that space looked
    /// unusually dark. The Milky Way, which goes through the view matrix, was correct throughout,
    /// which is exactly the kind of half-right that takes a while to notice.
    /// </remarks>
    internal void DrawStars(FlightSession session, Fix128Vec forward, Fix128Vec up,
        float fieldOfViewDegrees)
    {
        Fix128Vec right = FlightSession.Cross(forward, up).Normalized();

        Vector3 f = Unit(forward);
        Vector3 u = Unit(up);
        Vector3 r = Unit(right);

        float width = _device.Viewport.Width;
        float height = _device.Viewport.Height;
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;
        float tanY = MathF.Tan(MathHelper.ToRadians(fieldOfViewDegrees) * 0.5f);
        float tanX = tanY * (halfWidth / halfHeight);

        ReadOnlySpan<Star> stars = session.Stars;
        int drawn = 0;

        for (int i = 0; i < stars.Length; i++)
        {
            ref readonly Star star = ref stars[i];
            Vector3 direction = Unit(session.Apparent(star));

            float z = Vector3.Dot(direction, f);
            if (z <= 0.02f)
            {
                continue;
            }

            float x = Vector3.Dot(direction, r) / z;
            float y = Vector3.Dot(direction, u) / z;

            float sx = halfWidth + (x / tanX * halfWidth);
            float sy = halfHeight - (y / tanY * halfHeight);

            // A margin of a few dozen pixels, so a bright star's halo is not clipped at the edge.
            if (sx < -48f || sy < -48f || sx > width + 48f || sy > height + 48f)
            {
                continue;
            }

            float pixels = (float)(RadiusFor(star.Magnitude) / SkyRadius) / tanY * halfHeight;
            float draw = MathF.Max(pixels, 0.5f) * 2.0f;

            _sprites.Draw(
                _starTexture,
                new Vector2(sx, sy),
                null,
                ColourFor(star),
                0f,
                new Vector2(_starTexture.Width * 0.5f),
                draw / _starTexture.Width,
                SpriteEffects.None,
                0f);

            drawn++;
        }

        StarsDrawn = drawn;
    }

    /// <summary>
    /// Apparent radius of a star, in kilometres at the shell's distance.
    /// </summary>
    /// <remarks>
    /// Magnitude is already logarithmic — five magnitudes is a factor of a hundred in brightness —
    /// so the radius cannot be proportional to it, or Sirius becomes a hundred times the size of a
    /// faint star and the sky becomes a field of blobs. The exponent compresses the range the way a
    /// printed planisphere does, tuned so that a sixth-magnitude star is about one pixel on a
    /// 720-line frame at sixty degrees and Sirius is about thirty.
    /// </remarks>
    private static double RadiusFor(double magnitude)
    {
        double intensity = Math.Pow(10.0, -0.4 * (magnitude - 6.5));
        return 1.1e5 * Math.Pow(intensity, 0.28);
    }

    private static Color ColourFor(in Star star)
    {
        (double r, double g, double b) = StarColour.ForColourIndex(star.ColourIndex);

        // The catalogue's colours are linear light and a frame buffer is not, so the transfer
        // function goes on here. Without it every star is too bright and washed toward white.
        return new Color(
            (float)StarColour.SrgbEncoded(r),
            (float)StarColour.SrgbEncoded(g),
            (float)StarColour.SrgbEncoded(b));
    }

    /// <summary>Draws the band. The stars go through the sprite batch around this.</summary>
    internal void DrawShell(Matrix view, Matrix projection)
    {
        _effect.View = view;
        _effect.Projection = projection;

        // The shell is the furthest thing there is, so it neither occludes nor is occluded.
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.Opaque;
        _device.Indices = _shellIndices;
        _device.SetVertexBuffer(_shell);

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _shellIndexCount / 3);
        }
    }

    /// <summary>
    /// A soft disc: a hard core with a falloff, so a star reads as a point of light rather than as a
    /// square of texture.
    /// </summary>
    private static Texture2D MakeDisc(GraphicsDevice device, int size)
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

                float alpha = d >= 1f
                    ? 0f
                    : d < 0.30f
                        ? 1f
                        : MathF.Pow(1f - ((d - 0.30f) / 0.70f), 2.0f);

                pixels[(y * size) + x] = new Color(alpha, alpha, alpha, alpha);
            }
        }

        texture.SetData(pixels);
        return texture;
    }

    private static Vector3 Unit(Fix128Vec v) => new(
        (float)v.X.ToDouble(), (float)v.Y.ToDouble(), (float)v.Z.ToDouble());

    public void Dispose()
    {
        _shell.Dispose();
        _shellIndices.Dispose();
        _starTexture.Dispose();
        _effect.Dispose();
    }
}
