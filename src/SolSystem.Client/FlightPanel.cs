using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SolSystem.Core.Local;
using SolSystem.Core.Numerics;

namespace SolSystem.Client;

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
/// <para>
/// The panel is read-only against the simulation: it prints what the session and the ship say,
/// and the numbers it is told about the interface — time rate, drag pixels, wheel notches, focus
/// — are handed in each frame by <see cref="FlightGame"/>, because the panel owns none of them.
/// </para>
/// </remarks>
internal sealed class FlightPanel
{
    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private readonly SpriteFont _font;
    private readonly FlightSession _session;
    private readonly Flight _flight;
    private readonly Camera _camera;
    private readonly HullRenderer _hulls;

    internal FlightPanel(
        GraphicsDevice device,
        SpriteBatch sprites,
        Texture2D pixel,
        SpriteFont font,
        FlightSession session,
        Flight flight,
        Camera camera,
        HullRenderer hulls)
    {
        _device = device;
        _sprites = sprites;
        _pixel = pixel;
        _font = font;
        _session = session;
        _flight = flight;
        _camera = camera;
        _hulls = hulls;
    }

    /// <summary>Draws the frame furniture and the flight panel.</summary>
    internal void Draw(
        double timeRate,
        int timeRateIndex,
        double dragPixels,
        double wheelNotches,
        bool active)
    {
        _sprites.Begin();

        // A reticle with a gap at the centre, and the gap is the point: a filled crosshair covers
        // whatever you are aiming at, and the first version of this hid the Sun behind its own
        // cross — which reads as a renderer that has not drawn the Sun.
        int cx = _device.Viewport.Width / 2;
        int cy = _device.Viewport.Height / 2;
        var reticle = new Color(120, 200, 140, 170);
        _sprites.Draw(_pixel, new Rectangle(cx - 14, cy - 1, 9, 2), reticle);
        _sprites.Draw(_pixel, new Rectangle(cx + 6, cy - 1, 9, 2), reticle);
        _sprites.Draw(_pixel, new Rectangle(cx - 1, cy - 14, 2, 9), reticle);
        _sprites.Draw(_pixel, new Rectangle(cx - 1, cy + 6, 2, 9), reticle);

        // The frame, so the field of view is legible and the corners are not empty.
        var edge = new Color(60, 90, 110, 140);
        _sprites.Draw(_pixel, new Rectangle(0, 0, _device.Viewport.Width, 1), edge);
        _sprites.Draw(_pixel, new Rectangle(0, _device.Viewport.Height - 1,
            _device.Viewport.Width, 1), edge);
        _sprites.Draw(_pixel, new Rectangle(0, 0, 1, _device.Viewport.Height), edge);
        _sprites.Draw(_pixel, new Rectangle(_device.Viewport.Width - 1, 0, 1,
            _device.Viewport.Height), edge);

        DrawFlightPanel(timeRate, timeRateIndex, dragPixels, wheelNotches, active);

        _sprites.End();
    }

