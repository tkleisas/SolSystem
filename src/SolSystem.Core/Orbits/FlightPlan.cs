using SolSystem.Core.Numerics;

namespace SolSystem.Core.Orbits;

/// <summary>
/// One way of getting from here to there: how long it takes, and what it costs.
/// </summary>
/// <param name="Name">What the flight computer calls it.</param>
/// <param name="Seconds">Time from departure to arrival.</param>
/// <param name="DeltaV">The velocity change the tanks have to pay for, in m/s.</param>
/// <param name="PeakSpeed">Fastest the ship moves relative to the Sun, in m/s. Zero when ballistic.</param>
/// <param name="Feasible">Whether the ship's remaining delta-v covers it.</param>
/// <param name="Note">The one thing a pilot needs to know about this option.</param>
internal readonly record struct TransferOption(
    string Name,
    double Seconds,
    double DeltaV,
    double PeakSpeed,
    bool Feasible,
    string Note);

/// <summary>
/// The trajectory planner: what the flight computer offers when you point at somewhere.
/// </summary>
/// <remarks>
/// <para>
/// <b>There are two ways to cross the solar system in this setting and they differ by an order of
/// magnitude in both directions.</b> A torch ship can point at a destination and burn the whole way,
/// turning over at the midpoint to slow down; it can also switch the engine off, fall along a
/// Keplerian ellipse, and arrive months later having spent almost nothing. Neither is a mistake and
/// the choice between them is the game.
/// </para>
/// <para>
/// From Earth to Mars, as this planner computes it:
/// </para>
/// <list type="bullet">
/// <item><description><b>Direct at full thrust</b> — 33 days, 111 km/s.</description></item>
/// <item><description><b>Direct at a quarter thrust</b> — 65 days, 56 km/s. Half the fuel for twice the time, exactly.</description></item>
/// <item><description><b>Ballistic</b> — 259 days, 5.6 km/s. Eight times slower for a twentieth of the fuel.</description></item>
/// </list>
/// <para>
/// A ship with 403 km/s aboard can reach Jupiter direct at full thrust for 314 of them, which is a
/// one-way trip, or ballistic for 14 in two and a half years. That is what "limited resources is the
/// name of the game" means in numbers.
/// </para>
/// </remarks>
internal static class FlightPlan
{
    /// <summary>The Sun's gravitational parameter, km³/s². The frame's unit, so everything below is km and s.</summary>
    private const double SunGmKm = 1.32712440018e11;

    /// <summary>Kilometres in an astronomical unit.</summary>
    internal const double KilometresPerAu = 149_597_870.7;

    /// <summary>
    /// How much of full thrust the economy option uses.
    /// </summary>
    /// <remarks>
    /// A quarter, and the arithmetic behind that is worth stating because it is the whole shape of
    /// the choice. On a constant-thrust flip-and-burn, time goes as <c>1/sqrt(a)</c> and delta-v as
    /// <c>sqrt(a)</c>, so quartering the thrust halves the delta-v and doubles the time. There is no
    /// cleverer ratio: every point on the curve is the same trade at a different exchange rate, and
    /// the quarter mark is offered because it is the one a person can hold in their head.
    /// </remarks>
    private const double EconomyThrottle = 0.25;

    /// <summary>
    /// The centroid of the departure and arrival radii, used as the gravity to fight.
    /// </summary>
    /// <remarks>
    /// The planner does not integrate a trajectory and does not pretend to. What it does is account
    /// for the Sun's pull as a single average figure along the path, which is defensible because the
    /// thrust is 6.6 times the Sun's gravity at one astronomical unit: gravity is a correction of
    /// about ten per cent on a direct crossing, not the mechanism. On the ballistic option it IS the
    /// mechanism, and that option is solved exactly.
    /// </remarks>
    private static double MeanGravity(double fromKm, double toKm)
    {
        double middle = (fromKm + toKm) * 0.5;
        return SunGmKm / (middle * middle) * 1000.0;    // km³/s² / km² = km/s², then m/s²
    }

    /// <summary>
    /// Every way the ship can get from one heliocentric radius to another.
    /// </summary>
    /// <param name="fromKm">The ship's current distance from the Sun, km.</param>
    /// <param name="targetKm">The destination's distance from the Sun, km.</param>
    /// <param name="maxAcceleration">Full thrust divided by mass, m/s².</param>
    /// <param name="deltaVAvailable">What is left in the tanks, m/s.</param>
    internal static List<TransferOption> Options(
        double fromKm, double targetKm, double maxAcceleration, double deltaVAvailable)
    {
        var options = new List<TransferOption>();

        double straight = Math.Abs(targetKm - fromKm) * 1000.0;    // metres
        double gravity = MeanGravity(fromKm, targetKm);

        // Outbound the Sun pulls back and inbound it helps, so the correction takes the sign of the
        // crossing. A ship going to Venus is falling and gets the pull for free.
        bool outbound = targetKm > fromKm;
        double sign = outbound ? -1.0 : 1.0;

        AddTorch(options, "DIRECT", straight, maxAcceleration, gravity * sign, deltaVAvailable,
            "Full thrust, turn over at the midpoint.");

        AddTorch(options, "ECONOMY", straight, maxAcceleration * EconomyThrottle, gravity * sign,
            deltaVAvailable, "A quarter thrust. Twice the time, half the fuel.");

        options.Add(Ballistic(fromKm, targetKm, deltaVAvailable));

        return options;
    }

