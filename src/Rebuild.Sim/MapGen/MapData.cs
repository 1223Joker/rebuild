using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.MapGen;

public enum Terrain : byte
{
    Water = 0,
    Plains = 1,
    /// <summary>Plains suited for farms (counts as food, F6).</summary>
    Fertile = 2,
    Mountain = 3,
}

public enum MapObject : byte
{
    None = 0,
    Tree = 1,
    /// <summary>Stone outcrop; <see cref="MapData.Amount"/> = stone units.</summary>
    Stone = 2,
    /// <summary>Game animals (food, F6).</summary>
    Game = 3,
    /// <summary>Monster lair (docs/04-game-modes.md §5).</summary>
    Lair = 4,
}

public enum Resource : byte
{
    None = 0,
    Coal = 1,
    Iron = 2,
    Gold = 3,
    Fish = 4,
}

public readonly record struct StartPosition(int X, int Y, byte Team);

[System.Flags]
public enum TileFlags : byte
{
    None = 0,
    Walkable = 1,
    Buildable = 2,
}

/// <summary>
/// Generated map (docs/03-mapgen.md §3 step 12). Hashed layers: height, terrain, object, resource, amount
/// (one byte each per tile, row-major) + starts. <see cref="Flags"/> and <see cref="Region"/> are derived.
/// </summary>
public sealed class MapData
{
    private const uint Magic = 0x50414D42; // "BMAP" little-endian

    public MapData(MapSpec spec, int attempt)
    {
        Spec = spec;
        Attempt = attempt;
        Edge = spec.Edge;
        int n = Edge * Edge;
        Height = new byte[n];
        Terrain = new byte[n];
        Object = new byte[n];
        Resource = new byte[n];
        Amount = new byte[n];
        Flags = new byte[n];
        Region = new int[n];
        Starts = System.Array.Empty<StartPosition>();
    }

    public MapSpec Spec { get; }
    /// <summary>Retry attempt that produced this map (fully determined by the spec).</summary>
    public int Attempt { get; }
    public int Edge { get; }
    public int TileCount => Edge * Edge;

    public byte[] Height { get; }
    public byte[] Terrain { get; }
    public byte[] Object { get; }
    public byte[] Resource { get; }
    public byte[] Amount { get; }
    /// <summary>Derived <see cref="TileFlags"/>; not hashed.</summary>
    public byte[] Flags { get; }
    /// <summary>Derived walkable-component id (−1 = not walkable); not hashed.</summary>
    public int[] Region { get; }

    public StartPosition[] Starts { get; internal set; }

    /// <summary>Lair tile indices in ascending order.</summary>
    public IReadOnlyList<int> Lairs
    {
        get
        {
            var list = new List<int>();
            for (int i = 0; i < Object.Length; i++)
                if (Object[i] == (byte)MapObject.Lair) list.Add(i);
            return list;
        }
    }

    public int Index(int x, int y) => y * Edge + x;

    public bool IsWalkable(int i) => (Flags[i] & (byte)TileFlags.Walkable) != 0;

    public bool IsBuildable(int i) => (Flags[i] & (byte)TileFlags.Buildable) != 0;

    /// <summary>Canonical serialization of everything that defines the map.</summary>
    public void WriteTo(CanonicalWriter w)
    {
        w.WriteUInt32(Magic);
        Spec.WriteTo(w);
        w.WriteInt32(Attempt);
        w.WriteByte((byte)Starts.Length);
        foreach (var s in Starts)
        {
            w.WriteUInt16((ushort)s.X);
            w.WriteUInt16((ushort)s.Y);
            w.WriteByte(s.Team);
        }
        w.WriteRaw(Height);
        w.WriteRaw(Terrain);
        w.WriteRaw(Object);
        w.WriteRaw(Resource);
        w.WriteRaw(Amount);
    }

    /// <summary>MapHash = XxHash64 over <see cref="WriteTo"/> (docs/03-mapgen.md §3 step 12).</summary>
    public ulong ComputeHash()
    {
        var w = new CanonicalWriter(TileCount * 5 + 128);
        WriteTo(w);
        return StateHash.Of(w);
    }

    public static MapData ReadFrom(CanonicalReader r)
    {
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a Rebuild map");
        var spec = MapSpec.ReadFrom(r);
        var map = new MapData(spec, r.ReadInt32());
        int count = r.ReadByte();
        var starts = new StartPosition[count];
        for (int i = 0; i < count; i++) starts[i] = new StartPosition(r.ReadUInt16(), r.ReadUInt16(), r.ReadByte());
        map.Starts = starts;
        int n = map.TileCount;
        r.ReadRaw(n).CopyTo(map.Height, 0);
        r.ReadRaw(n).CopyTo(map.Terrain, 0);
        r.ReadRaw(n).CopyTo(map.Object, 0);
        r.ReadRaw(n).CopyTo(map.Resource, 0);
        r.ReadRaw(n).CopyTo(map.Amount, 0);
        MapGenerator.DeriveLayers(map);
        return map;
    }
}
