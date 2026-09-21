using System.Diagnostics;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;

namespace SolSystem.Spike;

/// <summary>The fractional width of a <see cref="VarFix{B}"/> as a type parameter, so each
/// candidate shape gets its own JIT-specialised arithmetic — the same code at the same
/// speed class, differing only in shift constants.</summary>
internal interface IFractionBits
{
    static abstract int FractionalBits { get; }
}

internal readonly struct F8 : IFractionBits { public static int FractionalBits => 8; }
internal readonly struct F16 : IFractionBits { public static int FractionalBits => 16; }
internal readonly struct F30 : IFractionBits { public static int FractionalBits => 30; }
internal readonly struct F32 : IFractionBits { public static int FractionalBits => 32; }

/// <summary>
/// A signed fixed-point number in a <see cref="long"/> with Int128 intermediates — the
/// shape of every 64-bit candidate: Q48.16, Q34.30, Q56.8 and the retired Q32.32 are all
/// this type with a different <typeparamref name="B"/>.
/// </summary>
internal readonly struct VarFix<B> where B : IFractionBits
{
    internal static int F => B.FractionalBits;

    internal readonly long Raw;

    private VarFix(long raw) => Raw = raw;

    internal static VarFix<B> Zero() => new(0);

    internal static VarFix<B> FromRaw(long raw) => new(raw);

    internal static VarFix<B> FromWhole(long whole) => new(whole << F);

    internal static VarFix<B> FromDouble(double value) =>
        new((long)Math.Round(value * (1L << F), MidpointRounding.ToEven));

    internal double ToDouble() => Raw / (double)(1L << F);

    public static VarFix<B> operator +(VarFix<B> a, VarFix<B> b) => new(a.Raw + b.Raw);

    public static VarFix<B> operator -(VarFix<B> a, VarFix<B> b) => new(a.Raw - b.Raw);

    public static VarFix<B> operator -(VarFix<B> a) => new(-a.Raw);

    public static VarFix<B> operator *(VarFix<B> a, VarFix<B> b) =>
        new((long)(((Int128)a.Raw * b.Raw) >> F));
}

internal struct VarVec<B> where B : IFractionBits
{
    internal VarFix<B> X;
    internal VarFix<B> Y;
    internal VarFix<B> Z;
}

/// <summary>
/// The width-variant experiment: can a 64-bit-storage type — Q48.16, Q34.30, Q56.8, or the
/// retired Q32.32 — hold the same one-year Earth orbit the Q64.64 control holds?
/// </summary>
/// <remarks>
/// <para>
/// Same algorithm, same step, same initial conditions as the Fix128 control in
/// <see cref="Program"/>; only the arithmetic type changes. Gravity cannot be written the
/// way <c>Verlet128</c> writes it, because the values stop fitting the type: no 64-bit
/// candidate can hold <c>r²</c> at 1 AU inside its own range, and Q34.30-km and Q48.16-m
/// cannot even store the Sun's GM. Gravity is therefore computed in raw integers with the
/// gravitational parameter entering as a wide scaled constant — which is the point. A
/// narrower type does not remove the width the physics needs; it moves the width into
/// manual intermediate handling at every call site.
/// </para>
/// <para>
/// The gravitational parameter is stored as <c>Scaled · 2^-Shift</c> unit³/s². Metres and
/// kilometres are exact integers; megametres needs the shift because GM is 132.712…
/// Mm³/s², and rounding it to a whole number injects a 0.2 % error that sends the orbit
/// out of the solar system — a bug the first version of this experiment shipped, in both
/// the variant AND the double control that was supposed to catch it.
/// </para>
/// </remarks>
internal static class WidthVariants
{
    private const int TicksPerYear = 8766;
    private const double MetresPerAu = 149_597_870_000.0;

    /// <summary>GM of the Sun as Scaled · 2^-Shift unit³/s², plus the plain value for reporting.</summary>
    private readonly record struct WideMu(Int128 Scaled, int Shift, double Physical);