    /// <summary>
    /// A constant-thrust flip-and-burn, with the Sun's pull as a net correction.
    /// </summary>
    /// <remarks>
    /// Accelerate to the midpoint, turn over, decelerate. For a crossing of length <c>d</c> at a net
    /// acceleration <c>a</c>:
    /// <code>
    ///   t      = 2·sqrt(d/a)
    ///   v_peak = sqrt(d·a)
    ///   Δv     = 2·v_peak
    /// </code>
    /// The peak speed is set by the distance, not by the drive — a longer crossing at the same
    /// acceleration goes faster as well as taking longer, which is why the delta-v climbs so sharply
    /// with distance and why Jupiter is a one-way trip.
    /// </remarks>
    private static void AddTorch(
        List<TransferOption> options,
        string name,
        double distanceMetres,
        double acceleration,
        double gravityCorrection,
        double deltaVAvailable,
        string note)
    {
        // Net acceleration: thrust minus whatever the Sun takes back. A correction that reaches
        // zero or below is a ship that cannot go, and saying so is more use than a negative time.
        double net = acceleration + gravityCorrection;

        if (net <= 1e-9 || distanceMetres <= 0.0)
        {
            options.Add(new TransferOption(name, double.PositiveInfinity, double.PositiveInfinity,
                0.0, false, "The Sun wins: this drive cannot cross that far."));
            return;
        }

        double seconds = 2.0 * Math.Sqrt(distanceMetres / net);

        // The peak speed is what the SHIP does; the delta-v is what the TANKS pay, and they are not
        // the same number once gravity is in it.
        //
        // Without gravity they coincide: a·t = 2·sqrt(d·a). With the Sun pulling back, the net
        // acceleration is smaller so the crossing takes longer — and the engine is running that whole
        // extra time. The tanks pay THRUST times time, not net times time. Charging the net would
        // have made a longer trip look cheaper, which is exactly backwards and is what the first
        // version of this did.
        double peak = Math.Sqrt(distanceMetres * net);
        double deltaV = acceleration * seconds;

        options.Add(new TransferOption(name, seconds, deltaV, peak, deltaV <= deltaVAvailable, note));
    }

    /// <summary>
    /// A Hohmann transfer: two impulses and a long fall between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cheapest way between two circular orbits, and it is exact rather than approximate — the
    /// vis-viva equation and the period of the transfer ellipse, nothing else. It is also the option
    /// that has to WAIT: the destination has to be in the right place when the ship arrives, and the
    /// phase angle that makes that true comes round once per synodic period.
    /// </para>
    /// <para>
    /// The ship's drive is not used for the impulses and cannot be — at four milligee it would take
    /// hours to deliver a burn that a chemical stage does in minutes. What the drive does on this
    /// option is deliver them over a longer arc, which costs a little more than the ideal figure
    /// below. The planner quotes the ideal and says so.
    /// </para>
    /// </remarks>
    private static TransferOption Ballistic(double fromKm, double targetKm, double deltaVAvailable)
    {
        if (fromKm <= 0.0 || targetKm <= 0.0)
        {
            return new TransferOption("BALLISTIC", double.PositiveInfinity, double.PositiveInfinity,
                0.0, false, "No orbit to leave from.");
        }

        double transferAxis = (fromKm + targetKm) * 0.5;

        double circularFrom = Math.Sqrt(SunGmKm / fromKm);
        double circularTo = Math.Sqrt(SunGmKm / targetKm);

        double periapsis = Math.Sqrt(SunGmKm * ((2.0 / fromKm) - (1.0 / transferAxis)));
        double apoapsis = Math.Sqrt(SunGmKm * ((2.0 / targetKm) - (1.0 / transferAxis)));

        double departure = Math.Abs(periapsis - circularFrom);
        double arrival = Math.Abs(circularTo - apoapsis);
        double deltaV = (departure + arrival) * 1000.0;            // km/s to m/s

        // Half the transfer ellipse's period.
        double seconds = Math.PI * Math.Sqrt((transferAxis * transferAxis * transferAxis) / SunGmKm);

        double days = seconds / 86400.0;

        return new TransferOption(
            "BALLISTIC",
            seconds,
            deltaV,
            0.0,
            deltaV <= deltaVAvailable,
            $"Engine off and fall. Waits for a launch window; {days:F0} days in transit.");
    }

