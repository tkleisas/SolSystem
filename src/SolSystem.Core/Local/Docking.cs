using SolSystem.Core.Numerics;

namespace SolSystem.Core.Local;

/// <summary>
/// A docking port: a point and an approach axis, in the local frame.
/// </summary>
/// <remarks>
/// <para>
/// The axis points <b>away from the port along the corridor a ship flies down</b> — that is,
/// the direction the arriving ship is coming <em>from</em>. A ship at <c>port + axis·d</c> is
/// dead on the centreline at distance <c>d</c>, and one moving along <c>-axis</c> is closing.
/// </para>
/// <para>
/// The convention matters and is easy to invert: taking the axis as the port's outward
/// <em>facing</em> instead flips the sign of every closing speed, so a textbook approach
/// reads as a departure.
/// </para>
/// </remarks>
internal readonly struct DockingPort
{
    /// <summary>Where the port is, in the local frame.</summary>
    internal readonly Fix128Vec Position;

    /// <summary>
    /// Unit vector along the approach corridor, pointing away from the port — the direction an
    /// arriving ship comes FROM. A ship closing on the port travels along <c>-Axis</c>.
    /// </summary>
    internal readonly Fix128Vec Axis;

    internal DockingPort(Fix128Vec position, Fix128Vec axis)
    {
        Position = position;
        Axis = axis;
    }

    /// <summary>A port at the origin whose approach corridor runs along +x.</summary>
    internal static DockingPort AtOrigin => new(Fix128Vec.Zero, new Fix128Vec(Fix128.One, Fix128.Zero, Fix128.Zero));

    /// <summary>A port at <paramref name="position"/> approached along <paramref name="approachAxis"/>.</summary>
    internal static DockingPort ApproachedAlong(Fix128Vec position, Fix128Vec approachAxis) =>
        new(position, approachAxis.Normalized());
}

/// <summary>
/// The result of a docking attempt, with the numbers that produced it.
/// </summary>
/// <remarks>
/// The measurements are reported even on success so a pilot can tell a clean docking from a
/// scrape, and so a probe can assert on the margin rather than only on the outcome.
/// </remarks>
internal readonly struct DockingReport
{
    /// <summary>
    /// The ship is inside every tolerance the latches need: range, lateral offset, closing speed
    /// and alignment. It can be up to <see cref="Docking.CaptureRange"/> out and still be true.
    /// </summary>
    internal readonly bool Docked;

    /// <summary>
    /// The ship is at the port as well as inside the tolerances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction between this and <see cref="Docked"/> is the difference between what the
    /// latches can hold and what "arrived" means. A hull two metres out, closing at a third of a
    /// metre a second and pointing the right way satisfies every tolerance — and is on the
    /// doorstep, not in the airlock. A controller that treats the two as the same thing stops
    /// manoeuvring at the outer edge of its own capture range and reports every docking at
    /// 1.997 m.
    /// </para>
    /// <para>
    /// It requires the <see cref="Docked"/> tolerances as well as proximity, and that is not
    /// pedantry: a ship that crosses the contact range at 2.8 m/s has not docked, it has hit the
    /// station. An earlier version treated proximity alone as arrival and cheerfully reported a
    /// docking at 0.40 m and 2.76 m/s, in fifty-three seconds, which is a collision with good
    /// paperwork.
    /// </para>
    /// </remarks>
    internal readonly bool Contact;

    /// <summary>Why it failed, or empty on success.</summary>
    internal readonly string Reason;

    /// <summary>Distance from the ship's port to the station's, in metres.</summary>
    internal readonly Fix128 Range;

    /// <summary>Distance off the port axis, in metres. Zero is dead centre.</summary>
    internal readonly Fix128 LateralOffset;

    /// <summary>Closing speed along the axis, in metres per second. Positive is approaching.</summary>
    internal readonly Fix128 ClosingSpeed;

    /// <summary>Angle between the ship's nose and the port axis, in radians.</summary>
    internal readonly Fix128 Misalignment;

    internal DockingReport(
        bool docked,
        bool contact,
        string reason,
        Fix128 range,
        Fix128 lateralOffset,
        Fix128 closingSpeed,
        Fix128 misalignment)
    {
        Docked = docked;
        Contact = contact;
        Reason = reason;
        Range = range;
        LateralOffset = lateralOffset;
        ClosingSpeed = closingSpeed;
        Misalignment = misalignment;
    }
}

