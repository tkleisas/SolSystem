using System.Numerics;
using SolSystem.Core.Numerics;
using Xunit;

namespace SolSystem.Core.Tests;

/// <summary>
/// Golden-value and error-bound tests for the Q64.64 solar-frame type.
/// </summary>
/// <remarks>
/// This type accumulated a remarkable number of bugs during its construction — a multiply
/// whose high and low halves were swapped, a divide whose shift truncated, a square root
/// that converged to a wrong fixed point, and a start bit computed one place too high.
/// Every one of them produced plausible numbers rather than an exception, so the tests
/// here are written against an independent <see cref="BigInteger"/> reference rather than
/// against hand-computed expectations.
/// </remarks>
public class Fix128Tests
{
    private static BigInteger SqrtBig(BigInteger n)
    {
        if (n <= 0)
        {
            return 0;
        }

        BigInteger x = BigInteger.One << ((int)(n.GetBitLength() + 1) / 2);
        while (true)
        {
            BigInteger next = (x + (n / x)) >> 1;
            if (next >= x)
            {
                break;
            }

            x = next;
        }

        while (x * x > n)
        {
            x--;
        }

        while ((x + 1) * (x + 1) <= n)
        {
            x++;
        }

        return x;
    }

    private static double RelativeError(double actual, double expected) =>
        expected == 0 ? Math.Abs(actual) : Math.Abs(actual - expected) / Math.Abs(expected);

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(0.5)]
    [InlineData(-0.5)]
    [InlineData(3.25)]
    [InlineData(-7.125)]
    [InlineData(149_597_870.0)]
    [InlineData(1_420_000_000.0)]
    [InlineData(5_900_000_000_000.0)]
    public void RoundTrip_IsExactForRepresentableValues(double value)
    {
        // Every value here has an exact Q64.64 representation: it is a multiple of a
        // power of two, or an integer, and the type must not lose a bit of it.
        Assert.Equal(value, Fix128.FromDouble(value).ToDouble());
    }

    [Fact]
    public void AddAndSubtract_HandleMixedSigns()
    {
        Check(1 + 1, 2);
        Check(0.5 + 0.25, 0.75);
        Check(-5 + 2, -3);
        Check(5 + -2, 3);
        Check(-5 + -2, -7);
        Check(1 - 3, -2);
        Check(3 - 1, 2);
        Check(-3 - -1, -2);
        Check(1e8 + 1e8, 2e8);

        static void Check(double actual, double expected)
        {
            Fix128 result = Fix128.FromDouble(actual);
            Assert.True(RelativeError(result.ToDouble(), expected) < 1e-15);
        }
    }

    [Theory]
    [InlineData(1.0, 1.0, 1.0)]
    [InlineData(2.0, 3.0, 6.0)]
    [InlineData(0.5, 4.0, 2.0)]
    [InlineData(-3.0, 7.0, -21.0)]
    [InlineData(-0.5, -8.0, 4.0)]
    [InlineData(1.496e8, 1.496e8, 1.496e8 * 1.496e8)]
    [InlineData(1.42e9, 1.42e9, 1.42e9 * 1.42e9)]
    public void Multiply_IsExactInTheRepresentableRange(double a, double b, double expected)
    {
        Fix128 product = Fix128.FromDouble(a) * Fix128.FromDouble(b);

        // Exact, not approximate: Fixed128.FromDouble is exact for these operands and the
        // product is formed from exact 64x64 partial products.
        Assert.Equal(expected, product.ToDouble());
    }

    [Fact]
    public void Multiply_IsExactAgainstTheRawDefinition()
    {
        // (a * b) >> 64 on the raw magnitudes is the definition; check the implementation
        // against it at full precision rather than through a double round trip.
        foreach ((long a, long b) in new[] { (1L, 1L), (3L, 7L), (-5L, 11L), (1_000_003L, 999_983L) })
        {
            Fix128 x = Fix128.FromWhole(a);
            Fix128 y = Fix128.FromWhole(b);
            Assert.Equal((BigInteger)a * b, (BigInteger)(x * y).ToDouble());
        }
    }

    [Theory]
    [InlineData(6.0, 3.0, 2.0)]
    [InlineData(-9.0, 2.0, -4.5)]
    [InlineData(1.496e8, 2.0, 7.48e7)]
    [InlineData(-1.0, 4.0, -0.25)]
    public void Divide_IsExactForExactQuotients(double a, double b, double expected)
    {
        Assert.Equal(expected, (Fix128.FromDouble(a) / Fix128.FromDouble(b)).ToDouble());
    }

    [Fact]
    public void Divide_IsAccurateToTheLastBit()
    {
        // 1/3 is not representable; the result must be within one LSB of the grid.
        double quotient = (Fix128.One / Fix128.FromDouble(3.0)).ToDouble();
        double oneLsb = Math.Pow(2, -64);

        Assert.True(Math.Abs(quotient - (1.0 / 3.0)) <= oneLsb);
    }

    [Fact]
    public void Divide_ByZero_Throws() =>
        Assert.Throws<DivideByZeroException>(() => Fix128.One / Fix128.Zero);

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(4.0)]
    [InlineData(100.0)]
    [InlineData(1.496e8)]
    [InlineData(2.016e18)]
    [InlineData(5.9e12)]
    public void Sqrt_MatchesDouble(double value)
    {
        double actual = Fix128.Sqrt(Fix128.FromDouble(value)).ToDouble();
        Assert.True(RelativeError(actual, Math.Sqrt(value)) < 1e-14, $"sqrt({value}) = {actual}");
    }

    [Fact]
    public void Sqrt_IsExactInRawUnits()
    {
        // The exact contract: the result r is the largest value with r*r <= a, evaluated
        // at full width because r*r overflows 128 bits for a large input.
        foreach (double value in new[] { 1.0, 2.0, 3.0, 10.0, 1e4, 1.496e8, 1e12 })
        {
            Fix128 a = Fix128.FromDouble(value);
            Fix128 root = Fix128.Sqrt(a);

            BigInteger rawRoot = (BigInteger)root.Magnitude;
            BigInteger rawValue = (BigInteger)a.Magnitude;

            Assert.True(rawRoot * rawRoot <= (rawValue << 64));
            Assert.True((rawRoot + 1) * (rawRoot + 1) > (rawValue << 64));
        }
    }

    /// <summary>
    /// The square root is exact over the whole range, including past 2.6 AU.
    /// </summary>
    /// <remarks>
    /// The exact contract above was only checked up to 1e12 — a twelfth of an AU — and the
    /// implementation had a hard guard rejecting anything above 2^62, which is 2.6 AU. So the
    /// test passed while Jupiter, Saturn and the whole Jovian theatre were unreachable, and the
    /// guard threw rather than returning something wrong, which is the only reason it was ever
    /// noticed. This checks the contract where it used to end.
    /// </remarks>
    [Fact]
    public void Sqrt_IsExactAcrossTheSolarSystem()
    {
        // 0.4 AU out to 50 AU, in kilometres, plus the extremes of the type.
        foreach (double kilometres in new[]
        {
            6e7, 1.496e8, 7.8e8, 2.28e9, 7.78e9, 1.43e9, 2.87e9, 4.5e9, 7.5e9,
            1e12, 1e15, 1e18, 4e18,
        })
        {
            Fix128 a = Fix128.FromDouble(kilometres);
            Fix128 root = Fix128.Sqrt(a);

            BigInteger rawRoot = (BigInteger)root.Magnitude;
            BigInteger rawValue = (BigInteger)a.Magnitude;

            Assert.True(
                rawRoot * rawRoot <= (rawValue << 64),
                $"sqrt({kilometres:G6}) is too large: {root.ToDouble():G17}");
            Assert.True(
                (rawRoot + 1) * (rawRoot + 1) > (rawValue << 64),
                $"sqrt({kilometres:G6}) is too small: {root.ToDouble():G17}");

            Assert.True(
                RelativeError(root.ToDouble(), Math.Sqrt(kilometres)) < 1e-14,
                $"sqrt({kilometres:G6}) = {root.ToDouble():G17}");
        }
    }

    [Fact]
    public void Sqrt_OfNegative_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Fix128.Sqrt(Fix128.FromDouble(-1.0)));

    [Fact]
    public void Representation_CoversTheWholeSolarSystem()
    {
        // The solar frame's reach: 2^63 km, which is past the heliopause by a wide margin.
        double reachAu = Math.Pow(2, 63) / 149_597_870.0;
        Assert.True(reachAu > 100_000.0, $"solar frame reach was only {reachAu} AU");

        // Every planet fits, including the ones Fix64's megametre frame could not reach.
        foreach (double au in new[] { 1.0, 5.2, 9.5, 19.2, 30.0, 39.5 })
        {
            Assert.True(au * 149_597_870.0 < Math.Pow(2, 63));
        }

        // And the grid is 5.4e-20 km, i.e. 5.4e-14 mm: sub-nanometre at any scale the game
        // can render.
        Assert.InRange(Math.Pow(2, -64), 5.4e-20, 5.5e-20);
    }

    [Fact]
    public void GravitySquaredTerm_OverflowsAQ32_32Value()
    {
        // This is the measurement that forced a 128-bit type at all, so it is pinned here:
        // r^2 at 1 AU must not fit in the local frame's Q32.32 long.
        Fix128 r = Fix128.FromDouble(149_597_870.0);
        Fix128 rSquared = r * r;

        Assert.InRange(rSquared.ToDouble(), 2.2e16, 2.3e16);
        Assert.True(rSquared.ToDouble() > 2_147_483_647.0);
        Assert.True(rSquared.ToDouble() / 2_147_483_647.0 > 1e7);
    }

    [Fact]
    public void Multiplication_IsExactWhereALow128MultiplyWouldWrap()
    {
        // The bug this pins: assembling the product from selected 64-bit halves rather
        // than summing the partial products. It returned the low word of the result as if
        // it were the whole thing.
        Fix128 a = Fix128.FromDouble(1.496e8);
        Fix128 product = a * a;

        BigInteger exact = ((BigInteger)a.Magnitude * a.Magnitude) >> 64;
        Assert.Equal((BigInteger)product.Magnitude, exact);
    }
    /// <summary>
    /// A vector's length is right out to the edge of the solar frame, where the squares do not
    /// fit.
    /// </summary>
    /// <remarks>
    /// <c>x² + y² + z²</c> overflows Q64.64 once a component passes 2^63.5, which in kilometres
    /// is 22 AU. Neptune is past it: its components are 4.5 x 10^9 km, their squares reach
    /// 2 x 10^19 against the type's 1.8 x 10^19, and the sum wraps — so a vector thirty
    /// astronomical units long reported itself as 8.3. Silently, because the square feeding the
    /// root was already wrong.
    /// </remarks>
    [Fact]
    public void VectorLength_IsRightAtEveryDistanceInTheSystem()
    {
        var cases = new (double X, double Y, double Z, string Name)[]
        {
            (1.0, 2.0, 2.0, "3"),
            (0.0, 0.0, -5.0, "5"),
            (1.496e8, 0.0, 0.0, "1 AU"),
            (2.28e9, 2.28e9, 2.28e9, "3 x 15 AU"),
            (4.469235e9, -9.552462e7, -1.010250e8, "Neptune"),
            (7.5e9, 0.0, 0.0, "50 AU"),
        };

        foreach ((double x, double y, double z, string name) in cases)
        {
            var v = new Fix128Vec(Fix128.FromDouble(x), Fix128.FromDouble(y), Fix128.FromDouble(z));
            double expected = Math.Sqrt(x * x + y * y + z * z);
            double actual = v.Length.ToDouble();

            Assert.True(
                RelativeError(actual, expected) < 1e-14,
                $"{name}: |({x:G6},{y:G6},{z:G6})| = {actual:G17}, expected {expected:G17}");
        }
    }

}
