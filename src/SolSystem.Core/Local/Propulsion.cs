using SolSystem.Core.Numerics;

namespace SolSystem.Core.Local;

/// <summary>
/// A source of gravity in the local frame, positioned in metres.
/// </summary>
/// <remarks>
/// A position and a gravitational parameter is all a two-body field needs. A moon, a planet
/// and — once the action layer exists — another ship all reduce to this, so the integrator
/// never has to know what it is falling towards.
/// </remarks>
internal readonly struct GravitySource
{
    internal readonly Fix128Vec Position;
    internal readonly Fix128 GravitationalParameter;

    internal GravitySource(Fix128Vec position, Fix128 gravitationalParameter)
    {
        Position = position;
        GravitationalParameter = gravitationalParameter;
    }

    /// <summary>The source at the origin with <paramref name="gm"/>.</summary>
    internal static GravitySource AtOrigin(Fix128 gm) => new(Fix128Vec.Zero, gm);

    /// <summary>Acceleration at <paramref name="position"/> towards this source.</summary>
    /// <remarks>
    /// <para>
    /// <c>a = GM·r̂/|r|²</c>, computed as <c>(GM / |r| / |r|) · r</c>. Both the width and the
    /// order are load-bearing.
    /// </para>
    /// <para>
    /// <b>The local frame is Q64.64 in metres.</b> A Q32.32 value cannot do this job in any
    /// unit, and both halves of that were measured rather than argued:
    /// </para>
    /// <list type="bullet">
    /// <item>In <b>metres</b>, a Q32.32 square cannot exceed 2.147 × 10⁹, so a position of
    /// more than about 46 km overflows <c>|r|²</c> — and a low Earth orbit is 150 times past
    /// that. The failure is silent: the sum wraps negative and the gravity becomes
    /// nonsense.</item>
    /// <item>In <b>megametres</b> the squares fit, but Earth's surface gravity becomes
    /// 8.13 × 10⁻⁶, leaving 16 bits of significand. The position increment
    /// <c>a·dt²/2</c> at a 120 Hz tick then comes to <b>two raw units</b>, so an orbit is
    /// almost entirely rounding: the measured energy and angular momentum drifted by 0.8 %
    /// over a single low Earth revolution.</item>
    /// </list>
    /// <para>
    /// Q64.64 in metres has neither problem: 5.4 × 10⁻²⁰ m of resolution and a reach of
    /// 9.2 × 10¹⁸ m. It is the same width the solar frame uses, so there is one numeric
    /// type to reason about rather than two.
    /// </para>
    /// <para>
    /// The two divisions rather than one are also deliberate. Two land directly on the
    /// acceleration and hold 2.5 × 10⁻¹¹ of relative accuracy; three
    /// (<c>GM/|r|³</c> then times <c>|r|</c>) leaves the quotient near 10⁻⁶ with a fraction
    /// of its bits, and the final multiply magnifies that to 3.7 × 10⁻⁵.
    /// </para>
    /// </remarks>
    internal Fix128Vec AccelerationAt(Fix128Vec position)
    {
        Fix128Vec offset = Position - position;
        Fix128 r = offset.Length;

        if (r <= Fix128.Zero)
        {
            throw new InvalidOperationException("A gravity source must not sit on the ship.");
        }

        // Three divisions, because the offset is multiplied in afterwards: GM/r² is the
        // acceleration MAGNITUDE, and the vector that carries the direction has length r,
        // so the scale that multiplies it has to be GM/r³. Written as successive divisions
        // rather than GM/(r·r·r) because r³ overflows Q64.64 at about 2.6 AU — well inside
        // the frame's reach.
        Fix128 scale = GravitationalParameter / r / r / r;
        return offset * scale;
    }

}

