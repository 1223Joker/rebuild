using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.World;

public enum BuildingState : byte
{
    /// <summary>Placed, waiting for construction (construction itself comes with the next M2 step).</summary>
    ConstructionSite = 0,
    Complete = 1,
}

/// <summary>
/// A placed building. (<see cref="X"/>, <see cref="Y"/>) is the footprint's top-left tile; the footprint is
/// <see cref="BuildingDefinition.Side"/>² tiles. <see cref="ClaimId"/> is its territory claim (0 = none).
/// </summary>
public readonly record struct Building(int Id, ushort Type, byte Owner, int X, int Y, byte Rotation, BuildingState State, int ClaimId)
{
    public BuildingDefinition Definition => BuildingCatalog.All[Type];

    /// <summary>Centre tile (territory claims are centred here).</summary>
    public int CenterX => X + Definition.Side / 2;
    public int CenterY => Y + Definition.Side / 2;
}

/// <summary>
/// All buildings of the match plus a per-tile occupancy grid (derived; rebuilt on load). Footprints never
/// overlap and keep a free ring of <see cref="Margin"/> tile(s) to every other footprint, so buildings never
/// wall off a passage between them (ASSUMPTION, docs/06-economy.md §1).
/// </summary>
public sealed class BuildingRegistry
{
    /// <summary>Free tiles required between two footprints.</summary>
    public const int Margin = 1;
    /// <summary>Highest valid rotation (quarter turns; cosmetic for square footprints).</summary>
    public const byte MaxRotation = 3;

    private readonly int _edge;
    /// <summary>Building id per tile; 0 = free (ids start at 1).</summary>
    private readonly int[] _occupant;
    /// <summary>Live buildings in ascending id order.</summary>
    private readonly List<Building> _buildings = new();

    public BuildingRegistry(int edge)
    {
        _edge = edge;
        _occupant = new int[edge * edge];
        NextId = 1;
    }

    public int Edge => _edge;
    public int NextId { get; private set; }
    public IReadOnlyList<Building> All => _buildings;

    /// <summary>Id of the building covering the tile, 0 if none.</summary>
    public int AtTile(int tile) => _occupant[tile];

    public bool TryGet(int id, out Building building)
    {
        int index = IndexOf(id);
        building = index >= 0 ? _buildings[index] : default;
        return index >= 0;
    }

    /// <summary>Whether a footprint lies inside the map and keeps <see cref="Margin"/> to every other footprint.</summary>
    public bool IsFootprintFree(int x, int y, int side)
    {
        if (x < 0 || y < 0 || side < 1 || x > _edge - side || y > _edge - side) return false;
        int x0 = System.Math.Max(0, x - Margin), y0 = System.Math.Max(0, y - Margin);
        int x1 = System.Math.Min(_edge - 1, x + side - 1 + Margin), y1 = System.Math.Min(_edge - 1, y + side - 1 + Margin);
        for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
                if (_occupant[ty * _edge + tx] != 0) return false;
        return true;
    }

    /// <summary>Adds a building and returns its id. Throws if the footprint is not free (callers validate first).</summary>
    public int Add(ushort type, byte owner, int x, int y, byte rotation, BuildingState state, int claimId)
    {
        if (type >= BuildingCatalog.All.Count) throw new System.ArgumentOutOfRangeException(nameof(type));
        if (rotation > MaxRotation) throw new System.ArgumentOutOfRangeException(nameof(rotation));
        if (!IsFootprintFree(x, y, BuildingCatalog.All[type].Side)) throw new System.InvalidOperationException("Footprint is not free");
        var b = new Building(NextId++, type, owner, x, y, rotation, state, claimId);
        _buildings.Add(b);
        Stamp(b, b.Id);
        return b.Id;
    }

    /// <summary>Removes a building and frees its footprint.</summary>
    public bool Remove(int id)
    {
        int index = IndexOf(id);
        if (index < 0) return false;
        Stamp(_buildings[index], 0);
        _buildings.RemoveAt(index);
        return true;
    }

    /// <summary>Canonical state: next id, then every building (the occupancy grid is derived).</summary>
    public void WriteTo(CanonicalWriter w)
    {
        w.WriteInt32(NextId);
        w.WriteInt32(_buildings.Count);
        foreach (var b in _buildings)
        {
            w.WriteInt32(b.Id);
            w.WriteUInt16(b.Type);
            w.WriteByte(b.Owner);
            w.WriteUInt16((ushort)b.X);
            w.WriteUInt16((ushort)b.Y);
            w.WriteByte(b.Rotation);
            w.WriteByte((byte)b.State);
            w.WriteInt32(b.ClaimId);
        }
    }

    /// <summary>Reads and validates the building list against the map size, player count and territory claims.</summary>
    public static BuildingRegistry ReadFrom(CanonicalReader r, int edge, int playerCount, Territory territory)
    {
        var reg = new BuildingRegistry(edge);
        int nextId = r.ReadInt32();
        if (nextId < 1) throw new InvalidDataException("Invalid next building id");
        int count = r.ReadInt32();
        if (count < 0 || count > edge * edge) throw new InvalidDataException("Invalid building count");
        int lastId = 0;
        var usedClaims = new List<int>();
        for (int i = 0; i < count; i++)
        {
            var b = new Building(r.ReadInt32(), r.ReadUInt16(), r.ReadByte(), r.ReadUInt16(), r.ReadUInt16(), r.ReadByte(),
                (BuildingState)r.ReadByte(), r.ReadInt32());
            if (b.Id <= lastId || b.Id >= nextId || b.Type >= BuildingCatalog.All.Count || b.Owner >= playerCount
                || b.Rotation > MaxRotation || b.State > BuildingState.Complete || !reg.IsFootprintFree(b.X, b.Y, b.Definition.Side))
                throw new InvalidDataException("Invalid building");
            if (b.ClaimId != 0)
            {
                if (!HasClaim(territory, b.ClaimId, b.Owner) || usedClaims.Contains(b.ClaimId))
                    throw new InvalidDataException("Building references an invalid territory claim");
                usedClaims.Add(b.ClaimId);
            }
            lastId = b.Id;
            reg._buildings.Add(b);
            reg.Stamp(b, b.Id);
        }
        reg.NextId = nextId;
        return reg;
    }

    private static bool HasClaim(Territory territory, int claimId, byte owner)
    {
        foreach (var c in territory.Claims)
            if (c.Id == claimId) return c.Owner == owner;
        return false;
    }

    private void Stamp(in Building b, int value)
    {
        int side = b.Definition.Side;
        for (int y = b.Y; y < b.Y + side; y++)
            for (int x = b.X; x < b.X + side; x++)
                _occupant[y * _edge + x] = value;
    }

    private int IndexOf(int id)
    {
        int lo = 0, hi = _buildings.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int cur = _buildings[mid].Id;
            if (cur == id) return mid;
            if (cur < id) lo = mid + 1;
            else hi = mid - 1;
        }
        return -1;
    }
}
