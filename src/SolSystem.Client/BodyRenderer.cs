using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;
using SolSystem.Core.Sky;

namespace SolSystem.Client;

/// <summary>
/// Draws the Sun, the Moon and the planets where the ephemeris says they are.
/// </summary>
/// <remarks>
/// <para>
/// Each is a sphere at its true distance and its true radius, in kilometres, with the observer at
/// the origin. Which means the angular size is not modelled, it is what falls out: the Sun is half a
/// degree across because it is 1.4 million kilometres wide at 150 million away, and the Moon is half
/// a degree across because it is 3 475 wide at 384 000. That they match is the oldest eclipse in
/// the solar system and it is not a coincidence the renderer has to be told about.
/// </para>
/// <para>
/// <b>Lighting comes from the Sun and nothing else is lit.</b> One directional light pointing from
/// the Sun towards the observer, so the Moon shows the phase it actually has on the date asked for
/// and the Earth shows a terminator. A body drawn with flat ambient light looks like a sticker, and
/// the phase of the Moon is the one thing about the sky that anybody can check without a reference.
/// </para>
/// <para>
/// The Sun is drawn without lighting at all, because the Sun is where the light comes from.
/// </para>
/// </remarks>
internal sealed class BodyRenderer : IDisposable
{
    /// <summary>Radius of the mesh every body is drawn with, before its own scale.</summary>
    private const float UnitRadius = 1.0f;

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly VertexBuffer _sphere;
    private readonly IndexBuffer _sphereIndices;
    private readonly int _sphereIndexCount;

    /// <summary>Textures, by file name. Loaded once; a body without one keeps its flat colour.</summary>
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private readonly string _textureDirectory;

    /// <summary>How many bodies were drawn in the last frame, for the heads-up display.</summary>
    internal int BodiesDrawn { get; private set; }

    /// <summary>Prints what each body resolved to. Diagnostic, for the headless renderer.</summary>
    internal static bool Verbose;

    internal BodyRenderer(GraphicsDevice device, string textureDirectory)
    {
        _device = device;
        _textureDirectory = textureDirectory;

        _effect = new BasicEffect(device)
        {
            TextureEnabled = false,
            Texture = null,
            VertexColorEnabled = false,
            LightingEnabled = true,
            World = Matrix.Identity,
            AmbientLightColor = new Vector3(0.06f),
            DiffuseColor = Vector3.One,
            SpecularColor = Vector3.Zero,
        };

        _effect.DirectionalLight0.Enabled = true;
        _effect.DirectionalLight1.Enabled = false;
        _effect.DirectionalLight2.Enabled = false;
        _effect.PreferPerPixelLighting = true;

        (_sphere, _sphereIndices, _sphereIndexCount) = BuildSphere(device, 64, 32);
    }

    /// <summary>A UV sphere of unit radius, which every body is drawn as.</summary>
    private static (VertexBuffer Buffer, IndexBuffer Indices, int IndexCount) BuildSphere(
        GraphicsDevice device, int longitudes, int latitudes)
    {
        var vertices = new VertexPositionNormalTexture[((longitudes + 1) * (latitudes + 1))];

        for (int lat = 0; lat <= latitudes; lat++)
        {
            double phi = Math.PI * (lat / (double)latitudes);
            float sinPhi = (float)Math.Sin(phi);
            float cosPhi = (float)Math.Cos(phi);

            for (int lon = 0; lon <= longitudes; lon++)
            {
                double theta = 2.0 * Math.PI * (lon / (double)longitudes);
                var normal = new Vector3(
                    sinPhi * (float)Math.Cos(theta),
                    sinPhi * (float)Math.Sin(theta),
                    cosPhi);

                // The half-turn offset puts the mesh's longitude zero on the map's prime meridian:
                // an equirectangular map has its u=0 at 180 degrees west, so without this every
                // body's texture is rotated half a turn and the Earth's continents are in the wrong
                // ocean.
                vertices[(lat * (longitudes + 1)) + lon] = new VertexPositionNormalTexture(
                    normal * UnitRadius, normal,
                    new Vector2((lon / (float)longitudes) + 0.5f, lat / (float)latitudes));
            }
        }

        var indices = new List<short>();
        for (int lat = 0; lat < latitudes; lat++)
        {
            for (int lon = 0; lon < longitudes; lon++)
            {
                short a = (short)((lat * (longitudes + 1)) + lon);
                short b = (short)(a + 1);
                short c = (short)(a + longitudes + 1);
                short d = (short)(c + 1);

                indices.Add(a); indices.Add(c); indices.Add(b);
                indices.Add(b); indices.Add(c); indices.Add(d);
            }
        }

        if (vertices.Length > short.MaxValue)
        {
            throw new InvalidOperationException(
                $"the body sphere has {vertices.Length} vertices and a sixteen-bit index reaches "
                + $"{short.MaxValue}");
        }

        var buffer = new VertexBuffer(device, VertexPositionNormalTexture.VertexDeclaration,
            vertices.Length, BufferUsage.WriteOnly);
        buffer.SetData(vertices);

        var indexBuffer = new IndexBuffer(device, IndexElementSize.SixteenBits,
            indices.Count, BufferUsage.WriteOnly);
        indexBuffer.SetData(indices.ToArray());

        return (buffer, indexBuffer, indices.Count);
    }

