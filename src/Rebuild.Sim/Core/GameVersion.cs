using Rebuild.Sim.Cultures;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.Core;

/// <summary>
/// Identifies sim rules + data. Peers, saves and replays must match exactly. Bump the version in
/// the same commit as any golden-hash change (docs/08-testing.md §3).
/// </summary>
public readonly record struct GameVersion(ushort Major, ushort Minor, ushort Patch, ulong DataHash)
{
    public static readonly GameVersion Current = new(0, 2, 0, CultureCatalog.DataHash);

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
