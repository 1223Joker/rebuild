using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.Core;

/// <summary>Independent RNG streams; adding a draw in one system cannot shift another (ADR 0006).</summary>
public enum RngStream : ulong
{
    MapGen = 1,
    Setup = 2,
    Economy = 3,
    Combat = 4,
    Monsters = 5,
    /// <summary>AI stream of slot s is <c>AiBase + s</c>.</summary>
    AiBase = 100,
}

/// <summary>
/// PCG32 (PCG-XSH-RR, 64-bit state, 32-bit output; O'Neill 2014, reference pcg32_random_r).
/// A class, not a struct, so the state cannot be copied by accident. Its state is sim state:
/// it is serialized and hashed.
/// </summary>
[DeterministicRng]
public sealed class Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private ulong _increment;

    /// <summary>Same seeding as the reference <c>pcg32_srandom_r(initstate, initseq)</c>.</summary>
    public Pcg32(ulong seed, ulong sequence)
    {
        _state = 0;
        _increment = (sequence << 1) | 1;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    private Pcg32(ulong state, ulong increment, bool raw)
    {
        _ = raw;
        _state = state;
        _increment = increment;
    }

    /// <summary>The stream <paramref name="stream"/> of a match: seed = SplitMix64(matchSeed ^ streamId).</summary>
    public static Pcg32 ForStream(ulong matchSeed, RngStream stream) =>
        new(SplitMix64.Mix(matchSeed ^ (ulong)stream), (ulong)stream);

    public static Pcg32 ForStream(ulong matchSeed, ulong streamId) =>
        new(SplitMix64.Mix(matchSeed ^ streamId), streamId);

    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * Multiplier + _increment);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
    }

    /// <summary>Uniform in [0, <paramref name="maxExclusive"/>), unbiased (Lemire 2019).</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new System.ArgumentOutOfRangeException(nameof(maxExclusive));
        uint bound = (uint)maxExclusive;
        ulong m = (ulong)NextUInt() * bound;
        uint low = (uint)m;
        if (low < bound)
        {
            uint threshold = (uint)(-bound) % bound;
            while (low < threshold)
            {
                m = (ulong)NextUInt() * bound;
                low = (uint)m;
            }
        }
        return (int)(m >> 32);
    }

    /// <summary>Uniform in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) throw new System.ArgumentOutOfRangeException(nameof(maxExclusive));
        return minInclusive + NextInt(maxExclusive - minInclusive);
    }

    /// <summary>True with probability <paramref name="partsPer65536"/> / 65 536.</summary>
    public bool Chance(int partsPer65536) => (int)(NextUInt() >> 16) < partsPer65536;

    public void WriteTo(CanonicalWriter w)
    {
        w.WriteUInt64(_state);
        w.WriteUInt64(_increment);
    }

    public static Pcg32 ReadFrom(CanonicalReader r)
    {
        ulong state = r.ReadUInt64();
        ulong increment = r.ReadUInt64();
        return new Pcg32(state, increment, raw: true);
    }
}
