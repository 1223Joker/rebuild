using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Goods;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.World;

public enum JobState : byte
{
    /// <summary>The carrier walks to the source storage; the unit is already reserved (taken out of its stock).</summary>
    ToPickup = 0,
    /// <summary>The carrier holds the unit and walks to the destination.</summary>
    Carrying = 1,
}

public enum JobKind : byte
{
    /// <summary>Moves one unit of a good.</summary>
    Transport = 0,
    /// <summary>
    /// Makes the carrier the destination's worker: it fetches the tool (<see cref="TransportJob.Good"/>) from the source
    /// storage and walks into the building; without a tool (<see cref="ProductionDefinition.NoTool"/>; source = destination,
    /// starts in <see cref="JobState.Carrying"/>) it walks straight there.
    /// </summary>
    Employ = 1,
}

/// <summary>
/// One unit of <see cref="Good"/> moved by carrier <see cref="CarrierId"/> from <see cref="SourceId"/> (a storage building
/// or a production building's output pile) to <see cref="DestinationId"/>: a construction site, a production building's
/// input pile, or a storage building (output overflow, or a carried unit whose destination vanished or became unreachable).
/// An <see cref="JobKind.Employ"/> job instead brings a worker (with its tool) to a production building.
/// </summary>
public readonly record struct TransportJob(int Id, byte Owner, int CarrierId, ushort Good, int SourceId, int DestinationId, JobState State,
    JobKind Kind = JobKind.Transport);

/// <summary>
/// Logistics system (docs/06-economy.md §3–4). Worker requests come first: every construction site with delivered material to work in (its
/// builder, see <see cref="Construction.BuilderTool"/>) and complete production building without a
/// worker (inside or on the way) gets the owner's idle carrier nearest to the owner's storage nearest to it that holds its
/// <see cref="ProductionDefinition.Tool"/> (none held: it waits), or — without a tool — the idle carrier nearest to the
/// building; the tool leaves the stock at once, and at the door the carrier becomes the building's worker
/// (<see cref="SettlerKind.Worker"/>), who keeps the tool. A worker job whose building vanished or cannot be reached takes
/// its tool to the nearest storage like a carried unit (no tool: the carrier just stops); so does a worker whose building is
/// demolished (<see cref="ReleaseWorker"/>). Requests: construction sites (missing planks and stone), production
/// buildings (input piles refilled to <see cref="Production.InputTarget"/>; a heated workplace's fuel pile, in autumn and winter, to
/// <see cref="Households.PantryTarget"/>), homes (pantry piles — food, water and, in autumn and winter, fuel — refilled to
/// <see cref="Households.PantryTarget"/>, a storage home's own stock of the need's goods to <see cref="Households.StockTarget"/>,
/// from other storages; food takes any food good) and output overflow (every unit left in an
/// output pile goes to a storage with room, see <see cref="BuildingDefinition.StorageCapacity"/>). Offers: storage stocks and output piles. Matching runs every tick after production and
/// before movement, in three passes over the buildings in id order (older first): site materials (planks, then stone),
/// production inputs and pantries (data order; a pile with alternative goods takes any of them), then
/// overflow. Each request takes the owner's offer of the good nearest to the
/// requester by <see cref="SectorDistance"/> (ties: lower id; never the requester itself), overflow the owner's nearest
/// storage with room; then the owner's idle carrier nearest to the source (ties: lower id); at most <see cref="MaxMatchesPerTick"/>
/// jobs per tick. A matched unit leaves the source stock or pile at once (reservation). Carriers execute their job inside
/// <see cref="Settlers.Step"/> via <see cref="Advance"/>: walk to the source door, pick up, walk to the destination door,
/// hand over. A pickup whose destination vanished or that cannot be reached returns the unit to its source; a carried
/// unit whose destination vanished or cannot be reached goes to the owner's nearest storage not marked unreachable
/// instead, and is lost when no such storage is left (ASSUMPTION until ground piles exist). A building a carrier failed to reach is skipped as source and
/// destination for <see cref="UnreachableBackoffTicks"/> (ASSUMPTION), so an unreachable site does not keep carriers busy.
/// </summary>
public sealed class Logistics
{
    /// <summary>Edge of a square logistics sector in tiles (docs/06-economy.md §4).</summary>
    public const int SectorSize = 16;
    /// <summary>Jobs created per tick at most (ASSUMPTION, docs/06-economy.md §4).</summary>
    public const int MaxMatchesPerTick = 200;
    /// <summary>Node-expansion limit of one job path search (ASSUMPTION until HPA*; larger than a wander search).</summary>
    public const int MaxExpansionsPerSearch = 16384;
    /// <summary>Ticks a building a carrier could not reach is left out of matching (30 s, ASSUMPTION).</summary>
    public const int UnreachableBackoffTicks = 300;

