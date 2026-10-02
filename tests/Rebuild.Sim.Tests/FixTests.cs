using System;
using System.Numerics;
using FsCheck;
using FsCheck.Xunit;
using Rebuild.Sim.Core;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>Fix against a BigInteger oracle (ADR 0002, docs/08-testing.md §2).</summary>
public class FixTests
{
    private const int Cases = 10_000;

    /// <summary>Longs spread over the whole range plus small and boundary values.</summary>
    public static class Generators
    {
        public static Arbitrary<long> Long()
        {
            var full = Gen.Two(Arb.Generate<int>()).Select(t => ((long)t.Item1 << 32) ^ (uint)t.Item2);
            var fixScale = Gen.Choose(-1_000_000, 1_000_000).Select(i => (long)i * 65_536 + i % 65_536);
            var small = Gen.Choose(-70_000, 70_000).Select(i => (long)i);
            var edges = Gen.Elements(long.MinValue, long.MinValue + 1, long.MaxValue, -1L, 0L, 1L, 65_536L, -65_536L);
            return Arb.From(Gen.Frequency(
                Tuple.Create(4, full), Tuple.Create(4, fixScale), Tuple.Create(2, small), Tuple.Create(1, edges)));
        }
    }

    private static BigInteger FloorDiv(BigInteger a, BigInteger b)
    {
        var q = BigInteger.DivRem(a, b, out var rem);
        if (!rem.IsZero && (rem.Sign < 0) != (b.Sign < 0)) q -= 1;
        return q;
    }

    /// <summary>Keep the low 64 bits, as the Fix implementation documents for out-of-range results.</summary>
    private static long Wrap(BigInteger v) => (long)(ulong)(v & ulong.MaxValue);

    [Property(MaxTest = Cases, Arbitrary = new[] { typeof(Generators) })]
    public bool Multiply_matches_floor_oracle(long a, long b) =>
        (Fix.FromRaw(a) * Fix.FromRaw(b)).Raw == Wrap(FloorDiv((BigInteger)a * b, 65_536));

    [Property(MaxTest = Cases, Arbitrary = new[] { typeof(Generators) })]
    public bool Divide_matches_truncating_oracle(long a, long b)
    {
        if (b == 0) return true;
        return (Fix.FromRaw(a) / Fix.FromRaw(b)).Raw == Wrap(BigInteger.Divide((BigInteger)a * 65_536, b));
    }

    [Property(MaxTest = Cases, Arbitrary = new[] { typeof(Generators) })]
    public bool Add_and_subtract_wrap(long a, long b) =>
        (Fix.FromRaw(a) + Fix.FromRaw(b)).Raw == Wrap((BigInteger)a + b)
        && (Fix.FromRaw(a) - Fix.FromRaw(b)).Raw == Wrap((BigInteger)a - b);

    [Property(MaxTest = Cases, Arbitrary = new[] { typeof(Generators) })]
    public bool FromRatio_matches_floor_oracle(long n, long d)
    {
        if (d == 0) return true;
        return Fix.FromRatio(n, d).Raw == Wrap(FloorDiv((BigInteger)n * 65_536, d));
    }

    [Property(MaxTest = Cases, Arbitrary = new[] { typeof(Generators) })]
    public bool Sqrt_is_exact_floor(long a)
    {
        if (a < 0) a = a == long.MinValue ? long.MaxValue : -a;
        long r = Fix.Sqrt(Fix.FromRaw(a)).Raw;
        BigInteger n = (BigInteger)a * 65_536;
        return (BigInteger)r * r <= n && ((BigInteger)r + 1) * ((BigInteger)r + 1) > n;
    }

    [Property(MaxTest = Cases, Arbitrary = new[] { typeof(Generators) })]
    public bool Rounding_helpers_match_oracle(long a)
    {
        var f = Fix.FromRaw(a);
        return f.FloorToInt() == (long)FloorDiv(a, 65_536)
            && (a > long.MaxValue - 65_535 || f.CeilToInt() == (long)-FloorDiv(-(BigInteger)a, 65_536))
            && (a > long.MaxValue - 32_768 || f.RoundToInt() == (long)FloorDiv((BigInteger)a + 32_768, 65_536))
            && f.Frac.Raw == (long)(a - FloorDiv(a, 65_536) * 65_536);
    }

    [Property(MaxTest = Cases, Arbitrary = new[] { typeof(Generators) })]
    public bool ToString_is_exact(long a)
    {
        // Parse the decimal text back with BigInteger arithmetic: value = text * 65536 exactly.
        string s = Fix.FromRaw(a).ToString();
        bool neg = s.StartsWith('-');
        string body = neg ? s[1..] : s;
        string[] parts = body.Split('.');
        BigInteger scaled = BigInteger.Parse(parts[0]) * 65_536;
        if (parts.Length == 2)
        {
            BigInteger frac = BigInteger.Parse(parts[1]);
            BigInteger pow = BigInteger.Pow(10, parts[1].Length);
            if (frac * 65_536 % pow != 0) return false;
            scaled += frac * 65_536 / pow;
        }
        return (neg ? -scaled : scaled) == a;
    }

    [Fact]
    public void Basic_values()
    {
        Assert.Equal(Fix.FromInt(6), Fix.FromInt(2) * Fix.FromInt(3));
        Assert.Equal(Fix.Half, Fix.FromRatio(1, 2));
        Assert.Equal("-1.5", Fix.FromRatio(-3, 2).ToString());
        Assert.Equal("0.0000152587890625", Fix.FromRaw(1).ToString());
        Assert.Equal(Fix.FromInt(3), Fix.Sqrt(Fix.FromInt(9)));
        Assert.Equal(-1L, Fix.FromRaw(-1).FloorToInt());
        Assert.Equal(0L, Fix.FromRaw(-1).CeilToInt());
        Assert.Equal(1L, Fix.Half.RoundToInt());
        Assert.Equal(0L, (-Fix.Half).RoundToInt());
    }

    [Fact]
    public void Division_by_zero_throws()
    {
        Assert.Throws<DivideByZeroException>(() => Fix.One / Fix.Zero);
        Assert.Throws<DivideByZeroException>(() => Fix.FromRatio(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fix.Sqrt(-Fix.One));
    }

    [Fact]
    public void Multiplication_rounds_toward_negative_infinity()
    {
        var tiny = Fix.FromRaw(1);
        Assert.Equal(0, (tiny * Fix.Half).Raw);
        Assert.Equal(-1, (-tiny * Fix.Half).Raw);
    }
}
