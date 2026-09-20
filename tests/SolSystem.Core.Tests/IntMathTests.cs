using SolSystem.Core.Numerics;
using Xunit;

namespace SolSystem.Core.Tests;

/// <summary>
/// <see cref="IntMath.Isqrt"/> is the one function whose correctness is not obvious
/// by inspection, so it is checked exhaustively at small magnitudes and against a
/// reference at large ones.
/// </summary>
public class IntMathTests
{
    [Fact]
    public void Isqrt_IsExactForSmallValues()
    {
        ulong root = 0;
        for (ulong n = 0; n < 20_000; n++)
        {
            while ((root + 1) * (root + 1) <= n)
            {
                root++;
            }

            Assert.Equal(root, IntMath.Isqrt(n));
        }
    }

    [Fact]
    public void Isqrt_IsExactAtPowersAndNeighbours()
    {
        // Squares, one below and one above: the off-by-one cases.
        for (ulong k = 1; k < 100_000; k += 997)
        {
            ulong square = k * k;
            Assert.Equal(k, IntMath.Isqrt(square));
            Assert.Equal(k - 1, IntMath.Isqrt(square - 1));
            Assert.Equal(k, IntMath.Isqrt(square + 1));
        }
    }

    [Fact]
    public void Isqrt_HandlesTheTopOfTheRange()
    {
        ulong max = ulong.MaxValue;
        ulong root = IntMath.Isqrt(max);

        Assert.Equal(4294967295UL, root);

        // The squared comparisons must be taken at 128 bits: (root+1)^2 overflows
        // ulong here, so a 64-bit check would wrap and report the wrong answer.
        Assert.True((UInt128)root * root <= max);
        Assert.True((UInt128)(root + 1) * (root + 1) > max);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(1UL << 32)]
    [InlineData((1UL << 32) + 1)]
    [InlineData(1UL << 62)]
    [InlineData((1UL << 63) + 12345)]
    public void Isqrt_IsAlwaysTheLargestRootThatFits(ulong n)
    {
        ulong root = IntMath.Isqrt(n);
        Assert.True(root * root <= n, $"root^2 {root * root} exceeded {n}");
        Assert.True((root + 1) * (root + 1) > n, $"(root+1)^2 did not exceed {n}");
    }

    [Fact]
    public void Isqrt_OfNegative_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => IntMath.Isqrt(-1L));

    [Fact]
    public void NarrowMul_ShiftsDownBy32Bits()
    {
        // One (raw 2^32) times One must be One, not 2^64 and not zero.
        Assert.Equal(Fix64.OneRaw, IntMath.NarrowMul(IntMath.Mul128(Fix64.OneRaw, Fix64.OneRaw)));

        // The overflow case a 64-bit intermediate would get wrong: 30 AU, squared.
        // 30 AU is 4.488e9 Mm, whose raw representation is far past 2^62, so
        // (a * b) >> 32 overflows long while the 128-bit path does not.
        long neptuneRaw = (long)((Int128)(30L * 149_597_870L) << 32);
        Int128 expected = ((Int128)neptuneRaw * neptuneRaw) >> 32;
        Assert.Equal((long)expected, IntMath.NarrowMul(IntMath.Mul128(neptuneRaw, neptuneRaw)));
    }
}