    /// <summary>
    /// Where each body is, from the observer, in kilometres.
    /// </summary>
    /// <remarks>
    /// Positions are relative to the observer and expressed in render units of a thousand kilometres,
    /// which is the compromise that makes the whole solar system fit in a float without the Earth
    /// losing its shape. At a thousand kilometres a unit, the Earth is 6.4 units across and Neptune
    /// is 4.5 million units away — a float resolves the Earth's surface at that distance to about a
    /// metre, and nothing is drawn further away than Neptune.
    /// </remarks>
    internal readonly record struct Body(
        string Name,
        Fix128Vec Heliocentric,
        double RadiusKilometres,
        Color Colour,
        bool Emissive,
        string? TextureFile,
        string? CloudTexture);

    /// <summary>
    /// Loads a body texture, or null if there is not one.
    /// </summary>
    /// <remarks>
    /// A missing texture is not an error: a body drawn as a flat colour is a body drawn, and the
    /// alternative is a client that will not start because one file is absent.
    /// </remarks>
    private Texture2D? TextureFor(string? file)
    {
        if (file is null)
        {
            return null;
        }

        if (_textures.TryGetValue(file, out Texture2D? cached))
        {
            return cached;
        }

        string path = Path.Combine(_textureDirectory, file);
        Texture2D? texture = null;

        if (File.Exists(path))
        {
            using FileStream stream = File.OpenRead(path);
            texture = Texture2D.FromStream(_device, stream);
        }
        else
        {
            Console.WriteLine($"  note: no texture at {path}; drawing a flat colour");
        }

        _textures[file] = texture!;
        return texture;
    }

    /// <summary>
    /// The orientation of a body's own frame in the ecliptic, or identity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the Earth has one, and it is the one that matters: without it the continents sit with
    /// their poles on the ecliptic axis and Greenwich is in the wrong place, which is the sort of
    /// error that is invisible unless you know what the Earth looks like from the south Atlantic at
    /// a particular hour.
    /// </para>
    /// <para>
    /// The chain is two rotations. <c>Rz(GMST)</c> carries the Earth-fixed frame into the equatorial
    /// frame — a point at longitude zero is at right ascension GMST by definition — and
    /// <c>Rx(−ε)</c> carries the equatorial frame into the ecliptic, which is the frame everything
    /// else in the client is in.
    /// </para>
    /// </remarks>
    private static Matrix OrientationFor(string name, double julianDate)
    {
        if (name != "Earth")
        {
            return Matrix.Identity;
        }

        double gmst = SiderealTime.GreenwichTurns(julianDate) * 2.0 * Math.PI;
        double obliquity = Frames.ObliquityDegrees(julianDate) * Math.PI / 180.0;

        return Matrix.CreateRotationZ((float)gmst) * Matrix.CreateRotationX((float)-obliquity);
    }

