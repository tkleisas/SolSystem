using SolSystem.Core.Numerics;

namespace SolSystem.Core.Orbits;

/// <summary>
/// The constants the simulation needs, in its own units: megametres and seconds.
/// </summary>
/// <remarks>
/// These are the one place an external measured quantity enters the simulation, so
/// they are gathered here rather than scattered through the code. Each is the
/// published value divided by 10^9 m^3 -> Mm^3 and rounded to its nearest
/// fixed-point representation; the resulting relative error is at the 1e-16 level,
/// far below anything the simulation can act on. They are written as literals
/// rather than computed with shifts, because a shift-based conversion of a number
/// this large silently loses the low bits.
/// </remarks>
internal static class Constants
{
    /// <summary>
    /// The strategic propagation step, in seconds.
    /// </summary>
    /// <remarks>
    /// This is the *orbital* step, not the navigation step. An orbit only has to be
    /// resolved, not flown, so one hour is ample: Earth's orbit becomes 8 766 steps
    /// per revolution, well inside the range where a symplectic integrator is
    /// accurate. The action layer ticks far faster and is a separate concern.
    /// </remarks>
    internal const long StrategicTickSeconds = 3600;

    // ------------------------------------------------------------------ local frame
    // Metres, for the Q64.64 type. These are the units a ship manoeuvres in.

    /// <summary>
    /// GM of the Earth for the local frame, in m³/s²: 3.986004418 × 10¹⁴, Q64.64.
    /// </summary>
    /// <remarks>
    /// The local frame is Q64.64 in metres, so its gravitational parameter is the SI value
    /// unchanged and <c>GM/r²</c> yields m/s² with no conversion anywhere.
    /// </remarks>
    internal static readonly Fix128 EarthGmLocal = Fix128.FromDouble(3.986004418e14);

    /// <summary>GM of Mars for the local frame, in m³/s²: 4.282837 × 10¹³, Q64.64.</summary>
    internal static readonly Fix128 MarsGmLocal = Fix128.FromDouble(4.282837e13);

    // ------------------------------------------------------------------ solar frame
    // Kilometres, for the Q64.64 type. These are the units an orbit is propagated in.
    //
    // Kept as doubles at construction time: they are measured quantities read once at
    // start-up, and the fixed-point conversion is exact to the last bit. Hard-coding
    // raw 128-bit magnitudes here would be unreadable and unverifiable.

    /// <summary>GM of the Sun: 1.32712440018 × 10^11 km^3/s^2.</summary>
    internal static readonly Fix128 SunGm = Fix128.FromDouble(1.32712440018e11);

    /// <summary>GM of the Earth: 3.986004418 × 10^5 km^3/s^2.</summary>
    internal static readonly Fix128 EarthGmKm = Fix128.FromDouble(3.986004418e5);

    /// <summary>GM of Jupiter: 1.26686534 × 10^8 km^3/s^2.</summary>
    internal static readonly Fix128 JupiterGmKm = Fix128.FromDouble(1.26686534e8);

    /// <summary>Earth's mean orbital radius about the Sun: 1 AU in kilometres.</summary>
    internal static readonly Fix128 EarthOrbitalRadiusKm = Fix128.FromDouble(149_597_870.0);

    /// <summary>Earth's mean orbital speed about the Sun: 29.78 km/s.</summary>
    internal static readonly Fix128 EarthOrbitalSpeedKm = Fix128.FromDouble(29.78);
}
