using Rebuild.Sim.Buildings;
using Rebuild.Sim.Cultures;
using Rebuild.Sim.Goods;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.Core;

/// <summary>
/// Identifies sim rules + data. Peers, saves and replays must match exactly. Bump the version in
/// the same commit as any golden-hash change (docs/08-testing.md §3).
/// </summary>
public readonly record struct GameVersion(ushort Major, ushort Minor, ushort Patch, ulong DataHash)
{
    public static readonly GameVersion Current = new(0, 22, 0, CombinedDataHash);

    /// <summary>All game data hashes folded into one (FNV-1a 64 continued over the building, then the good hash bytes).</summary>
    public static ulong CombinedDataHash => Fold(Fold(CultureCatalog.DataHash, BuildingCatalog.DataHash), GoodCatalog.DataHash);

    private static ulong Fold(ulong hash, ulong value)
    {
        for (int i = 0; i < 8; i++)
        {
            hash ^= (value >> (8 * i)) & 0xFF;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    public void WriteTo(CanonicalWriter w)
    {
        w.WriteUInt16(Major);
        w.WriteUInt16(Minor);
        w.WriteUInt16(Patch);
        w.WriteUInt64(DataHash);
    }

    public static GameVersion ReadFrom(CanonicalReader r) =>
        new(r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt64());

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}+{DataHash:x16}");
}