    // From 1.32712440018 × 10^20 m³/s².
    private static readonly WideMu SunGmMetres = new((Int128)132_712_440_018 * 1_000_000_000, 0, 1.32712440018e20);
    private static readonly WideMu SunGmKm = new(132_712_440_018, 0, 1.32712440018e11);
    private static readonly WideMu SunGmMm = new((Int128)Math.Round(132.712440018 * 4_294_967_296.0), 32, 132.712440018);

    internal static void Run()
    {
        Console.WriteLine("Width variants — the same orbit, the integrator unchanged, storage narrowed to 64 bits");
        Console.WriteLine("--------------------------------------------------------------------------------------");
        Console.WriteLine("  Same algorithm, step and initial conditions as the Q64.64 control. All variants are");
        Console.WriteLine("  a long with Int128 intermediates, so they share one speed class; what differs is");
        Console.WriteLine("  range, resolution, and which constants stop fitting the type. Every timing keeps the");
        Console.WriteLine("  last of forty passes: the tiered JIT needs about ten long-loop executions to reach");
        Console.WriteLine("  final code (set SPIKE_TIMING_TRACE=1 to see the cliff), and a first-pass measurement");
        Console.WriteLine("  bills the JIT to the type rather than the arithmetic.");
        Console.WriteLine();

        RunYear<F16>("Q48.16, metre", 1.0, SunGmMetres);
        RunYear<F30>("Q34.30, km   ", 1e3, SunGmKm);
        RunYear<F16>("Q48.16, km   ", 1e3, SunGmKm);
        RunYear<F8>("Q56.8,  km   ", 1e3, SunGmKm);
        RunYear<F32>("Q32.32, Mm   ", 1e6, SunGmMm);

        RunFineTicks();
    }

    private static void RunYear<B>(string label, double metresPerUnit, WideMu mu) where B : IFractionBits
    {
        int f = VarFix<B>.F;
        double radiusUnits = MetresPerAu / metresPerUnit;
        double speedUnits = 29_780.0 / metresPerUnit;
        double quantum = Math.Pow(2, -f);
        double rangeUnits = Math.Pow(2, 63 - f);

        // The quanta context: whether the physics registers in the type at all.
        double solarA = mu.Physical / (radiusUnits * radiusUnits);
        double aQuanta = solarA / quantum;
        double dvQuanta = solarA * 3600.0 / quantum;
        bool gmFits = mu.Physical < rangeUnits;
        bool rSquaredFits = radiusUnits * radiusUnits < rangeUnits;

        Console.WriteLine($"  {label}  range ±{rangeUnits * metresPerUnit / MetresPerAu,8:G4} AU   grid {quantum * metresPerUnit,9:G3} m");
        Console.WriteLine($"      GM storable in the type: {(gmFits ? "yes" : "NO — wide-constant path")}"
            + $"   |   r² storable at 1 AU: {(rSquaredFits ? "yes" : "NO — raw-integer path")}");
        Console.WriteLine($"      solar acceleration at 1 AU = {aQuanta:G3} quanta of the type's LSB;"
            + $" Δv per 1 h step = {dvQuanta:G4} quanta");
        if (aQuanta < 1.0)
        {
            Console.WriteLine("      ACCELERATION IS BELOW THE LSB — gravity rounds to zero and the orbit never curves.");
        }

        VarVec<B> pos = new()
        {
            X = VarFix<B>.FromDouble(radiusUnits),
            Y = VarFix<B>.Zero(),
            Z = VarFix<B>.Zero(),
        };
        VarVec<B> vel = new()
        {
            X = VarFix<B>.Zero(),
            Y = VarFix<B>.FromDouble(speedUnits),
            Z = VarFix<B>.Zero(),
        };
        VarFix<B> dt = VarFix<B>.FromWhole(3600);

        var doubleState = new Program.DoubleState(
            new Program.Double3(radiusUnits, 0, 0),
            new Program.Double3(0, speedUnits, 0));
        double gmDouble = mu.Physical;

        double variantEnergy0 = Energy(pos, vel, mu);
        double doubleEnergy0 = Program.DoubleEnergy(doubleState, gmDouble);

        // Integrate the real state once, untimed, for the accuracy figures.
        RunLoop(ref pos, ref vel, mu, dt, ref doubleState, gmDouble, TicksPerYear);

        // Timing: three passes on a throwaway copy, keeping the last. The tiered JIT and
        // the CPU's own frequency ramp both bill the early passes; the machine warms up
        // measurably over the run, which is visible in the transcript itself.
        double nsPerStep = TimePasses(pos, vel, mu, dt, doubleState, gmDouble, TicksPerYear);
        double doubleNsPerStep = TimeDoublePasses(doubleState, gmDouble, TicksPerYear);

        double radiusKm = Length(pos) * metresPerUnit / 1000.0;
        double error = ErrorMetres(pos, doubleState, metresPerUnit);
        double drift = RelativeDrift(variantEnergy0, Energy(pos, vel, mu));
        double doubleDrift = RelativeDrift(doubleEnergy0, Program.DoubleEnergy(doubleState, gmDouble));
        double relative = error / (2 * Math.PI * MetresPerAu);

        Console.WriteLine($"      final radius {radiusKm,16:N3} km   position error vs double {error,11:G4} m"
            + $"   ({relative:G3} of the year's path)");
        Console.WriteLine($"      energy drift {drift,9:G4}   (double control {doubleDrift:G3})"
            + $"   cost {nsPerStep,7:N0} ns/step"
            + $"   (double {doubleNsPerStep:N0})");
        Console.WriteLine();
    }

