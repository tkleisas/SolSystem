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
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly VertexBuffer _sphere;
    private readonly IndexBuffer _sphereIndices;
    private readonly int _sphereIndexCount;
    private readonly PlanetRenderer _planet;

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

        (VertexPositionNormalTexture[] vertices, short[] indices) = SphereMesh.Build(64, 32);

        _sphere = new VertexBuffer(
            device, VertexPositionNormalTexture.VertexDeclaration, vertices.Length,
            BufferUsage.WriteOnly);
        _sphere.SetData(vertices);

        _sphereIndices = new IndexBuffer(
            device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
        _sphereIndices.SetData(indices);
        _sphereIndexCount = indices.Length;

        // The Earth gets the shader; the rest keep the fixed-function sphere. Both use this mesh,
        // so the day map, the cloud deck and the city lights cannot drift apart.
        _planet = new PlanetRenderer(device, vertices, indices);
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
        string? CloudTexture,
        string? NightTexture = null);

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
            new Color(0.24f, 0.42f, 0.72f), false, "earth_albedo.jpg", "earth_clouds.png",
            "earth_night.jpg"));

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

        // The Earth's shader takes clip-space vertices, so the projection is done on the CPU.
        Matrix viewProjection = view * projection;

        // THE LIGHT'S DIRECTION IS WHERE IT IS GOING, NOT WHERE IT COMES FROM. MonoGame's
        // BasicEffect shader computes `dot(-lightDirection, normal)`, so it wants the direction
        // the light travels. Passing the direction towards the Sun lights the far hemisphere and
        // leaves the near one black — which is why the Earth read as a dark disc with a bright
        // rim on the wrong side. The parameter above is named for what it is, towards the Sun;
        // the negation belongs here, at the one place the convention changes.
        _effect.DirectionalLight0.Direction = -toSun;
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

            // The cull itself is in BodyCulling, where the frustum rule is stated and tested. The
            // short version: the visible cone is the wider half-angle plus the body's angular
            // radius, not half the vertical field of view clamped at ninety degrees.
            if (!BodyCulling.IsVisible(
                facing, angularRadius, _device.Viewport.AspectRatio, 60.0))
            {
                if (Verbose)
                {
                    Console.WriteLine($"  {body.Name,-8} culled: facing {facing:F4}");
                }

                continue;
            }

            var centre = new Vector3(
                (float)(offset.X.ToDouble() * scale),
                (float)(offset.Y.ToDouble() * scale),
                (float)(offset.Z.ToDouble() * scale));

            Texture2D? texture = TextureFor(body.TextureFile);
            Texture2D? clouds = TextureFor(body.CloudTexture);
            Texture2D? night = TextureFor(body.NightTexture);

            // The Earth is the one body with a night side worth lighting and an atmosphere worth
            // scattering, so it goes through Planet.fx. Everything else keeps the fixed-function
            // sphere; a missing day map falls back to that too.
            bool earth = body.Name == "Earth" && texture is not null;

            // Scale, then the body's own orientation, then place it.
            Matrix orientation = OrientationFor(body.Name, session.JulianDate);
            _effect.World = Matrix.CreateScale((float)body.RadiusKilometres * scale)
                * orientation
                * Matrix.CreateTranslation(centre);

            _device.SetVertexBuffer(_sphere);
            _device.Indices = _sphereIndices;

            if (earth)
            {
                _planet.DrawSurface(
                    _device,
                    centre,
                    (float)body.RadiusKilometres * scale,
                    orientation,
                    toSun,
                    viewProjection,
                    texture!,
                    night,
                    clouds);
            }
            else
            {
                _effect.Texture = texture;
                _effect.TextureEnabled = texture is not null;

                if (body.Emissive)
                {
                    // The Sun is where the light comes from, so it is drawn as its own colour rather
                    // than as a surface something else shines on — which means the EMISSIVE term.
                    //
                    // The obvious choice is to put the colour in AmbientLightColor, and it draws a black
                    // disc: the ambient term in this effect is AmbientLightColor × DiffuseColor, so a
                    // material with no diffuse colour is black however bright the ambient light is.
                    // The Sun was a black circle in the middle of its own glare for one iteration.
                    _effect.EmissiveColor = body.Colour.ToVector3();
                    _effect.AmbientLightColor = Vector3.Zero;
                    _effect.DiffuseColor = Vector3.Zero;
                }
                else
                {
                    _effect.EmissiveColor = Vector3.Zero;
                    _effect.AmbientLightColor = new Vector3(0.06f);

                    // A TEXTURED BODY IS ALREADY THE COLOUR IT SHOULD BE. The baked map carries the
                    // surface: the Earth's ocean and land, Mars's dust and dark albedo features, the
                    // Moon's maria. Multiplying that by the decorative fallback colour darkened every
                    // textured world — the Earth was blue-tinted into a night side that never saw day —
                    // so the tint is used only where there is no map to disagree with.
                    _effect.DiffuseColor = texture is not null
                        ? Vector3.One
                        : body.Colour.ToVector3();
                }

                foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _device.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList, 0, 0, _sphereIndexCount / 3);
                }
            }

            if (clouds is not null && !body.Emissive && !earth)
            {
                // The cloud deck, as a sphere a fifth of a per cent larger carrying the baked
                // coverage map as its alpha. It is drawn immediately after its body so painter's
                // order still holds, and blended non-premultiplied because the mask is a plain
                // alpha rather than a multiplied one — before that it was a solid white shell.
                //
                // The Earth's surface went through Planet.fx, so the BasicEffect's material state
                // may still describe the previous body; set it for the clouds explicitly.
                _device.SetVertexBuffer(_sphere);
                _device.Indices = _sphereIndices;

                _effect.Texture = clouds;
                _effect.TextureEnabled = true;
                _effect.EmissiveColor = Vector3.Zero;
                _effect.AmbientLightColor = new Vector3(0.06f);
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

            if (earth)
            {
                // The outer half of the atmosphere: a 2% shell, drawn after the clouds so a cloud
                // top at the limb is inside the haze rather than painted over it.
                _planet.DrawAtmosphere(
                    _device,
                    centre,
                    (float)body.RadiusKilometres * scale,
                    orientation,
                    toSun,
                    viewProjection);
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
        _planet.Dispose();

        foreach (Texture2D texture in _textures.Values)
        {
            texture?.Dispose();
        }

        _effect.Dispose();
    }
}
