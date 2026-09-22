using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SolSystem.Core.Numerics;
using SolSystem.Core.Local;
using SolSystem.Core.Orbits;

namespace SolSystem.Client;

/// <summary>
/// The corridor speed gates: hexagonal rings down the docking corridor, coloured by whether the
/// ship's current closing speed is survivable at each ring's distance from the port.
/// </summary>
/// <remarks>
/// <para>
/// This is the semi-automatic docking aid. The nav overlay (slice 1) answers "where is the port";
/// the gates answer the question a docking is actually lost by: <em>am I too fast for the room I
/// have left?</em> Each ring lies in a plane perpendicular to the corridor at a fixed distance
/// from the port, so the ladder of rings is a ruler laid down the approach, and each ring's colour
/// is a verdict about passing it at the ship's present speed.
/// </para>
/// <para>
/// <b>RED is the braking law, not "above the speed limit".</b> A ring is red when the distance the
/// ship needs to stop — <c>v²/2a</c> of powered braking plus <c>v·t_turn</c> of coasting through
/// the reversal, computed by <see cref="Autohelm.BrakingDistance"/> with the helm's own
/// <see cref="Autohelm.ReversalSeconds"/> — exceeds the corridor that remains beyond that ring.
/// The engine fires along the nose, so every metre of deceleration is prepaid with thirty seconds
/// of turning round, and a warning that ignores the turn is a warning that arrives half a minute
/// late. The red front on the ladder <em>is</em> the ship's stopping point, drawn on the corridor:
/// rings inside it are already lost at this speed, rings outside it are still recoverable.
/// A ring is also red when the ship's speed is past the glideslope's target for that range by
/// more than a tight tolerance — the profile is the operations version of the same fact, and the
/// fast side of it is the side that kills.
/// </para>
/// <para>
/// <b>GREEN is on the profile, BLUE is dawdling.</b> The profile is the classical glideslope of
/// <see cref="Glideslope"/> — a straight line from the corridor rate at the corridor's start down
/// to the contact rate at the port — evaluated here exactly as <see cref="Approach"/> builds it,
/// capped by what the drive can shed inside the corridor. The slow side of the tolerance is
/// generous and the fast side tight, because too slow costs a minute and too fast costs the ship.
/// A ship at rest relative to the station is not "below the profile": it is parked at the start
/// of the approach, which is the nominal state, and the ladder reads green.
/// </para>
/// <para>
/// The rings are world geometry, not markers: they are sized to the corridor's own lateral
/// envelope (<see cref="Docking.MaxLateralOffset"/>, one metre — the corridor really is that
/// narrow), projected from un-normalised ship-relative metre vectors so the near rings loom and
/// the far ones converge on the port, and never edge-clamped. Rings the ship has already passed
/// retire; the next ring ahead is the prominent one.
/// </para>
/// </remarks>
internal sealed class CorridorGates
{
    /// <summary>The corridor's nominal length, in metres — the range the approach starts from.</summary>
    /// <remarks>
    /// Two kilometres, matching <see cref="Approach"/>: its run rate is "two kilometres in a few
    /// minutes", and its feasibility cap <c>sqrt(a·r₀)</c> is evaluated over the same length.
    /// </remarks>
    private const double CorridorMetres = 2000.0;

    /// <summary>The rate the corridor is run at before the drive caps it, m/s. Approach's figure.</summary>
    private const double CorridorRate = 10.0;

    /// <summary>The rate at contact, m/s. Positive and never zero, for the reason Approach gives.</summary>
    private const double ContactRate = 0.10;

    /// <summary>
    /// Closing speed below which the ship is parked rather than slow, in m/s.
    /// </summary>
    /// <remarks>
    /// The glideslope governs a ship that is RUNNING the corridor. A ship holding station beside
    /// it is at the start of the approach, not below it — reporting a parked ship as "too slow" at
    /// every range would paint the whole ladder blue in the one state nothing is wrong in. Half
    /// the contact rate, so any genuine motion toward the port is judged against the profile.
    /// </remarks>
    private const double ParkedSpeed = 0.05;

