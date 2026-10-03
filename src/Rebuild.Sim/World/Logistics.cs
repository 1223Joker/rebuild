using System.Collections.Generic;
using System.IO;
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

/// <summary>
/// One unit of <see cref="Good"/> moved by carrier <see cref="CarrierId"/> from <see cref="SourceId"/> (a storage building
/// or a production building's output pile) to <see cref="DestinationId"/>: a construction site, a production building's
/// input pile, or a storage building (output overflow, or a carried unit whose destination vanished or became unreachable).
/// </summary>
public readonly record struct TransportJob(int Id, byte Owner, int CarrierId, ushort Good, int SourceId, int DestinationId, JobState State);

/// <summary>
/// Logistics system (docs/06-economy.md §4). Requests: construction sites (missing planks and stone), production
/// buildings (input piles refilled to <see cref="Production.InputTarget"/>) and output overflow (every unit left in an
/// output pile goes to a storage). Offers: storage stocks and output piles. Matching runs every tick after production and
/// before movement, in three passes over the buildings in id order (older first): site materials (planks, then stone),
/// production inputs (data order; a pile with alternative goods, such as a mine's food, takes any of them), then
/// overflow. Each request takes the owner's offer of the good nearest to the
/// requester by <see cref="SectorDistance"/> (ties: lower id; never the requester itself), overflow the owner's nearest
/// storage; then the owner's idle carrier nearest to the source (ties: lower id); at most <see cref="MaxMatchesPerTick"/>
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
    /// input pile index, else -1 (storages and goods the building does not request).
    /// </summary>
    private static int SlotOf(in Building b, int good)
    {
        if (b.State == BuildingState.ConstructionSite) return good == GoodIds.Plank ? 0 : good == GoodIds.Stone ? 1 : -1;
        return b.Definition.Production?.InputIndexOf(good) ?? -1;
    }

    /// <summary>Whether a carried unit of <paramref name="good"/> may be handed over at the building at list index <paramref name="index"/>.</summary>
    private static bool Accepts(BuildingRegistry buildings, int index, int good) =>
        SlotOf(buildings.All[index], good) >= 0 || buildings.StockAt(index) != null;

    /// <summary>Units on their way to each request slot, by building list index (2 slots per building, see <see cref="SlotOf"/>).</summary>
    private int[] Pending(BuildingRegistry buildings)
    {
        var pending = new int[buildings.All.Count * 2];
        foreach (var job in _jobs)
        {
            int index = buildings.IndexOf(job.DestinationId);
            if (index < 0) continue;
            int slot = SlotOf(buildings.All[index], job.Good);
            if (slot >= 0) pending[2 * index + slot]++;
        }
        return pending;
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
    /// Creates transport jobs for site materials, production inputs and output overflow. Runs once per tick;
    /// <paramref name="tick"/> is the tick being simulated.
    /// </summary>
    public void Match(int tick, BuildingRegistry buildings, Settlers settlers)
    {
        _unreachable.RemoveAll(e => e.Until <= tick || buildings.IndexOf(e.BuildingId) < 0);
        var all = buildings.All;
        var pending = Pending(buildings);
        var idle = new List<int>();
        for (int i = 0; i < settlers.All.Count; i++)
        {
            var s = settlers.All[i];
            if (s.Kind == SettlerKind.Carrier && s.JobId == 0 && s.State == SettlerState.Idle) idle.Add(i);
        }
        var noCarrier = new bool[256];
        int matches = 0;
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
                    // Overflow: every unit left in the output pile goes to the nearest storage.
                    var piles = buildings.PilesAt(i);
                    if (piles == null) continue;
                    while (piles[piles.Length - 1] > 0 && matches < MaxMatchesPerTick)
                    {
                        int storage = NearestStorage(buildings, b.Owner, good: -1, tile);
                        if (storage < 0) break;
                        if (!TryCreate(buildings, settlers, idle, b.Owner, p!.Output, i, storage))
                        {
                            noCarrier[b.Owner] = true;
                            break;
                        }
                        matches++;
                    }
                    continue;
                }
                int slots = pass == 0
                    ? (b.State == BuildingState.ConstructionSite ? 2 : 0)
                    : (b.State == BuildingState.Complete && p != null ? p.Inputs.Count : 0);
                for (int k = 0; k < slots && !noCarrier[b.Owner]; k++)
                {
                    IReadOnlyList<ushort> goods;
                    int need;
                    if (pass == 0)
                    {
                        goods = k == 0 ? PlankOnly : StoneOnly;
                        need = k == 0 ? b.Definition.CostPlanks - b.DeliveredPlanks : b.Definition.CostStone - b.DeliveredStone;
                    }
                    else
                    {
                        goods = p!.Alternatives[k];
                        need = Production.InputTarget - buildings.PilesAt(i)![k];
                    }
                    need -= pending[2 * i + k];
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
    /// Reserves one unit of <paramref name="good"/> at the source (stock or output pile) and gives it to the owner's idle
    /// carrier nearest to the source; false (nothing reserved) if the owner has no idle carrier.
    /// </summary>
    private bool TryCreate(BuildingRegistry buildings, Settlers settlers, List<int> idle, byte owner, ushort good, int source, int destination)
    {
        var src = buildings.All[source];
        int pick = NearestCarrier(settlers, idle, owner, src.CenterY * _edge + src.CenterX);
        if (pick < 0) return false;
        var stock = buildings.StockAt(source);
        if (stock != null) stock[good]--;
        else buildings.PilesAt(source)![^1]--;
        int carrier = idle[pick];
        idle.RemoveAt(pick);
        var job = new TransportJob(NextId++, owner, settlers.All[carrier].Id, good, src.Id, buildings.All[destination].Id, JobState.ToPickup);
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
                int units = stock != null ? stock[g] : piles != null && all[i].Definition.Production!.Output == g ? piles[^1] : 0;
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
    /// (ties: lower id) whose stock holds <paramref name="good"/> (any storage if good &lt; 0), or -1.
    /// </summary>
    private int NearestStorage(BuildingRegistry buildings, byte owner, int good, int tile)
    {
        var all = buildings.All;
        int best = -1, bestDistance = int.MaxValue;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Owner != owner) continue;
            var stock = buildings.StockAt(i);
            if (stock == null || (good >= 0 && stock[good] <= 0) || IsUnreachable(all[i].Id)) continue;
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
    /// <paramref name="budget"/> (no search while it is used up; the carrier retries next tick).
    /// </summary>
    internal Settler Advance(int tick, Settler s, List<int> path, MapData map, Territory territory, BuildingRegistry buildings,
        Pathfinder pathfinder, ref int budget)
    {
        int j = IndexOf(s.JobId);
        var job = _jobs[j];
        while (true)
        {
            if (job.State == JobState.ToPickup)
            {
                int dest = buildings.IndexOf(job.DestinationId);
                if (dest < 0 || !Accepts(buildings, dest, job.Good))
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
                if (dest < 0 || !Accepts(buildings, dest, job.Good))
                {
                    if (!Retarget(s, j, buildings)) return Finish(s, j);
                    job = _jobs[j];
                    continue;
                }
                var target = buildings.All[dest];
                int door = Settlers.DoorOf(target, map, buildings);
                if (door == s.Tile)
                {
                    var stock = buildings.StockAt(dest);
                    if (stock != null) stock[job.Good]++;
                    else if (target.State == BuildingState.Complete) buildings.PilesAt(dest)![SlotOf(target, job.Good)]++;
                    else if (job.Good == GoodIds.Plank) buildings.Update(dest, target with { DeliveredPlanks = target.DeliveredPlanks + 1 });
                    else buildings.Update(dest, target with { DeliveredStone = target.DeliveredStone + 1 });
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

    /// <summary>Sends a carried unit to the owner's storage nearest to the carrier that is not marked unreachable; false if there is none.</summary>
    private bool Retarget(Settler s, int j, BuildingRegistry buildings)
    {
        int storage = NearestStorage(buildings, s.Owner, good: -1, s.Tile);
        if (storage < 0) return false;
        _jobs[j] = _jobs[j] with { DestinationId = buildings.All[storage].Id };
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
            else buildings.PilesAt(source)![^1]++;
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
    /// with that output, a one-to-one link to carriers of the same owner, no site receiving more than its cost and no input
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
                (JobState)r.ReadByte());
            int carrier = settlers.IndexOf(job.CarrierId);
            int source = buildings.IndexOf(job.SourceId);
            if (job.Id <= lastId || job.Id >= nextId || job.Owner >= playerCount || job.Good >= GoodCatalog.All.Count
                || job.SourceId < 1 || job.SourceId >= buildings.NextId || job.DestinationId < 1 || job.DestinationId >= buildings.NextId
                || job.State > JobState.Carrying || carrier < 0 || settlers.All[carrier].JobId != job.Id
                || settlers.All[carrier].Owner != job.Owner
                || (source >= 0 && buildings.StockAt(source) == null
                    && (buildings.PilesAt(source) == null || buildings.All[source].Definition.Production!.Output != job.Good)))
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
        var pending = reg.Pending(buildings);
        var reserved = reg.ReservedOutput(buildings);
        for (int i = 0; i < buildings.All.Count; i++)
        {
            var b = buildings.All[i];
            if (b.State == BuildingState.ConstructionSite)
            {
                if (b.DeliveredPlanks + pending[2 * i] > b.Definition.CostPlanks || b.DeliveredStone + pending[2 * i + 1] > b.Definition.CostStone)
                    throw new InvalidDataException("More material on the way than a site needs");
                continue;
            }
            var piles = buildings.PilesAt(i);
            if (piles == null) continue;
            for (int k = 0; k < piles.Length - 1; k++)
                if (piles[k] + pending[2 * i + k] > Production.InputTarget)
                    throw new InvalidDataException("More input on the way than a production building takes");
            if (piles[^1] + reserved[i] + (b.Cycle > 0 ? 1 : 0) > Production.OutputCap)
                throw new InvalidDataException("Output pile over capacity");
        }
        reg.NextId = nextId;
        return reg;
    }
}
