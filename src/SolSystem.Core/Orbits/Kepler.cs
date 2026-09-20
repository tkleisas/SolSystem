using SolSystem.Core.Numerics;

namespace SolSystem.Core.Orbits;

/// <summary>
/// A two-body orbit in the solar frame: kilometres, seconds and turns.
/// </summary>
/// <remarks>
/// <para>
/// Bodies travel on <b>Keplerian rails</b>: given the elements and a time, the position is
/// solved for directly rather than integrated. That is the design's choice for everything
/// far away, because it costs the same at any distance and accumulates no error over
/// centuries of simulated time. Only things that are actively manoeuvring, and only while
/// they are near enough to matter, are integrated.
/// </para>
/// <para>
/// Angles are in turns rather than radians, so a full revolution is exact and wrapping is a
/// bit operation instead of a subtraction of an irrational constant.
/// </para>
/// </remarks>
internal readonly struct OrbitalElements
{
    /// <summary>Newton iterations for Kepler's equation. See <see cref="SolveKepler"/>.</summary>
    private const int KeplerIterations = 6;

    /// <summary>Radians per turn, for the shape of the Kepler iteration only.</summary>
    private static readonly Fix128 TwoPi = Fix128.FromDouble(2.0 * Math.PI);

    /// <summary>Semi-major axis, in kilometres.</summary>
    internal readonly Fix128 SemiMajorAxis;

    /// <summary>Eccentricity, below 1 for a closed orbit.</summary>
    internal readonly Fix128 Eccentricity;

    /// <summary>Inclination to the reference plane, in turns.</summary>
    internal readonly Fix128 Inclination;

    /// <summary>Argument of periapsis, in turns.</summary>
    internal readonly Fix128 ArgumentOfPeriapsis;

    /// <summary>Longitude of the ascending node, in turns.</summary>
    internal readonly Fix128 LongitudeOfAscendingNode;

    /// <summary>Mean anomaly at <see cref="Epoch"/>, in turns.</summary>
    internal readonly Fix128 MeanAnomalyAtEpoch;

    /// <summary>The time at which <see cref="MeanAnomalyAtEpoch"/> applies, in seconds.</summary>
    internal readonly Fix128 Epoch;

    /// <summary>
    /// Mean motion, in <b>radians</b> per second.
    /// </summary>
    /// <remarks>
    /// Radians rather than turns because <c>sqrt(GM/a)/a</c> yields radians directly and
    /// the conversion to turns costs a rounding against a 2π that is not exactly
    /// representable. Converted once per evaluation instead, in <see cref="StateAt"/>.
    /// </remarks>
    internal readonly Fix128 MeanMotion;

    /// <summary>GM of the attracting body, in km³/s².</summary>
    internal readonly Fix128 GravitationalParameter;

    internal OrbitalElements(
        Fix128 semiMajorAxis,
        Fix128 eccentricity,
        Fix128 inclination,
        Fix128 argumentOfPeriapsis,
        Fix128 longitudeOfAscendingNode,
        Fix128 meanAnomalyAtEpoch,
        Fix128 epoch,
        Fix128 meanMotion,
        Fix128 gravitationalParameter)
    {
        SemiMajorAxis = semiMajorAxis;
        Eccentricity = eccentricity;
        Inclination = inclination;
        ArgumentOfPeriapsis = argumentOfPeriapsis;
        LongitudeOfAscendingNode = longitudeOfAscendingNode;
        MeanAnomalyAtEpoch = meanAnomalyAtEpoch;
        Epoch = epoch;
        MeanMotion = meanMotion;
        GravitationalParameter = gravitationalParameter;
    }

    /// <summary>
    /// Builds elements from the eccentric anomaly at epoch, deriving the mean anomaly and the
    /// mean motion.
    /// </summary>
    /// <remarks>
    /// <c>M = E - e·sin(E)</c> converts the starting point, and the mean motion comes from
    /// <c>n = sqrt(GM/a³)</c>. That is evaluated as <c>sqrt(GM/a)/a</c> rather than
    /// <c>sqrt(GM/a³)</c>, because <c>a³</c> overflows Q64.64 past a few tens of AU while
    /// <c>GM/a</c> stays small. This is the standard trick for orbital mechanics in a
    /// fixed-width type.
    /// </remarks>
    internal static OrbitalElements FromEccentricAnomaly(
        Fix128 semiMajorAxis,
        Fix128 eccentricity,
        Fix128 inclination,
        Fix128 argumentOfPeriapsis,
        Fix128 longitudeOfAscendingNode,
        Fix128 eccentricAnomalyAtEpoch,
        Fix128 epoch,
        Fix128 gravitationalParameter)
    {
        // M = E - e·sin(E), with sin evaluated on the turn-valued E directly.
        Fix128 meanAtEpoch = WrapTurns(eccentricAnomalyAtEpoch
            - eccentricity * Trig128.SinTurn(eccentricAnomalyAtEpoch));

        // sqrt(GM/a)/a IS the mean motion in RADIANS per second; the conversion to turns
        // happens once, below, rather than here. Dividing by 2pi here and multiplying back
        // out again in StateAt would round twice, and 2pi is not exactly representable.
        Fix128 meanMotionRadians = Fix128.Sqrt(gravitationalParameter / semiMajorAxis)
            / semiMajorAxis;

        return new OrbitalElements(
            semiMajorAxis,
            eccentricity,
            inclination,
            argumentOfPeriapsis,
            longitudeOfAscendingNode,
            meanAtEpoch,
            epoch,
            meanMotionRadians,
            gravitationalParameter);
    }

    /// <summary>Position and velocity at <paramref name="time"/>, in the solar frame.</summary>
    internal SolarState StateAt(Fix128 time)
    {
        // Mean anomaly advances linearly with time and is wrapped to [0, 1) turns. Wrapping
        // keeps the Newton iteration's starting guess inside the range where it converges,
        // which matters after a long run: a century of Earth orbits is 10^4 turns.
        Fix128 meanAnomalyRadians = MeanAnomalyAtEpoch + MeanMotion * (time - Epoch);
        Fix128 meanAnomaly = WrapTurns(RadiansToTurns(meanAnomalyRadians));

        Fix128 eccentricAnomaly = SolveKepler(meanAnomaly, Eccentricity);

        Fix128 sinE = Trig128.SinTurn(eccentricAnomaly);
        Fix128 cosE = Trig128.CosTurn(eccentricAnomaly);

        Fix128 oneMinusECosE = Fix128.One - Eccentricity * cosE;
        Fix128 sqrtOneMinusESquared = Fix128.Sqrt(Fix128.One - Eccentricity * Eccentricity);

        // Perifocal frame: +x towards periapsis, +z along the orbit normal.
        Fix128 xPerifocal = SemiMajorAxis * (cosE - Eccentricity);
        Fix128 yPerifocal = SemiMajorAxis * sqrtOneMinusESquared * sinE;

        // Speed scale is sqrt(GM/a)/(1 - e·cos E). sqrt(GM·a) would overflow past a few AU.
        Fix128 speedScale = Fix128.Sqrt(GravitationalParameter / SemiMajorAxis) / oneMinusECosE;
        Fix128 vxPerifocal = -speedScale * sinE;
        Fix128 vyPerifocal = speedScale * sqrtOneMinusESquared * cosE;

        return new SolarState(
            RotateFromPerifocal(new Fix128Vec(xPerifocal, yPerifocal, Fix128.Zero)),
            RotateFromPerifocal(new Fix128Vec(vxPerifocal, vyPerifocal, Fix128.Zero)));
    }

    /// <summary>
    /// Rotates a vector out of the perifocal frame: by the argument of periapsis about z,
    /// then the inclination about x, then the longitude of the ascending node about z.
    /// </summary>
    /// <remarks>
    /// The composite matrix is <c>R₃(Ω)·R₁(i)·R₃(ω)</c>, expanded by hand. Getting the order
    /// wrong is the classic orbital-mechanics bug: <c>R₃(Ω)·R₁(i)</c> and its transpose
    /// differ only in the signs of the inclination terms, and both produce plausible orbits
    /// that simply tilt the wrong way.
    /// </remarks>
    private Fix128Vec RotateFromPerifocal(Fix128Vec perifocal)
    {
        Fix128 cosNode = Trig128.CosTurn(LongitudeOfAscendingNode);
        Fix128 sinNode = Trig128.SinTurn(LongitudeOfAscendingNode);
        Fix128 cosInclination = Trig128.CosTurn(Inclination);
        Fix128 sinInclination = Trig128.SinTurn(Inclination);
        Fix128 cosPeri = Trig128.CosTurn(ArgumentOfPeriapsis);
        Fix128 sinPeri = Trig128.SinTurn(ArgumentOfPeriapsis);

        Fix128 m11 = cosNode * cosPeri - sinNode * sinPeri * cosInclination;
        Fix128 m12 = -cosNode * sinPeri - sinNode * cosPeri * cosInclination;
        Fix128 m21 = sinNode * cosPeri + cosNode * sinPeri * cosInclination;
        Fix128 m22 = -sinNode * sinPeri + cosNode * cosPeri * cosInclination;
        Fix128 m31 = sinPeri * sinInclination;
        Fix128 m32 = cosPeri * sinInclination;

        return new Fix128Vec(
            perifocal.X * m11 + perifocal.Y * m12,
            perifocal.X * m21 + perifocal.Y * m22,
            perifocal.X * m31 + perifocal.Y * m32);
    }

    /// <summary>Radians to turns.</summary>
    private static Fix128 RadiansToTurns(Fix128 radians) => radians / TwoPi;

    /// <summary>Turns to radians.</summary>
    private static Fix128 TurnsToRadians(Fix128 turns) => turns * TwoPi;

    /// <summary>Wraps a turn value into [0, 1), keeping the sign of the result positive.</summary>
    private static Fix128 WrapTurns(Fix128 turns)
    {
        if (turns.IsZero)
        {
            return turns;
        }

        // The low 64 bits of the magnitude are the position within a revolution; the bits
        // above are whole revolutions and are discarded.
        return Fix128.FromRaw(turns.Magnitude & 0xFFFF_FFFF_FFFF_FFFFUL, turns.Negative);
    }

    /// <summary>
    /// Solves Kepler's equation <c>M = E - e·sin(E)</c> for the eccentric anomaly, in turns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The iteration is carried out in <b>radians</b> and the result converted to turns,
    /// because <c>sin(E)</c> delivered in turns would need the derivative
    /// <c>1 - e·cos(E)</c> in matching units and the two would silently disagree by a factor
    /// of 2π.
    /// </para>
    /// <para>
    /// Newton's method from <c>E₀ = M</c>. The derivative is at least <c>1 - e</c>, so for
    /// the eccentricities in play it cannot approach zero and the iteration cannot divide by
    /// it. The count is fixed rather than convergence-tested, so the result is a pure
    /// function of the inputs.
    /// </para>
    /// </remarks>
    private static Fix128 SolveKepler(Fix128 meanAnomalyTurns, Fix128 eccentricity)
    {
        Fix128 meanAnomaly = TurnsToRadians(meanAnomalyTurns);
        Fix128 eccentricAnomaly = meanAnomaly;

        for (int i = 0; i < KeplerIterations; i++)
        {
            Fix128 eccentricAnomalyTurns = RadiansToTurns(eccentricAnomaly);
            Fix128 sinE = Trig128.SinTurn(eccentricAnomalyTurns);
            Fix128 cosE = Trig128.CosTurn(eccentricAnomalyTurns);

            Fix128 residual = eccentricAnomaly - eccentricity * sinE - meanAnomaly;
            eccentricAnomaly -= residual / (Fix128.One - eccentricity * cosE);
        }

        return WrapTurns(RadiansToTurns(eccentricAnomaly));
    }
}