    /// <summary>
    /// How far over the profile a ring tolerates before it goes red, in m/s.
    /// </summary>
    /// <remarks>
    /// Tight, because fast is the lethal side: the contact rate itself, or ten per cent of the
    /// target, whichever is larger. Inside the last hundred metres the band is a tenth of a metre
    /// a second wide.
    /// </remarks>
    private static double FastTolerance(double target) => Math.Max(ContactRate, 0.10 * target);

    /// <summary>
    /// How far under the profile a ring tolerates before it goes blue, in m/s.
    /// </summary>
    /// <remarks>
    /// Generous, because slow only costs time: half the target, with a floor so the shallow end
    /// of the corridor is not hair-trigger either.
    /// </remarks>
    private static double SlowTolerance(double target) => Math.Max(0.30, 0.50 * target);

    /// <summary>
    /// The ring stations: distances from the port down the corridor, in metres.
    /// </summary>
    /// <remarks>
    /// Dense near the port, where the envelope is metres wide and the speed budget is tenths of a
    /// metre a second, and sparse far out, where the corridor is a line to run down. The ladder
    /// reaches past the 400 m spawn standoff to the two kilometres the nominal approach starts
    /// from, because the gates are most useful before the corridor looks close.
    /// </remarks>
    private static readonly double[] Ladder =
    [
        25.0, 50.0, 75.0, 100.0,
        150.0, 200.0, 250.0, 300.0, 350.0, 400.0,
        500.0, 600.0, 700.0, 800.0, 900.0, 1000.0,
        1200.0, 1400.0, 1600.0, 1800.0, 2000.0,
    ];

    /// <summary>Show the gates only within this range of the port, in metres.</summary>
    /// <remarks>
    /// Three kilometres: a little past the end of the ladder. Further out the ship is not on an
    /// approach and the rings are clutter — and at planetary ranges a one-metre ring is a
    /// sub-pixel lie about what is visible.
    /// </remarks>
    private const double ShowWithinMetres = 3000.0;

    /// <summary>The three verdicts a gate can give, in order of how loudly they demand attention.</summary>
    private enum State
    {
        /// <summary>Closing speed on the glideslope, or parked.</summary>
        Green,

        /// <summary>Closing speed well under the profile: safe, and slow.</summary>
        Blue,

        /// <summary>Cannot stop in the corridor left, or past the profile's tight fast side.</summary>
        Red,
    }

    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;

    internal CorridorGates(SpriteBatch sprites, Texture2D pixel)
    {
        _sprites = sprites;
        _pixel = pixel;
    }