    /// <summary>
    /// The local-frame regime: Q48.16 metres at a navigation tick, where the question is
    /// not range but whether a per-tick increment survives quantisation.
    /// </summary>
    /// <remarks>
    /// The step is 1/128 s rather than 1/120 s because it is exactly representable in every
    /// width — a rounded dt would run every type at a slightly wrong rate and confound the
    /// drift with a time-scale bias.
    /// </remarks>
    private static void RunFineTicks()
    {
        const double dtSeconds = 1.0 / 128.0;
        const int ticks = 128 * 3600; // one hour

        Console.WriteLine("  The navigation tick — one hour at dt = 1/128 s (exact in every width)");
        Console.WriteLine("      Q48.16 metre quanta: solar Δv per tick "
            + $"{1.32712440018e20 / (MetresPerAu * MetresPerAu) * dtSeconds / Math.Pow(2, -16):G3};"
            + $" a 4 milligee burn per tick {0.04 * dtSeconds / Math.Pow(2, -16):G3}");

        RunFine<F16>("Q48.16, metre", SunGmMetres, dtSeconds, ticks);

        // The Q64.64 control over the same window, for contrast.
        Fix128 dt = Fix128.FromDouble(dtSeconds);
        Fix128Vec fPos = new(Constants.EarthOrbitalRadiusKm, Fix128.Zero, Fix128.Zero);
        Fix128Vec fVel = new(Fix128.Zero, Constants.EarthOrbitalSpeedKm, Fix128.Zero);
        var fixedState = new SolarState(fPos, fVel);
        Fix128 fixedEnergy0 = Verlet128.SpecificEnergy(fixedState, Constants.SunGm);

        var warmState = fixedState;
        for (int tick = 1; tick <= ticks; tick++)
        {
            Verlet128.Step(ref fixedState, Constants.SunGm, dt);
        }

        double fixedNs = 0;
        for (int pass = 0; pass < 40; pass++)
        {
            var clock = Stopwatch.StartNew();
            for (int tick = 1; tick <= ticks; tick++)
            {
                Verlet128.Step(ref warmState, Constants.SunGm, dt);
            }

            clock.Stop();
            fixedNs = clock.Elapsed.TotalSeconds / ticks * 1e9;
        }

        double fixedDrift = RelativeDrift(fixedEnergy0.ToDouble(), Verlet128.SpecificEnergy(fixedState, Constants.SunGm).ToDouble());
        Console.WriteLine($"      Q64.64, km    energy drift {fixedDrift,9:G4}   cost {fixedNs,7:N0} ns/step");
        Console.WriteLine();
    }

