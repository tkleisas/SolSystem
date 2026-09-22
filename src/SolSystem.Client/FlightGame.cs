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
    private FlightPanel _panel = null!;
    private ChartScreen _chartScreen = null!;
    private Texture2D _sharedPixel = null!;
    private readonly Camera _camera = new();
    private Plume _plume = null!;
    private NavOverlay _nav = null!;
    private CorridorGates _gates = null!;
    private Autohelm _helm;
    private bool _chartOpen;
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
    private const double TickSeconds = Constants.NavigationTickSeconds;

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
        _flight = new Flight(Hulls.Courier(
            _session.Station.Port.Position
                + (_session.Station.Port.Axis * Fix128.FromDouble(_options.Standoff)),
            _session.Station.Velocity,
            FacingAlong(-_session.Station.Port.Axis)));

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

        // The pixel and the font are the panels' drawing materials: the flight panel draws its
        // rectangles with the pixel and its text with the font, and the chart screen does the
        // same. The overlays below were given them before the panels existed; they are shared,
        // not owned, and disposed once at the end of the run.
        Texture2D pixel = new(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
        SpriteFont hud = Content.Load<SpriteFont>("Hud");

        _nav = new NavOverlay(_sprites, pixel, hud);
        _gates = new CorridorGates(_sprites, pixel);

        // The panels are built last, because they read the session and the ship, and the chart
        // screen owns the chart's own drawing state. A destination on the command line is selected
        // here, after the chart exists to hold it.
        _panel = new FlightPanel(GraphicsDevice, _sprites, pixel, hud, _session, _flight,
            _camera, _hulls);
        _chartScreen = new ChartScreen(GraphicsDevice, _sprites, hud);
        _sharedPixel = pixel;

        if (_options.Destination.Length > 0
            && Enum.TryParse(_options.Destination, ignoreCase: true, out Ephemeris.Body chosen))
        {
            _chartScreen.Select(chosen);
        }

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keys = KeysWithHolds(Keyboard.GetState());
        MouseState mouse = Mouse.GetState();

        if (JustPressed(keys, Keys.M))
        {
            _chartOpen = !_chartOpen;
        }

        // The time compression, read here rather than inside the interactive path, so that a
        // headless run can be told to hold Up and the ladder can be checked. It could not be,
        // before, and an untestable control is an unverified one. With the chart open the keys
        // belong to the course list instead, so the ladder is not read: one keypress moving
        // both the clock and the course cursor was two controls on one keypress.
        if (_chartOpen)
        {
            // ENTER with a destination selected hands the controls over. The chart owns the
            // course list and the cursor; it hands back the decision, and the flight side
            // carries it out.
            if (_chartScreen.Read(keys, mouse, _previousKeys, _previousMouse)
                is Ephemeris.Body destination)
            {
                Engage(destination, _chartScreen.SelectedOption);
            }
        }
        else
        {
            ReadTimeCompression(keys);
        }

        if (_options.Headless)
        {
            // No keyboard, and a fixed step, so that frame N is at N/60 of a second and two renders
            // of the same frame are the same image. The compression still applies to it.
            double step = ShotSeconds * _timeRate;

            _simulatedSeconds += step;
            _session.Advance(step);
            _sun.Update(step);

            // The ship is always on the clock, held keys or not: since the station genuinely
            // orbits, a ship left unsimulated is a ship the station leaves behind at 7.7 km/s,
            // and a verification shot of that is a picture of a lie. The hold only adds keys.
            SimulateTicks(keys, step);
        }
        else
        {
            ReadCamera(keys, mouse);
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

    private void ReadCamera(KeyboardState keys, MouseState mouse)
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
    /// Hands the controls to the flight computer for the chosen course.
    /// </summary>
    private void Engage(Ephemeris.Body destination, TransferOption? optionOrNull)
    {
        SolarSystem system = _session.System;

        // Nothing planned, nothing to engage. The first version engaged on a default option when
        // the chart had never been drawn — a full-throttle crossing to nowhere, chosen silently.
        if (optionOrNull is not TransferOption option)
        {
            Console.WriteLine(
                "  nothing to engage: no course is planned. Open the chart, pick a body, plan.");
            return;
        }

        // The option owns its own throttle now: the planner quotes what flying it commands,
        // because the exchange rate between throttle and time is the planner's arithmetic,
        // not the client's.
        Fix128 throttle = Fix128.FromDouble(option.Throttle);

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

        // The arrival radius is coarse on purpose: the autohelm is a transit computer, not a
        // docking pilot, and arriving within 2 000 km of where the destination now sits is the
        // stop it is for. Refining from there is the docking corridor's job.
        const double ArrivalRadiusMetres = 2_000_000.0;

        _helm = Autohelm.To(targetHere, Fix128.FromDouble(ArrivalRadiusMetres), throttle);

        _chartOpen = false;

        Console.WriteLine($"  autohelm engaged: {destination}, {option.Name}, "
            + $"{option.Seconds / 86400.0:F1} days, {option.DeltaV / 1000.0:F1} km/s");
    }

    private void Simulate(GameTime gameTime, KeyboardState keys)
    {
        double seconds = gameTime.ElapsedGameTime.TotalSeconds * _timeRate;
        _simulatedSeconds += seconds;
        _session.Advance(seconds);
        _sun.Update(seconds);

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
            _chartScreen.Draw(_session, _flight, _helm);
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

        // The chart replaces everything. Its frames take the early return above — DrawChart
        // clears the frame itself and draws the screen — so by here the chart is always closed,
        // and the flying view's overlays are always drawn.
        //
        // The nav markers read the same camera basis the passes were rendered with, so a
        // marker sits on the thing it names in every camera mode. The lineup is a measuring
        // bench rather than a view of the sky, and nothing in it is anywhere, so it gets none.
        // The corridor gates follow the same rule, for the same reason.
        if (!_options.Lineup)
        {
            _nav.Draw(GraphicsDevice, _session, _flight, cameraForward, cameraUp,
                FieldOfViewDegrees, PortOffset(), _chartScreen.Selected);
            _gates.Draw(GraphicsDevice, _session, _flight, nearView, FieldOfViewDegrees);
        }

        _panel.Draw(_timeRate, _timeRateIndex, _dragPixels, _wheelNotches, IsActive);
        FinishFrame(target, gameTime);
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

        Fix128Vec axis = Fix128Vec.Cross(reference, nose);

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


    private static string Describe(Fix128Vec v) =>
        $"({v.X.ToDouble(),6:F3},{v.Y.ToDouble(),6:F3},{v.Z.ToDouble(),6:F3})";

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
                + $"time {FlightPanel.DescribeRate(_timeRate)}, nav lights lit {_hulls.LightsLit}");

            if (_options.Hold.Length > 0)
            {
                Attitude attitude = _flight.Ship.Attitude;

                Fix128Vec nose = attitude.Forward;
                Fix128Vec deck = attitude.Rotate(
                    new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One));
                Fix128Vec starboard = Fix128Vec.Cross(nose, deck).Normalized();

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
        _chartScreen.Dispose();
        _courier.Dispose();
        _station.Dispose();
        _freighter.Dispose();
        _sprites.Dispose();
        _sharedPixel.Dispose();
        base.UnloadContent();
    }
}