    /// <summary>
    /// Draws the ladder for one frame, through the camera the frame was rendered with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read-only, like the nav overlay: the gates are a view of the ship's state and the corridor,
    /// not a participant in either.
    /// </para>
    /// <para>
    /// The projection goes through the near pass's own VIEW MATRIX, not a basis rebuilt from the
    /// ship's origin, and that distinction is the difference between rings and fiction. The
    /// cockpit camera sits fifty-nine metres ahead of the ship's origin and the chase camera a
    /// hundred and thirty behind it; a gate fifty metres out projected from the origin lands
    /// behind the real camera, or a ring the ship is about to thread projects ten times too
    /// small. The first version of this did exactly that, and the nearest gates simply vanished.
    /// Bodies and markers at kilometre range never notice a fifty-metre parallax; a one-metre
    /// ring fifty metres away notices nothing else.
    /// </para>
    /// </remarks>
    internal void Draw(
        GraphicsDevice device,
        FlightSession session,
        Flight flight,
        Matrix nearView,
        float fieldOfViewDegrees)
    {
        DockingPort port = session.Station.Port;

        // The ship's corridor geometry, in the same convention DrawFlightPanel reports: the offset
        // from the port, and its component along the corridor axis, which points AWAY from the
        // port — so a ship approaching has a falling `along` and passes each gate as it crosses
        // the gate's own station.
        Fix128Vec offset = flight.Ship.Position - port.Position;
        double range = offset.Length.ToDouble();
        if (range > ShowWithinMetres || offset.IsZero)
        {
            return;
        }

        double along = Fix128Vec.Dot(offset, port.Axis).ToDouble();

        // Closing speed, positive approaching — the Docking.Evaluate convention, not the nav
        // overlay's. Measured along the live bearing to the port rather than the fixed axis, for
        // the reason Approach records: the two disagree once the ship is past the port.
        Fix128Vec toPort = offset.IsZero ? -port.Axis : -offset.Normalized();
        Fix128Vec relative = flight.Ship.Velocity - session.Station.Velocity;
        double closing = Fix128Vec.Dot(relative, toPort).ToDouble();

        // The drive's deceleration, computed the way the approach law computes it — thrust over
        // mass, capped by the hull's ceiling — because the braking law has to be the same law the
        // ship can actually fly.
        double accel = DriveAcceleration(flight.Ship);

        // The stopping distance at the current closing speed. Only a closing ship needs one: a
        // ship moving away is not spending corridor at all.
        double braking = closing > 0.0 && accel > 0.0
            ? Autohelm.BrakingDistance(Fix128.FromDouble(closing), Fix128.FromDouble(accel),
                Autohelm.ReversalSeconds).ToDouble()
            : 0.0;

        // The reference glideslope for the corridor, built as Approach builds it: the run rate
        // capped by what the drive can shed over the corridor's length. The law itself lives in
        // fixed point now; the client converts at the boundary.
        double initial = Math.Max(ContactRate,
            Math.Min(CorridorRate, Math.Sqrt(accel * CorridorMetres)));
        var profile = Glideslope.For(
            Fix128.FromDouble(CorridorMetres), Fix128.FromDouble(initial), Fix128.FromDouble(ContactRate));

        float width = device.Viewport.Width;
        float height = device.Viewport.Height;
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;
        float tanY = MathF.Tan(MathHelper.ToRadians(fieldOfViewDegrees) * 0.5f);
        float tanX = tanY * (width / height);

        // Through the near pass's view matrix, so a vertex lands exactly where the rasteriser
        // puts the same point on the station's hull: view space, camera at the origin, looking
        // down -z. A ring the camera is not looking at is simply not drawn — no edge clamp.
        bool Project(Vector3 v, out Vector2 screen)
        {
            Vector3 view = Vector3.Transform(v, nearView);
            float z = -view.Z;
            if (z < 0.3f)
            {
                // Behind the camera, or so close to its plane the divide explodes. The near
                // pass clips at half a metre; this is the same decision made cheaply.
                screen = default;
                return false;
            }

            screen = new Vector2(
                halfWidth + (view.X / z / tanX * halfWidth),
                halfHeight - (view.Y / z / tanY * halfHeight));
            return true;
        }

        // The corridor's perpendiculars, seeded the way the station's own transform seeds them:
        // any perpendicular pair will do, because the corridor is symmetric about its axis.
        Vector3 axis = Unit(port.Axis);
        Vector3 seed = MathF.Abs(axis.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 across = Vector3.Normalize(Vector3.Cross(axis, seed));
        Vector3 up2 = Vector3.Normalize(Vector3.Cross(axis, across));

        Vector3 portRel = Unit(port.Position - flight.Ship.Position);
        float radius = (float)Docking.MaxLateralOffset.ToDouble();

        // The gate the ship should fly through next: the nearest station still ahead, which is
        // the FARTHEST one not yet passed — the ship flies the corridor from far to near, so
        // "ahead" means closer to the port than the ship is.
        double nextGate = double.MinValue;
        foreach (double station in Ladder)
        {
            if (station <= along && station > nextGate)
            {
                nextGate = station;
            }
        }

        var green = new Color(150, 220, 175);
        var blue = new Color(115, 165, 230);
        var red = new Color(235, 95, 75);

        _sprites.Begin();

        foreach (double station in Ladder)
        {
            // Retired: the gate's station is FARTHER from the port than the ship's corridor
            // coordinate, so it is behind the ship — the corridor runs from far to near, and a
            // gate is passed when the ship's coordinate falls below the gate's station. Dropped
            // rather than faded: a gate behind the ship says nothing, and a dim one still says
            // it in pixels. (The first version of this retired exactly the wrong set, and the
            // cockpit view of a ship at four hundred metres showed an empty corridor — every
            // gate between the ship and the port had been dropped and every gate behind the
            // ship drawn, invisibly, astern.)
            if (station > along)
            {
                continue;
            }

            State state = Verdict(station, closing, braking, profile);
            Color colour = state switch
            {
                State.Red => red,
                State.Blue => blue,
                _ => green,
            };

            bool next = station == nextGate;
            if (!next)
            {
                colour = new Color(colour.R, colour.G, colour.B, (byte)130);
            }

            Vector3 centreRel = portRel + (axis * (float)station);

            // A hexagon: six vertices on the corridor's lateral envelope, twelve segments —
            // two per edge, so no single segment spans enough of the view to bend the ring
            // where the projection is steep up close.
            var vertices = new Vector3[6];
            for (int k = 0; k < 6; k++)
            {
                float angle = MathF.Tau * k / 6f;
                vertices[k] = centreRel
                    + ((across * MathF.Cos(angle)) + (up2 * MathF.Sin(angle))) * radius;
            }

            for (int k = 0; k < 6; k++)
            {
                Vector3 a = vertices[k];
                Vector3 b = vertices[(k + 1) % 6];
                Vector3 mid = (a + b) * 0.5f;

                Segment(a, mid);
                Segment(mid, b);
            }

            void Segment(Vector3 from, Vector3 to)
            {
                if (Project(from, out Vector2 p0) && Project(to, out Vector2 p1))
                {
                    Line(p0, p1, colour, next ? 2.5f : 1.5f);
                }
            }
        }

        _sprites.End();
    }

    /// <summary>
    /// One gate's verdict about passing it at the ship's present closing speed.
    /// </summary>
    private static State Verdict(double station, double closing, double braking,
        Glideslope profile)
    {
        // Red is the braking law first: past this ring at this speed, the ship cannot stop in
        // the corridor that remains. The reversal is priced in — see the type's remarks.
        if (braking > station)
        {
            return State.Red;
        }

        double target = profile.RateAt(Fix128.FromDouble(station)).ToDouble();

        // Then the profile's fast side, with its tight tolerance. An over-profile ship that can
        // still stop is not yet lost, but it is flying the corridor wrong in the dangerous
        // direction, and that is what red is for.
        if (closing > target + FastTolerance(target))
        {
            return State.Red;
        }

        // Parked is green: a ship at rest relative to the station is at the start of the
        // approach, not below it.
        if (closing < ParkedSpeed || closing >= target - SlowTolerance(target))
        {
            return State.Green;
        }

        return State.Blue;
    }

    /// <summary>The drive's acceleration, capped by the hull's own ceiling — Approach's formula.</summary>
    private static double DriveAcceleration(in Ship ship)
    {
        double accel = ship.Engine.ThrustKilonewtons.ToDouble() / ship.Mass.ToDouble();
        return Math.Min(accel, ship.Engine.MaxAccelerationInMetresPerSecondSquared.ToDouble());
    }

    /// <summary>A hairline, drawn from the one white pixel stretched and turned.</summary>
    private void Line(Vector2 from, Vector2 to, Color colour, float thickness)
    {
        Vector2 d = to - from;
        float length = d.Length();
        if (length < 0.5f)
        {
            return;
        }

        _sprites.Draw(_pixel, from, null, colour, MathF.Atan2(d.Y, d.X),
            new Vector2(0f, 0.5f), new Vector2(length, thickness), SpriteEffects.None, 0f);
    }

    private static Vector3 Unit(Fix128Vec v) => new(
        (float)v.X.ToDouble(), (float)v.Y.ToDouble(), (float)v.Z.ToDouble());
}