    private static void RunFine<B>(string label, WideMu mu, double dtSeconds, int ticks) where B : IFractionBits
    {
        VarVec<B> pos = new()
        {
            X = VarFix<B>.FromDouble(MetresPerAu),
            Y = VarFix<B>.Zero(),
            Z = VarFix<B>.Zero(),
        };
        VarVec<B> vel = new()
        {
            X = VarFix<B>.Zero(),
            Y = VarFix<B>.FromDouble(29_780.0),
            Z = VarFix<B>.Zero(),
        };
        VarFix<B> dt = VarFix<B>.FromDouble(dtSeconds);

        var doubleState = new Program.DoubleState(
            new Program.Double3(MetresPerAu, 0, 0),
            new Program.Double3(0, 29_780.0, 0));

        double variantEnergy0 = Energy(pos, vel, mu);

        RunLoop(ref pos, ref vel, mu, dt, ref doubleState, mu.Physical, ticks);

        double nsPerStep = TimePasses(pos, vel, mu, dt, doubleState, mu.Physical, ticks);

        double drift = RelativeDrift(variantEnergy0, Energy(pos, vel, mu));
        double error = ErrorMetres(pos, doubleState, 1.0);
        Console.WriteLine($"      {label}  energy drift {drift,9:G4}   position error vs double {error,11:G4} m"
            + $"   cost {nsPerStep,7:N0} ns/step");
    }

    // ------------------------------------------------------------------ arithmetic

