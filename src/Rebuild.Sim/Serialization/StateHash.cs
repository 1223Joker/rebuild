using System.IO.Hashing;

namespace Rebuild.Sim.Serialization;

/// <summary>State hash = XxHash64 (seed 0) over the canonical serialization (ADR 0006).</summary>
public static class StateHash
{
    public static ulong Of(System.ReadOnlySpan<byte> canonicalBytes) => XxHash64.HashToUInt64(canonicalBytes);

    public static ulong Of(CanonicalWriter writer) => Of(writer.WrittenSpan);
}
