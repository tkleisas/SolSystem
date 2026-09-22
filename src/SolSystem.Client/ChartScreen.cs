using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;

namespace SolSystem.Client;

/// <summary>
/// The solar-system chart: the map, the destination panel and the courses to it.
/// </summary>
/// <remarks>
/// <para>
/// One feature, drawn and driven as one object. The chart has state of its own — which body is
/// selected, which course the cursor is on, and the list the planner built for that selection —
/// and none of it is flight state: the ship does not change because a menu is open. What crosses
/// back to the flight side is a decision: a destination handed to <see cref="FlightGame"/> when
/// ENTER is pressed with one selected.
/// </remarks>
/// <para>
/// The thumbstick is deliberately ignored: the keyboard has focus. That was true when the
/// gamepad was added to this client too, which is why nothing here reads one.
/// </para>
/// </remarks>
internal sealed class ChartScreen
{
    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _sprites;
    private readonly SpriteFont _font;
    private readonly Chart _chart;

    private readonly List<TransferOption> _courses = new();
    private int _courseIndex;

    /// <summary>Which body the course list was built for, so it is not rebuilt every frame.</summary>
    private Ephemeris.Body? _selectedFor;

    internal ChartScreen(GraphicsDevice device, SpriteBatch sprites, SpriteFont font)
    {
        _device = device;
        _sprites = sprites;
        _font = font;
        _chart = new Chart(device, sprites);
    }

    /// <summary>The body the chart has selected, if any.</summary>
    internal Ephemeris.Body? Selected => _chart.Selected;

    /// <summary>
    /// The course the cursor is on, for a run that never drew the panel.
    /// </summary>
    /// <remarks>
    /// The course list is only built while the chart is drawn, so a headless or scripted run
    /// reaches the engage key with an empty list. Returning the clamped cursor's option anyway
    /// would engage a default record — full throttle to nowhere — and a report is more use.
    /// </remarks>
    internal TransferOption? SelectedOption =>
        _courses.Count == 0 ? null : _courses[Math.Clamp(_courseIndex, 0, _courses.Count - 1)];

    /// <summary>Selects a destination directly, for a run that was told where to go.</summary>
    internal void Select(Ephemeris.Body body) => _chart.Selected = body;

    /// <summary>Releases the chart's GPU resources. Everything else here is managed.</summary>
    internal void Dispose() => _chart.Dispose();

    /// <summary>
    /// Reads the chart's controls, and returns the destination to engage, or null.
    /// </summary>
    internal Ephemeris.Body? Read(
        KeyboardState keys,
        MouseState mouse,
        KeyboardState previousKeys,
        MouseState previousMouse)
    {
        if (JustPressed(keys, Keys.Tab, previousKeys))
        {
            _chart.NextBody();
            _courses.Clear();
        }

        int notches = mouse.ScrollWheelValue - previousMouse.ScrollWheelValue;
        if (notches != 0)
        {
            _chart.AdjustZoom(notches / 120f);
        }

        // A click picks the body under it, which is what a person tries first.
        if (mouse.LeftButton == ButtonState.Pressed
            && previousMouse.LeftButton == ButtonState.Released)
        {
            if (_chart.Nearest(new Vector2(mouse.X, mouse.Y)) is Ephemeris.Body hit)
            {
                _chart.Selected = hit;
                _courses.Clear();
            }
        }

        // Up and down move through the courses rather than the time compression, which has no
        // meaning while the clock is not being watched.
        if (JustPressed(keys, Keys.Down, previousKeys))
        {
            _courseIndex = Math.Min(_courseIndex + 1, Math.Max(_courses.Count - 1, 0));
        }

        if (JustPressed(keys, Keys.Up, previousKeys))
        {
            _courseIndex = Math.Max(_courseIndex - 1, 0);
        }

        return JustPressed(keys, Keys.Enter, previousKeys) ? _chart.Selected : null;
    }

    /// <summary>Draws the chart and the panel under it. A chart frame draws nothing else.</summary>
    internal void Draw(FlightSession session, Flight flight, Autohelm helm)
    {
        SolarSystem system = session.System;

        _sprites.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.AnisotropicClamp);
        _chart.Draw(session, system, null);
        _sprites.End();