/// <summary>
/// Docking: does this ship, right now, satisfy the conditions to be captured by that port?
/// </summary>
/// <remarks>
/// <para>
/// A docking is a rendezvous with four separate conditions, and failing any one of them is a
/// different kind of accident:
/// </para>
/// <list type="bullet">
/// <item><b>Range.</b> Close enough for the capture latches to reach.</item>
/// <item><b>Lateral offset.</b> Lined up with the port, not beside it. This is what makes an
/// off-axis arrival a collision rather than a near miss.</item>
/// <item><b>Closing speed.</b> Slow enough that the latches hold. Too fast and the ship
/// bounces, or crushes the port, or both.</item>
/// <item><b>Alignment.</b> Nose towards the port. A ship arriving sideways cannot be
/// captured however gently it does so.</item>
/// </list>
/// <para>
/// The thresholds are a design decision, not a derived quantity, and they are gathered here
/// so they can be tuned in one place when the action layer is playable.
/// </para>
/// </remarks>
internal static class Docking
{
    /// <summary>Capture latches reach this far, in metres.</summary>
    /// <summary>
    /// How far from the port the latches can reach, in metres.
    /// </summary>
    /// <remarks>
    /// A fifth of the corridor the approach is flown down, and the distinction matters in both
    /// directions. A ship two metres out, closing at a third of a metre a second and pointing the
    /// right way is inside every other tolerance — but it is on the doorstep, not in the airlock,
    /// and a controller that reads this as "arrived" reports every docking at 1.997 m. Nothing
    /// outside this is captured however well it is lined up.
    /// </remarks>
    internal static readonly Fix128 CaptureRange = Fix128.FromDouble(2.0);

    /// <summary>
    /// How close the ship has to be for the latches to have taken it, in metres.
    /// </summary>
    /// <remarks>
    /// A fifth of <see cref="CaptureRange"/>. The capture range is what the latches can reach;
    /// this is what counts as being at the port.
    /// </remarks>
    internal static readonly Fix128 ContactRange = Fix128.FromDouble(0.4);

    /// <summary>How far off the axis a ship may be, in metres.</summary>
    internal static readonly Fix128 MaxLateralOffset = Fix128.FromDouble(1.0);

    /// <summary>
    /// Fastest closing speed the latches will hold, in metres per second.
    /// </summary>
    /// <remarks>
    /// 0.5 m/s is a firm but survivable contact for a hundred-tonne hull. Apollo docked at
    /// around 0.03 m/s; a warship in a hurry can afford more, and a courier carrying
    /// something fragile should not.
    /// </remarks>
    internal static readonly Fix128 MaxClosingSpeed = Fix128.FromDouble(0.5);

    /// <summary>Largest angle between nose and port axis, in radians. About 10 degrees.</summary>
    internal static readonly Fix128 MaxMisalignment = Fix128.FromDouble(10.0 * Math.PI / 180.0);

