using SolSystem.Core.Numerics;

namespace SolSystem.Core.Local;

/// <summary>
/// The approach profile: how fast to close, as a function of how far there is left to go.
/// </summary>
/// <remarks>
/// <para>
/// This is not invented here. It is the <b>classical glideslope</b> of Hablani, Tapper and
/// Dana-Bashian, <i>Guidance and Relative Navigation for Autonomous Rendezvous in a Circular
/// Orbit</i>, Journal of Guidance, Control and Dynamics, 2002 — the standard reference for the
/// terminal phase of a rendezvous, first defined for the Space Shuttle by Pearson in 1989 and in
/// use for every approach since. <c>docs/TRIP-ENERGY.md</c> §17 records where it comes from and why
/// it is shaped the way it is.
/// </para>
/// <para>
/// Hablani's profile is an exponential in <em>time</em>. Writing <c>ρ</c> for the range and
/// <c>ρ̇</c> for its rate, with <c>ρ̇₀</c> and <c>ρ̇_T</c> the commanded rates at the start and at
/// contact and <c>λ = (ρ̇₀ − ρ̇_T)/ρ₀</c>:
/// </para>
/// <code>
///   ρ(τ) = ρ₀·e^(λτ) + ρ̇_T·ρ₀/(ρ̇₀ − ρ̇_T)·(e^(λτ) − 1)
///   ρ̇(τ) = ρ̇₀·e^(λτ)
/// </code>
/// <para>
/// Eliminating the time between those two gives something much better than an exponential, and it
/// is the reason the profile is called a <em>glideslope</em> rather than a curve. Working in closing
/// rate <c>v = −ρ̇</c>, which is positive when approaching:
/// </para>
/// <code>
///   v(r) = v_T + (v₀ − v_T)·r/r₀
/// </code>
/// <para>
/// <b>A straight line in the range-rate plane.</b> That is an algebraic identity, not an
/// approximation — the exponential in time is exactly a straight line in phase space — and it is
/// what makes the profile usable as a control law: the commanded rate at any range is one multiply
/// and one add, with no exponentials and no integration.
/// </para>
/// <para>
/// The two rates are the whole parameterisation, and they are both physical. <c>v_T</c> is the rate
/// at contact, which the capture latches bound from above; <c>v₀</c> at <c>r₀</c> is how briskly
/// the corridor is run. Everything between them follows. The slope <c>(v₀ − v_T)/r₀</c> is also the
/// reciprocal of the time constant, so the profile's duration is <c>r₀/(v₀ − v_T)</c> — one number
/// that says how long the approach takes, which is exactly what a player wants to know.
/// </para>
/// <para>
/// Two consequences worth stating because they are what the earlier hand-rolled laws got wrong.
/// <b>The commanded rate never reaches zero</b>: at contact it is <c>v_T</c>, so the ship is always
/// still moving when it arrives, which is what a latch needs — an envelope that requires a positive
/// closing rate will refuse a ship that has stopped dead on the axis, which is the failure mode
/// that cost this project several days. And <b>there is no gain to tune</b>: the profile is set by
/// the rates, and the controller's only job is to sit on the line.
/// </para>
/// </remarks>
internal readonly struct Glideslope
{
    /// <summary>Range at which the approach starts, in metres.</summary>
    internal readonly Fix128 Range;

    /// <summary>Closing rate at <see cref="Range"/>, in metres per second. Positive is approaching.</summary>
    internal readonly Fix128 InitialRate;

    /// <summary>Closing rate at contact, in metres per second. Positive, and never zero.</summary>
    internal readonly Fix128 ContactRate;

    internal Glideslope(Fix128 range, Fix128 initialRate, Fix128 contactRate)
    {
        Range = range;
        InitialRate = initialRate;
        ContactRate = contactRate;
    }

    /// <summary>
    /// A glideslope from the corridor length and the two rates.
    /// </summary>
    /// <param name="range">The length of the corridor, in metres.</param>
    /// <param name="initialRate">
    /// The rate to fly at the far end. Bounded by what the drive can shed in the corridor,
    /// which <see cref="Feasible"/> checks.
    /// </param>
    /// <param name="contactRate">
    /// The rate at contact. Bounded from above by the capture latches and from below by the need to
    /// keep moving — see <see cref="Docking.MaxClosingSpeed"/>.
    /// </param>
    internal static Glideslope For(Fix128 range, Fix128 initialRate, Fix128 contactRate) =>
        new(Fix128.Max(range, Fix128.FromDouble(1e-6)), initialRate, contactRate);

    /// <summary>
    /// The commanded closing rate at a range, in metres per second.
    /// </summary>
    /// <remarks>
    /// The straight line, clamped at the far end so that a ship beyond the corridor start is given
    /// the initial rate rather than a rate extrapolated past it. Past contact the line would go
    /// negative — commanding the ship to retreat — so it is floored there too. Evaluated in
    /// <see cref="Fix128"/> throughout: one divide, one multiply, one add, all exact.
    /// </remarks>
    internal Fix128 RateAt(Fix128 range)
    {
        Fix128 t = Fix128.Clamp(range / Range, Fix128.Zero, Fix128.One);
        return ContactRate + ((InitialRate - ContactRate) * t);
    }

    /// <summary>How long the profile takes end to end, in seconds, or null if it never arrives.</summary>
    /// <remarks>
    /// <c>r₀/(v₀ − v_T)</c>, which is the reciprocal of the slope: fly the line from one end to the
    /// other and the time falls out. Null if the two rates are equal, which is a corridor flown
    /// at a constant rate — legal, and it never arrives. The double version reported
    /// <c>double.PositiveInfinity</c>; there is no infinity in Q64.64, so the honest answer is
    /// "no answer", and a display that wants one prints "never".
    /// </remarks>
    internal Fix128? DurationSeconds =>
        InitialRate > ContactRate ? Range / (InitialRate - ContactRate) : null;

    /// <summary>
    /// The time constant of the equivalent exponential decay, in seconds.
    /// </summary>
    /// <remarks>
    /// The same number as <see cref="DurationSeconds"/> by a different route, kept separate because
    /// the two are conceptually different: one is how long the approach takes, the other is the
    /// <c>1/λ</c> of Hablani's profile. A test asserts they agree, which is what catches a sign
    /// slip in either.
    /// </remarks>
    internal Fix128? TimeConstantSeconds => DurationSeconds;

    /// <summary>
    /// Whether a ship at rest can actually fly this profile with a given acceleration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The glideslope is a line, and a ship following it decelerates the whole way. The deceleration
    /// required is <c>|dv/dt| = |dv/dr|·v</c>, which along the line is
    /// <c>((v₀ − v_T)/r₀)·v</c> — largest where the ship is fastest, so the binding constraint is at
    /// the far end:
    /// </para>
    /// <code>
    ///   a_required = (v₀ − v_T)·v₀/r₀
    /// </code>
    /// <para>
    /// A drive that cannot make that cannot fly the profile, and asking for it anyway produces a
    /// ship that drifts above the line and arrives too fast. This is the check that a corridor
    /// length and a pair of rates are mutually possible, and it is worth making explicit because
    /// the failure is silent: the numbers all look reasonable and the ship simply misses.
    /// </para>
    /// <para>
    /// A reversing ship needs more than this, because it cannot decelerate at all for the half
    /// minute it spends coming about. That cost is <see cref="Approach"/>'s to pay, not the
    /// profile's.
    /// </para>
    /// </remarks>
    internal bool Feasible(Fix128 acceleration) =>
        acceleration > Fix128.Zero && (InitialRate - ContactRate) * InitialRate / Range <= acceleration;

    public override string ToString()
    {
        string duration = DurationSeconds is Fix128 d ? $"{d.ToDouble():F0} s" : "never";
        return $"{InitialRate.ToDouble():F2} m/s at {Range.ToDouble():F0} m to "
            + $"{ContactRate.ToDouble():F3} m/s at contact ({duration})";
    }
}