        DrawPanel(session, flight, helm);
    }

    /// <summary>The chart's own display: what is selected, and what it would cost to go there.</summary>
    private void DrawPanel(FlightSession session, Flight flight, Autohelm helm)
    {
        _sprites.Begin();

        var ink = new Color(150, 220, 175);
        var dim = new Color(110, 150, 135);
        var warn = new Color(230, 170, 90);
        var chosen = new Color(255, 220, 130);

        _sprites.DrawString(_font, "CHART  \u00b7  SOL  \u00b7  J2000 ECLIPTIC", new Vector2(28f, 24f), dim);

        // The scale, stated. A logarithmic chart that does not say so is a lie about distance, and
        // this one compresses by a factor of seventy-seven between Mercury and Neptune.
        _sprites.DrawString(_font,
            $"zoom x{_chart.Zoom:F0}   radii compressed: r_screen ~ log(1 + r / 0.1 AU)",
            new Vector2(28f, 46f), dim);

        if (_chart.Selected is Ephemeris.Body body)
        {
            var at = new Vector2(28f, 92f);
            _sprites.DrawString(_font,
                $"DESTINATION  {SolarSystem.BodyOf(body).Name.ToUpperInvariant()}", at, chosen);
            at.Y += FlightUi.Line * 1.6f;

            DrawCourseOptions(session, flight, helm, at, body, ink, dim, warn);
        }
        else
        {
            _sprites.DrawString(_font, "TAB or click a body to choose a destination",
                new Vector2(28f, 100f), dim);
        }

        _sprites.DrawString(_font,
            "TAB next   click select   wheel zoom   UP/DN course   ENTER engage   M close",
            new Vector2(28f, _device.Viewport.Height - 40f), dim);

        _sprites.End();
    }

    /// <summary>
    /// The courses to the selected destination, with their times and their fuel.
    /// </summary>
    /// <remarks>
    /// The whole feature on one panel: one destination, three ways to reach it, and a factor of eight
    /// in time against a factor of twenty in fuel. Which one the pilot picks is the game.
    /// </remarks>
    private void DrawCourseOptions(
        FlightSession session,
        Flight flight,
        Autohelm helm,
        Vector2 at,
        Ephemeris.Body body,
        Color ink,
        Color dim,
        Color warn)
    {
        SolarSystem system = session.System;

        // THE SHIP'S POSITION IS EARTH-CENTRED AND THE DESTINATION'S IS HELIOCENTRIC, and mixing the
        // two put the ship six thousand eight hundred kilometres from the Sun instead of one hundred
        // and fifty million. Every course then came out as a hundred and ninety days and none of them
        // was affordable. The session's observer position is the heliocentric one.
        double hereKm = session.ObserverPosition.Length.ToDouble();

        // For the COURSES, the destination is its orbit rather than its current position: a transfer
        // is between two orbits. The chart still draws the body where it is.
        double thereKm = SolarSystem.MeanOrbitKm(body);
        double thereNowKm = system.Heliocentric(body).Position.Length.ToDouble();
        double acceleration = flight.Ship.Engine.MaxAccelerationInMetresPerSecondSquared.ToDouble();

        if (_courses.Count == 0 || _selectedFor != body)
        {
            _courses.Clear();
            _courses.AddRange(FlightPlan.Options(
                hereKm, thereKm, acceleration, flight.DeltaV));

            _selectedFor = body;
            _courseIndex = 0;
        }

        _sprites.DrawString(_font,
            $"orbit {thereKm / FlightPlan.KilometresPerAu:F3} AU   "
            + $"currently {thereNowKm / FlightPlan.KilometresPerAu:F3} AU out", at, dim);
        at.Y += FlightUi.Line;

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

            _sprites.DrawString(_font,
                $"{(cursor ? ">" : " ")} {option.Name,-10} {when}  {fuel}"
                + (option.Feasible ? string.Empty : "   NOT ENOUGH FUEL"),
                at, cursor ? Color.White : (option.Feasible ? ink : warn));

            at.Y += FlightUi.Line;
        }

        at.Y += FlightUi.Line * 0.6f;

        TransferOption pick = _courses[Math.Clamp(_courseIndex, 0, _courses.Count - 1)];
        _sprites.DrawString(_font, pick.Note, at, dim);
        at.Y += FlightUi.Line;

        double used = double.IsInfinity(pick.DeltaV) || flight.DeltaV <= 0.0
            ? 0.0
            : pick.DeltaV / flight.DeltaV * 100.0;

        _sprites.DrawString(_font,
            $"tanks hold {flight.DeltaV / 1000.0:F1} km/s; this course uses {used:F0}%", at, dim);
        at.Y += FlightUi.Line * 1.4f;

        // Whether the drive can beat the gravity it is sitting in. This is not a detail: at four
        // milligee the thrust is 0.45 per cent of the Earth's pull at low orbit, so a ship at the
        // station CANNOT fly a straight-line course anywhere. It has to spiral out first, which is a
        // different manoeuvre and is not flown here.
        double shipRadiusKm = flight.Ship.Position.Length.ToDouble() / 1000.0;
        double stationGmKm = session.Station.GmMetres.ToDouble() / 1e9;

        if (shipRadiusKm > 1.0)
        {
            double ratio = FlightPlan.ThrustToGravity(
                stationGmKm, shipRadiusKm, acceleration);

            if (ratio < 1.0)
            {
                FlightPlan.EscapeCost escape = FlightPlan.Escape(
                    stationGmKm, shipRadiusKm, acceleration);

                _sprites.DrawString(_font,
                    $"IN A GRAVITY WELL: thrust is {ratio * 100:F1}% of local gravity",
                    at, warn);
                at.Y += FlightUi.Line;

                // Both prices, because they differ by a factor of two and a half and which one
                // applies is a property of the drive rather than of the destination.
                _sprites.DrawString(_font,
                    $"  spiral out first: {escape.SpiralDeltaV / 1000.0:F2} km/s over "
                    + $"{escape.SpiralSeconds / 3600.0:F1} h", at, warn);
                at.Y += FlightUi.Line;

                _sprites.DrawString(_font,
                    $"  (an impulsive escape would be {escape.ImpulsiveDeltaV / 1000.0:F2} km/s, "
                    + $"but that burn is {escape.Orbits:F0} orbits long)", at, dim);
                at.Y += FlightUi.Line;

                _sprites.DrawString(_font,
                    "  a straight-line course cannot be flown from here", at, dim);
                at.Y += FlightUi.Line * 1.4f;
            }
        }

        if (helm.Engaged)
        {
            _sprites.DrawString(_font, $"FLIGHT COMPUTER  {helm.Describe()}", at, new Color(255, 220, 130));
            at.Y += FlightUi.Line;
            _sprites.DrawString(_font,
                $"  elapsed {helm.ElapsedSeconds.ToDouble() / 86400.0:F2} days", at, dim);
        }
        else
        {
            _sprites.DrawString(_font, "ENTER hands the controls to the flight computer", at, dim);
        }
    }

    private static bool JustPressed(KeyboardState keys, Keys key, KeyboardState previous) =>
        keys.IsKeyDown(key) && !previous.IsKeyDown(key);
}