    /// <summary>
    /// What it costs to climb out of a gravity well, and how the cost depends on the drive.
    /// </summary>
    /// <param name="ImpulsiveDeltaV">The price if the burn could be instantaneous.</param>
    /// <param name="SpiralDeltaV">The price for a slow tangential spiral.</param>
    /// <param name="Orbits">How many orbits the impulsive burn would take at this thrust.</param>
    /// <param name="SpiralSeconds">How long the spiral takes.</param>
    internal readonly record struct EscapeCost(
        double ImpulsiveDeltaV,
        double SpiralDeltaV,
        double Orbits,
        double SpiralSeconds)
    {
        /// <summary>
        /// Whether the drive is slow enough that the spiral price is the one it pays.
        /// </summary>
        /// <remarks>
        /// A burn has to be short compared with an orbit to earn the impulsive price, and this asks
        /// whether it is. One orbit is the line, and in practice a drive is well inside one regime or
        /// the other — the interesting range is a factor of a hundred either side, not a factor of
        /// two.
        /// </remarks>
        internal bool IsSpiral => Orbits > 1.0;
    }

    /// <summary>
    /// What it costs to reach escape speed, which is NOT one number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same escape costs 3.18 km/s or 7.67 km/s depending on how fast the drive can deliver
    /// it, and this is the Oberth effect.</b> An instantaneous prograde burn at low orbit adds
    /// <c>v·dv</c> of specific energy per metre a second, and it does it at the highest speed the ship
    /// will ever have. Solving <c>v² + 2·v_c·Δv − v_c² = 0</c> gives <c>Δv = (√2 − 1)·v_c = 3.18</c>.
    /// A slow spiral spends its metres a second at every radius from here to infinity, where the
    /// speed is lower, and the energy accounting gives exactly <c>Δv = v_c = 7.67</c>.
    /// </para>
    /// <para>
    /// <b>The ratio is 2.41 and there is no way to buy the cheap one with a weak drive.</b> The
    /// impulsive burn would take 22.5 hours at four milligee — <b>fourteen and a half orbits</b> — so
    /// there is no point in the orbit at which to deliver it. Thrusting prograde is the spiral, and
    /// the spiral pays the spiral price.
    /// </para>
    /// <para>
    /// A finite-thrust escape lies between the two limits. This reports both and says which regime
    /// the drive is in, rather than interpolating: the honest middle needs a low-thrust trajectory
    /// optimiser and this is not one.
    /// </para>
    /// </remarks>
    internal static EscapeCost Escape(double centralGmKm, double radiusKm, double acceleration)
    {
        if (radiusKm <= 0.0 || acceleration <= 0.0)
        {
            return new EscapeCost(double.PositiveInfinity, double.PositiveInfinity, 0.0,
                double.PositiveInfinity);
        }

        double circular = Math.Sqrt(centralGmKm / radiusKm) * 1000.0;      // m/s
        double period = 2.0 * Math.PI * Math.Sqrt(
            (radiusKm * radiusKm * radiusKm) / centralGmKm);               // s

        double impulsive = (Math.Sqrt(2.0) - 1.0) * circular;
        double spiral = circular;

        return new EscapeCost(
            impulsive,
            spiral,
            impulsive / acceleration / period,
            spiral / acceleration);
    }

    /// <summary>
    /// Whether a drive can beat the gravity where it is.
    /// </summary>
    /// <remarks>
    /// The number that decides whether a straight-line torch course is flyable at all. Below one, the
    /// ship is in a well and has to spiral; above it, it can point and burn.
    /// </remarks>
    internal static double ThrustToGravity(double centralGmKm, double radiusKm, double acceleration)
    {
        if (radiusKm <= 0.0)
        {
            return double.PositiveInfinity;
        }

        double gravity = centralGmKm / (radiusKm * radiusKm) * 1000.0;   // m/s²
        return gravity <= 0.0 ? double.PositiveInfinity : acceleration / gravity;
    }

    /// <summary>
    /// Where the destination has to be, relative to the ship, when a Hohmann transfer departs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ship arrives at the far side of the transfer ellipse after half its period, so the target
    /// must arrive there at the same moment. The angle it has to be ahead by is
    /// <c>180° − n·t</c>, where <c>n</c> is the target's mean motion and <c>t</c> the transit time.
    /// </para>
    /// <para>
    /// For Earth to Mars this comes out at <b>44.3°</b>, which is the figure every reference gives
    /// for the Earth–Mars launch window. That it falls out of the arithmetic here rather than being
    /// quoted from somewhere is the point of computing it.
    /// </para>
    /// </remarks>
    internal static double DeparturePhaseDegrees(double fromKm, double targetKm)
    {
        double transferAxis = (fromKm + targetKm) * 0.5;
        double seconds = Math.PI * Math.Sqrt((transferAxis * transferAxis * transferAxis) / SunGmKm);
        double meanMotion = Math.Sqrt(SunGmKm / (targetKm * targetKm * targetKm));

        double phase = Math.PI - (meanMotion * seconds);
        return phase * 180.0 / Math.PI;
    }
}
