namespace Rebuild.Sim.Core;

/// <summary>SplitMix64 (Steele, Lea, Flood 2014); used only to derive seeds (ADR 0006).</summary>
public static class SplitMix64
{
    /// <summary>Advances <paramref name="state"/> and returns the next output.</summary>
    public static ulong Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>One-shot mix of a 64-bit value.</summary>
    public static ulong Mix(ulong value) => Next(ref value);
}
