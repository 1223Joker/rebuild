using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Goods;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.World;

public enum BuildingState : byte
{
    /// <summary>Placed; receives materials and is built up by <see cref="Construction"/>.</summary>
    ConstructionSite = 0,
    Complete = 1,
}

/// <summary>
/// A placed building. (<see cref="X"/>, <see cref="Y"/>) is the footprint's top-left tile; the footprint is
/// <see cref="BuildingDefinition.Side"/>² tiles. <see cref="ClaimId"/> is its territory claim (0 = none).
/// <see cref="DeliveredPlanks"/>, <see cref="DeliveredStone"/> and <see cref="WorkDone"/> track construction
/// progress of a site (all 0 once complete). <see cref="Cycle"/> is the elapsed work-cycle ticks of a complete
/// production building (0 = no cycle running; <see cref="Production"/>); <see cref="Choice"/> is the index in
/// <see cref="ProductionDefinition.Outputs"/> the running cycle makes (picked at its start by a smith's quota; 0 when idle).
/// <see cref="DigLeft"/> is the digger work a site still needs before its footprint is level (<see cref="Construction.DigWork"/>; 0 = level).
/// </summary>
public readonly record struct Building(int Id, ushort Type, byte Owner, int X, int Y, byte Rotation, BuildingState State, int ClaimId,
    int DeliveredPlanks = 0, int DeliveredStone = 0, int WorkDone = 0, int Cycle = 0, int Choice = 0, int DigLeft = 0)
{
    public BuildingDefinition Definition => BuildingCatalog.All[Type];

    /// <summary>Centre tile (territory claims are centred here).</summary>
    public int CenterX => X + Definition.Side / 2;
    public int CenterY => Y + Definition.Side / 2;
}

