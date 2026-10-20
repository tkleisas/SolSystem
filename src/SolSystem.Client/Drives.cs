using SolSystem.Core.Local;
using SolSystem.Core.Numerics;

namespace SolSystem.Client;

/// <summary>The two drives a courier may be built around.</summary>
internal enum Drive
{
    /// <summary>High thrust, short burn, half the exhaust velocity: the Illuminus drive.</summary>
    Illuminus,

    /// <summary>Lower thrust, long burn, twice the exhaust velocity: the Workers drive.</summary>
    Workers,
}

/// <summary>
/// The two drives, and the table that makes the choice a trade rather than an upgrade.
/// </summary>
/// <remarks>
/// <para>
/// The Workers drive spends its propellant at 1 200 km/s of exhaust velocity, so a tank buys twice
/// the delta-v of the Illuminus drive's 600 km/s. It pays for that in thrust: 5.5 kN instead of
/// 9.0, so every burn is longer and every deadline is tighter.
/// </para>
/// <para>
/// The Illuminus drive is the fast one: 9 kN against a 145-tonne ship is about six milligee, so a
/// manoeuvre that takes minutes on the Workers drive takes half as long, and the fuel bill for the
/// same delta-v is twice as large. The contract is priced in time and fuel; this is the table that
/// prices the ship.
/// </para>
/// </remarks>
internal static class Drives
{
    internal static Engine Engine(Drive drive) => drive switch
    {
        Drive.Workers => Core.Local.Engine.Crewed(
            Fix128.FromDouble(5.5),
            Fix128.FromDouble(1_200_000.0 / 9.80665)),

        _ => Core.Local.Engine.Crewed(
            Fix128.FromDouble(9.0),
            Fix128.FromDouble(600_000.0 / 9.80665),
            Fix128.FromDouble(0.0064)),
    };

    internal static string Name(Drive drive) => drive switch
    {
        Drive.Workers => "WORKERS",
        _ => "ILLUMINUS",
    };

    internal static double ExhaustKilometresPerSecond(Drive drive) => drive switch
    {
        Drive.Workers => 1_200.0,
        _ => 600.0,
    };

    internal static double ThrustKilonewtons(Drive drive) => drive switch
    {
        Drive.Workers => 5.5,
        _ => 9.0,
    };
}
