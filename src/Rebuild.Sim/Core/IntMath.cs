using System;
namespace Rebuild.Sim.Core;

/// <summary>Integer helpers with explicitly defined rounding.</summary>
public static class IntMath
{
    /// <summary>Division rounding toward negative infinity (C# '/' truncates toward zero).</summary>
    public static long FloorDiv(long a, long b)
    {
        long q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
        return q;
    }

    /// <summary>Exact floor of the square root (digit-by-digit, fixed iteration count).</summary>
    public static ulong Isqrt(UInt128 n)
    {
        UInt128 result = 0;
        UInt128 bit = (UInt128)1 << 126;
        while (bit > n) bit >>= 2;
        while (bit != 0)
        {
            if (n >= result + bit)
            {
                n -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }
            bit >>= 2;
        }
        return (ulong)result;
    }

    /// <summary>Exact floor of the square root of a non-negative long.</summary>
    public static long Isqrt(long n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), "negative");
        return (long)Isqrt((UInt128)(ulong)n);
    }
}