    /// <summary>Builds the list of bodies the client draws, from the ephemeris.</summary>
    internal static List<Body> BodiesFor(FlightSession session)
    {
        SolarSystem system = session.System;

        var bodies = new List<Body>
        {
            new("Sun", Fix128Vec.Zero, 696_000.0, new Color(1.0f, 0.97f, 0.90f), true,
                "sun_photosphere.png", null),
            new("Moon", system.MoonHeliocentric().Position, 1_737.4, new Color(0.62f, 0.61f, 0.60f),
                false, "moon_surface.png", null),
            new("Mercury", system.Heliocentric(Ephemeris.Body.Mercury).Position, 2_439.7,
                new Color(0.55f, 0.52f, 0.50f), false, "mercury_surface.png", null),
            new("Venus", system.Heliocentric(Ephemeris.Body.Venus).Position, 6_051.8,
                new Color(0.90f, 0.86f, 0.72f), false, "venus_surface.png", null),
            new("Mars", system.Heliocentric(Ephemeris.Body.Mars).Position, 3_396.2,
                new Color(0.72f, 0.38f, 0.24f), false, "mars_surface.png", null),
            new("Ceres", system.Heliocentric(Ephemeris.Body.Ceres).Position, 469.7,
                new Color(0.42f, 0.41f, 0.40f), false, "ceres_surface.png", null),
            new("Jupiter", system.Heliocentric(Ephemeris.Body.Jupiter).Position, 71_492.0,
                new Color(0.80f, 0.72f, 0.58f), false, null, null),
            new("Saturn", system.Heliocentric(Ephemeris.Body.Saturn).Position, 60_268.0,
                new Color(0.82f, 0.76f, 0.60f), false, null, null),
        };

        // The Earth is drawn as the body the observer is orbiting, from the same table, rather than
        // as a special case — and it carries a cloud deck, which is a second sphere a fifth of a per
        // cent larger, exactly as the Blender preview builds it.
        bodies.Add(new Body("Earth", system.Heliocentric(Ephemeris.Body.Earth).Position, 6_378.1,
            new Color(0.24f, 0.42f, 0.72f), false, "earth_albedo.jpg", "earth_clouds.png"));

        return bodies;
    }