/// <summary>
/// A rocket engine, by the two numbers that actually matter.
/// </summary>
/// <remarks>
/// Thrust and specific impulse. Everything else — exhaust velocity, mass flow, burn time,
/// delta-v — is derived, so the numbers cannot drift into disagreeing with each other the
/// way they can when a designer edits four fields by hand.
/// </remarks>
internal readonly struct Engine
{
    /// <summary>Standard gravity, m/s². The conventional unit specific impulse is quoted in.</summary>
    internal static readonly Fix128 StandardGravity = Fix128.FromDouble(9.80665);

    /// <summary>Maximum thrust, in kilonewtons.</summary>
    internal readonly Fix128 ThrustKilonewtons;

    /// <summary>Specific impulse, in seconds.</summary>
    internal readonly Fix128 SpecificImpulse;

    /// <summary>
    /// The largest acceleration this drive may be run at, in m/s².
    /// </summary>
    /// <remarks>
    /// A property of the drive-and-hull pairing rather than of the engine alone, because it
    /// is really a statement about what the ship can survive. A crewed hull is held between
    /// 0.1 g and 1 g: below 0.1 g a fusion torch is not worth lighting for manoeuvring, and
    /// above 1 g the crew is being squashed. A mechanical hull — an Illuminus shell — has no
    /// flesh to squash and can take 10 to 100 g, at which point the limit becomes the
    /// structural frame carrying the thrust rather than anything inside it.
    /// </remarks>
    internal readonly Fix128 MaxAccelerationInMetresPerSecondSquared;

    internal Engine(Fix128 thrustKilonewtons, Fix128 specificImpulse, Fix128 maxAccelerationInMetresPerSecondSquared)
    {
        ThrustKilonewtons = thrustKilonewtons;
        SpecificImpulse = specificImpulse;
        MaxAccelerationInMetresPerSecondSquared = maxAccelerationInMetresPerSecondSquared;
    }

    /// <summary>An engine for a crewed hull, limited to <paramref name="maxG"/>.</summary>
    internal static Engine Crewed(Fix128 thrustKilonewtons, Fix128 specificImpulse, Fix128 maxG = default) =>
        new(thrustKilonewtons, specificImpulse,
            (maxG == Fix128.Zero ? Fix128.One : maxG) * StandardGravity);

    /// <summary>
    /// An engine for a mechanical hull, limited to <paramref name="maxG"/>.
    /// </summary>
    /// <remarks>
    /// Defaults to 10 g, the bottom of the shell band: enough to be recognisably inhuman,
    /// and the point at which the ship's structure rather than its occupant becomes the
    /// constraint.
    /// </remarks>
    internal static Engine Shell(Fix128 thrustKilonewtons, Fix128 specificImpulse, Fix128 maxG = default) =>
        new(thrustKilonewtons, specificImpulse,
            (maxG == Fix128.Zero ? Fix128.FromDouble(10.0) : maxG) * StandardGravity);

    /// <summary>The crewed hull's acceleration band, in g.</summary>
    internal static readonly Fix128 CrewedMinimumG = Fix128.FromDouble(0.1);

    /// <summary>The crewed hull's ceiling, in g.</summary>
    internal static readonly Fix128 CrewedMaximumG = Fix128.One;

    /// <summary>The mechanical hull's floor, in g.</summary>
    internal static readonly Fix128 ShellMinimumG = Fix128.FromDouble(10.0);

    /// <summary>The mechanical hull's ceiling, in g.</summary>
    internal static readonly Fix128 ShellMaximumG = Fix128.FromDouble(100.0);

    /// <summary>Exhaust velocity in m/s: <c>Isp · g₀</c>.</summary>
    /// <remarks>
    /// This is what a delta-v budget is measured against, and the reason specific impulse is
    /// the single most important number in the design's economy: the Workers' antimatter
    /// drive and the Illuminus' fusion torch differ here before they differ anywhere else.
    /// </remarks>
    internal Fix128 ExhaustVelocityMetresPerSecond => SpecificImpulse * StandardGravity;

    /// <summary>
    /// Propellant mass flow at full throttle, in tonnes per second.
    /// </summary>
    /// <remarks>
    /// <c>ṁ = F / vₑ</c>. A kilonewton is 1000 N and a tonne is 1000 kg, so the two factors
    /// of 1000 cancel and this is numerically <c>thrust / exhaust velocity in m/s</c>.
    /// </remarks>
    internal Fix128 MassFlowTonnesPerSecond =>
        ThrustKilonewtons / ExhaustVelocityMetresPerSecond;
}

/// <summary>Conversions the local frame needs, and the units it works in.</summary>
/// <remarks>
/// The frame's unit is the <b>metre</b>, for seconds and tonnes alongside it. A megametre
/// would give the frame more reach than it needs and cost three decimal digits of
/// acceleration precision, which at a 120 Hz tick is the difference between a stable orbit
/// and a rounding artefact.
/// </remarks>
internal static class Units
{
    /// <summary>Kilograms in a tonne.</summary>
    internal static readonly Fix128 KilogramsPerTonne = Fix128.FromDouble(1000.0);

    /// <summary>Kilonewtons per tonne, which is exactly 1 m/s².</summary>
    internal static readonly Fix128 KilonewtonsPerTonneToMetresPerSecondSquared = Fix128.One;
}