    private static void RunLoop<B>(ref VarVec<B> pos, ref VarVec<B> vel, WideMu mu, VarFix<B> dt,
        ref Program.DoubleState doubleState, double gmDouble, int ticks) where B : IFractionBits
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            Step(ref pos, ref vel, mu, dt);
            Program.DoubleStep(ref doubleState, gmDouble, dt.ToDouble());
        }
    }

    private static void RunDoubleLoop(ref Program.DoubleState state, double gm, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            Program.DoubleStep(ref state, gm, 3600.0);
        }
    }

    /// <summary>
    /// Timing passes on a throwaway copy of the state, keeping the last pass's cost.
    /// </summary>
    /// <remarks>
    /// Forty passes, not three: the tiered JIT promotes a method after roughly thirty
    /// calls, and on-stack replacement only rescues loops much longer than a year's 8 766
    /// ticks. Fewer passes measure the un-optimised tier — the year's pass and the hour's
    /// pass ran the same code at a 10× cost difference until this was counted properly.
    /// </remarks>
    private static double TimePasses<B>(VarVec<B> pos, VarVec<B> vel, WideMu mu, VarFix<B> dt,
        Program.DoubleState doubleState, double gmDouble, int ticks) where B : IFractionBits
    {
        double nsPerStep = 0;
        for (int pass = 0; pass < 40; pass++)
        {
            var p = pos;
            var v = vel;
            Program.DoubleState d = doubleState;

            var clock = Stopwatch.StartNew();
            RunLoop(ref p, ref v, mu, dt, ref d, gmDouble, ticks);
            clock.Stop();
            nsPerStep = clock.Elapsed.TotalSeconds / ticks * 1e9;
            if (Environment.GetEnvironmentVariable("SPIKE_TIMING_TRACE") == "1")
            {
                Console.WriteLine($"        pass {pass,2}: {nsPerStep,7:N0} ns/step");
            }
        }

        return nsPerStep;
    }

    private static double TimeDoublePasses(Program.DoubleState state, double gm, int ticks)
    {
        double nsPerStep = 0;
        for (int pass = 0; pass < 40; pass++)
        {
            var s = state;
            var clock = Stopwatch.StartNew();
            RunDoubleLoop(ref s, gm, ticks);
            clock.Stop();
            nsPerStep = clock.Elapsed.TotalSeconds / ticks * 1e9;
        }

        return nsPerStep;
    }

    /// <summary>
    /// Gravity in raw integers: <c>a = -μ·r/|r|³</c> as magnitude <c>μ/|r|²</c> times the
    /// unit vector, with μ a wide scaled constant and no intermediate ever materialised in
    /// the narrow type. <c>(μ &lt;&lt; 3f) / r²</c> is the acceleration at scale 2^f; the
    /// shift is placed so the quotient does not truncate to zero at 1 AU for the surviving
    /// variants.
    /// </summary>
    private static VarVec<B> Gravity<B>(VarVec<B> p, WideMu mu) where B : IFractionBits
    {
        int f = VarFix<B>.F;
        Int128 rSquared = (Int128)p.X.Raw * p.X.Raw
            + (Int128)p.Y.Raw * p.Y.Raw
            + (Int128)p.Z.Raw * p.Z.Raw;
        long r = (long)IntMath.Isqrt((UInt128)rSquared);

        long aMag = (long)((mu.Scaled << (3 * f - mu.Shift)) / rSquared);
        return new VarVec<B>
        {
            X = VarFix<B>.FromRaw((long)-((Int128)aMag * p.X.Raw / r)),
            Y = VarFix<B>.FromRaw((long)-((Int128)aMag * p.Y.Raw / r)),
            Z = VarFix<B>.FromRaw((long)-((Int128)aMag * p.Z.Raw / r)),
        };
    }

    private static void Step<B>(ref VarVec<B> pos, ref VarVec<B> vel, WideMu mu, VarFix<B> dt) where B : IFractionBits
    {
        VarFix<B> halfDt = dt * VarFix<B>.FromRaw(1L << (VarFix<B>.F - 1));
        VarVec<B> a = Gravity(pos, mu);
        VarFix<B> halfDtSq = halfDt * dt;

        VarVec<B> newPos = new()
        {
            X = pos.X + vel.X * dt + a.X * halfDtSq,
            Y = pos.Y + vel.Y * dt + a.Y * halfDtSq,
            Z = pos.Z + vel.Z * dt + a.Z * halfDtSq,
        };

        VarVec<B> na = Gravity(newPos, mu);

        pos = newPos;
        vel = new VarVec<B>
        {
            X = vel.X + (a.X + na.X) * halfDt,
            Y = vel.Y + (a.Y + na.Y) * halfDt,
            Z = vel.Z + (a.Z + na.Z) * halfDt,
        };
    }

    /// <summary>Specific orbital energy v²/2 − μ/r, with μ/r taken in raw integers.</summary>
    private static double Energy<B>(VarVec<B> pos, VarVec<B> vel, WideMu mu) where B : IFractionBits
    {
        int f = VarFix<B>.F;
        Int128 rSquared = (Int128)pos.X.Raw * pos.X.Raw
            + (Int128)pos.Y.Raw * pos.Y.Raw
            + (Int128)pos.Z.Raw * pos.Z.Raw;
        long r = (long)IntMath.Isqrt((UInt128)rSquared);
        long muOverR = (long)((mu.Scaled << (2 * f - mu.Shift)) / r);

        VarFix<B> half = VarFix<B>.FromRaw(1L << (f - 1));
        VarFix<B> v2 = vel.X * vel.X + vel.Y * vel.Y + vel.Z * vel.Z;
        return (v2 * half - VarFix<B>.FromRaw(muOverR)).ToDouble();
    }

    private static double Length<B>(VarVec<B> v) where B : IFractionBits
    {
        double x = v.X.ToDouble();
        double y = v.Y.ToDouble();
        double z = v.Z.ToDouble();
        return Math.Sqrt(x * x + y * y + z * z);
    }

    private static double ErrorMetres<B>(VarVec<B> a, Program.DoubleState b, double metresPerUnit) where B : IFractionBits
    {
        double dx = a.X.ToDouble() - b.Position.X;
        double dy = a.Y.ToDouble() - b.Position.Y;
        double dz = a.Z.ToDouble() - b.Position.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) * metresPerUnit;
    }

    private static double RelativeDrift(double start, double end) => Math.Abs((end - start) / start);
}