/// <summary>
/// All buildings of the match plus a per-tile occupancy grid (derived; rebuilt on load). Footprints never
/// overlap and keep a free ring of <see cref="Margin"/> tile(s) to every other footprint, so buildings never
/// wall off a passage between them (ASSUMPTION, docs/06-economy.md §1). Complete storage buildings
/// (<see cref="BuildingDefinition.IsStorage"/>) own a goods stock, one count per good in <see cref="GoodCatalog"/>;
/// complete production buildings (<see cref="BuildingDefinition.Production"/>) own piles: one per input, then one output
/// pile per <see cref="ProductionDefinition.Outputs"/> entry. Complete homes (<see cref="BuildingDefinition.Beds"/> &gt; 0)
/// own <see cref="Households"/> need counters, and those without a stock three pantry piles (food, water, fuel) as their piles;
/// heated workplaces (<see cref="Households.IsHeatedWorkplace"/>) own need counters and a fuel pile after their output piles.
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
    /// <summary>Goods stock per building, parallel to <see cref="_buildings"/>; null for non-storage buildings and sites.</summary>
    private readonly List<int[]?> _stocks = new();
    /// <summary>Input piles then output piles per building, parallel to <see cref="_buildings"/>; null unless complete production.</summary>
    private readonly List<int[]?> _piles = new();
    /// <summary>Need counters per building (<see cref="Households.CounterCount"/>), parallel to <see cref="_buildings"/>; null unless a complete home.</summary>
    private readonly List<int[]?> _needs = new();

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

    /// <summary>Goods stock of a building (indexed by good), or null if it has none.</summary>
    public IReadOnlyList<int>? StockOf(int id)
    {
        int index = IndexOf(id);
        return index >= 0 ? _stocks[index] : null;
    }

    /// <summary>Mutable stock by list index (systems only).</summary>
    internal int[]? StockAt(int index) => _stocks[index];

    /// <summary>Piles of a production building (inputs in data order, then outputs in data order), or null if it has none.</summary>
    public IReadOnlyList<int>? PilesOf(int id)
    {
        int index = IndexOf(id);
        return index >= 0 ? _piles[index] : null;
    }

    /// <summary>Mutable piles by list index (systems only).</summary>
    internal int[]? PilesAt(int index) => _piles[index];

    /// <summary>Need counters of a home (see <see cref="Households"/>), or null.</summary>
    public IReadOnlyList<int>? NeedsOf(int id)
    {
        int index = IndexOf(id);
        return index >= 0 ? _needs[index] : null;
    }

    /// <summary>Mutable need counters by list index (systems only).</summary>
    internal int[]? NeedsAt(int index) => _needs[index];

    /// <summary>Whether the building is a complete home (has beds).</summary>
    public static bool IsHome(in Building b) => b.State == BuildingState.Complete && b.Definition.Beds > 0;

    /// <summary>Whether the building is a complete home without a stock, eating from its pantry piles.</summary>
    public static bool HasPantry(in Building b) => IsHome(b) && !b.Definition.IsStorage;

    private static int[]? NewNeeds(in Building b) =>
        IsHome(b) || Households.IsHeatedWorkplace(b) ? new int[Households.CounterCount] : null;

    private static int[]? NewStock(in Building b) =>
        b.State == BuildingState.Complete && b.Definition.IsStorage ? new int[GoodCatalog.All.Count] : null;

    private static int[]? NewPiles(in Building b) =>
        b.State == BuildingState.Complete && b.Definition.Production is { } p
            ? new int[Households.FuelPile(p) + (Households.IsHeatedWorkplace(b) ? 1 : 0)]
        : HasPantry(b) ? new int[Households.NeedCount] : null;

    /// <summary>
    /// Replaces the building at list index <paramref name="index"/> (same id and footprint; systems only). A storage
    /// building gets an empty stock and a production building empty piles when it becomes complete.
    /// </summary>
    internal void Update(int index, in Building b)
    {
        var old = _buildings[index];
        if (b.Id != old.Id || b.Type != old.Type || b.X != old.X || b.Y != old.Y)
            throw new System.InvalidOperationException("Update must keep id, type and footprint");
        _buildings[index] = b;
        _stocks[index] ??= NewStock(b);
        _piles[index] ??= NewPiles(b);
        _needs[index] ??= NewNeeds(b);
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
    public int Add(ushort type, byte owner, int x, int y, byte rotation, BuildingState state, int claimId, int digLeft = 0)
    {
        if (type >= BuildingCatalog.All.Count) throw new System.ArgumentOutOfRangeException(nameof(type));
        if (rotation > MaxRotation) throw new System.ArgumentOutOfRangeException(nameof(rotation));
        if (!IsFootprintFree(x, y, BuildingCatalog.All[type].Side)) throw new System.InvalidOperationException("Footprint is not free");
        var b = new Building(NextId++, type, owner, x, y, rotation, state, claimId, DigLeft: digLeft);
        _buildings.Add(b);
        _stocks.Add(NewStock(b));
        _piles.Add(NewPiles(b));
        _needs.Add(NewNeeds(b));
        Stamp(b, b.Id);
        return b.Id;
    }

    /// <summary>Removes a building and frees its footprint; its stock is lost.</summary>
    public bool Remove(int id)
    {
        int index = IndexOf(id);
        if (index < 0) return false;
        Stamp(_buildings[index], 0);
        _buildings.RemoveAt(index);
        _stocks.RemoveAt(index);
        _piles.RemoveAt(index);
        _needs.RemoveAt(index);
        return true;
    }

    /// <summary>Index in <see cref="All"/> of building <paramref name="id"/>, or -1.</summary>
    public int IndexOf(int id)
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

    /// <summary>Canonical state: next id, then every building with its construction/cycle progress, stock, piles and need counters (the occupancy grid is derived).</summary>
    public void WriteTo(CanonicalWriter w)
    {
        w.WriteInt32(NextId);
        w.WriteInt32(_buildings.Count);
        for (int i = 0; i < _buildings.Count; i++)
        {
            var b = _buildings[i];
            w.WriteInt32(b.Id);
            w.WriteUInt16(b.Type);
            w.WriteByte(b.Owner);
            w.WriteUInt16((ushort)b.X);
            w.WriteUInt16((ushort)b.Y);
            w.WriteByte(b.Rotation);
            w.WriteByte((byte)b.State);
            w.WriteInt32(b.ClaimId);
            w.WriteByte((byte)b.DeliveredPlanks);
            w.WriteByte((byte)b.DeliveredStone);
            w.WriteUInt16((ushort)b.WorkDone);
            w.WriteUInt16((ushort)b.Cycle);
            w.WriteByte((byte)b.Choice);
            w.WriteUInt16((ushort)b.DigLeft);
            var stock = _stocks[i];
            w.WriteByte(stock == null ? (byte)0 : (byte)1);
            if (stock != null)
                foreach (int n in stock) w.WriteInt32(n);
            var piles = _piles[i];
            if (piles != null)
                foreach (int n in piles) w.WriteByte((byte)n);
            var needs = _needs[i];
            if (needs != null)
                foreach (int n in needs) w.WriteInt32(n);
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
                (BuildingState)r.ReadByte(), r.ReadInt32(), r.ReadByte(), r.ReadByte(), r.ReadUInt16(), r.ReadUInt16(), r.ReadByte(), r.ReadUInt16());
            if (b.Id <= lastId || b.Id >= nextId || b.Type >= BuildingCatalog.All.Count || b.Owner >= playerCount
                || b.Rotation > MaxRotation || b.State > BuildingState.Complete || !reg.IsFootprintFree(b.X, b.Y, b.Definition.Side))
                throw new InvalidDataException("Invalid building");
            if (!Construction.IsConsistent(b))
                throw new InvalidDataException("Invalid construction progress or claim");
            bool hasStock = r.ReadByte() switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException("Invalid stock flag"),
            };
            if (hasStock != (b.State == BuildingState.Complete && b.Definition.IsStorage))
                throw new InvalidDataException("Stock does not match the building");
            int[]? stock = null;
            if (hasStock)
            {
                stock = new int[GoodCatalog.All.Count];
                for (int g = 0; g < stock.Length; g++)
                    if ((stock[g] = r.ReadInt32()) < 0) throw new InvalidDataException("Negative stock");
            }
            var piles = NewPiles(b);
            if (piles != null && b.Definition.Production == null)
            {
                for (int k = 0; k < piles.Length; k++)
                    if ((piles[k] = r.ReadByte()) > Households.PantryTarget) throw new InvalidDataException("Invalid pantry pile");
            }
            else if (piles != null)
            {
                // Input piles never exceed the refill target; the output pile is checked against reservations by Logistics
                // and stays empty for a planter, which produces no good; a fuel pile holds at most a pantry's units.
                var p = b.Definition.Production!;
                int outputCap = p.Output == ProductionDefinition.NoOutput ? 0 : Production.OutputCap;
                for (int k = 0; k < piles.Length; k++)
                    if ((piles[k] = r.ReadByte()) > (k < p.Inputs.Count ? Production.InputTarget
                            : k == Households.FuelPile(p) ? Households.PantryTarget : outputCap))
                        throw new InvalidDataException("Invalid production pile");
            }
            var needs = NewNeeds(b);
            if (needs != null)
            {
                // Due counters stay below their period, or at it while a unit is unpaid; unpaid ticks only while one is due;
                // a workplace only counts heat.
                for (int k = 0; k < needs.Length; k++) needs[k] = r.ReadInt32();
                for (int need = 0; need < Households.NeedCount; need++)
                {
                    int period = Households.Period(b, need), unpaid = needs[Households.NeedCount + need];
                    if (needs[need] < 0 || needs[need] > period || unpaid < 0 || (unpaid > 0 && needs[need] != period)
                        || (need != Households.Heat && !IsHome(b) && (needs[need] != 0 || unpaid != 0)))
                        throw new InvalidDataException("Invalid need counters");
                }
            }
            if (b.ClaimId != 0)
            {
                if (!HasClaim(territory, b.ClaimId, b.Owner) || usedClaims.Contains(b.ClaimId))
                    throw new InvalidDataException("Building references an invalid territory claim");
                usedClaims.Add(b.ClaimId);
            }
            lastId = b.Id;
            reg._buildings.Add(b);
            reg._stocks.Add(stock);
            reg._piles.Add(piles);
            reg._needs.Add(needs);
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
}