    private void DrawFlightPanel(
        double timeRate,
        int timeRateIndex,
        double dragPixels,
        double wheelNotches,
        bool active)
    {
        // Speed relative to the station, which is the number that matters for a docking and the one
        // that reads zero when the ship is holding station. Its speed relative to the EARTH is seven
        // and a half kilometres a second and always will be, because that is what being in orbit is.
        double speed = (_flight.Ship.Velocity - _session.Station.Velocity).Length.ToDouble();

        // Range from the ship to the docking port, both measured from the Earth's centre.
        double range = (_flight.Ship.Position - _session.Station.Port.Position).Length.ToDouble();

        // And the same thing along the corridor, signed: positive is outside the port, negative is
        // past it. A range alone cannot tell a pilot which side of the dock they are on.
        double along = Fix128Vec.Dot(_flight.Ship.Position - _session.Station.Port.Position,
            _session.Station.Port.Axis).ToDouble();

        var ink = new Color(150, 220, 175);
        var dim = new Color(110, 150, 135);
        var warn = new Color(230, 170, 90);

        var at = new Vector2(28f, 26f);

        _sprites.DrawString(_font, "ILLUMINUS COURIER  \u00b7  MERIDIAN", at, dim);
        at.Y += FlightUi.Line * 1.6f;

        _sprites.DrawString(_font, $"SPEED      {speed,10:F1} m/s", at, ink);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font, $"RANGE      {range,10:F0} m", at, ink);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font, $"ON CORRIDOR{along,10:F0} m", at, dim);
        at.Y += FlightUi.Line;

        // The build's own name, dimmest thing on the panel: a screenshot in a bug report
        // that carries the version is a screenshot you can reproduce.
        _sprites.DrawString(_font, $"BUILD      {BuildInfo.Version,10}", at, dim);
        at.Y += FlightUi.Line;

        // The clock, because the sky turns and the player should be able to see it turn.
        _sprites.DrawString(_font, $"EPOCH JD   {_session.JulianDate,10:F4}", at, dim);
        at.Y += FlightUi.Line;

        // Time compression, on the display, because a clock running at a thousand times real time
        // and a clock running at one look exactly the same until you have watched one of them for a
        // minute.
        _sprites.DrawString(_font, $"TIME       {DescribeRate(timeRate),10}", at,
            timeRateIndex == 1 ? dim : ink);
        at.Y += FlightUi.Line * 1.6f;

        // Delta-v first among the propellant figures, and deliberately: it is the one that decides
        // where the ship can go, and the other two are ways of saying the same thing.
        double deltaV = _flight.DeltaV;
        double seconds = _flight.FullThrottleSeconds;

        _sprites.DrawString(_font, $"DELTA-V    {deltaV,10:F1} m/s", at,
            deltaV < 2000.0 ? warn : ink);
        at.Y += FlightUi.Line;

        _sprites.DrawString(_font, $"PROPELLANT {_flight.Propellant,10:F2} t", at,
            _flight.PropellantFraction < 0.05 ? warn : ink);
        at.Y += FlightUi.Line;

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

        _sprites.DrawString(_font, $"BURN       {burn} at full", at, ink);
        at.Y += FlightUi.Line;

        DrawThrottle(at, ink, dim);

        // The controls, on screen, because a player who cannot find the camera has a simulation they
        // can only watch. The first version of this client had one fixed view and said so nowhere.
        at.Y += FlightUi.Line * 1.9f;
        _sprites.DrawString(_font, $"VIEW       {_camera.Describe()}", at, ink);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font, $"MOUSE      drag {dragPixels,5:F0} px   "
            + $"wheel {wheelNotches,4:F0}   {(active ? "window active" : "WINDOW NOT FOCUSED")}",
            at, active ? dim : warn);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font, "  C view   L-drag look   R-drag orbit   wheel zoom", at, dim);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font, "  W/S throttle   A/D yaw   R/F pitch", at, dim);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font, "  Q/E roll   Z/X full/cut   UP/DN time", at, dim);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font, $"NAV LIGHTS {_hulls.LightsLit} lit of the convention", at, dim);
    }

    /// <summary>The throttle, as a bar, because a number is the wrong shape for a setting.</summary>
    private void DrawThrottle(Vector2 at, Color ink, Color dim)
    {
        at.Y += 14f;
        _sprites.DrawString(_font, "THROTTLE", at, dim);

        var track = new Rectangle((int)at.X + 96, (int)at.Y + 3, 220, 12);
        _sprites.Draw(_pixel, track, new Color(30, 44, 40));

        var fill = new Rectangle(track.X, track.Y, (int)(track.Width * _flight.Throttle), track.Height);
        _sprites.Draw(_pixel, fill, ink);

        _sprites.DrawString(_font, $"{_flight.Throttle * 100.0,5:F0}%",
            new Vector2(track.Right + 12, at.Y), ink);
    }

    /// <summary>A time rate, in the unit a person reads it in.</summary>
    internal static string DescribeRate(double rate) => rate switch
    {
        >= 1.0 => $"x{rate:F0}",
        _ => $"x{rate:F1}",
    };
}
