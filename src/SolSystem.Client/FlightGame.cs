using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SolSystem.Core.Numerics;
using SolSystem.Core.Local;
using SolSystem.Core.Orbits;

namespace SolSystem.Client;

/// <summary>
/// The client: a window onto the sky, and a ship to fly in it.
/// </summary>
/// <remarks>
/// <para>
/// This is the phase 0 gate made runnable — <i>if flying this ship is not fun with nothing else
/// attached, nothing else rescues it</i> — and the honest way to test that is to be able to look at
/// it. So the client has two modes: an interactive window, and <c>--shot</c>, which renders one
/// frame to a PNG and exits.
/// </para>
/// <para>
/// The headless mode is what makes any of this checkable. A 3D scene is easy to believe and hard to
/// test; a frame is a file that can be looked at, and every claim this project makes about the sky
/// is a claim about what a frame should contain — the Milky Way through Sagittarius, the Sun 0.53
/// degrees across, the planets where the ephemeris puts them.
/// </para>
/// <para>
/// Nothing here simulates physics. The session comes from <c>SolSystem.Core</c>; this class turns it
/// into pixels and keystrokes.
/// </para>
/// </remarks>
internal sealed class FlightGame : Game
{
    /// <summary>
    /// Field of view, in degrees.
    /// </summary>
    /// <remarks>
    /// Sixty is a compromise and worth naming. Naked-eye astronomy is about a hundred and twenty —
    /// you see a lot of sky and everything is tiny. A spacecraft's forward view is narrower. Sixty
    /// puts the Moon at about a ninth of the frame height, which is roughly how it reads through a
    /// window, and leaves enough sky that the Milky Way is obviously a band rather than a smudge.
    /// </remarks>
    private const float FieldOfViewDegrees = 60.0f;

    /// <summary>
    /// Render units per kilometre.
    /// </summary>
    /// <remarks>
    /// A thousandth, so a unit is a thousand kilometres. The Earth is 6.4 units across and Neptune is
    /// four and a half million units away, which is a range a float handles without the Earth losing
    /// its surface to rounding. Drawing in kilometres would put the Earth at 6 378 and Neptune at
    /// 4.5 billion, and the second of those has a float resolution of five hundred kilometres.
    /// </remarks>
    private const float KilometresPerUnit = 0.001f;

    private readonly LaunchOptions _options;
    private readonly GraphicsDeviceManager _graphics;

    private FlightSession _session = null!;
    private SkyRenderer _sky = null!;
    private BodyRenderer _bodies = null!;
    private SunRenderer _sun = null!;
    private HullRenderer _hulls = null!;
    private Hull _courier = null!;
    private Hull _station = null!;
    private Hull _freighter = null!;
    private Flight _flight = null!;
    private SpriteBatch _sprites = null!;
    private Texture2D _pixel = null!;
    private SpriteFont _hud = null!;

    /// <summary>One navigation tick: 120 Hz, the rate the whole local frame was written for.</summary>
    private const double TickSeconds = 1.0 / 120.0;

    private KeyboardState _previousKeys;
    private double _simulatedSeconds;
    private int _frame;

