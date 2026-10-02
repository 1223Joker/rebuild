using System.Numerics;
using FsCheck.Xunit;
using Rebuild.Sim.Core;
using Rebuild.Sim.Cultures;
using Xunit;

namespace Rebuild.Sim.Tests;

public class IntMathTests
{
    [Theory]
    [InlineData(7, 2, 3)]
    [InlineData(-7, 2, -4)]
    [InlineData(7, -2, -4)]
    [InlineData(-7, -2, 3)]
    [InlineData(-6, 2, -3)]
    public void FloorDiv_rounds_down(long a, long b, long expected) => Assert.Equal(expected, IntMath.FloorDiv(a, b));

    [Property(MaxTest = 10_000)]
    public bool Isqrt_is_exact_floor(long n)
    {
        if (n < 0) n = n == long.MinValue ? long.MaxValue : -n;
        long r = IntMath.Isqrt(n);
        return (BigInteger)r * r <= n && ((BigInteger)r + 1) * ((BigInteger)r + 1) > n;
    }

    [Theory]
    [InlineData(10, 125, 12)]
    [InlineData(-10, 125, -13)]
    [InlineData(10, 100, 10)]
    [InlineData(7, 50, 3)]
    public void Percentage_floors(long value, int percent, long expected) =>
        Assert.Equal(expected, Percentage.Apply(value, percent));
}
