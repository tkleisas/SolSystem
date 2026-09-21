using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SolSystem.Core.Numerics;
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
    private SpriteBatch _sprites = null!;
    private Texture2D _pixel = null!;

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
        Window.Title = "SolSystem — flight";
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

        // The body report is worth reading when a frame looks wrong, and noise otherwise, so it is
        // asked for rather than volunteered.
        if (_options.Headless && _options.Verbose)
        {
            BodyRenderer.Verbose = true;
        }
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

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

        if (JustPressed(keys, Keys.Escape))
        {
            Exit();
        }
    }

    private bool JustPressed(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

    protected override void Draw(GameTime gameTime)
    {
        RenderTarget2D? target = null;

        if (_options.Headless)
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

        // Then the bodies, with depth, so a planet occults the stars behind it and the Earth
        // occults everything.
        _bodies.Draw(_session, view, projection, KilometresPerUnit);

        DrawHud();

        if (target is not null)
        {
            GraphicsDevice.SetRenderTarget(null);
            Save(target, _options.ShotPath!);
            target.Dispose();
            Exit();
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

        // A frame round the edge, so the field of view is legible and the corners are not empty.
        var edge = new Color(60, 90, 110, 120);
        _sprites.Draw(_pixel, new Rectangle(0, 0, GraphicsDevice.Viewport.Width, 1), edge);
        _sprites.Draw(_pixel, new Rectangle(0, GraphicsDevice.Viewport.Height - 1,
            GraphicsDevice.Viewport.Width, 1), edge);
        _sprites.Draw(_pixel, new Rectangle(0, 0, 1, GraphicsDevice.Viewport.Height), edge);
        _sprites.Draw(_pixel, new Rectangle(GraphicsDevice.Viewport.Width - 1, 0, 1,
            GraphicsDevice.Viewport.Height), edge);

        _sprites.End();
    }

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
        _sprites.Dispose();
        _pixel.Dispose();
        base.UnloadContent();
    }
}