    private readonly int _edge;
    /// <summary>Open jobs in ascending id order.</summary>
    private readonly List<TransportJob> _jobs = new();
    /// <summary>Buildings left out of matching until the given tick (exclusive), in ascending building id order.</summary>
    private readonly List<(int BuildingId, int Until)> _unreachable = new();

    public Logistics(int edge)
    {
        _edge = edge;
        NextId = 1;
    }

    public int NextId { get; private set; }
    public IReadOnlyList<TransportJob> All => _jobs;
    /// <summary>Buildings currently left out of matching, with the tick their back-off ends.</summary>
    public IReadOnlyList<(int BuildingId, int Until)> Unreachable => _unreachable;

    /// <summary>Whether matching skips building <paramref name="id"/>.</summary>
    public bool IsUnreachable(int id)
    {
        foreach (var (b, _) in _unreachable)
            if (b == id) return true;
        return false;
    }

    private void MarkUnreachable(int id, int tick)
    {
        int at = 0;
        while (at < _unreachable.Count && _unreachable[at].BuildingId < id) at++;
        if (at < _unreachable.Count && _unreachable[at].BuildingId == id) _unreachable[at] = (id, tick + UnreachableBackoffTicks);
        else _unreachable.Insert(at, (id, tick + UnreachableBackoffTicks));
    }

    /// <summary>Chebyshev distance of the sectors of two tiles.</summary>
    public static int SectorDistance(int edge, int a, int b) =>
        System.Math.Max(System.Math.Abs(a % edge / SectorSize - b % edge / SectorSize),
                        System.Math.Abs(a / edge / SectorSize - b / edge / SectorSize));

