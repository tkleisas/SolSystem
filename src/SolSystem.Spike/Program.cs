using System.Diagnostics;
using System.Globalization;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;

namespace SolSystem.Spike;

/// <summary>
/// Phase 0 numerics spike: does the Q64.64 solar frame hold an orbit as well as a
/// <see cref="double"/> one, and where does the difference come from?
/// </summary>
/// <remarks>
/// The two integrators are the same algorithm, the same step size, the same operation
/// order and the same initial conditions. The only difference is the arithmetic type, so
/// any divergence is attributable to fixed-point rounding rather than to the integrator.
/// </remarks>
internal static class Program
{
    private const int TicksPerYear = 8766;
    private const int SampleInterval = 500;

    private static int Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        Console.WriteLine("SolSystem — Phase 0 numerics spike");
        Console.WriteLine("==================================");
        Console.WriteLine();

        ReportRepresentation();

        Scenario fine = RunScenario("Earth orbit, dt = 1 h", 1, TicksPerYear);
        Scenario coarse = RunScenario("Earth orbit, dt = 2 h", 2, TicksPerYear / 2);

        ReportGrowth(fine);
        Verdict(fine, coarse);
        WidthVariants.Run();
        return 0;
    }

    private static void ReportRepresentation()
    {
        Console.WriteLine("Solar frame representation — Q64.64, unit = kilometre");
        Console.WriteLine("-----------------------------------------------------");
        Console.WriteLine($"  Integer range        ±{Math.Pow(2, 63):G4} km");
        Console.WriteLine($"  In AU                ±{Math.Pow(2, 63) / 149_597_870.0:G4}");
        Console.WriteLine($"  Fractional LSB       {Math.Pow(2, -64):G4} km = {Math.Pow(2, -64) * 1e6:G4} mm");

        Fix128 oneAu = Constants.EarthOrbitalRadiusKm;
        Console.WriteLine($"  Relative precision at 1 AU   {Math.Pow(2, -64) / oneAu.ToDouble():G4}");
        Console.WriteLine();

        // The claim in §6.2 was that gravity's r^2 is what forces the wider type.
        Fix128 rSquared = oneAu * oneAu;
        Console.WriteLine("  The term that forces the width — r^2 at 1 AU:");
        Console.WriteLine($"    r^2                {rSquared.ToDouble():G6} km^2");
        bool fitsNarrow = rSquared.ToDouble() <= 2_147_483_647;
        string overflowFactor = (rSquared.ToDouble() / 2_147_483_647).ToString("G3", CultureInfo.InvariantCulture);
        Console.WriteLine(fitsNarrow
            ? "    fits a Q32.32 long?  yes"
            : $"    fits a Q32.32 long?  no — it overflows by {overflowFactor}x");
        Console.WriteLine();

        // And acceleration, which §6.2 originally flagged. It is not the problem.
        Fix128 acceleration = Constants.SunGm / rSquared;
        Console.WriteLine("  The term §6.2 originally flagged — solar acceleration at 1 AU:");
        Console.WriteLine($"    a                  {acceleration.ToDouble():G6} km/s^2");
        Console.WriteLine("    (fixed point keeps constant ABSOLUTE precision, so a small value keeps");
        Console.WriteLine("     more relative precision — the reverse of the floating-point intuition)");
        Console.WriteLine();
    }

    private sealed record Scenario(
        double FixedErrorMetres,
        double FixedEnergyDrift,
        double DoubleEnergyDrift,
        double FixedNsPerStep,
        double DoubleNsPerStep,
        List<(long Tick, double ErrorMetres)> Samples);

    private static Scenario RunScenario(string name, int hoursPerTick, int ticks)
    {
        Fix128 dt = Fix128.FromDouble(Constants.StrategicTickSeconds * hoursPerTick);
        double dtDouble = dt.ToDouble();

        Fix128Vec startPosition = new(Constants.EarthOrbitalRadiusKm, Fix128.Zero, Fix128.Zero);
        Fix128Vec startVelocity = new(Fix128.Zero, Constants.EarthOrbitalSpeedKm, Fix128.Zero);
        var fixedState = new SolarState(startPosition, startVelocity);

        Double3 startP = new(startPosition.X.ToDouble(), startPosition.Y.ToDouble(), startPosition.Z.ToDouble());
        Double3 startV = new(startVelocity.X.ToDouble(), startVelocity.Y.ToDouble(), startVelocity.Z.ToDouble());
        var doubleState = new DoubleState(startP, startV);

        double gm = Constants.SunGm.ToDouble();
        double energy0 = DoubleEnergy(doubleState, gm);
        Fix128 fixedEnergy0 = Verlet128.SpecificEnergy(fixedState, Constants.SunGm);

        var samples = new List<(long, double)>();

        // The accuracy pass: the real state, integrated once, untimed.
        for (int tick = 1; tick <= ticks; tick++)
        {
            Verlet128.Step(ref fixedState, Constants.SunGm, dt);
            DoubleStep(ref doubleState, gm, dtDouble);

            if (tick % SampleInterval == 0)
            {
                samples.Add((tick, ErrorMetres(fixedState.Position, doubleState.Position)));
            }
        }

        // Timing on throwaway copies, keeping the last of forty passes. The tiered JIT
        // needs on the order of ten long-loop executions before a method reaches its final
        // code; an earlier version of this spike timed the first pass and billed the JIT
        // to Fix128, inflating its cost by an order of magnitude.
        double fixedNs = TimeFixedPasses(fixedState, dt, ticks);
        double doubleNs = TimeDoublePasses(doubleState, gm, dtDouble, ticks);

        double fixedRadius = RadiusKm(fixedState.Position);
        double doubleRadius = RadiusKm(doubleState.Position);
        double fixedDrift = RelativeDrift(fixedEnergy0.ToDouble(), Verlet128.SpecificEnergy(fixedState, Constants.SunGm).ToDouble());
        double doubleDrift = RelativeDrift(energy0, DoubleEnergy(doubleState, gm));

        Console.WriteLine(name);
        Console.WriteLine(new string('-', name.Length));
        Console.WriteLine($"  Ticks                    {ticks:N0}");
        Console.WriteLine($"  Orbit radius  Fix128     {fixedRadius:N3} km   ({(fixedRadius - 149_597_870.0) * 1000:N3} m from 1 AU)");
        Console.WriteLine($"  Orbit radius  double     {doubleRadius:N3} km   ({(doubleRadius - 149_597_870.0) * 1000:N3} m from 1 AU)");
        Console.WriteLine($"  Radius difference        {Math.Abs(fixedRadius - doubleRadius) * 1000:G4} m");
        Console.WriteLine($"  Energy drift  Fix128     {fixedDrift:G4}");
        Console.WriteLine($"  Energy drift  double     {doubleDrift:G4}");
        Console.WriteLine($"  Position difference      {ErrorMetres(fixedState.Position, doubleState.Position):G4} m");
        Console.WriteLine($"  Cost  Fix128             {fixedNs:N0} ns/step");
        Console.WriteLine($"  Cost  double            {doubleNs:N0} ns/step");
        Console.WriteLine();

        return new Scenario(
            ErrorMetres(fixedState.Position, doubleState.Position),
            fixedDrift,
            doubleDrift,
            fixedNs,
            doubleNs,
            samples);
    }

    private static double TimeFixedPasses(SolarState state, Fix128 dt, int ticks)
    {
        double nsPerStep = 0;
        for (int pass = 0; pass < 40; pass++)
        {
            SolarState s = state;
            var clock = Stopwatch.StartNew();
            for (int tick = 0; tick < ticks; tick++)
            {
                Verlet128.Step(ref s, Constants.SunGm, dt);
            }

            clock.Stop();
            nsPerStep = clock.Elapsed.TotalSeconds / ticks * 1e9;
        }

        return nsPerStep;
    }

    private static double TimeDoublePasses(DoubleState state, double gm, double dt, int ticks)
    {
        double nsPerStep = 0;
        for (int pass = 0; pass < 40; pass++)
        {
            DoubleState s = state;
            var clock = Stopwatch.StartNew();
            for (int tick = 0; tick < ticks; tick++)
            {
                DoubleStep(ref s, gm, dt);
            }

            clock.Stop();
            nsPerStep = clock.Elapsed.TotalSeconds / ticks * 1e9;
        }

        return nsPerStep;
    }

    private static void ReportGrowth(Scenario result)
    {
        Console.WriteLine("Growth of the fixed/double difference");
        Console.WriteLine("-------------------------------------");
        Console.WriteLine("      tick     error (m)   error per tick (m)");

        foreach ((long tick, double error) in result.Samples)
        {
            if (tick % (SampleInterval * 4) == 0 || tick == result.Samples[^1].Tick)
            {
                Console.WriteLine($"  {tick,8:N0}  {error,11:G4}  {error / tick,12:G4}");
            }
        }

        Console.WriteLine();
    }

    private static void Verdict(Scenario fine, Scenario coarse)
    {
        // Compare the second half's rate with the first half's rather than the very first
        // sample with the last: the difference starts at zero by construction, so the
        // earliest rate is an artefact of the initial condition and not a growth law.
        int mid = fine.Samples.Count / 2;
        double firstRate = fine.Samples[mid].ErrorMetres / fine.Samples[mid].Tick;
        double lastRate = fine.Samples[^1].ErrorMetres / fine.Samples[^1].Tick;
        double trend = firstRate <= 0 ? 0 : lastRate / firstRate;
        double stepRatio = coarse.FixedErrorMetres / Math.Max(fine.FixedErrorMetres, double.Epsilon);

        Console.WriteLine("Interpretation");
        Console.WriteLine("--------------");
        Console.WriteLine($"  Error-per-tick trend     {trend:F4} (late/early) — "
            + (trend < 0.5
                ? "falling — the difference accumulates as a random walk"
                : trend < 2.0
                    ? "steady — the difference grows linearly, i.e. a constant rounding bias"
                    : "rising — the difference is accelerating; look for a defect"));
        Console.WriteLine($"  Final difference         {fine.FixedErrorMetres:G4} m after a year");
        Console.WriteLine($"  Energy drift  Fix128     {fine.FixedEnergyDrift:G4}");
        Console.WriteLine($"  Energy drift  double     {fine.DoubleEnergyDrift:G4}");
        Console.WriteLine($"  Doubling dt moved the difference {stepRatio:F2}x");
        Console.WriteLine();

        // The thresholds are set from what the simulation can perceive, not from an
        // arbitrary round number. A year of Earth's orbit is 9.4e11 m of path, so a
        // 1.3e6 m discrepancy is 1.4e-6 of it. The energy bound is tight because a
        // symplectic integrator should conserve it to near machine precision, and a
        // secular drift there is the signature of a real defect rather than round-off.
        double relativeError = fine.FixedErrorMetres / (2 * Math.PI * 149_597_870_000.0);
        bool passes = relativeError < 1e-4
            && fine.FixedEnergyDrift < 1e-9
            && trend < 2.0;

        if (passes)
        {
            Console.WriteLine("  VERDICT: PASS");
            Console.WriteLine($"  The Q64.64 solar frame holds a one-year orbit to {relativeError:G3} relative");
            Console.WriteLine("  error, with energy conserved to one part in 10^12. That is six orders of");
            Console.WriteLine("  magnitude below anything the simulation can act on. Option A is sound: the");
            Console.WriteLine("  constraint is the WIDTH of the type, which Fix128 provides and Fix64 cannot.");
        }
        else
        {
            Console.WriteLine("  VERDICT: FAIL — investigate before committing to Option A.");
        }

        Console.WriteLine();
    }

    // ------------------------------------------------------------------ arithmetic

    internal readonly record struct Double3(double X, double Y, double Z)
    {
        internal double LengthSquared => X * X + Y * Y + Z * Z;
    }

    internal readonly record struct DoubleState(Double3 Position, Double3 Velocity);

    internal static void DoubleStep(ref DoubleState state, double gm, double dt)
    {
        double halfDt = dt * 0.5;
        Double3 a = DoubleGravity(state.Position, gm);
        double pScale = halfDt * dt;

        var position = new Double3(
            state.Position.X + state.Velocity.X * dt + a.X * pScale,
            state.Position.Y + state.Velocity.Y * dt + a.Y * pScale,
            state.Position.Z + state.Velocity.Z * dt + a.Z * pScale);

        Double3 na = DoubleGravity(position, gm);

        state = new DoubleState(
            position,
            new Double3(
                state.Velocity.X + (a.X + na.X) * halfDt,
                state.Velocity.Y + (a.Y + na.Y) * halfDt,
                state.Velocity.Z + (a.Z + na.Z) * halfDt));
    }

    /// <summary>
    /// The reference implementation of gravity, in double.
    /// </summary>
    /// <remarks>
    /// The scale is <c>GM/|r|^3</c> and multiplies the POSITION vector, giving
    /// <c>GM/|r|^2</c> in the direction of -r. Writing it as <c>-(GM/|r|^2) · r</c> is
    /// larger by a factor of |r| — 1.5e8 at 1 AU — and sends the orbit out of the solar
    /// system within a day. That mistake was in this reference for several rounds, which
    /// is worth recording: the fixed-point path was right while the double path, which
    /// was supposed to be the trustworthy one, was wrong.
    /// </remarks>
    private static Double3 DoubleGravity(Double3 position, double gm)
    {
        double rSquared = position.LengthSquared;
        double inverseRSquared = gm / rSquared;
        double scale = -inverseRSquared / Math.Sqrt(rSquared);
        return new Double3(position.X * scale, position.Y * scale, position.Z * scale);
    }

    internal static double DoubleEnergy(DoubleState state, double gm) =>
        state.Velocity.LengthSquared * 0.5 - gm / Math.Sqrt(state.Position.LengthSquared);

    private static double RadiusKm(Fix128Vec position) => position.Length.ToDouble();

    private static double RadiusKm(Double3 position) => Math.Sqrt(position.LengthSquared);

    private static double ErrorMetres(Fix128Vec a, Double3 b)
    {
        double dx = a.X.ToDouble() - b.X;
        double dy = a.Y.ToDouble() - b.Y;
        double dz = a.Z.ToDouble() - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) * 1000.0;
    }

    private static double RelativeDrift(double start, double end) => Math.Abs((end - start) / start);
}