    /// <summary>
    /// Draws the bodies, lit from the Sun.
    /// </summary>
    /// <param name="session">Where the observer is and when it is.</param>
    /// <param name="cameraForward">The direction the CAMERA is looking, not the session's
    /// spawn aim. Culling against the spawn direction emptied the sky whenever the view
    /// swung past about half a field of view from it — the view matrices have used the
    /// live camera all along, and the cull has to use the same one.</param>
    /// <param name="view">The view matrix, which has the observer at the origin.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="scale">Render units per kilometre.</param>
    internal void Draw(FlightSession session, Fix128Vec cameraForward,
        Matrix view, Matrix projection, float scale)
    {
        _effect.View = view;
        _effect.Projection = projection;

        // No depth test, and the bodies are drawn furthest first instead.
        //
        // This is not laziness, it is the only thing that works across the range involved. The eye
        // is four hundred metres above the Earth and the Sun is a hundred and forty-seven million
        // kilometres away, so the near plane has to be a ten-thousandth of a unit and the far plane
        // five million — a ratio of ten billion. A twenty-four-bit depth buffer resolves about
        // z²/(near·2²⁴) at distance z, which at the Sun's distance is three million units, and the
        // Sun is six hundred and ninety-six units across. It fails the depth test entirely and the
        // first version of this drew an empty sky with the Sun reported as drawn.
        //
        // Painter's algorithm is exact for convex bodies that do not intersect, and spheres in the
        // solar system do not intersect. A transit would need better, and a transit is a thing this
        // game may well want later.
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
        _device.BlendState = BlendState.Opaque;
        _device.Indices = _sphereIndices;
        _device.SetVertexBuffer(_sphere);

        // One light, from the Sun. In render space that is the direction from the body towards the
        // origin of the heliocentric frame, which is where the Sun is.
        Vector3 toSun = Vector3.Normalize(new Vector3(
            -(float)(session.ObserverPosition.X.ToDouble() * scale),
            -(float)(session.ObserverPosition.Y.ToDouble() * scale),
            -(float)(session.ObserverPosition.Z.ToDouble() * scale)));

        _effect.DirectionalLight0.Direction = toSun;
        _effect.DirectionalLight0.DiffuseColor = Vector3.One;

        // Furthest first, so a nearer body paints over a further one.
        List<Body> bodies = BodiesFor(session);
        bodies.Sort((a, b) =>
            (b.Heliocentric - session.ObserverPosition).Length
                .CompareTo((a.Heliocentric - session.ObserverPosition).Length));

        int drawn = 0;

        foreach (Body body in bodies)
        {
            Fix128Vec offset = body.Heliocentric - session.ObserverPosition;
            double distance = offset.Length.ToDouble();

            // A body behind the camera still costs a matrix and a draw call, and at planetary
            // distances the ones behind you are most of them.
            Fix128Vec direction = offset.Normalized();
            double facing =
                (direction.X.ToDouble() * cameraForward.X.ToDouble())
                + (direction.Y.ToDouble() * cameraForward.Y.ToDouble())
                + (direction.Z.ToDouble() * cameraForward.Z.ToDouble());

            double angularRadius = Math.Asin(Math.Clamp(
                body.RadiusKilometres / Math.Max(distance, body.RadiusKilometres), 0.0, 1.0));

            // Half a degree of slack, so a body just off the edge still draws its limb.
            double fieldSlack = Math.Cos(Math.Min(Math.PI * 0.5, (60.0 * Math.PI / 180.0 * 0.5) + angularRadius));
            if (facing < fieldSlack && facing < 0.999)
            {
                if (Verbose)
                {
                    Console.WriteLine($"  {body.Name,-8} culled: facing {facing:F4} < {fieldSlack:F4}");
                }

                continue;
            }

            var centre = new Vector3(
                (float)(offset.X.ToDouble() * scale),
                (float)(offset.Y.ToDouble() * scale),
                (float)(offset.Z.ToDouble() * scale));

            Texture2D? texture = TextureFor(body.TextureFile);
            Texture2D? clouds = TextureFor(body.CloudTexture);
            _effect.Texture = texture;
            _effect.TextureEnabled = texture is not null;

            // Scale, then the body's own orientation, then place it.
            Matrix orientation = OrientationFor(body.Name, session.JulianDate);
            _effect.World = Matrix.CreateScale((float)body.RadiusKilometres * scale)
                * orientation
                * Matrix.CreateTranslation(centre);

            if (body.Emissive)
            {
                // The Sun is where the light comes from, so it is drawn as its own colour rather
                // than as a surface something else shines on — which means the EMISSIVE term.
                //
                // The obvious choice is to put the colour in AmbientLightColor, and it draws a black
                // disc: the ambient term in this effect is AmbientLightColor × DiffuseColor, so a
                // material with no diffuse colour is black however bright the ambient light is. The
                // Sun was a black circle in the middle of its own glare for one iteration.
                _effect.EmissiveColor = body.Colour.ToVector3();
                _effect.AmbientLightColor = Vector3.Zero;
                _effect.DiffuseColor = Vector3.Zero;
            }
            else
            {
                _effect.EmissiveColor = Vector3.Zero;
                _effect.AmbientLightColor = new Vector3(0.06f);
                _effect.DiffuseColor = body.Colour.ToVector3();
            }

            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _sphereIndexCount / 3);
            }

            if (clouds is not null && !body.Emissive)
            {
                // The cloud deck, as a sphere a fifth of a per cent larger carrying the baked
                // coverage map as its alpha. It is drawn immediately after its body so painter's
                // order still holds, and blended non-premultiplied because the mask is a plain
                // alpha rather than a multiplied one — before that it was a solid white shell.
                _effect.Texture = clouds;
                _effect.TextureEnabled = true;
                _effect.DiffuseColor = Vector3.One;
                _effect.World = Matrix.CreateScale((float)body.RadiusKilometres * scale * 1.002f)
                    * orientation
                    * Matrix.CreateTranslation(centre);

                _device.BlendState = BlendState.NonPremultiplied;

                foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _device.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList, 0, 0, _sphereIndexCount / 3);
                }

                _device.BlendState = BlendState.Opaque;
            }



            if (Verbose)
            {
                Console.WriteLine(
                    $"  {body.Name,-8} distance {distance,16:N0} km  "
                    + $"angular radius {angularRadius * 180.0 / Math.PI,7:F3} deg  "
                    + $"facing {facing,7:F4}  centre ({centre.X,12:F3},{centre.Y,12:F3},{centre.Z,12:F3})  "
                    + $"drawn");
            }

            drawn++;
        }

        BodiesDrawn = drawn;
        _effect.DiffuseColor = Vector3.One;
    }

    public void Dispose()
    {
        _sphere.Dispose();
        _sphereIndices.Dispose();

        foreach (Texture2D texture in _textures.Values)
        {
            texture?.Dispose();
        }

        _effect.Dispose();
    }
}
