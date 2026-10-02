using System.IO;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.Match;

public enum MapSize : byte
{
    Small = 0,  // 192²
    Medium = 1, // 256²
    Large = 2,  // 384²
    ExtraLarge = 3, // 512²
}

public enum ResourceDensity : byte
{
    Low = 0,
    Normal = 1,
    High = 2,
}

/// <summary>Fairness construction mode (docs/03-mapgen.md §3 step 6).</summary>
public enum MapSymmetry : byte
{
    None = 0,
    /// <summary>Point reflection through the map centre; 2 starts or 2 equal teams.</summary>
    Mirror = 1,
    /// <summary>4-fold rotation about the map centre; exactly 4 starts.</summary>
    Rotational = 2,
    /// <summary>Identical (rotated) start-zone template at every start; any start count.</summary>
    Equalized = 3,
}

public enum MonsterDensity : byte
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>Requested terrain shares in percent; plains get the rest.</summary>
public readonly record struct TerrainMix(byte Water, byte Mountain, byte Forest)
{
    public static readonly TerrainMix Default = new(15, 15, 25);

    public int Plains => 100 - Water - Mountain - Forest;
}

/// <summary>
/// Map generation parameters (docs/03-mapgen.md §2). Same spec + <see cref="GeneratorVersion"/> ⇒ bit-identical
/// map on every platform. <see cref="Teams"/> packs the team of start i into bits [4i, 4i+4).
/// </summary>
public readonly record struct MapSpec(
    ulong Seed,
    MapSize Size,
    byte PlayerCount,
    uint Teams,
    TerrainMix Mix,
    ResourceDensity Resources,
    MapSymmetry Symmetry,
    MonsterDensity Monsters,
    ushort GeneratorVersion)
{
    public const byte FormatVersion = 2;

    /// <summary>Free-for-all team layout: start i is in team i.</summary>
    public const uint FreeForAll = 0x76543210;

    /// <summary>Start zones (radius 28) stop fitting on the start ring of a 192² map beyond 6 starts.</summary>
    public const int MaxStartsSmall = 6;

    /// <summary>Defaults of docs/03-mapgen.md §2 (FFA, 15/15/25 mix, Normal resources, Equalized, no monsters).</summary>
    public MapSpec(ulong seed, MapSize size, byte playerCount)
        : this(seed, size, playerCount, FreeForAll, TerrainMix.Default, ResourceDensity.Normal,
               MapSymmetry.Equalized, MonsterDensity.None, MapGenerator.Version)
    {
    }

    public static int EdgeLength(MapSize size) => size switch
    {
        MapSize.Small => 192,
        MapSize.Medium => 256,
        MapSize.Large => 384,
        MapSize.ExtraLarge => 512,
        _ => throw new System.ArgumentOutOfRangeException(nameof(size)),
    };

    public int Edge => EdgeLength(Size);

    public int TeamOf(int start) => (int)((Teams >> (4 * start)) & 0xF);

    /// <summary>Packs a team list (one entry per start) into <see cref="Teams"/>.</summary>
    public static uint PackTeams(System.ReadOnlySpan<int> teams)
    {
        uint packed = FreeForAll;
        for (int i = 0; i < teams.Length; i++)
        {
            if (teams[i] < 0 || teams[i] > 7) throw new System.ArgumentOutOfRangeException(nameof(teams));
            packed = (packed & ~(0xFu << (4 * i))) | ((uint)teams[i] << (4 * i));
        }
        return packed;
    }

    /// <summary>Null if the spec can be generated, else the reason.</summary>
    public string? Check()
    {
        if (Size > MapSize.ExtraLarge) return "unknown size";
        if (PlayerCount < 2 || PlayerCount > MatchSetup.MaxSlots) return "2..8 starts required";
        if (Size == MapSize.Small && PlayerCount > MaxStartsSmall) return $"Small maps hold at most {MaxStartsSmall} starts";
        for (int i = 0; i < MatchSetup.MaxSlots; i++)
            if (TeamOf(i) > 7) return $"team of start {i} out of range";
        if (Mix.Water + Mix.Mountain + Mix.Forest > 90) return "water + mountain + forest must leave >= 10 % plains";
        if (Mix.Water > 40 || Mix.Mountain > 40) return "water and mountain shares are limited to 40 %";
        if (Resources > ResourceDensity.High || Monsters > MonsterDensity.High || Symmetry > MapSymmetry.Equalized)
            return "unknown enum value";
        int[] teamSizes = new int[8];
        int teamCount = 0;
        for (int i = 0; i < PlayerCount; i++)
            if (teamSizes[TeamOf(i)]++ == 0) teamCount++;
        switch (Symmetry)
        {
            case MapSymmetry.Mirror:
                if (teamCount != 2) return "Mirror needs exactly 2 teams";
                for (int t = 0; t < 8; t++)
                    if (teamSizes[t] != 0 && teamSizes[t] * 2 != PlayerCount) return "Mirror needs 2 teams of equal size";
                break;
            case MapSymmetry.Rotational:
                if (PlayerCount != 4) return "Rotational needs exactly 4 starts";
                for (int t = 0; t < 8; t++)
                    if (teamSizes[t] == 3) return "Rotational cannot split 4 starts 3:1";
                break;
        }
        return null;
    }

    public void WriteTo(CanonicalWriter w)
    {
        w.WriteByte(FormatVersion);
        w.WriteUInt64(Seed);
        w.WriteByte((byte)Size);
        w.WriteByte(PlayerCount);
        w.WriteUInt32(Teams);
        w.WriteByte(Mix.Water);
        w.WriteByte(Mix.Mountain);
        w.WriteByte(Mix.Forest);
        w.WriteByte((byte)Resources);
        w.WriteByte((byte)Symmetry);
        w.WriteByte((byte)Monsters);
        w.WriteUInt16(GeneratorVersion);
    }

    public static MapSpec ReadFrom(CanonicalReader r)
    {
        byte version = r.ReadByte();
        if (version != FormatVersion) throw new InvalidDataException($"Unsupported MapSpec version {version}");
        ulong seed = r.ReadUInt64();
        var size = (MapSize)r.ReadByte();
        byte players = r.ReadByte();
        uint teams = r.ReadUInt32();
        var mix = new TerrainMix(r.ReadByte(), r.ReadByte(), r.ReadByte());
        var spec = new MapSpec(seed, size, players, teams, mix, (ResourceDensity)r.ReadByte(),
            (MapSymmetry)r.ReadByte(), (MonsterDensity)r.ReadByte(), r.ReadUInt16());
        string? error = spec.Check();
        if (error != null) throw new InvalidDataException("Invalid MapSpec: " + error);
        return spec;
    }
}
