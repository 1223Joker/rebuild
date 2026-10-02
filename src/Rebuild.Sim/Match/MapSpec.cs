using System.IO;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.Match;

public enum MapSize : byte
{
    Small = 0,  // 192²
    Medium = 1, // 256²
    Large = 2,  // 384²
    ExtraLarge = 3, // 512²
}

/// <summary>
/// Map generation parameters (docs/03-mapgen.md §2). M0 carries only seed, size and player count;
/// M1 adds the remaining parameters and bumps <see cref="FormatVersion"/>.
/// </summary>
public readonly record struct MapSpec(ulong Seed, MapSize Size, byte PlayerCount)
{
    public const byte FormatVersion = 1;

    public static int EdgeLength(MapSize size) => size switch
    {
        MapSize.Small => 192,
        MapSize.Medium => 256,
        MapSize.Large => 384,
        MapSize.ExtraLarge => 512,
        _ => throw new System.ArgumentOutOfRangeException(nameof(size)),
    };

    public void WriteTo(CanonicalWriter w)
    {
        w.WriteByte(FormatVersion);
        w.WriteUInt64(Seed);
        w.WriteByte((byte)Size);
        w.WriteByte(PlayerCount);
    }

    public static MapSpec ReadFrom(CanonicalReader r)
    {
        byte version = r.ReadByte();
        if (version != FormatVersion) throw new InvalidDataException($"Unsupported MapSpec version {version}");
        var spec = new MapSpec(r.ReadUInt64(), (MapSize)r.ReadByte(), r.ReadByte());
        if (spec.Size > MapSize.ExtraLarge || spec.PlayerCount < 1 || spec.PlayerCount > MatchSetup.MaxSlots)
            throw new InvalidDataException("Invalid MapSpec");
        return spec;
    }
}
