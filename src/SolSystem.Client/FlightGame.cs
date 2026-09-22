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

    /// <summary>The line height every panel draws on.</summary>
    private const float Line = 21f;

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
    private readonly Camera _camera = new();
    private Plume _plume = null!;
    private Chart _chart = null!;
    private NavOverlay _nav = null!;
    private CorridorGates _gates = null!;
    private Autohelm _helm;
    private bool _chartOpen;
    private readonly List<TransferOption> _courses = new();
    private int _courseIndex;

    /// <summary>Which body the course list was built for, so it is not rebuilt every frame.</summary>
    private Ephemeris.Body? _selectedFor;
    private MouseState _previousMouse;

    /// <summary>The attitude the run started with, so a held key can be measured against it.</summary>
    private Fix128Vec _startNose;
    private Fix128Vec _startDeck;

    /// <summary>Pixels dragged and notches scrolled, cumulative, for the display.</summary>
    private double _dragPixels;
    private double _wheelNotches;

    /// <summary>The compression in force, for the display.</summary>
    private double _timeRate = 1.0;

    /// <summary>One navigation tick: 120 Hz, the rate the whole local frame was written for.</summary>
    private const double TickSeconds = 1.0 / 120.0;

    /// <summary>
    /// The step a headless render advances the clock by, so that frame N is at N/60 of a second.
    /// </summary>
    /// <remarks>
    /// Headless mode skips <see cref="Simulate"/> entirely, which was right for a still frame and
    /// wrong for anything animated: the navigation lights are on a flash schedule, and with the
    /// clock pinned at zero every frame showed every light lit. The readout said "5 lit" at frame 2,
    /// frame 40 and frame 90, which is three measurements of the same instant.
    /// </remarks>
    private const double ShotSeconds = 1.0 / 60.0;

    private KeyboardState _previousKeys;
    private double _simulatedSeconds;
    private int _frame;

    /// <summary>
    /// The time compression ladder.
    /// </summary>
    /// <remarks>
    /// A LADDER, stepped one rung per press, rather than a multiplier held down. The first version
    /// multiplied the rate by sixty for as long as the key was held, which has two faults: there is
    /// no way to ask for twice, and there is no way to know what you got, because nothing displayed
    /// it. A player reported exactly that. Five rungs is a range from slow enough to watch a docking
    /// to fast enough to cross to Jupiter, and every one of them fits on the display.
    /// </remarks>
    private static readonly double[] TimeRates = [0.1, 1.0, 10.0, 100.0, 1000.0];

    private int _timeRateIndex = 1;

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

        _sun = new SunRenderer(GraphicsDevice);
        _hulls = new HullRenderer(GraphicsDevice);
        _plume = new Plume(GraphicsDevice, _sprites);
        _chart = new Chart(GraphicsDevice, _sprites);

        string root = FlightSession.RepositoryRoot();
        _courier = Hull.Load(GraphicsDevice, Path.Combine(root, "art", "models", "ships",
            "illuminus_courier.glb"));

        _station = Hull.Load(GraphicsDevice, Path.Combine(root, "art", "models", "stations",
            "meridian.glb"));

        _freighter = Hull.Load(GraphicsDevice, Path.Combine(root, "art", "models", "ships",
            "workers_freighter.glb"));

        Console.WriteLine($"  courier: {Largest(_courier):F0} m, {_courier.Parts.Count} parts");
        if (_options.Verbose)
        {
            foreach (Hull.Part part in _courier.Parts)
            {
                Console.WriteLine($"    part {part.Name,-28} material {part.Material.Name,-24} "
                    + $"emissive {part.Material.EmissiveFactor}");
            }
        }
        Console.WriteLine($"  meridian: {Largest(_station):F0} m, {_station.Parts.Count} parts");
        Console.WriteLine($"  freighter: {Largest(_freighter):F0} m, {_freighter.Parts.Count} parts");


        _camera.Use(_options.Camera);
        _camera.StartAt(_options.CameraYaw, _options.CameraPitch, _options.CameraDistance);
        _chartOpen = _options.Chart;

        if (_options.Destination.Length > 0
            && Enum.TryParse(_options.Destination, ignoreCase: true, out Ephemeris.Body chosen))
        {
            _chart.Selected = chosen;
        }

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

        _flight.SetThrottle(_options.Throttle);

        if (_options.Hold.Length > 0)
        {
            _startNose = _flight.Ship.Attitude.Forward;
            _startDeck = _flight.Ship.Attitude.Rotate(
                new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One));

            Console.WriteLine($"  holding {_options.Hold}");
            Console.WriteLine($"    at start: nose {Describe(_startNose)}  deck {Describe(_startDeck)}");
        }

        // The body report is worth reading when a frame looks wrong, and noise otherwise, so it is
        // asked for rather than volunteered.
        if (_options.Headless && _options.Verbose)
        {
            BodyRenderer.Verbose = true;
        }
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _hud = Content.Load<SpriteFont>("Hud");
        _nav = new NavOverlay(_sprites, _pixel, _hud);
        _gates = new CorridorGates(_sprites, _pixel);

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keys = KeysWithHolds(Keyboard.GetState());
        MouseState mouse = Mouse.GetState();

        // The time compression, read here rather than inside the interactive path, so that a
        // headless run can be told to hold Up and the ladder can be checked. It could not be,
        // before, and an untestable control is an unverified one.
        ReadTimeCompression(keys);

        if (JustPressed(keys, Keys.M))
        {
            _chartOpen = !_chartOpen;
        }

        if (_chartOpen)
        {
            ReadChart(keys, mouse);
        }

        if (_options.Headless)
        {
            // No keyboard, and a fixed step, so that frame N is at N/60 of a second and two renders
            // of the same frame are the same image. The compression still applies to it.
            double step = ShotSeconds * _timeRate;

            _simulatedSeconds += step;
            _session.Advance(step);

            // The ship is always on the clock, held keys or not: since the station genuinely
            // orbits, a ship left unsimulated is a ship the station leaves behind at 7.7 km/s,
            // and a verification shot of that is a picture of a lie. The hold only adds keys.
            SimulateTicks(keys, step);
        }
        else
        {
            ReadCamera(keys, mouse, gameTime.ElapsedGameTime.TotalSeconds);
            Simulate(gameTime, keys);
        }

        _previousKeys = keys;
        _previousMouse = mouse;

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
    /// <summary>
    /// Steps the ship, at the rate the physics was written for.
    /// </summary>
    /// <remarks>
    /// The clock runs at whatever rate the player asked for; the ship always steps at 120 Hz,
    /// because a fixed step is what makes the same inputs give the same flight. The number of ticks
    /// is capped, so at high time compression on a slow machine the clock and the hull come apart —
    /// see the note in the README.
    /// </remarks>
    private void SimulateTicks(KeyboardState keys, double seconds)
    {
        Span<GravitySource> sources = stackalloc GravitySource[1];
        sources[0] = _session.Station.GravitySource;

        int ticks = Math.Clamp((int)Math.Round(seconds / TickSeconds), 0, 240);

        if (_helm.Engaged)
        {
            // The computer has the controls. The pilot's keys are ignored rather than blended, so
            // there is never a question of who is flying.
            for (int i = 0; i < ticks; i++)
            {
                _helm.Step(ref _flight.Ship, sources, Fix128.FromDouble(TickSeconds));
            }

            return;
        }

        Command command = _flight.Read(keys, seconds);

        for (int i = 0; i < ticks; i++)
        {
            _flight.Step(sources, TickSeconds, command);
        }
    }

    /// <summary>
    /// The camera's own controls, which are separate from the ship's.
    /// </summary>
    /// <remarks>
    /// Dragging with the left button orbits; the wheel zooms; <c>C</c> changes mode. Holding the
    /// button rather than using raw mouse movement is deliberate — a space game where looking away
    /// from the window spins the ship's view is a space game that cannot be paused by looking away
    /// from it.
    /// </remarks>
    /// <summary>
    /// The keyboard the ship sees: the real one, plus anything <c>--hold</c> asked for.
    /// </summary>
    /// <remarks>
    /// A headless run has no window to press, so a held key is pressed here instead. This goes in
    /// through the same <see cref="KeyboardState"/> the ship already reads, which means the test
    /// exercises the real control path rather than a parallel one written for testing.
    /// </remarks>
    private KeyboardState KeysWithHolds(KeyboardState keys)
    {
        if (_options.Hold.Length == 0 || !_options.Headless)
        {
            return keys;
        }

        var pressed = new List<Keys>();

        // Comma-separated, and each token is either a single character or a key's own name — so
        // `--hold D` is the letter and `--hold Up` is the arrow. Single characters only was the
        // first version, and it meant the letter U rather than the Up arrow, which is a difference
        // that silently tests nothing at all.
        foreach (string token in _options.Hold.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse(token, ignoreCase: true, out Keys key))
            {
                pressed.Add(key);
            }
            else
            {
                Console.WriteLine($"  note: '{token}' is not a key");
            }
        }

        return new KeyboardState([.. pressed]);
    }

    private void ReadCamera(KeyboardState keys, MouseState mouse, double seconds)
    {
        float dx = mouse.X - _previousMouse.X;
        float dy = mouse.Y - _previousMouse.Y;

        // LOOK with the left button, ORBIT with the right. They were both the left one, and the
        // left one was the orbit — so a drag swung the camera round the hull instead of turning the
        // view, which reads as the ship rotating.
        if (mouse.LeftButton == ButtonState.Pressed
            && _previousMouse.LeftButton == ButtonState.Pressed)
        {
            _camera.Look(dx, dy);
            _dragPixels += Math.Abs(dx) + Math.Abs(dy);
        }

        if (mouse.RightButton == ButtonState.Pressed
            && _previousMouse.RightButton == ButtonState.Pressed)
        {
            _camera.Orbit(dx, dy);
            _dragPixels += Math.Abs(dx) + Math.Abs(dy);
        }

        int notches = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (notches != 0)
        {
            _camera.Zoom(notches / 120f);
            _wheelNotches += Math.Abs(notches) / 120.0;
        }

        if (JustPressed(keys, Keys.C))
        {
            _camera.Next();
        }

        _ = seconds;
    }

    /// <summary>Steps the compression ladder, one rung per press.</summary>
    private void ReadTimeCompression(KeyboardState keys)
    {
        if (JustPressed(keys, Keys.Up))
        {
            _timeRateIndex = Math.Min(_timeRateIndex + 1, TimeRates.Length - 1);
        }

        if (JustPressed(keys, Keys.Down))
        {
            _timeRateIndex = Math.Max(_timeRateIndex - 1, 0);
        }

        _timeRate = TimeRates[_timeRateIndex] * _options.TimeRate;
    }

    /// <summary>
    /// The chart's controls, which have the keyboard while it is open.
    /// </summary>
    /// <remarks>
    /// The thumbstick is deliberately ignored: the keyboard has focus. That was true when the
    /// gamepad was added to this client too, which is why nothing in the code below reads one.
    /// </remarks>
    private void ReadChart(KeyboardState keys, MouseState mouse)
    {
        if (JustPressed(keys, Keys.Tab))
        {
            _chart.NextBody();
            _courses.Clear();
        }

        int notches = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (notches != 0)
        {
            _chart.AdjustZoom(notches / 120f);
        }

        // A click picks the body under it, which is what a person tries first.
        if (mouse.LeftButton == ButtonState.Pressed
            && _previousMouse.LeftButton == ButtonState.Released)
        {
            if (_chart.Nearest(new Vector2(mouse.X, mouse.Y)) is Ephemeris.Body hit)
            {
                _chart.Selected = hit;
                _courses.Clear();
            }
        }

        // Up and down move through the courses rather than the time compression, which has no
        // meaning while the clock is not being watched.
        if (JustPressed(keys, Keys.Down))
        {
            _courseIndex = Math.Min(_courseIndex + 1, Math.Max(_courses.Count - 1, 0));
        }

        if (JustPressed(keys, Keys.Up))
        {
            _courseIndex = Math.Max(_courseIndex - 1, 0);
        }

        if (JustPressed(keys, Keys.Enter) && _chart.Selected is Ephemeris.Body destination)
        {
            Engage(destination);
        }
    }

    /// <summary>
    /// Hands the controls to the flight computer for the chosen course.
    /// </summary>
    private void Engage(Ephemeris.Body destination)
    {
        var system = new SolarSystem();
        system.SetTime((_session.JulianDate - Ephemeris.J2000JulianDate) * 86400.0);

        TransferOption option = _courses.Count > 0
            ? _courses[Math.Clamp(_courseIndex, 0, _courses.Count - 1)]
            : default;

        Fix128 throttle = option.Name == "ECONOMY"
            ? Fix128.FromDouble(0.25)
            : Fix128.One;

        // A torch crossing is steered by the computer; a ballistic one is not flown here at all,
        // because it needs a launch window and two timed impulses and this does neither. Engaging on
        // a ballistic course hands over the heading and leaves the pilot the timing.
        if (option.Name == "BALLISTIC")
        {
            Console.WriteLine("  ballistic courses are not flown by this computer; "
                + "the plan is a heading and a window, not an autopilot");
            return;
        }

        // THE TARGET HAS TO BE IN THE SHIP'S OWN FRAME. The ship is in the Earth-centred local frame
        // and the ephemeris is heliocentric, so the destination is re-expressed as an offset from the
        // Earth — which is also the physically right thing, because what the ship has to cross first
        // is the distance from where it is to where the planet will be.
        Ephemeris.State earth = system.Heliocentric(Ephemeris.Body.Earth);
        Fix128Vec targetHere = (system.Heliocentric(destination).Position - earth.Position)
            * Fix128.FromDouble(1000.0);        // km to metres

        // And refuse if the drive cannot beat the local gravity, rather than flying a course that
        // ends with the ship falling out of orbit.
        double radiusKm = _flight.Ship.Position.Length.ToDouble() / 1000.0;
        double gmKm = _session.Station.GmMetres.ToDouble() / 1e9;

        double acceleration = _flight.Ship.Engine.MaxAccelerationInMetresPerSecondSquared.ToDouble();

        if (FlightPlan.ThrustToGravity(gmKm, radiusKm, acceleration) < 1.0)
        {
            Console.WriteLine(
                "  cannot engage: the drive is weaker than the local gravity. Spiral out first.");
            return;
        }

        _helm = Autohelm.To(targetHere, Fix128.FromDouble(2_000_000.0), throttle);

        _chartOpen = false;

        Console.WriteLine($"  autohelm engaged: {destination}, {option.Name}, "
            + $"{option.Seconds / 86400.0:F1} days, {option.DeltaV / 1000.0:F1} km/s");
    }

    private void Simulate(GameTime gameTime, KeyboardState keys)
    {
        double seconds = gameTime.ElapsedGameTime.TotalSeconds * _timeRate;
        _simulatedSeconds += seconds;
        _session.Advance(seconds);

        SimulateTicks(keys, seconds);

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
        // A render target is needed both for a one-shot render and for a bounded run that was asked
        // to save its last frame — the back buffer cannot be read back directly.
        //
        // The frame test is an EQUALITY, so the frame is saved exactly once. With `>=` the save
        // happened on every frame from the trigger onwards, and a run that produced one image wrote
        // it sixty times a second until it exited.
        RenderTarget2D? target = null;
        bool saving = _options.OneShot
            || (_options.ShotPath is not null && _options.Frames > 0 && _frame == _options.Frames - 1);

        if (saving)
        {
            target = new RenderTarget2D(
                GraphicsDevice, _options.Width, _options.Height, false,
                SurfaceFormat.Color, DepthFormat.Depth24);
            GraphicsDevice.SetRenderTarget(target);
        }

        GraphicsDevice.Clear(new Color(2, 3, 6));

        if (_chartOpen)
        {
            DrawChart();
            FinishFrame(target, gameTime);
            return;
        }

        // THE CAMERA IS BUILT FIRST, BECAUSE BOTH PASSES NEED IT.
        //
        // The near pass -- the hull, the station, the plume -- has always used the camera. The far
        // pass -- the sky shell, the Sun, the Earth and the other bodies -- used `BuildView`, which
        // reads the SESSION's forward and up: a direction fixed when the session started and never
        // changed since.
        //
        // So dragging turned the ship and the station while the Earth, the Sun and the Milky Way
        // stayed exactly where they were. Reported twice, and the second report is the diagnosis:
        //
        //     "I see translation movement for the ship (not earth)"
        //     "I see rotation movement for the ship (not earth again)"
        //
        // A view is one thing. Two passes that disagree about where the camera is looking are not a
        // rendering bug that looks like a physics bug, they are a world split in half.
        (Matrix nearView, Fix128Vec cameraForward, Fix128Vec cameraUp) =
            _camera.Build(NoseVector(), DeckVector(), PortOffset(), Largest(_courier));

        Matrix view = Matrix.CreateLookAt(
            Vector3.Zero, Unit(cameraForward), Unit(cameraUp));

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

        // THE CAMERA IS BUILT ONCE AND USED FOR EVERYTHING. The star projection needs the same
        // basis the view matrix was made from — the camera's, not the ship's — and building it twice
        // is how the two drifted apart in the first place.
        _sprites.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.AnisotropicClamp);
        _sky.DrawStars(_session, cameraForward, cameraUp, FieldOfViewDegrees);
        _sprites.End();

        // Then the bodies, so a planet occults the stars behind it and the Earth occults everything.
        _bodies.Draw(_session, cameraForward, view, projection, KilometresPerUnit);

        // The Sun after them: it is a source, drawn additively, and anything nearer should paint
        // over its glow rather than be painted over by it.
        _sun.Draw(_session, cameraForward, cameraUp, view, projection, KilometresPerUnit);

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
            _hulls.Seconds = _simulatedSeconds;
        _hulls.BeginFrame();
        _hulls.Draw(_courier, ShipTransform(), nearView, close, sunDirection,
            EarthDirection(), Earthshine());

            // The station, in the same metre-scale pass, positioned relative to the ship. This is the
            // frame that answers the only scale question that matters — whether the thing you are
            // flying looks right beside the thing you are flying to — and it is why the two are drawn
            // together rather than in separate passes at separate scales.
            // The station's lights are on the station's own clock, which is the same one.
            _hulls.Draw(_station, StationTransform(), nearView, close, sunDirection,
                EarthDirection(), Earthshine());
        }

        // The plume, in the same near pass as the hull, and after it so it draws over the engine
        // bells rather than behind them.
        if (!_options.Lineup)
        {
            _sprites.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.AnisotropicClamp);
            _plume.Draw(nearView, close, NoseVector(), _flight.Throttle, 220f);
            _sprites.End();
        }

        if (_chartOpen)
        {
            // The chart replaces everything. It is drawn last and over an already-cleared frame,
            // which is the cheapest way to have two views that do not have to agree about scale:
            // the flying view has a unit of a thousand kilometres, the chart has one of thirty
            // astronomical units, and nothing needs to reconcile them.
            GraphicsDevice.Clear(new Color(4, 6, 12));
            DrawChartScreen();
        }
        else
        {
            // The nav markers read the same camera basis the passes were rendered with, so a
            // marker sits on the thing it names in every camera mode. The lineup is a measuring
            // bench rather than a view of the sky, and nothing in it is anywhere, so it gets none.
            // The corridor gates follow the same rule, for the same reason.
            if (!_options.Lineup)
            {
                _nav.Draw(GraphicsDevice, _session, _flight, cameraForward, cameraUp,
                    FieldOfViewDegrees, PortOffset(), _chart.Selected);
                _gates.Draw(GraphicsDevice, _session, _flight, nearView, FieldOfViewDegrees);
            }

            DrawHud();
        }

        if (target is not null)
        {
            GraphicsDevice.SetRenderTarget(null);
            Save(target, _options.ShotPath!);
            target.Dispose();

            // A one-shot has nothing left to do; a bounded run does too, once it has its frame.
            if (_options.ShotPath is not null)
            {
                Exit();
            }
        }

        base.Draw(gameTime);
        _frame++;
    }

    /// <summary>
    /// The camera, built fresh each frame from the ship's attitude and the camera mode.
    /// </summary>
    /// <remarks>
    /// The camera sits at the observer and looks wherever <see cref="Camera.Build"/> last put it —
    /// the session's own spawn aim (<see cref="FlightSession.Forward"/>) is written once at launch
    /// and used for nothing but that first aim since. The translation is by the negative of the
    /// observer's position <em>relative to the Earth's centre</em> rather than its heliocentric
    /// position, because a float cannot resolve a metre at 1.5 × 10⁸ kilometres and the local frame
    /// is where everything being drawn already lives.
    /// </remarks>

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

    /// <summary>Draws the chart itself, over the cleared frame.</summary>
    private void DrawChart()
    {
        var system = new SolarSystem();
        system.SetTime((_session.JulianDate - Ephemeris.J2000JulianDate) * 86400.0);

        _sprites.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.AnisotropicClamp);
        _chart.Draw(_session, system, null);
        _sprites.End();

        DrawChartScreen();
    }

    /// <summary>The chart's own display: what is selected, and what it would cost to go there.</summary>
    private void DrawChartScreen()
    {
        _sprites.Begin();

        var ink = new Color(150, 220, 175);
        var dim = new Color(110, 150, 135);
        var warn = new Color(230, 170, 90);
        var chosen = new Color(255, 220, 130);

        _sprites.DrawString(_hud, "CHART  \u00b7  SOL  \u00b7  J2000 ECLIPTIC", new Vector2(28f, 24f), dim);

        // The scale, stated. A logarithmic chart that does not say so is a lie about distance, and
        // this one compresses by a factor of seventy-seven between Mercury and Neptune.
        _sprites.DrawString(_hud,
            $"zoom x{_chart.Zoom:F0}   radii compressed: r_screen ~ log(1 + r / 0.1 AU)",
            new Vector2(28f, 46f), dim);

        if (_chart.Selected is Ephemeris.Body body)
        {
            var at = new Vector2(28f, 92f);
            _sprites.DrawString(_hud,
                $"DESTINATION  {SolarSystem.BodyOf(body).Name.ToUpperInvariant()}", at, chosen);
            at.Y += Line * 1.6f;

            DrawCourseOptions(at, body, ink, dim, warn);
        }
        else
        {
            _sprites.DrawString(_hud, "TAB or click a body to choose a destination",
                new Vector2(28f, 100f), dim);
        }

        _sprites.DrawString(_hud,
            "TAB next   click select   wheel zoom   UP/DN course   ENTER engage   M close",
            new Vector2(28f, GraphicsDevice.Viewport.Height - 40f), dim);

        _sprites.End();
    }

    /// <summary>
    /// The courses to the selected destination, with their times and their fuel.
    /// </summary>
    /// <remarks>
    /// The whole feature on one panel: one destination, three ways to reach it, and a factor of eight
    /// in time against a factor of twenty in fuel. Which one the pilot picks is the game.
    /// </remarks>
    private void DrawCourseOptions(Vector2 at, Ephemeris.Body body, Color ink, Color dim, Color warn)
    {
        var system = new SolarSystem();
        system.SetTime((_session.JulianDate - Ephemeris.J2000JulianDate) * 86400.0);

        // THE SHIP'S POSITION IS EARTH-CENTRED AND THE DESTINATION'S IS HELIOCENTRIC, and mixing the
        // two put the ship six thousand eight hundred kilometres from the Sun instead of one hundred
        // and fifty million. Every course then came out as a hundred and ninety days and none of them
        // was affordable. The session's observer position is the heliocentric one.
        double hereKm = _session.ObserverPosition.Length.ToDouble();

        // For the COURSES, the destination is its orbit rather than its current position: a transfer
        // is between two orbits. The chart still draws the body where it is.
        double thereKm = SolarSystem.MeanOrbitKm(body);
        double thereNowKm = system.Heliocentric(body).Position.Length.ToDouble();
        double acceleration = _flight.Ship.Engine.MaxAccelerationInMetresPerSecondSquared.ToDouble();

        if (_courses.Count == 0 || _selectedFor != body)
        {
            _courses.Clear();
            _courses.AddRange(FlightPlan.Options(
                hereKm, thereKm, acceleration, _flight.DeltaV));

            _selectedFor = body;
            _courseIndex = 0;
        }

        _sprites.DrawString(_hud,
            $"orbit {thereKm / FlightPlan.KilometresPerAu:F3} AU   "
            + $"currently {thereNowKm / FlightPlan.KilometresPerAu:F3} AU out", at, dim);
        at.Y += Line;

        for (int i = 0; i < _courses.Count; i++)
        {
            TransferOption option = _courses[i];
            bool cursor = i == _courseIndex;

            string when = double.IsInfinity(option.Seconds)
                ? "     never"
                : option.Seconds > 86400.0 * 900.0
                    ? $"{option.Seconds / 86400.0 / 365.25,5:F1} yr"
                    : $"{option.Seconds / 86400.0,5:F1} d";

            string fuel = double.IsInfinity(option.DeltaV)
                ? "    ---"
                : $"{option.DeltaV / 1000.0,6:F1} km/s";

            _sprites.DrawString(_hud,
                $"{(cursor ? ">" : " ")} {option.Name,-10} {when}  {fuel}"
                + (option.Feasible ? string.Empty : "   NOT ENOUGH FUEL"),
                at, cursor ? Color.White : (option.Feasible ? ink : warn));

            at.Y += Line;
        }

        at.Y += Line * 0.6f;

        TransferOption pick = _courses[Math.Clamp(_courseIndex, 0, _courses.Count - 1)];
        _sprites.DrawString(_hud, pick.Note, at, dim);
        at.Y += Line;

        double used = double.IsInfinity(pick.DeltaV) || _flight.DeltaV <= 0.0
            ? 0.0
            : pick.DeltaV / _flight.DeltaV * 100.0;

        _sprites.DrawString(_hud,
            $"tanks hold {_flight.DeltaV / 1000.0:F1} km/s; this course uses {used:F0}%", at, dim);
        at.Y += Line * 1.4f;

        // Whether the drive can beat the gravity it is sitting in. This is not a detail: at four
        // milligee the thrust is 0.45 per cent of the Earth's pull at low orbit, so a ship at the
        // station CANNOT fly a straight-line course anywhere. It has to spiral out first, which is a
        // different manoeuvre and is not flown here.
        double shipRadiusKm = _flight.Ship.Position.Length.ToDouble() / 1000.0;
        double stationGmKm = _session.Station.GmMetres.ToDouble() / 1e9;

        if (shipRadiusKm > 1.0)
        {
            double ratio = FlightPlan.ThrustToGravity(
                stationGmKm, shipRadiusKm, acceleration);

            if (ratio < 1.0)
            {
                FlightPlan.EscapeCost escape = FlightPlan.Escape(
                    stationGmKm, shipRadiusKm, acceleration);

                _sprites.DrawString(_hud,
                    $"IN A GRAVITY WELL: thrust is {ratio * 100:F1}% of local gravity",
                    at, warn);
                at.Y += Line;

                // Both prices, because they differ by a factor of two and a half and which one
                // applies is a property of the drive rather than of the destination.
                _sprites.DrawString(_hud,
                    $"  spiral out first: {escape.SpiralDeltaV / 1000.0:F2} km/s over "
                    + $"{escape.SpiralSeconds / 3600.0:F1} h", at, warn);
                at.Y += Line;

                _sprites.DrawString(_hud,
                    $"  (an impulsive escape would be {escape.ImpulsiveDeltaV / 1000.0:F2} km/s, "
                    + $"but that burn is {escape.Orbits:F0} orbits long)", at, dim);
                at.Y += Line;

                _sprites.DrawString(_hud,
                    "  a straight-line course cannot be flown from here", at, dim);
                at.Y += Line * 1.4f;
            }
        }

        if (_helm.Engaged)
        {
            _sprites.DrawString(_hud, $"FLIGHT COMPUTER  {_helm.Describe()}", at, new Color(255, 220, 130));
            at.Y += Line;
            _sprites.DrawString(_hud,
                $"  elapsed {_helm.ElapsedSeconds / 86400.0:F2} days", at, dim);
        }
        else
        {
            _sprites.DrawString(_hud, "ENTER hands the controls to the flight computer", at, dim);
        }
    }

    /// <summary>The tail of a frame: save if asked, and count it.</summary>
    private void FinishFrame(RenderTarget2D? target, GameTime gameTime)
    {
        if (target is not null)
        {
            GraphicsDevice.SetRenderTarget(null);
            Save(target, _options.ShotPath!);
            target.Dispose();

            // A one-shot has nothing left to do; a bounded run does too, once it has its frame.
            if (_options.ShotPath is not null)
            {
                Exit();
            }
        }

        base.Draw(gameTime);
        _frame++;
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

        _sprites.DrawString(_hud, "ILLUMINUS COURIER  \u00b7  MERIDIAN", at, dim);
        at.Y += Line * 1.6f;

        _sprites.DrawString(_hud, $"SPEED      {speed,10:F1} m/s", at, ink);
        at.Y += Line;
        _sprites.DrawString(_hud, $"RANGE      {range,10:F0} m", at, ink);
        at.Y += Line;
        _sprites.DrawString(_hud, $"ON CORRIDOR{along,10:F0} m", at, dim);
        at.Y += Line;

        // The build's own name, dimmest thing on the panel: a screenshot in a bug report
        // that carries the version is a screenshot you can reproduce.
        _sprites.DrawString(_hud, $"BUILD      {BuildInfo.Version,10}", at, dim);
        at.Y += Line;

        // The clock, because the sky turns and the player should be able to see it turn.
        _sprites.DrawString(_hud, $"EPOCH JD   {_session.JulianDate,10:F4}", at, dim);
        at.Y += Line;

        // Time compression, on the display, because a clock running at a thousand times real time
        // and a clock running at one look exactly the same until you have watched one of them for a
        // minute.
        _sprites.DrawString(_hud, $"TIME       {DescribeRate(_timeRate),10}", at,
            _timeRateIndex == 1 ? dim : ink);
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

        // The controls, on screen, because a player who cannot find the camera has a simulation they
        // can only watch. The first version of this client had one fixed view and said so nowhere.
        at.Y += Line * 1.9f;
        _sprites.DrawString(_hud, $"VIEW       {_camera.Describe()}", at, ink);
        at.Y += Line;
        _sprites.DrawString(_hud, $"MOUSE      drag {_dragPixels,5:F0} px   "
            + $"wheel {_wheelNotches,4:F0}   {(IsActive ? "window active" : "WINDOW NOT FOCUSED")}",
            at, IsActive ? dim : warn);
        at.Y += Line;
        _sprites.DrawString(_hud, "  C view   L-drag look   R-drag orbit   wheel zoom", at, dim);
        at.Y += Line;
        _sprites.DrawString(_hud, "  W/S throttle   A/D yaw   R/F pitch", at, dim);
        at.Y += Line;
        _sprites.DrawString(_hud, "  Q/E roll   Z/X full/cut   UP/DN time", at, dim);
        at.Y += Line;
        _sprites.DrawString(_hud, $"NAV LIGHTS {_hulls.LightsLit} lit of the convention", at, dim);
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
    /// <summary>
    /// Where the player's hull is and which way it points, in render space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE MODEL'S AXES ARE NOT THE SIMULATION'S, and this is the matrix that reconciles them. A hull
    /// is modelled nose-up about Blender's +z, and the glTF exporter turns Blender's z-up into y-up —
    /// so in the file the ship is long along <b>+Y</b>. The simulation's ship is a rotation vector
    /// about +x, so its nose is its own +X.
    /// </para>
    /// <para>
    /// The convention, fixed here and written down because every future asset depends on it:
    /// </para>
    /// <list type="bullet">
    /// <item><description>model <b>+Y</b> is the nose — Blender +z, the direction of travel</description></item>
    /// <item><description>model <b>+X</b> is dorsal, the ship's up — Blender +x</description></item>
    /// <item><description>model <b>+Z</b> is to port — Blender −y, because starboard is
    /// <c>cross(nose, deck)</c> and that lands on Blender +y</description></item>
    /// </list>
    /// <para>
    /// The first version of this put the nose on the model's +X, which is its eleven-metre beam. The
    /// ship flew <b>sideways</b>: sixty-seven metres of hull presented broadside to the direction of
    /// travel, and a rotation about its own nose that rolled it rather than turning it.
    /// </para>
    /// </remarks>
    private Matrix ShipTransform()
    {
        Attitude attitude = _flight.Ship.Attitude;

        Vector3 nose = Unit(attitude.Forward);
        Vector3 deck = Unit(attitude.Rotate(new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One)));

        // Model +Z is to port, and port is the negative of starboard.
        Vector3 port = Vector3.Cross(deck, nose);

        return new Matrix(
            deck.X, deck.Y, deck.Z, 0f,
            nose.X, nose.Y, nose.Z, 0f,
            port.X, port.Y, port.Z, 0f,
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

        Vector3 offset = PortOffset();

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
    /// Laid out along the view's right axis and all at the same distance from the camera, which is
    /// the only arrangement in which relative size is readable.
    /// </remarks>
    private void DrawLineup(Matrix projection, Vector3 sunDirection)
    {
        Vector3 nose = NoseVector();
        Vector3 deck = DeckVector();
        Vector3 right = Vector3.Normalize(Vector3.Cross(nose, deck));

        (Hull Hull, string Name)[] assets =
        [
            (_courier, "courier"),
            (_freighter, "freighter"),
            (_station, "meridian"),
        ];

        // EVERY MODEL IS LONG ALONG ITS OWN +Y, so the lineup rotation is the one that puts the
        // model's +Y on the screen's right axis — which makes every asset broadside to the camera and
        // measured along the same direction. The first version used CreateRotationY(90 degrees),
        // which is a rotation ABOUT y and therefore leaves y exactly where it was: every asset stayed
        // pointed at the sky and the whole row rendered edge-on as a set of vertical slivers.
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

        // The camera goes far enough back to hold the whole row, derived from the span rather than
        // fixed: the span went from 700 m to 2 800 m when the station was rescaled, and a fixed
        // distance silently framed two thirds of it.
        float distance = span * 0.85f;
        float at = -span * 0.5f;

        Matrix view = _camera.Build(nose, deck, new Vector3(0f, 0f, -distance)).View;

        foreach ((Hull hull, string name) in assets)
        {
            float size = Largest(hull);
            Vector3 centre = (nose * distance) + (right * (at + (size * 0.5f)));

            _hulls.Draw(hull, facing * Matrix.CreateTranslation(centre), view, projection,
                sunDirection, Vector3.Zero, 0f);

            Console.WriteLine(
                $"  lineup: {name,-10} {size,7:F0} m wide, centred at {at + (size * 0.5f),8:F0} m");

            at += size + Gap;
        }

        Console.WriteLine($"  lineup: span {span:F0} m, camera at {distance:F0} m");
    }

    /// <summary>
    /// The direction from the ship to the Earth, in the near pass's frame.
    /// </summary>
    /// <remarks>
    /// The negative of the ship's own position, because the local frame's origin is the centre of the
    /// Earth. That is the same fact that made the gravity source simple and it makes this simple too.
    /// </remarks>
    private Vector3 EarthDirection()
    {
        Fix128Vec position = _flight.Ship.Position;
        if (position.IsZero)
        {
            return Vector3.Zero;
        }

        return -Vector3.Normalize(Unit(position));
    }

    /// <summary>
    /// How much light the Earth throws back onto the ship, from 0 to 1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Bond albedo of the Earth is 0.306, and the fraction of sky the planet fills is the solid
    /// angle it subtends over 4π. Close in, the Earth fills nearly a hemisphere and the figure is
    /// half the albedo; from the Moon it is nothing.
    /// </para>
    /// <para>
    /// <c>sin θ = R / d</c> for the planet's angular radius θ, so the fraction of sky is
    /// <c>(1 − cos θ)/2</c> and the whole thing is one line. It gives 0.14 at four hundred kilometres
    /// and 0.0002 at the Moon, which is the right shape and the right magnitudes.
    /// </para>
    /// </remarks>
    private float Earthshine()
    {
        const double EarthRadiusKm = 6_378.1;
        const double BondAlbedo = 0.306;

        double distanceKm = _flight.Ship.Position.Length.ToDouble() / 1000.0;
        if (distanceKm <= EarthRadiusKm)
        {
            return 0f;
        }

        double sinTheta = EarthRadiusKm / distanceKm;
        if (sinTheta <= 0.0)
        {
            return 0f;
        }

        double cosTheta = Math.Sqrt(Math.Max(0.0, 1.0 - (sinTheta * sinTheta)));
        double skyFraction = (1.0 - cosTheta) * 0.5;

        return (float)(BondAlbedo * skyFraction);
    }

    /// <summary>The hull's nose, as a unit vector in the ecliptic frame.</summary>
    private Vector3 NoseVector() => Unit(_flight.Ship.Attitude.Forward);

    /// <summary>The hull's roof, as a unit vector in the ecliptic frame.</summary>
    private Vector3 DeckVector() => Unit(_flight.Ship.Attitude.Rotate(
        new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One)));

    /// <summary>Where the station's docking port is, in metres, relative to the hull.</summary>
    private Vector3 PortOffset() => Unit(_session.Station.Port.Position - _flight.Ship.Position);

    /// <summary>The longest dimension of a hull, in metres.</summary>
    private static float Largest(Hull hull) =>
        MathF.Max(hull.Size.X, MathF.Max(hull.Size.Y, hull.Size.Z));

    /// <summary>How far behind and above the hull the chase camera sits, in metres.</summary>
    private const float ChaseDistance = 130f;
    private const float ChaseLift = 42f;
    private const float ChaseLead = 40f;

    /// <summary>A time rate, in the unit a person reads it in.</summary>
    private static string DescribeRate(double rate) => rate switch
    {
        >= 1.0 => $"x{rate:F0}",
        _ => $"x{rate:F1}",
    };

    private static string Describe(Fix128Vec v) =>
        $"({v.X.ToDouble(),6:F3},{v.Y.ToDouble(),6:F3},{v.Z.ToDouble(),6:F3})";

    private static Fix128 Dot(Fix128Vec a, Fix128Vec b) =>
        (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static Vector3 Unit(Fix128Vec v) => new(
        (float)v.X.ToDouble(), (float)v.Y.ToDouble(), (float)v.Z.ToDouble());

    private void Save(Texture2D texture, string path)
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
        if (_options.Verbose)
        {
            Console.WriteLine($"  frame {_frame} at t = {_simulatedSeconds:F3} s, "
                + $"time {DescribeRate(_timeRate)}, nav lights lit {_hulls.LightsLit}");

            if (_options.Hold.Length > 0)
            {
                Attitude attitude = _flight.Ship.Attitude;

                Fix128Vec nose = attitude.Forward;
                Fix128Vec deck = attitude.Rotate(
                    new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One));
                Fix128Vec starboard = FlightSession.Cross(nose, deck).Normalized();

                Console.WriteLine($"    nose {Describe(nose)}  deck {Describe(deck)}");

                // Against the axes the ship STARTED with, not its own — dotting a vector with a
                // basis built from itself is identically zero, which is a diagnostic that reports
                // "no rotation" for every input. It did, and it cost an hour.
                Console.WriteLine($"      from start: nose {Describe(nose - _startNose)}"
                    + $"  deck {Describe(deck - _startDeck)}");
            }
        }
    }

    protected override void UnloadContent()
    {
        _sky.Dispose();
        _bodies.Dispose();
        _sun.Dispose();
        _hulls.Dispose();
        _plume.Dispose();
        _chart.Dispose();
        _courier.Dispose();
        _station.Dispose();
        _freighter.Dispose();
        _sprites.Dispose();
        _pixel.Dispose();
        base.UnloadContent();
    }
}
