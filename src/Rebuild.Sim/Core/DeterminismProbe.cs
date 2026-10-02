using System.IO.Hashing;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.Core;

/// <summary>
/// Cross-platform canary: a long mixed sequence of Pcg32, SplitMix64, Fix and integer operations,
/// hashed. Every OS/CPU/build configuration must print the same value (docs/09-roadmap.md M0).
/// </summary>
public static class DeterminismProbe
{
    public const int DefaultSteps = 1_000_000;

    public static ulong Run(ulong seed, int steps)
    {
        var rng = Pcg32.ForStream(seed, RngStream.Economy);
        ulong mix = seed;
        var hasher = new XxHash64();
        var w = new CanonicalWriter(64 * 1024);
        Fix acc = Fix.One;
        for (int i = 0; i < steps; i++)
        {
            uint u = rng.NextUInt();
            int k = rng.NextInt(1000);
            bool chance = rng.Chance(30_000);
            ulong sm = SplitMix64.Next(ref mix);

            Fix a = Fix.FromRaw(((long)u << 8) - (1L << 39));
            Fix b = Fix.FromRatio(k - 500, 37);
            Fix product = a * b;
            Fix quotient = b != Fix.Zero ? a / b : a;
            Fix root = Fix.Sqrt(Fix.Abs(a));
            acc = chance ? acc + product : acc - quotient;
            if (acc.Raw > (1L << 50) || acc.Raw < -(1L << 50)) acc = Fix.FromRaw(acc.Raw >> 20);
            long floorDiv = IntMath.FloorDiv((long)u - int.MaxValue, k - 500 == 0 ? 1 : k - 500);

            w.WriteUInt32(u);
            w.WriteInt32(k);
            w.WriteBool(chance);
            w.WriteUInt64(sm);
            w.WriteFix(product);
            w.WriteFix(quotient);
            w.WriteFix(root);
            w.WriteFix(acc);
            w.WriteInt64(floorDiv);
            w.WriteInt64(acc.FloorToInt() ^ acc.CeilToInt() ^ acc.RoundToInt());
            if (w.Length > 60 * 1024)
            {
                hasher.Append(w.WrittenSpan);
                w.Clear();
            }
        }
        rng.WriteTo(w);
        hasher.Append(w.WrittenSpan);
        return hasher.GetCurrentHashAsUInt64();
    }
}
