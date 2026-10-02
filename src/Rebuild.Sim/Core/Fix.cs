using System;
using System.Globalization;

namespace Rebuild.Sim.Core;

/// <summary>
/// Q48.16 fixed-point number in a <see cref="long"/> (ADR 0002).
/// <list type="bullet">
/// <item>Add/sub: plain <c>long</c> arithmetic, wraps on overflow.</item>
/// <item>Mul: full 128-bit product, then arithmetic shift → rounds toward negative infinity (floor).</item>
/// <item>Div: <c>(a &lt;&lt; 16) / b</c> in 128 bit → truncates toward zero (C# integer division).</item>
/// <item>Results outside the <c>long</c> range wrap (low 64 bits are kept).</item>
/// </list>
/// There is deliberately no conversion to or from floating point; the client converts via Raw.
/// </summary>
public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
{
    public const int FractionBits = 16;
    public const long OneRaw = 1L << FractionBits;
    private const long FractionMask = OneRaw - 1;

    public static readonly Fix Zero = new(0);
    public static readonly Fix One = new(OneRaw);
    public static readonly Fix Half = new(OneRaw / 2);
    public static readonly Fix MinValue = new(long.MinValue);
    public static readonly Fix MaxValue = new(long.MaxValue);

    /// <summary>Underlying Q48.16 bits.</summary>
    public readonly long Raw;

    private Fix(long raw) => Raw = raw;

    public static Fix FromRaw(long raw) => new(raw);

    public static Fix FromInt(long value) => new(value << FractionBits);

    /// <summary>numerator / denominator, rounded toward negative infinity.</summary>
    public static Fix FromRatio(long numerator, long denominator)
    {
        if (denominator == 0) throw new DivideByZeroException();
        Int128 n = (Int128)numerator << FractionBits;
        Int128 q = n / denominator;
        if (n % denominator != 0 && ((n < 0) != (denominator < 0))) q--;
        return new((long)q);
    }

    /// <summary>Largest integer ≤ value.</summary>
    public long FloorToInt() => Raw >> FractionBits;

    /// <summary>Smallest integer ≥ value.</summary>
    public long CeilToInt() => (Raw + FractionMask) >> FractionBits;

    /// <summary>Nearest integer; exact halves round up (toward positive infinity).</summary>
    public long RoundToInt() => (Raw + (OneRaw >> 1)) >> FractionBits;

    /// <summary>Fractional part in [0, 1).</summary>
    public Fix Frac => new(Raw & FractionMask);

    public static Fix operator +(Fix a, Fix b) => new(a.Raw + b.Raw);
    public static Fix operator -(Fix a, Fix b) => new(a.Raw - b.Raw);
    public static Fix operator -(Fix a) => new(-a.Raw);

    public static Fix operator *(Fix a, Fix b) => new((long)(((Int128)a.Raw * b.Raw) >> FractionBits));
    public static Fix operator *(Fix a, long b) => new(a.Raw * b);
    public static Fix operator *(long a, Fix b) => new(a * b.Raw);

    public static Fix operator /(Fix a, Fix b)
    {
        if (b.Raw == 0) throw new DivideByZeroException();
        return new((long)(((Int128)a.Raw << FractionBits) / b.Raw));
    }

    public static Fix operator /(Fix a, long b) => new(a.Raw / b);

    public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
    public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;
    public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
    public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
    public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;

    public static Fix Abs(Fix a) => a.Raw < 0 ? new(-a.Raw) : a;
    public static Fix Min(Fix a, Fix b) => a.Raw <= b.Raw ? a : b;
    public static Fix Max(Fix a, Fix b) => a.Raw >= b.Raw ? a : b;
    public static Fix Clamp(Fix v, Fix min, Fix max) => v.Raw < min.Raw ? min : v.Raw > max.Raw ? max : v;

    /// <summary>Floor of the exact square root. Throws for negative values.</summary>
    public static Fix Sqrt(Fix a)
    {
        if (a.Raw < 0) throw new ArgumentOutOfRangeException(nameof(a), "negative");
        return new((long)IntMath.Isqrt((UInt128)(ulong)a.Raw << FractionBits));
    }

    public bool Equals(Fix other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fix f && f.Raw == Raw;
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);

    /// <summary>Exact decimal text (debug/dumps only), e.g. "-1.5".</summary>
    public override string ToString()
    {
        // |Raw| as UInt128 to survive long.MinValue.
        UInt128 abs = Raw < 0 ? (UInt128)(-(Int128)Raw) : (UInt128)(ulong)Raw;
        UInt128 intPart = abs >> FractionBits;
        UInt128 frac = abs & FractionMask;
        var sb = new System.Text.StringBuilder();
        if (Raw < 0) sb.Append('-');
        sb.Append(intPart.ToString(CultureInfo.InvariantCulture));
        if (frac != 0)
        {
            sb.Append('.');
            // 2^-16 has exactly 16 decimal places; emit until the remainder is zero.
            while (frac != 0)
            {
                frac *= 10;
                sb.Append((char)('0' + (int)(frac >> FractionBits)));
                frac &= FractionMask;
            }
        }
        return sb.ToString();
    }
}