    internal FlightGame(LaunchOptions options)
    {
        _options = options;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = options.Width,
            PreferredBackBufferHeight = options.Height,
            SynchronizeWithVerticalRetrace = true,
        };

        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        // A shot runs as fast as it can and exits; an interactive session is a game and should
        // behave like one.
        IsFixedTimeStep = !options.Headless;
        // ASCII ONLY in the window title, and it is not fussiness.
        //
        // This was "SolSystem — flight" with an em dash, and the title bar rendered it as
        // "SolSystem ⯑⯑⯑ flight": three replacement glyphs, because SDL takes the title
        // as UTF-8 and something between here and the window manager read those three bytes as three
        // Latin-1 characters. The in-game type is unaffected — it goes through a sprite font this
        // program built — but a title bar is not worth a fight with an encoding, and there is a
        // hyphen on every keyboard.
        Window.Title = "SolSystem - flight";
    }

    protected override void Initialize()
    {
        Window.AllowUserResizing = true;
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _session = FlightSession.Start(_options);
        _sprites = new SpriteBatch(GraphicsDevice);
        _sky = new SkyRenderer(GraphicsDevice, _sprites);
        _bodies = new BodyRenderer(
            GraphicsDevice, Path.Combine(FlightSession.RepositoryRoot(), "art", "textures"));

        _sun = new SunRenderer(GraphicsDevice, Content);
        _hulls = new HullRenderer(GraphicsDevice);

        string root = FlightSession.RepositoryRoot();
        _courier = Hull.Load(GraphicsDevice, Path.Combine(root, "art", "models", "ships",
            "illuminus_courier.glb"));

        _station = Hull.Load(GraphicsDevice, Path.Combine(root, "art", "models", "stations",
            "meridian.glb"));

        _freighter = Hull.Load(GraphicsDevice, Path.Combine(root, "art", "models", "ships",
            "workers_freighter.glb"));

        Console.WriteLine($"  courier: {Largest(_courier):F0} m, {_courier.Parts.Count} parts");
        Console.WriteLine($"  meridian: {Largest(_station):F0} m, {_station.Parts.Count} parts");
        Console.WriteLine($"  freighter: {Largest(_freighter):F0} m, {_freighter.Parts.Count} parts");

        // The player's ship starts on the station's docking corridor, co-orbiting with the station.
        //
        // THE LOCAL FRAME'S ORIGIN IS THE CENTRE OF THE EARTH. That is what makes gravity a single
        // point source at the origin, which is what `GravitySource.AtOrigin` means and what the
        // station's own orbital state is expressed in. An earlier version of this measured the ship
        // from the docking PORT instead — which is a perfectly good frame for the corridor, and the
        // frame the docking law works in — and then pointed the gravity source at the origin anyway.
        // The Earth's centre is six thousand seven hundred and seventy-eight kilometres from the
        // port, so the ship was being pulled towards the port as though the entire mass of the planet
        // were there: two and a half BILLION metres per second squared, and by the time anyone looked
        // at the display the ship was ten thousand kilometres a second and most of a million
        // kilometres away, which is what the range readout was showing.
        _flight = Flight.Start(
            _session.Station.Port.Position
                + (_session.Station.Port.Axis * Fix128.FromDouble(_options.Standoff)),
            _session.Station.Velocity,
            FacingAlong(-_session.Station.Port.Axis));

        // The body report is worth reading when a frame looks wrong, and noise otherwise, so it is
        // asked for rather than volunteered.
        if (_options.Headless && _options.Verbose)
        {
            BodyRenderer.Verbose = true;
        }
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _hud = Content.Load<SpriteFont>("Hud");

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keys = Keyboard.GetState();

        if (!_options.Headless)
        {
            Simulate(gameTime, keys);
        }

        _previousKeys = keys;

        // A bounded interactive run, for checking that the loop a player gets actually runs. It
        // goes through Update and Draw exactly as an unbounded one does; only the exit differs.
        if (_options.Frames > 0 && _frame >= _options.Frames)
        {
            Exit();
        }

        base.Update(gameTime);
    }

    /// <summary>
    /// Advances the world and reads the controls.
    /// </summary>
    /// <remarks>
    /// The clock runs off real time so that a session left open shows the Earth turning under the
    /// sky: one frame per sixtieth of a second at a rate of one is one simulated second per real
    /// second, and holding the time-rate key runs it up to an hour a second, which is fast enough to
    /// watch the Sun come round.
    /// </remarks>
    private void Simulate(GameTime gameTime, KeyboardState keys)
    {
        double rate = _options.TimeRate;

        if (keys.IsKeyDown(Keys.Up))
        {
            rate *= 60.0;
        }

        if (keys.IsKeyDown(Keys.Down))
        {
            rate *= 0.1;
        }

        double seconds = gameTime.ElapsedGameTime.TotalSeconds * rate;
        _simulatedSeconds += seconds;
        _session.Advance(seconds);

        // One tick of the simulation, at the rate the physics was written for. The clock runs at
        // whatever rate the player asked for; the ship always steps at 120 Hz, because a fixed step
        // is what makes the same inputs give the same flight.
        Command command = _flight.Read(keys, seconds);

        Span<GravitySource> sources = stackalloc GravitySource[1];
        sources[0] = _session.Station.GravitySource;

        int ticks = Math.Clamp((int)Math.Round(seconds / TickSeconds), 0, 240);
        for (int i = 0; i < ticks; i++)
        {
            _flight.Step(sources, TickSeconds, command);
        }

        // The camera is inside the ship, so the ship drives the observer and not the reverse. The
        // session wants an offset from the station's centre and the ship's position is measured from
        // the Earth's, so the station's own offset is what joins the two.
        _session.SetLocalOffset(
            _flight.Ship.Position - _session.Station.Offset,
            _flight.Ship.Velocity - _session.Station.Velocity,
            _session.Earth());

        if (JustPressed(keys, Keys.Escape))
        {
            Exit();
        }
    }

    private bool JustPressed(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

    protected override void Draw(GameTime gameTime)
    {
        // A render target is needed both for the headless shot and for a bounded interactive run
        // that was asked to save its last frame — the back buffer cannot be read back directly.
        RenderTarget2D? target = null;
        bool saving = _options.Headless
            || (_options.ShotPath is not null && _options.Frames > 0 && _frame >= _options.Frames - 1);

        if (saving)
        {
            target = new RenderTarget2D(
                GraphicsDevice, _options.Width, _options.Height, false,
                SurfaceFormat.Color, DepthFormat.Depth24);
            GraphicsDevice.SetRenderTarget(target);
        }

        GraphicsDevice.Clear(new Color(2, 3, 6));

        Matrix view = BuildView();

        // The far plane has to reach Neptune, which is four and a half thousand units away at this
        // scale, and the near plane has to let the camera sit inside the docking corridor — four
        // hundred metres, or four ten-thousandths of a unit. That is a hundred-million-to-one range
        // on the depth buffer and it is asking for z-fighting between the station and the Earth; the
        // fix, when it matters, is a second pass with its own near and far.
        Matrix projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(FieldOfViewDegrees),
            GraphicsDevice.Viewport.AspectRatio,
            0.0004f,
            5.0e6f);

        // The band first, in 3D with no depth, then the stars through the sprite batch on top of
        // it. Both are at infinity, so nothing occludes them and they occlude nothing, which is why
        // they can be drawn in this order without depth.
        _sky.DrawShell(view, projection);

        _sprites.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.AnisotropicClamp);
        _sky.DrawStars(_session, FieldOfViewDegrees);
        _sprites.End();

        // Then the bodies, so a planet occults the stars behind it and the Earth occults everything.
        _bodies.Draw(_session, view, projection, KilometresPerUnit);

        // The Sun after them: it is a source, drawn additively, and anything nearer should paint
        // over its glow rather than be painted over by it.
        _sun.Draw(_session, view, projection, KilometresPerUnit);

        // And the player's hull, seen from outside. A chase camera rather than a cockpit, because
        // there is no cockpit interior modelled and a hull you cannot see is a hull you cannot tell
        // is working.
        // Where the Sun is from the hull, for the light on it.
        Vector3 sunDirection = Vector3.Normalize(new Vector3(
            (float)(-_session.ObserverPosition.X.ToDouble() * KilometresPerUnit),
            (float)(-_session.ObserverPosition.Y.ToDouble() * KilometresPerUnit),
            (float)(-_session.ObserverPosition.Z.ToDouble() * KilometresPerUnit)));

        // The hull is drawn in its own frame, with its own projection, and that is not a
        // convenience — it is the only way the numbers work.
        //
        // The far pass has a unit of a thousand kilometres, because Neptune is four and a half
        // million of them away. A fifty-metre hull is five hundred-millionths of one of those, and
        // the near plane needed to keep the Earth sharp is four hundred metres, which would put the
        // ship behind the camera's own clipping plane. So the near pass uses a unit of one METRE,
        // the hull sits at its origin, and the background is already painted.
        Matrix close = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(FieldOfViewDegrees),
            GraphicsDevice.Viewport.AspectRatio,
            0.5f,
            2.0e5f);

        if (_options.Lineup)
        {
            // The lineup replaces the flying view rather than being drawn over it. Drawing both was
            // the first version, and the result was every asset superimposed on the one it was
            // supposed to be measured against.
            DrawLineup(close, sunDirection);
        }
        else
        {
            _hulls.Draw(_courier, ShipTransform(), ChaseCamera(), close, sunDirection);

            // The station, in the same metre-scale pass, positioned relative to the ship. This is the
            // frame that answers the only scale question that matters — whether the thing you are
            // flying looks right beside the thing you are flying to — and it is why the two are drawn
            // together rather than in separate passes at separate scales.
            _hulls.Draw(_station, StationTransform(), ChaseCamera(), close, sunDirection);
        }

        DrawHud();

        if (target is not null)
        {
            GraphicsDevice.SetRenderTarget(null);
            Save(target, _options.ShotPath!);
            target.Dispose();

            if (_options.Headless)
            {
                Exit();
            }
        }

        base.Draw(gameTime);
        _frame++;
    }

    /// <summary>
    /// The camera, built from the session's position and orientation.
    /// </summary>
    /// <remarks>
    /// The camera sits at the observer and looks along <see cref="FlightSession.Forward"/>. The
    /// translation is by the negative of the observer's position <em>relative to the Earth's
    /// centre</em> rather than its heliocentric position, because a float cannot resolve a metre at
    /// 1.5 × 10⁸ kilometres and the local frame is where everything being drawn already lives.
    /// </remarks>
    private Matrix BuildView()
    {
        // The camera is at the origin and everything drawn is expressed relative to it, which is what
        // keeps the precision: a body's position is computed by subtracting two heliocentric
        // positions and then scaled, rather than by putting a scaled absolute position through a
        // view matrix that would lose the Earth to rounding.
        var forward = new Vector3(
            (float)_session.Forward.X.ToDouble(),
            (float)_session.Forward.Y.ToDouble(),
            (float)_session.Forward.Z.ToDouble());

        var up = new Vector3(
            (float)_session.Up.X.ToDouble(),
            (float)_session.Up.Y.ToDouble(),
            (float)_session.Up.Z.ToDouble());

        return Matrix.CreateLookAt(Vector3.Zero, forward, up);
    }

    /// <summary>
    /// A minimal heads-up display: what the ship is doing, in text.
    /// </summary>
    /// <remarks>
    /// Drawn with rectangles and the default sprite font rather than with a font, because a content
    /// pipeline is a build step and this is a flight view. A block per line proves the layout and can
    /// be replaced by real text when there is a font to replace it with.
    /// </remarks>
    /// <summary>
    /// The heads-up display: what the ship is doing, in numbers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layout is a flight display rather than a game overlay, and the reason is the setting:
    /// every number on it is one a pilot would actually be watching, and there are six of them.
    /// </para>
    /// <para>
    /// <b>Delta-v is the one that matters.</b> It is the currency every manoeuvre in the game is
    /// priced in, and it is shown next to the propellant and the burn time it buys, because "six
    /// kilometres a second left" means nothing until it is also "three minutes of full throttle".
    /// A player who runs the tanks dry a hundred million kilometres from Ceres has made a decision,
    /// not suffered an accident, and the display should make that decision legible from the first
    /// frame rather than at the moment it becomes irreversible.
    /// </para>
    /// </remarks>
    private void DrawHud()
    {
        _sprites.Begin();

        // A reticle with a gap at the centre, and the gap is the point: a filled crosshair covers
        // whatever you are aiming at, and the first version of this hid the Sun behind its own
        // cross — which reads as a renderer that has not drawn the Sun.
        int cx = GraphicsDevice.Viewport.Width / 2;
        int cy = GraphicsDevice.Viewport.Height / 2;
        var reticle = new Color(120, 200, 140, 170);
        _sprites.Draw(_pixel, new Rectangle(cx - 14, cy - 1, 9, 2), reticle);
        _sprites.Draw(_pixel, new Rectangle(cx + 6, cy - 1, 9, 2), reticle);
        _sprites.Draw(_pixel, new Rectangle(cx - 1, cy - 14, 2, 9), reticle);
        _sprites.Draw(_pixel, new Rectangle(cx - 1, cy + 6, 2, 9), reticle);

        // The frame, so the field of view is legible and the corners are not empty.
        var edge = new Color(60, 90, 110, 140);
        _sprites.Draw(_pixel, new Rectangle(0, 0, GraphicsDevice.Viewport.Width, 1), edge);
        _sprites.Draw(_pixel, new Rectangle(0, GraphicsDevice.Viewport.Height - 1,
            GraphicsDevice.Viewport.Width, 1), edge);
        _sprites.Draw(_pixel, new Rectangle(0, 0, 1, GraphicsDevice.Viewport.Height), edge);
        _sprites.Draw(_pixel, new Rectangle(GraphicsDevice.Viewport.Width - 1, 0, 1,
            GraphicsDevice.Viewport.Height), edge);

        DrawFlightPanel();

        _sprites.End();
    }

    private void DrawFlightPanel()
    {
        // Speed relative to the station, which is the number that matters for a docking and the one
        // that reads zero when the ship is holding station. Its speed relative to the EARTH is seven
        // and a half kilometres a second and always will be, because that is what being in orbit is.
        double speed = (_flight.Ship.Velocity - _session.Station.Velocity).Length.ToDouble();

        // Range from the ship to the docking port, both measured from the Earth's centre.
        double range = (_flight.Ship.Position - _session.Station.Port.Position).Length.ToDouble();

        // And the same thing along the corridor, signed: positive is outside the port, negative is
        // past it. A range alone cannot tell a pilot which side of the dock they are on.
        double along = Dot(_flight.Ship.Position - _session.Station.Port.Position,
            _session.Station.Port.Axis).ToDouble();

        var ink = new Color(150, 220, 175);
        var dim = new Color(110, 150, 135);
        var warn = new Color(230, 170, 90);

        var at = new Vector2(28f, 26f);
        const float Line = 21f;

        _sprites.DrawString(_hud, "ILLUMINUS COURIER  \u00b7  MERIDIAN", at, dim);
        at.Y += Line * 1.6f;

        _sprites.DrawString(_hud, $"SPEED      {speed,10:F1} m/s", at, ink);
        at.Y += Line;
        _sprites.DrawString(_hud, $"RANGE      {range,10:F0} m", at, ink);
        at.Y += Line;
        _sprites.DrawString(_hud, $"ON CORRIDOR{along,10:F0} m", at, dim);
        at.Y += Line;

        // The clock, because the sky turns and the player should be able to see it turn.
        _sprites.DrawString(_hud, $"EPOCH JD   {_session.JulianDate,10:F4}", at, dim);
        at.Y += Line * 1.6f;

        // Delta-v first among the propellant figures, and deliberately: it is the one that decides
        // where the ship can go, and the other two are ways of saying the same thing.
        double deltaV = _flight.DeltaV;
        double seconds = _flight.FullThrottleSeconds;

        _sprites.DrawString(_hud, $"DELTA-V    {deltaV,10:F1} m/s", at,
            deltaV < 2000.0 ? warn : ink);
        at.Y += Line;

        _sprites.DrawString(_hud, $"PROPELLANT {_flight.Propellant,10:F2} t", at,
            _flight.PropellantFraction < 0.05 ? warn : ink);
        at.Y += Line;

        // Burn time as hours and minutes, because four hundred seconds and four hours are the same
        // number to a reader and very different facts to a pilot.
        // Days past a day, because a torch burn measured in hours runs to four digits and stops
        // meaning anything. This is not a manoeuvre, it is a cruise.
        string burn = seconds switch
        {
            > 172800.0 => $"{seconds / 86400.0,9:F1} days",
            > 3600.0 => $"{seconds / 3600.0,9:F1} h",
            _ => $"{seconds / 60.0,9:F1} min",
        };

        _sprites.DrawString(_hud, $"BURN       {burn} at full", at, ink);
        at.Y += Line;

        DrawThrottle(at, ink, dim);
    }

    /// <summary>The throttle, as a bar, because a number is the wrong shape for a setting.</summary>
    private void DrawThrottle(Vector2 at, Color ink, Color dim)
    {
        at.Y += 14f;
        _sprites.DrawString(_hud, "THROTTLE", at, dim);

        var track = new Rectangle((int)at.X + 96, (int)at.Y + 3, 220, 12);
        _sprites.Draw(_pixel, track, new Color(30, 44, 40));

        var fill = new Rectangle(track.X, track.Y, (int)(track.Width * _flight.Throttle), track.Height);
        _sprites.Draw(_pixel, fill, ink);

        _sprites.DrawString(_hud, $"{_flight.Throttle * 100.0,5:F0}%", 
            new Vector2(track.Right + 12, at.Y), ink);
    }

    /// <summary>
    /// An attitude whose nose points along a direction.
    /// </summary>
    /// <remarks>
    /// A rotation from the hull's own +x to the direction asked for, about the axis perpendicular to
    /// both. The degenerate case — asked to face exactly backwards — has no perpendicular axis, and
    /// is answered with a half turn about the deck rather than with a NaN.
    /// </remarks>
    private static Attitude FacingAlong(Fix128Vec direction)
    {
        Fix128Vec nose = direction.Normalized();
        var reference = new Fix128Vec(Fix128.One, Fix128.Zero, Fix128.Zero);

        Fix128Vec axis = FlightSession.Cross(reference, nose);

        double cross = axis.Length.ToDouble();
        double dot = ((reference.X * nose.X) + (reference.Y * nose.Y) + (reference.Z * nose.Z))
            .ToDouble();

        if (cross < 1e-9)
        {
            // Already facing that way, or facing exactly away. Facing away is a half turn about any
            // perpendicular axis, and the deck's up is the one that keeps the ship the right way up.
            return dot > 0.0
                ? new Attitude(Fix128Vec.Zero, Fix128Vec.Zero)
                : new Attitude(new Fix128Vec(
                    Fix128.Zero, Fix128.Zero, Fix128.FromDouble(Math.PI)), Fix128Vec.Zero);
        }

        // The angle between two unit vectors, as a double. This runs once, when a session starts,
        // and is a camera framing decision rather than a simulation one — so it does not need the
        // fixed-point trigonometry the physics gets.
        double angle = Math.Atan2(cross, dot);
        return new Attitude(axis * Fix128.FromDouble(angle / cross), Fix128Vec.Zero);
    }

    /// <summary>
    /// The chase camera: behind the hull and a little above it, looking at it.
    /// </summary>
    /// <remarks>
    /// Distances are in render units, where a unit is a thousand kilometres, so the offsets below are
    /// in hundreds of metres. The hull is fifty metres long, which at this scale is five hundredths
    /// of a unit — small enough that the near plane matters more than the position does.
    /// </remarks>
    private Matrix ChaseCamera()
    {
        Vector3 nose = Unit(_flight.Ship.Attitude.Forward);
        Vector3 up = Unit(_flight.Ship.Attitude.Rotate(
            new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One)));

        // A chase camera built from the ship's own up as well as its forward, so that rolling the
        // hull rolls the view with it. Anchoring it to a world axis instead makes a barrel roll look
        // like the sky turning, which is disorienting in a way that is hard to attribute.
        Vector3 eye = (-nose * ChaseDistance) + (up * ChaseLift);

        // Aimed slightly ahead of the hull, which puts the ship low in the frame and the direction
        // of travel in the middle of it — the same framing a racing game uses, for the same reason.
        return Matrix.CreateLookAt(eye, nose * ChaseLead, up);
    }

    /// <summary>
    /// Where the player's hull is and which way it points, in render space.
    /// </summary>
    /// <remarks>
    /// The hull's own axes are its nose, its deck's up and the cross of the two, which is the frame
    /// the models were built in — so this is the attitude rotated from the ship's frame into the
    /// world's, and nothing else.
    /// </remarks>
    private Matrix ShipTransform()
    {
        Attitude attitude = _flight.Ship.Attitude;

        Vector3 nose = Unit(attitude.Forward);
        Vector3 up = Unit(attitude.Rotate(new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One)));
        Vector3 side = Unit(attitude.Rotate(new Fix128Vec(Fix128.Zero, Fix128.One, Fix128.Zero)));

        // The hull sits at the origin of its own pass: the camera goes to it rather than it coming
        // to the camera, which keeps a fifty-metre ship at a scale a float resolves.
        return new Matrix(
            nose.X, nose.Y, nose.Z, 0f,
            side.X, side.Y, side.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            0f, 0f, 0f, 1f);
    }

    /// <summary>
    /// Where the station is and which way it points, relative to the ship, in metres.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every model in this project has its docking port or its engine plane at its origin and its
    /// long axis along the model's own +Y — the ships because Blender's z-up becomes y-up on export,
    /// and the station because it is rotated to match. So the transform here is entirely a matter of
    /// where the port axis goes, and the other two axes are free: the station is a wheel and turns
    /// about its spindle, so any perpendicular pair will do.
    /// </para>
    /// <para>
    /// The first version of this put the station's spindle on x, which is the axis the *builder* laid
    /// it out on, and the client drew a 2 km wheel edge-on as a vertical sliver.
    /// </para>
    /// </remarks>
    private Matrix StationTransform()
    {
        Vector3 forward = Unit(_session.Station.Port.Axis);

        Vector3 seed = MathF.Abs(forward.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 side = Vector3.Normalize(Vector3.Cross(forward, seed));
        Vector3 up = Vector3.Cross(side, forward);

        Vector3 offset = Unit(_session.Station.Port.Position - _flight.Ship.Position);

        return new Matrix(
            side.X, side.Y, side.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            offset.X, offset.Y, offset.Z, 1f);
    }

    /// <summary>
    /// Every asset, at its true size, side by side.
    /// </summary>
    /// <remarks>
    /// Laid out nose to tail along the view's right axis and all at the same distance from the
    /// camera, which is the only arrangement in which relative size is readable. The separation is
    /// half the largest asset, so nothing overlaps and the gaps are obviously gaps.
    /// </remarks>
    private void DrawLineup(Matrix projection, Vector3 sunDirection)
    {
        Vector3 nose = Unit(_flight.Ship.Attitude.Forward);
        Vector3 up = Unit(_flight.Ship.Attitude.Rotate(
            new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One)));
        Vector3 right = Vector3.Normalize(Vector3.Cross(nose, up));

        (Hull Hull, string Name)[] assets =
        [
            (_courier, "courier"),
            (_freighter, "freighter"),
            (_station, "meridian"),
        ];

        // EVERY MODEL IS LONG ALONG ITS OWN +Y, so the lineup rotation is the one that puts the
        // model's +Y on the screen's right axis — which makes every asset broadside to the camera
        // and measured along the same direction. None is foreshortened into looking smaller.
        //
        // The first version used CreateRotationY(90 degrees), which is a rotation ABOUT y and
        // therefore leaves y exactly where it was: every asset stayed pointed at the sky and the
        // whole row rendered edge-on as a set of vertical slivers.
        Vector3 row0 = -nose;
        Vector3 row1 = right;
        Vector3 row2 = Vector3.Cross(row0, row1);

        Matrix facing = new(
            row0.X, row0.Y, row0.Z, 0f,
            row1.X, row1.Y, row1.Z, 0f,
            row2.X, row2.Y, row2.Z, 0f,
            0f, 0f, 0f, 1f);

        const float Gap = 120f;
        float span = assets.Sum(a => Largest(a.Hull)) + (Gap * (assets.Length - 1));

        // The camera goes far enough back to hold the whole row. Derived from the span rather than
        // fixed, because the span went from 700 m to 2 800 m when the station was rescaled and a
        // fixed distance silently framed two thirds of it.
        float distance = span * 0.85f;

        // Centred: the row is built outward from the middle so that the largest thing, which is the
        // one being judged, sits on the axis.
        float at = -span * 0.5f;

        foreach ((Hull hull, string name) in assets)
        {
            float size = Largest(hull);
            Vector3 centre = (nose * distance) + (right * (at + (size * 0.5f)));

            _hulls.Draw(hull, facing * Matrix.CreateTranslation(centre), ChaseCamera(), projection,
                sunDirection);

            Console.WriteLine($"  lineup: {name,-10} {size,7:F0} m wide, centred at {at + (size * 0.5f),8:F0} m");
            at += size + Gap;
        }

        Console.WriteLine($"  lineup: span {span:F0} m, camera at {distance:F0} m");
    }

    /// <summary>The longest dimension of a hull, in metres.</summary>
    private static float Largest(Hull hull) =>
        MathF.Max(hull.Size.X, MathF.Max(hull.Size.Y, hull.Size.Z));

    /// <summary>How far behind and above the hull the chase camera sits, in metres.</summary>
    private const float ChaseDistance = 130f;
    private const float ChaseLift = 42f;
    private const float ChaseLead = 40f;

    private static Fix128 Dot(Fix128Vec a, Fix128Vec b) =>
        (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static Vector3 Unit(Fix128Vec v) => new(
        (float)v.X.ToDouble(), (float)v.Y.ToDouble(), (float)v.Z.ToDouble());

    private static void Save(Texture2D texture, string path)
    {
        string full = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        string? directory = Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = File.Create(full);
        texture.SaveAsPng(stream, texture.Width, texture.Height);
        Console.WriteLine($"wrote {full} ({texture.Width}×{texture.Height})");
    }

    protected override void UnloadContent()
    {
        _sky.Dispose();
        _bodies.Dispose();
        _sun.Dispose();
        _hulls.Dispose();
        _courier.Dispose();
        _station.Dispose();
        _freighter.Dispose();
        _sprites.Dispose();
        _pixel.Dispose();
        base.UnloadContent();
    }
}