    /// <summary>Index in <see cref="All"/> of job <paramref name="id"/>, or -1.</summary>
    public int IndexOf(int id)
    {
        int lo = 0, hi = _jobs.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int cur = _jobs[mid].Id;
            if (cur == id) return mid;
            if (cur < id) lo = mid + 1;
            else hi = mid - 1;
        }
        return -1;
    }

    /// <summary>
    /// Request slot of <paramref name="good"/> at a building: a site's planks 0 / stone 1, a complete production building's
    /// input pile index or, at a heated workplace, its fuel after the inputs (pile <see cref="PileOf"/>), a home's need (food 0,
    /// water 1, fuel 2; a pantry pile, or the stock of a storage home), else -1 (other storages and goods the building does not request).
    /// </summary>
    private static int SlotOf(in Building b, int good)
    {
        if (b.State == BuildingState.ConstructionSite) return good == GoodIds.Plank ? 0 : good == GoodIds.Stone ? 1 : -1;
        if (BuildingRegistry.IsHome(b))
            return Households.NeedOf(good);
        var p = b.Definition.Production;
        if (p == null) return -1;
        int k = p.InputIndexOf(good);
        return k >= 0 || !Households.IsHeatedWorkplace(b) || Households.NeedOf(good) != Households.Heat ? k : p.Inputs.Count;
    }

    /// <summary>Pile of request slot <paramref name="slot"/> at a complete non-storage building (a workplace's fuel slot → its fuel pile).</summary>
    private static int PileOf(in Building b, int slot) =>
        b.Definition.Production is { } p && slot == p.Inputs.Count ? Households.FuelPile(p) : slot;

    /// <summary>Pile index of <paramref name="good"/>'s output pile at a complete production building, or -1.</summary>
    private static int OutputPileOf(in Building b, int good)
    {
        var p = b.Definition.Production;
        if (p == null || b.State != BuildingState.Complete) return -1;
        int k = p.OutputIndexOf(good);
        return k < 0 ? -1 : p.Inputs.Count + k;
    }

    /// <summary>Whether a carried unit of <paramref name="good"/> may be handed over at the building at list index <paramref name="index"/>.</summary>
    private static bool Accepts(BuildingRegistry buildings, int index, int good) =>
        SlotOf(buildings.All[index], good) >= 0 || buildings.StockAt(index) != null;

    /// <summary>Whether the job may still go to the building at list index <paramref name="index"/>.</summary>
    private static bool Fits(BuildingRegistry buildings, int index, in TransportJob job)
    {
        if (job.Kind == JobKind.Transport) return Accepts(buildings, index, job.Good);
        var b = buildings.All[index];
        return TakesWorker(b) && ToolOf(b) == job.Good;
    }

    /// <summary>Whether a unit is on its way to building <paramref name="id"/>.</summary>
    internal bool HasDeliveryTo(int id)
    {
        foreach (var job in _jobs)
            if (job.Kind == JobKind.Transport && job.DestinationId == id) return true;
        return false;
    }

    /// <summary>Whether a building takes a worker: a construction site (its builder) or a complete production building.</summary>
    internal static bool TakesWorker(in Building b) =>
        b.State == BuildingState.ConstructionSite || b.Definition.Production != null;

    /// <summary>
    /// Tool of a building's worker: a site's digger takes <see cref="Construction.DiggerTool"/> while it is not level, its builder
    /// <see cref="Construction.BuilderTool"/>, else the production's tool.
    /// </summary>
    internal static ushort ToolOf(in Building b) =>
        b.State != BuildingState.ConstructionSite ? b.Definition.Production?.Tool ?? ProductionDefinition.NoTool
        : b.DigLeft > 0 ? Construction.DiggerTool : Construction.BuilderTool;

    /// <summary>Whether each building, by list index, has its worker inside or on the way.</summary>
    private bool[] Staffed(BuildingRegistry buildings, Settlers settlers)
    {
        var staffed = settlers.Working(buildings);
        foreach (var job in _jobs)
        {
            if (job.Kind != JobKind.Employ) continue;
            int index = buildings.IndexOf(job.DestinationId);
            if (index >= 0) staffed[index] = true;
        }
        return staffed;
    }

    /// <summary>Request slots per building in <see cref="Pending"/> (a home's needs; sites 2, production at most 2 inputs + fuel).</summary>
    private const int Slots = Households.NeedCount;

    /// <summary>Units on their way to each request slot, by building list index (<see cref="Slots"/> per building, see <see cref="SlotOf"/>).</summary>
    private int[] Pending(BuildingRegistry buildings)
    {
        var pending = new int[buildings.All.Count * Slots];
        foreach (var job in _jobs)
        {
            if (job.Kind != JobKind.Transport) continue;
            int index = buildings.IndexOf(job.DestinationId);
            if (index < 0) continue;
            int slot = SlotOf(buildings.All[index], job.Good);
            if (slot >= 0) pending[Slots * index + slot]++;
        }
        return pending;
    }

    /// <summary>
    /// Units each storage still takes as overflow, by building list index: <see cref="BuildingDefinition.StorageCapacity"/>
    /// − stock − units on the way to it (0 for other buildings; may be negative after returns).
    /// </summary>
    private int[] Room(BuildingRegistry buildings)
    {
        var room = new int[buildings.All.Count];
        for (int i = 0; i < room.Length; i++)
        {
            var stock = buildings.StockAt(i);
            if (stock == null) continue;
            room[i] = buildings.All[i].Definition.StorageCapacity;
            foreach (int units in stock) room[i] -= units;
        }
        foreach (var job in _jobs)
        {
            if (job.Kind != JobKind.Transport) continue;
            int index = buildings.IndexOf(job.DestinationId);
            if (index >= 0 && buildings.StockAt(index) != null) room[index]--;
        }
        return room;
    }

    /// <summary>Units reserved from each building's stock or output pile by jobs not yet picked up, by building list index.</summary>
    internal int[] ReservedOutput(BuildingRegistry buildings)
    {
        var reserved = new int[buildings.All.Count];
        foreach (var job in _jobs)
        {
            if (job.State != JobState.ToPickup) continue;
            int index = buildings.IndexOf(job.SourceId);
            if (index >= 0) reserved[index]++;
        }
        return reserved;
    }

    /// <summary>
    /// Creates worker jobs, then transport jobs for site materials, production inputs and output overflow. Runs once per tick;
    /// <paramref name="tick"/> is the tick being simulated, <paramref name="season"/> its season (homes fetch fuel from autumn on).
    /// </summary>
    public void Match(int tick, BuildingRegistry buildings, Settlers settlers, Season season)
    {
        _unreachable.RemoveAll(e => e.Until <= tick || buildings.IndexOf(e.BuildingId) < 0);
        var all = buildings.All;
        var pending = Pending(buildings);
        var idle = new List<int>();
        for (int i = 0; i < settlers.All.Count; i++)
        {
            var s = settlers.All[i];
            if (s.Kind == SettlerKind.Carrier && s.JobId == 0 && s.State == SettlerState.Idle && s.HomelessTicks == 0) idle.Add(i);
        }
        var noCarrier = new bool[256];
        int matches = 0;
        var staffed = Staffed(buildings, settlers);
        for (int i = 0; i < all.Count && matches < MaxMatchesPerTick && idle.Count > 0; i++)
        {
            // Workers (and builders): the tool from the nearest storage holding it, or straight to the building without a tool.
            var b = all[i];
            if (!TakesWorker(b) || staffed[i] || noCarrier[b.Owner] || IsUnreachable(b.Id)) continue;
            if (b.State == BuildingState.ConstructionSite && b.DigLeft == 0 && b.WorkDone == (b.DeliveredPlanks + b.DeliveredStone) * Construction.WorkTicksPerMaterial)
                continue; // a digger at once, a builder only once delivered material waits to be worked in
            ushort tool = ToolOf(b);
            int source = i;
            if (tool != ProductionDefinition.NoTool)
            {
                source = NearestStorage(buildings, b.Owner, tool, b.CenterY * _edge + b.CenterX);
                if (source < 0) continue;
            }
            if (TryCreate(buildings, settlers, idle, b.Owner, tool, source, i, JobKind.Employ)) matches++;
            else noCarrier[b.Owner] = true;
        }
        int[]? room = null;
        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 0; i < all.Count && matches < MaxMatchesPerTick && idle.Count > 0; i++)
            {
                var b = all[i];
                if (noCarrier[b.Owner] || IsUnreachable(b.Id)) continue;
                var p = b.Definition.Production;
                int tile = b.CenterY * _edge + b.CenterX;
                if (pass == 2)
                {
                    // Overflow: every unit left in an output pile goes to the nearest storage with room (output piles in data
                    // order); with none, it stays and the full pile pauses the building.
                    var piles = buildings.PilesAt(i);
                    if (piles == null || p == null) continue;
                    for (int o = p.Inputs.Count; o < Households.FuelPile(p) && !noCarrier[b.Owner]; o++)
                        while (piles[o] > 0 && matches < MaxMatchesPerTick)
                        {
                            room ??= Room(buildings);
                            int storage = NearestStorage(buildings, b.Owner, good: -1, tile, room);
                            if (storage < 0) break;
                            if (!TryCreate(buildings, settlers, idle, b.Owner, p.Outputs[o - p.Inputs.Count], i, storage))
                            {
                                noCarrier[b.Owner] = true;
                                break;
                            }
                            room[storage]--;
                            matches++;
                        }
                    continue;
                }
                bool home = BuildingRegistry.IsHome(b);
                int slots = pass == 0
                    ? (b.State == BuildingState.ConstructionSite ? 2 : 0)
                    : home ? Households.NeedCount
                    : b.State == BuildingState.Complete && p != null ? p.Inputs.Count + (Households.IsHeatedWorkplace(b) ? 1 : 0) : 0;
                for (int k = 0; k < slots && !noCarrier[b.Owner]; k++)
                {
                    IReadOnlyList<ushort> goods;
                    int need;
                    if (pass == 0)
                    {
                        goods = k == 0 ? PlankOnly : StoneOnly;
                        need = k == 0 ? b.Definition.CostPlanks - b.DeliveredPlanks : b.Definition.CostStone - b.DeliveredStone;
                    }
                    else if (home)
                    {
                        if (k == Households.Heat && season < Season.Autumn) continue; // fuel is stockpiled from autumn on
                        // A storage home (castle) counts the need's goods in its stock and fetches them from other storages.
                        goods = Households.NeedGoods[k];
                        var stock = buildings.StockAt(i);
                        if (stock == null) need = Households.PantryTarget - buildings.PilesAt(i)![k];
                        else
                        {
                            need = Households.StockTarget(b);
                            foreach (ushort g in goods) need -= stock[g];
                        }
                    }
                    else if (k == p!.Inputs.Count)
                    {
                        // A heated workplace's fuel pile, stockpiled from autumn on like a pantry's.
                        if (season < Season.Autumn) continue;
                        goods = Households.FuelGoods;
                        need = Households.PantryTarget - buildings.PilesAt(i)![Households.FuelPile(p)];
                    }
                    else
                    {
                        goods = p.Alternatives[k];
                        need = Production.InputTarget - buildings.PilesAt(i)![k];
                    }
                    need -= pending[Slots * i + k];
                    while (need > 0 && matches < MaxMatchesPerTick)
                    {
                        var (source, good) = NearestSource(buildings, b.Owner, goods, tile, exclude: i);
                        if (source < 0) break;
                        if (!TryCreate(buildings, settlers, idle, b.Owner, good, source, i))
                        {
                            noCarrier[b.Owner] = true;
                            break;
                        }
                        need--;
                        matches++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Reserves one unit of <paramref name="good"/> at the source (stock or output pile; nothing for a worker without a tool,
    /// whose source is the destination) and gives it to the owner's idle carrier nearest to the source; false (nothing
    /// reserved) if the owner has no idle carrier.
    /// </summary>
    private bool TryCreate(BuildingRegistry buildings, Settlers settlers, List<int> idle, byte owner, ushort good, int source, int destination,
        JobKind kind = JobKind.Transport)
    {
        var src = buildings.All[source];
        int pick = NearestCarrier(settlers, idle, owner, src.CenterY * _edge + src.CenterX);
        if (pick < 0) return false;
        bool noTool = good == ProductionDefinition.NoTool;
        var stock = buildings.StockAt(source);
        if (stock != null) stock[good]--;
        else if (!noTool) buildings.PilesAt(source)![OutputPileOf(src, good)]--;
        int carrier = idle[pick];
        idle.RemoveAt(pick);
        var job = new TransportJob(NextId++, owner, settlers.All[carrier].Id, good, src.Id, buildings.All[destination].Id,
            noTool ? JobState.Carrying : JobState.ToPickup, kind);
        _jobs.Add(job);
        settlers.AssignJob(carrier, job.Id);
        return true;
    }

    private static readonly ushort[] PlankOnly = { (ushort)GoodIds.Plank };
    private static readonly ushort[] StoneOnly = { (ushort)GoodIds.Stone };

    /// <summary>
    /// The owner's offer of one of <paramref name="goods"/> nearest to <paramref name="tile"/> by sector distance (ties: lower
    /// id): a storage holding any of them (the good it holds most of; ties: earlier in <paramref name="goods"/>) or a
    /// production building with one of them in its output pile, never <paramref name="exclude"/>. Returns its list index
    /// and the good, or (-1, 0).
    /// </summary>
    private (int Index, ushort Good) NearestSource(BuildingRegistry buildings, byte owner, IReadOnlyList<ushort> goods, int tile, int exclude)
    {
        var all = buildings.All;
        int best = -1, bestDistance = int.MaxValue;
        ushort bestGood = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Owner != owner || i == exclude || IsUnreachable(all[i].Id)) continue;
            var stock = buildings.StockAt(i);
            var piles = buildings.PilesAt(i);
            int offered = -1, most = 0;
            foreach (ushort g in goods)
            {
                int pile = piles != null ? OutputPileOf(all[i], g) : -1;
                int units = stock != null ? stock[g] : pile >= 0 ? piles![pile] : 0;
                if (units > most)
                {
                    offered = g;
                    most = units;
                }
            }
            if (offered < 0) continue;
            int d = SectorDistance(_edge, all[i].CenterY * _edge + all[i].CenterX, tile);
            if (d < bestDistance)
            {
                best = i;
                bestDistance = d;
                bestGood = (ushort)offered;
            }
        }
        return (best, bestGood);
    }

    /// <summary>
    /// List index of the owner's complete storage building nearest to <paramref name="tile"/> by sector distance
    /// (ties: lower id) whose stock holds <paramref name="good"/> (any storage if good &lt; 0) and, if
    /// <paramref name="room"/> is given, that has room left (see <see cref="Room"/>), or -1.
    /// </summary>
    private int NearestStorage(BuildingRegistry buildings, byte owner, int good, int tile, int[]? room = null)
    {
        var all = buildings.All;
        int best = -1, bestDistance = int.MaxValue;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Owner != owner) continue;
            var stock = buildings.StockAt(i);
            if (stock == null || (good >= 0 && stock[good] <= 0) || (room != null && room[i] <= 0) || IsUnreachable(all[i].Id)) continue;
            int d = SectorDistance(_edge, all[i].CenterY * _edge + all[i].CenterX, tile);
            if (d < bestDistance)
            {
                best = i;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>Position in <paramref name="idle"/> of the owner's carrier nearest to <paramref name="tile"/> (ties: lower id), or -1.</summary>
    private int NearestCarrier(Settlers settlers, List<int> idle, byte owner, int tile)
    {
        int best = -1, bestDistance = int.MaxValue;
        for (int p = 0; p < idle.Count; p++)
        {
            var s = settlers.All[idle[p]];
            if (s.Owner != owner) continue;
            int d = SectorDistance(_edge, s.Tile, tile);
            if (d < bestDistance)
            {
                best = p;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>
    /// Advances the job of an idle carrier whose wait is over (called by <see cref="Settlers.Step"/>): checks that source
    /// and destination still fit, picks up or hands over at the target door, or plans the walk there within
    /// <paramref name="budget"/> (no search while it is used up; the carrier retries next tick). A unit handed over to an
    /// input pile or a construction site counts as consumed (<see cref="ProductionStatistics"/>).
    /// </summary>
    internal Settler Advance(int tick, Settler s, List<int> path, MapData map, Territory territory, BuildingRegistry buildings,
        ProductionStatistics statistics, Pathfinder pathfinder, ref int budget)
    {
        int j = IndexOf(s.JobId);
        var job = _jobs[j];
        while (true)
        {
            if (job.State == JobState.ToPickup)
            {
                int dest = buildings.IndexOf(job.DestinationId);
                if (dest < 0 || !Fits(buildings, dest, job))
                    return Cancel(s, j, buildings, wait: 0);
                if (!buildings.TryGet(job.SourceId, out var source))
                    return Finish(s, j); // the reserved unit was lost with the demolished storage
                int door = Settlers.DoorOf(source, map, buildings);
                if (door == s.Tile)
                {
                    job = job with { State = JobState.Carrying };
                    _jobs[j] = job;
                    continue;
                }
                if (budget <= 0) return s;
                if (door >= 0 && Plan(s, door, path, map, territory, buildings, pathfinder, ref budget))
                    return s with { State = SettlerState.Walking, Progress = 0 };
                MarkUnreachable(source.Id, tick);
                return Cancel(s, j, buildings, Settlers.MinIdleTicks);
            }
            else
            {
                int dest = buildings.IndexOf(job.DestinationId);
                if (dest < 0 || !Fits(buildings, dest, job))
                {
                    if (!Retarget(s, j, buildings)) return Finish(s, j);
                    job = _jobs[j];
                    continue;
                }
                var target = buildings.All[dest];
                int door = Settlers.DoorOf(target, map, buildings);
                if (door == s.Tile && job.Kind == JobKind.Employ)
                {
                    // The carrier enters as the building's worker and keeps its tool.
                    return Finish(s, j) with { Kind = SettlerKind.Worker, WorkplaceId = target.Id };
                }
                if (door == s.Tile)
                {
                    var stock = buildings.StockAt(dest);
                    if (stock != null) stock[job.Good]++;
                    else if (target.State == BuildingState.Complete) buildings.PilesAt(dest)![PileOf(target, SlotOf(target, job.Good))]++;
                    else if (job.Good == GoodIds.Plank) buildings.Update(dest, target with { DeliveredPlanks = target.DeliveredPlanks + 1 });
                    else buildings.Update(dest, target with { DeliveredStone = target.DeliveredStone + 1 });
                    if (stock == null) statistics.Consume(job.Owner, job.Good); // handed over to an input pile or a site
                    return Finish(s, j);
                }
                if (budget <= 0) return s;
                if (door >= 0 && Plan(s, door, path, map, territory, buildings, pathfinder, ref budget))
                    return s with { State = SettlerState.Walking, Progress = 0 };
                // Unreachable: the unit goes to the nearest storage not marked unreachable (this target now is); lost if none is left.
                MarkUnreachable(target.Id, tick);
                if (!Retarget(s, j, buildings)) return Finish(s, j);
                return s with { WaitTicks = Settlers.MinIdleTicks };
            }
        }
    }

    /// <summary>
    /// Brings the worker of building <paramref name="buildingId"/> out as a carrier before the building is demolished; it
    /// carries its tool to the owner's storage nearest to it (lost only if the owner has no storage left).
    /// </summary>
    internal void ReleaseWorker(BuildingRegistry buildings, Settlers settlers, int buildingId)
    {
        for (int i = 0; i < settlers.All.Count; i++)
            if (settlers.All[i].Kind == SettlerKind.Worker && settlers.All[i].WorkplaceId == buildingId)
            {
                ReleaseWorkerAt(buildings, settlers, i);
                return;
            }
    }

    /// <summary>Brings the worker at settler list index <paramref name="index"/> out of its workplace as in <see cref="ReleaseWorker"/>.</summary>
    internal void ReleaseWorkerAt(BuildingRegistry buildings, Settlers settlers, int index)
    {
        var s = settlers.All[index];
        ushort tool = buildings.TryGet(s.WorkplaceId, out var b) ? ToolOf(b) : ProductionDefinition.NoTool;
        s = s with { Kind = SettlerKind.Carrier, WorkplaceId = 0, WaitTicks = Settlers.MinIdleTicks };
        int storage = tool == ProductionDefinition.NoTool ? -1 : NearestStorage(buildings, s.Owner, good: -1, s.Tile);
        if (storage >= 0)
        {
            var job = new TransportJob(NextId++, s.Owner, s.Id, tool, b.Id, buildings.All[storage].Id, JobState.Carrying);
            _jobs.Add(job);
            s = s with { JobId = job.Id, WaitTicks = 0 };
        }
        settlers.Replace(index, s);
    }

    private bool Plan(Settler s, int goal, List<int> path, MapData map, Territory territory, BuildingRegistry buildings,
        Pathfinder pathfinder, ref int budget)
    {
        byte owner = s.Owner;
        int cost = pathfinder.FindPath(t => Settlers.IsPassable(map, territory, buildings, owner, t), s.Tile, goal,
            MaxExpansionsPerSearch, path);
        budget -= pathfinder.Expansions;
        if (cost <= 0)
        {
            path.Clear();
            return false;
        }
        path.Reverse();
        return true;
    }

    /// <summary>
    /// Sends a carried unit (a worker's tool) to the owner's storage nearest to the carrier that is not marked unreachable;
    /// false if there is none or a worker carries no tool.
    /// </summary>
    private bool Retarget(Settler s, int j, BuildingRegistry buildings)
    {
        if (_jobs[j].Good == ProductionDefinition.NoTool) return false;
        int storage = NearestStorage(buildings, s.Owner, good: -1, s.Tile);
        if (storage < 0) return false;
        _jobs[j] = _jobs[j] with { DestinationId = buildings.All[storage].Id, Kind = JobKind.Transport };
        return true;
    }

    /// <summary>Drops a job before pickup and returns its unit to the source stock or output pile (lost if the source is gone).</summary>
    private Settler Cancel(Settler s, int j, BuildingRegistry buildings, int wait)
    {
        var job = _jobs[j];
        int source = buildings.IndexOf(job.SourceId);
        if (source >= 0)
        {
            var stock = buildings.StockAt(source);
            if (stock != null) stock[job.Good]++;
            else buildings.PilesAt(source)![OutputPileOf(buildings.All[source], job.Good)]++;
        }
        return Finish(s, j) with { WaitTicks = wait };
    }

    private Settler Finish(Settler s, int j)
    {
        _jobs.RemoveAt(j);
        return s with { JobId = 0, State = SettlerState.Idle, Progress = 0, WaitTicks = 0 };
    }

    /// <summary>Canonical state: next id, every open job, then the unreachable back-offs.</summary>
    public void WriteTo(CanonicalWriter w)
    {
        w.WriteInt32(NextId);
        w.WriteInt32(_jobs.Count);
        foreach (var job in _jobs)
        {
            w.WriteInt32(job.Id);
            w.WriteByte(job.Owner);
            w.WriteInt32(job.CarrierId);
            w.WriteUInt16(job.Good);
            w.WriteInt32(job.SourceId);
            w.WriteInt32(job.DestinationId);
            w.WriteByte((byte)job.State);
            w.WriteByte((byte)job.Kind);
        }
        w.WriteInt32(_unreachable.Count);
        foreach (var (id, until) in _unreachable)
        {
            w.WriteInt32(id);
            w.WriteInt32(until);
        }
    }

    /// <summary>
    /// Reads and validates jobs (ids, owners, goods, building ids, a live source being a storage or a production building
    /// with that output — a worker job's live source a storage or, without a tool, its destination with the job carrying;
    /// its destination is checked when the carrier acts — a one-to-one link to carriers of the same owner, at most one worker inside or on the way per building and
    /// only in construction sites and complete production buildings, a running cycle only with its worker inside, no site receiving more than its cost and no input
    /// pile more than <see cref="Production.InputTarget"/>, delivered + on the way, no output pile over
    /// <see cref="Production.OutputCap"/> counting reserved units and a running cycle) and the back-offs (ascending ids of live buildings).
    /// </summary>
    public static Logistics ReadFrom(CanonicalReader r, int edge, int playerCount, BuildingRegistry buildings, Settlers settlers)
    {
        var reg = new Logistics(edge);
        int nextId = r.ReadInt32();
        if (nextId < 1) throw new InvalidDataException("Invalid next job id");
        int count = r.ReadInt32();
        if (count < 0 || count >= nextId || count > settlers.All.Count) throw new InvalidDataException("Invalid job count");
        int lastId = 0;
        for (int i = 0; i < count; i++)
        {
            var job = new TransportJob(r.ReadInt32(), r.ReadByte(), r.ReadInt32(), r.ReadUInt16(), r.ReadInt32(), r.ReadInt32(),
                (JobState)r.ReadByte(), (JobKind)r.ReadByte());
            int carrier = settlers.IndexOf(job.CarrierId);
            int source = buildings.IndexOf(job.SourceId);
            bool noTool = job.Kind == JobKind.Employ && job.Good == ProductionDefinition.NoTool;
            if (job.Id <= lastId || job.Id >= nextId || job.Owner >= playerCount || job.Kind > JobKind.Employ
                || (job.Good >= GoodCatalog.All.Count && !noTool)
                || job.SourceId < 1 || job.SourceId >= buildings.NextId || job.DestinationId < 1 || job.DestinationId >= buildings.NextId
                || job.State > JobState.Carrying || carrier < 0 || settlers.All[carrier].JobId != job.Id
                || settlers.All[carrier].Owner != job.Owner
                || (noTool ? job.State != JobState.Carrying || job.SourceId != job.DestinationId
                    : job.State == JobState.ToPickup && source >= 0 && buildings.StockAt(source) == null
                      && (job.Kind == JobKind.Employ || buildings.PilesAt(source) == null || OutputPileOf(buildings.All[source], job.Good) < 0)))
                throw new InvalidDataException("Invalid transport job");
            lastId = job.Id;
            reg._jobs.Add(job);
        }
        int busy = 0;
        foreach (var s in settlers.All)
            if (s.JobId != 0) busy++;
        if (busy != count) throw new InvalidDataException("Settler references a missing transport job");
        int unreachable = r.ReadInt32();
        if (unreachable < 0 || unreachable > buildings.All.Count) throw new InvalidDataException("Invalid unreachable count");
        int lastBuilding = 0;
        for (int i = 0; i < unreachable; i++)
        {
            int id = r.ReadInt32(), until = r.ReadInt32();
            if (id <= lastBuilding || buildings.IndexOf(id) < 0 || until < 0) throw new InvalidDataException("Invalid unreachable entry");
            lastBuilding = id;
            reg._unreachable.Add((id, until));
        }
        var workers = new int[buildings.All.Count];
        foreach (var s in settlers.All)
        {
            if (s.Kind != SettlerKind.Worker) continue;
            int index = buildings.IndexOf(s.WorkplaceId);
            if (index < 0) throw new InvalidDataException("Worker of a missing building");
            if (!TakesWorker(buildings.All[index]))
                throw new InvalidDataException("Worker of a building that takes none");
            workers[index]++;
        }
        var working = settlers.Working(buildings);
        foreach (var job in reg._jobs)
        {
            int index = job.Kind == JobKind.Employ ? buildings.IndexOf(job.DestinationId) : -1;
            if (index >= 0) workers[index]++;
        }
        var pending = reg.Pending(buildings);
        var reserved = reg.ReservedOutput(buildings);
        for (int i = 0; i < buildings.All.Count; i++)
        {
            var b = buildings.All[i];
            if (workers[i] > 1) throw new InvalidDataException("More than one worker for a building");
            if (b.State == BuildingState.ConstructionSite)
            {
                if (b.DeliveredPlanks + pending[Slots * i] > b.Definition.CostPlanks || b.DeliveredStone + pending[Slots * i + 1] > b.Definition.CostStone)
                    throw new InvalidDataException("More material on the way than a site needs");
                continue;
            }
            if (b.Cycle > 0 && !working[i]) throw new InvalidDataException("Work cycle running without its worker");
            var piles = buildings.PilesAt(i);
            if (piles == null) continue;
            if (BuildingRegistry.IsHome(b))
            {
                for (int k = 0; k < Households.NeedCount; k++)
                    if (piles[k] + pending[Slots * i + k] > Households.PantryTarget)
                        throw new InvalidDataException("More food, water or fuel on the way than a pantry takes");
                continue;
            }
            var p = b.Definition.Production!;
            for (int k = 0; k < p.Inputs.Count; k++)
                if (piles[k] + pending[Slots * i + k] > Production.InputTarget)
                    throw new InvalidDataException("More input on the way than a production building takes");
            if (Households.IsHeatedWorkplace(b) && piles[Households.FuelPile(p)] + pending[Slots * i + p.Inputs.Count] > Households.PantryTarget)
                throw new InvalidDataException("More fuel on the way than a workplace takes");
            if (Production.OutputUnits(piles, p) + reserved[i] + (b.Cycle > 0 ? 1 : 0) > Production.OutputCap)
                throw new InvalidDataException("Output pile over capacity");
        }
        reg.NextId = nextId;
        return reg;
    }
}
