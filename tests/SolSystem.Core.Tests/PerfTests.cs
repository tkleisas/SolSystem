using System.Diagnostics;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;
using Xunit;
using Xunit.Abstractions;

namespace SolSystem.Core.Tests;

/// <summary>
/// What the simulation costs per tick, measured rather than assumed.
/// </summary>
/// <remarks>
/// The design fixes 120 Hz for the navigation tick, which is a decision about feel and
/// about integration error. It is also 10.4 million ticks a day, so the cost of one tick
/// decides whether a strategic timescale is affordable or a curiosity: at a microsecond a
/// day of simulation is ten seconds, and at ten microseconds it is two minutes.
/// </remarks>
public class PerfTests
{
    private readonly ITestOutputHelper _o;

    public PerfTests(ITestOutputHelper o) => _o = o;

    private static Fix128 F(double v) => Fix128.FromDouble(v);
    private static Fix128Vec V(double x, double y, double z) => new(F(x), F(y), F(z));

    [Fact]
    public void Measure_TheCostOfATick()
    {
        const int iterations = 200_000;

        // A station's own step: gravity plus velocity Verlet.
        Station station = Station.InCircularOrbit(
            Ephemeris.Body.Earth, "T", F(6_778_100.0), Fix128Vec.Zero, V(1, 0, 0));
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            station.Step(F(Constants.NavigationTickSeconds));
        }

        sw.Stop();
        double stationTick = sw.Elapsed.TotalMicroseconds / iterations;

        // The full body table through the ephemeris, which every frame needs.
        sw.Restart();
        for (int i = 0; i < iterations / 100; i++)
        {
            for (int b = 0; b < 8; b++)
            {
                _ = Ephemeris.AtSecondsFromJ2000((Ephemeris.Body)b, i * 0.008);
            }
        }

        sw.Stop();
        double bodyTick = sw.Elapsed.TotalMicroseconds / (iterations / 100) / 8.0;

        // One body, which is the common case for a local-frame query.
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            _ = Ephemeris.AtSecondsFromJ2000(Ephemeris.Body.Earth, i * 0.008);
        }

        sw.Stop();
        double oneBody = sw.Elapsed.TotalMicroseconds / iterations;

        // A ship step, which includes the attitude, the engine and the gravity.
        var ship = new Core.Local.Ship(
            V(200, 0, 0), V(0, 0, 0), F(90), F(9.9322),
            Core.Local.Engine.Crewed(F(3.92), Core.Local.Engine.CrewedSpecificImpulse),
            new Core.Local.Attitude(Fix128Vec.Zero, Fix128Vec.Zero));
        var sources = new[] { new Core.Local.GravitySource(Fix128Vec.Zero, F(3.986004418e14)) };
        var command = Core.Local.Command.Coast;
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            ship.Step(sources, F(Constants.NavigationTickSeconds), command);
        }

        sw.Stop();
        double shipTick = sw.Elapsed.TotalMicroseconds / iterations;

        // Where the station's own step goes, so a slow tick can be attributed rather
        // than guessed at.
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            _ = station.GravityAt(station.Offset);
        }

        sw.Stop();
        double gravityOnly = sw.Elapsed.TotalMicroseconds / iterations;

        sw.Restart();
        // The magnitudes are chosen to stay inside the frame, because a checked build is
        // entitled to throw on a value that leaves it, and the cost of a Q64.64 operation is
        // the same at any magnitude. The scale note is still worth stating: r³ of the
        // station's 6 778 km orbit is 3.1 × 10²⁰ m³, which Q64.64 in metres cannot hold —
        // the real gravity code never materialises r³ for exactly that reason and divides
        // through a shared power of two instead (see GravityAt). This chain runs the same
        // operations at magnitudes that stay representable.
        var local = new Fix128Vec(
            Fix128.FromDouble(150.0),
            Fix128.FromDouble(90.0),
            Fix128.FromDouble(60.0));
        for (int i = 0; i < iterations; i++)
        {
            Fix128 r = local.Length;
            Fix128 rSquared = r * r;
            Fix128 rCubed = rSquared * r;
            _ = local * rCubed;
        }

        sw.Stop();
        double vectorMachinery = sw.Elapsed.TotalMicroseconds / iterations;

        _o.WriteLine($"  station gravity   {gravityOnly,10:F4} us");
        _o.WriteLine($"  vector machinery  {vectorMachinery,10:F4} us");
        _o.WriteLine($"station step        {stationTick,10:F4} us");
        _o.WriteLine($"eight bodies        {bodyTick,10:F4} us per body");
        _o.WriteLine($"one body            {oneBody,10:F4} us");
        _o.WriteLine($"ship step (coast)   {shipTick,10:F4} us");
        _o.WriteLine(string.Empty);
        foreach (double perTick in new[] { stationTick, shipTick, oneBody, bodyTick * 8 })
        {
            _o.WriteLine($"  at {perTick:F3} us a tick, one simulated day is "
                + $"{perTick * 10_368_000 / 1e6,8:F1} s");
        }

        // Not a benchmark, a tripwire: if a tick ever costs a millisecond the strategic
        // timescale stops being affordable and the design needs to know before the client
        // is built on it.
        Assert.True(stationTick < 500.0, $"a station step costs {stationTick:F1} us");
    }
}