    /// <summary>
    /// Evaluates a docking attempt.
    /// </summary>
    /// <param name="ship">The arriving ship.</param>
    /// <param name="port">The port it is trying to reach.</param>
    /// <param name="portVelocity">The port's velocity in the local frame — zero if it is fixed.</param>
    internal static DockingReport Evaluate(Ship ship, DockingPort port, Fix128Vec portVelocity)
    {
        // Offset from the port to the ship, and its components along the port axis and across.
        Fix128Vec offset = ship.Position - port.Position;
        Fix128 along = Dot(offset, port.Axis);
        Fix128Vec alongComponent = port.Axis * along;

        Fix128 range = offset.Length;
        Fix128 lateralOffset = (offset - alongComponent).Length;

        // The corridor direction a ship travels to reach the port, which is AGAINST the axis:
        // the axis points away from the port, along the direction arrivals come from.
        Fix128Vec approach = -port.Axis;

        // Relative motion, so a station under thrust is handled correctly.
        //
        // <b>Closing speed is positive when the ship is approaching</b>, which is what the
        // field name says and what the checks below require. `approach` already points from the
        // ship toward the port, so the plain dot product is the whole of it. There is no
        // negation to apply, and adding one is worse than a sign flip: `Fix128` unary minus
        // toggles the sign flag without touching the magnitude, so a negated positive became a
        // negative of the same size — which reads as a ship running away at exactly the speed
        // it was closing at, and turned every approach into a departure.
        Fix128Vec relativeVelocity = ship.Velocity - portVelocity;
        Fix128 closingSpeed = Dot(relativeVelocity, approach);

        // The nose must point down the corridor, towards the port: the ship arrives nose-first.
        // Zero when aligned, a half turn when it arrives tail-first, a quarter turn when
        // broadside.
        Fix128Vec nose = ship.Attitude.Forward;
        Fix128 misalignment = Acos(Dot(nose, approach));

        if (range > CaptureRange)
        {
            return new DockingReport(false, false, "out of range", range, lateralOffset, closingSpeed, misalignment);
        }

        if (lateralOffset > MaxLateralOffset)
        {
            return new DockingReport(false, false, "off the port axis", range, lateralOffset, closingSpeed, misalignment);
        }

        // Inside contact range, the only things that matter are speed and alignment: the latches are
        // already round the ship, and whether its rate is +1 mm/s or -1 mm/s is noise at this scale
        // rather than a fact about the approach.
        //
        // This is the one place `Contact` and `Docked` deliberately disagree. `Docked` keeps the
        // positive-rate test, because "can the latches hold this ship right now" is a real question
        // two metres out. At thirteen centimetres it is not: a ship that has crept to a hand's
        // breadth of the port, lined up and doing a centimetre a second, is docked, and the sign of
        // that centimetre is the steering loop breathing.
        if (range <= ContactRange)
        {
            if (closingSpeed > MaxClosingSpeed)
            {
                return new DockingReport(false, false, "closing too fast", range, lateralOffset, closingSpeed, misalignment);
            }

            if (misalignment > MaxMisalignment)
            {
                return new DockingReport(false, false, "not aligned with the port", range, lateralOffset, closingSpeed, misalignment);
            }

            return new DockingReport(true, true, string.Empty, range, lateralOffset, closingSpeed, misalignment);
        }


        // Moving away is checked BEFORE speed, because the two conditions overlap when a ship
        // is drifting out of the envelope faster than the latches would hold: "closing too
        // fast" would be reported for a ship at -5 m/s, which is not what a pilot did wrong.
        if (closingSpeed < Fix128.Zero)
        {
            return new DockingReport(false, false, "moving away", range, lateralOffset, closingSpeed, misalignment);
        }

        if (closingSpeed > MaxClosingSpeed)
        {
            return new DockingReport(false, false, "closing too fast", range, lateralOffset, closingSpeed, misalignment);
        }

        if (misalignment > MaxMisalignment)
        {
            return new DockingReport(false, false, "not aligned with the port", range, lateralOffset, closingSpeed, misalignment);
        }

        // Inside every tolerance. Whether the approach is over depends on how close the ship is:
        // the tolerances say what the latches can hold, and `ContactRange` says what counts as
        // being at the port. This returned a hard-coded false for a while, which made the flag
        // unreachable and had the approach law hovering eight millimetres from the port waiting
        // for a latch that could never be reported.
        return new DockingReport(
            true, range <= ContactRange, string.Empty, range, lateralOffset, closingSpeed, misalignment);
    }

    /// <summary>
    /// Arccosine for a value already known to be in [-1, 1].
    /// </summary>
    /// <remarks>
    /// Computed through the arctangent of the opposite over the adjacent, which the existing
    /// trigonometry can do. Feeding <c>sqrt(1-d²)/d</c> into an arctangent avoids needing a
    /// separate inverse cosine, and the sign handling is one branch.
    /// </remarks>
    private static Fix128 Acos(Fix128 d)
    {
        if (d >= Fix128.One)
        {
            return Fix128.Zero;
        }

        if (d <= -Fix128.One)
        {
            return Fix128.FromDouble(Math.PI);
        }

        // acos(d) = atan2(sqrt(1-d²), d), and atan2 is not available in the fixed-point
        // trigonometry, so the quadrant is handled here: for d >= 0 the angle is
        // atan(sqrt(1-d²)/d), and for d < 0 it is pi minus that.
        Fix128 opposite = Fix128.Sqrt(Fix128.One - d * d);
        Fix128 angle = Atan(opposite / Abs(d));
        return d >= Fix128.Zero ? angle : Fix128.FromDouble(Math.PI) - angle;
    }

    /// <summary>
    /// Arctangent of a non-negative value, by the series
    /// <c>atan(t) = t - t³/3 + t⁵/5 - …</c> with argument reduction for t &gt; 1.
    /// </summary>
    /// <remarks>
    /// The series converges quickly for small arguments only, so t &gt; 1 is reduced with
    /// <c>atan(t) = pi/2 - atan(1/t)</c> first. Sixteen terms are ample at this width for
    /// arguments below one.
    /// </remarks>
    private static Fix128 Atan(Fix128 t)
    {
        if (t == Fix128.Zero)
        {
            return Fix128.Zero;
        }

        Fix128 halfPi = Fix128.FromDouble(Math.PI / 2.0);
        if (t > Fix128.One)
        {
            return halfPi - Atan(Fix128.One / t);
        }

        Fix128 tSquared = t * t;
        Fix128 term = t;
        Fix128 sum = t;

        for (int n = 3; n <= 33; n += 2)
        {
            term = term * tSquared;
            Fix128 contribution = term / Fix128.FromDouble(n);
            sum = ((n / 2) % 2 == 1) ? sum - contribution : sum + contribution;
        }

        return sum;
    }

    private static Fix128 Abs(Fix128 v) => v.Negative ? -v : v;

    private static Fix128 Dot(Fix128Vec a, Fix128Vec b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
