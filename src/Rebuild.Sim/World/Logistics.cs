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
/// One unit of <see cref="Good"/> moved by carrier <see cref="CarrierId"/> from storage building <see cref="SourceId"/>
/// to <see cref="DestinationId"/>: a construction site, or (when the site vanished or became unreachable while the unit
/// was carried) a storage building of the owner.
/// </summary>
public readonly record struct TransportJob(int Id, byte Owner, int CarrierId, ushort Good, int SourceId, int DestinationId, JobState State);

/// <summary>
/// Logistics system (docs/06-economy.md §4), first pass: construction sites request their missing planks and stone,
/// storage stocks offer them, idle carriers carry one unit per <see cref="TransportJob"/>. Matching runs every tick
/// after construction and before movement: sites in id order (older first), planks before stone; for each missing
/// unit the owner's storage holding the good that is nearest by <see cref="SectorDistance"/> (ties: lower id), then the
/// owner's idle carrier nearest to that storage (ties: lower id); at most <see cref="MaxMatchesPerTick"/> jobs per tick.
/// A matched unit leaves the source stock at once (reservation). Carriers execute their job inside
/// <see cref="Settlers.Step"/> via <see cref="Advance"/>: walk to the source door, pick up, walk to the destination door,
/// hand over. A pickup whose site vanished or that cannot be reached returns the unit to its source; a carried unit
/// whose site vanished or cannot be reached goes to the owner's nearest storage instead, and is lost if that fails too
/// (ASSUMPTION until ground piles exist). A building a carrier failed to reach is skipped as source and destination for
/// <see cref="UnreachableBackoffTicks"/> (ASSUMPTION), so an unreachable site does not keep carriers busy.
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

    /// <summary>Units of planks and stone on their way to each construction site, by building list index (2 per building).</summary>
    private int[] Pending(BuildingRegistry buildings)
    {
        var pending = new int[buildings.All.Count * 2];
        foreach (var job in _jobs)
        {
            int index = buildings.IndexOf(job.DestinationId);
            if (index < 0 || buildings.All[index].State != BuildingState.ConstructionSite) continue;
            if (job.Good == GoodIds.Plank) pending[2 * index]++;
            else if (job.Good == GoodIds.Stone) pending[2 * index + 1]++;
        }
        return pending;
    }

    /// <summary>
    /// Creates transport jobs for the missing materials of construction sites. Runs once per tick;
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
        for (int i = 0; i < all.Count && matches < MaxMatchesPerTick && idle.Count > 0; i++)
        {
            var site = all[i];
            if (site.State != BuildingState.ConstructionSite || noCarrier[site.Owner] || IsUnreachable(site.Id)) continue;
            var def = site.Definition;
            int siteTile = site.CenterY * _edge + site.CenterX;
            for (int k = 0; k < 2 && !noCarrier[site.Owner]; k++)
            {
                ushort good = (ushort)(k == 0 ? GoodIds.Plank : GoodIds.Stone);
                int need = k == 0 ? def.CostPlanks - site.DeliveredPlanks : def.CostStone - site.DeliveredStone;
                need -= pending[2 * i + k];
                while (need > 0 && matches < MaxMatchesPerTick)
                {
                    int source = NearestStorage(buildings, site.Owner, good, siteTile);
                    if (source < 0) break;
                    var src = all[source];
                    int pick = NearestCarrier(settlers, idle, site.Owner, src.CenterY * _edge + src.CenterX);
                    if (pick < 0)
                    {
                        noCarrier[site.Owner] = true;
                        break;
                    }
                    buildings.StockAt(source)![good]--;
                    int carrier = idle[pick];
                    idle.RemoveAt(pick);
                    var job = new TransportJob(NextId++, site.Owner, settlers.All[carrier].Id, good, src.Id, site.Id, JobState.ToPickup);
                    _jobs.Add(job);
                    settlers.AssignJob(carrier, job.Id);
                    need--;
                    matches++;
                }
            }
        }
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
                if (dest < 0 || buildings.All[dest].State != BuildingState.ConstructionSite)
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
                bool fits = dest >= 0 && (buildings.All[dest].State == BuildingState.ConstructionSite || buildings.StockAt(dest) != null);
                if (!fits)
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
                    else if (job.Good == GoodIds.Plank) buildings.Update(dest, target with { DeliveredPlanks = target.DeliveredPlanks + 1 });
                    else buildings.Update(dest, target with { DeliveredStone = target.DeliveredStone + 1 });
                    return Finish(s, j);
                }
                if (budget <= 0) return s;
                if (door >= 0 && Plan(s, door, path, map, territory, buildings, pathfinder, ref budget))
                    return s with { State = SettlerState.Walking, Progress = 0 };
                // Unreachable: a site's unit goes to the nearest storage instead; a storage's unit is lost.
                MarkUnreachable(target.Id, tick);
                if (buildings.StockAt(dest) != null || !Retarget(s, j, buildings)) return Finish(s, j);
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

    /// <summary>Sends a carried unit to the owner's storage nearest to the carrier; false if the owner has none.</summary>
    private bool Retarget(Settler s, int j, BuildingRegistry buildings)
    {
        int storage = NearestStorage(buildings, s.Owner, good: -1, s.Tile);
        if (storage < 0) return false;
        _jobs[j] = _jobs[j] with { DestinationId = buildings.All[storage].Id };
        return true;
    }

    /// <summary>Drops a job before pickup and returns its unit to the source stock (lost if the source is gone).</summary>
    private Settler Cancel(Settler s, int j, BuildingRegistry buildings, int wait)
    {
        var job = _jobs[j];
        int source = buildings.IndexOf(job.SourceId);
        if (source >= 0) buildings.StockAt(source)![job.Good]++;
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
    /// Reads and validates jobs (ids, owners, goods, building ids, a live source being a storage, a one-to-one link to carriers of the same owner, no
    /// site receiving more than its cost, delivered + on the way) and the back-offs (ascending ids of live buildings).
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
            if (job.Id <= lastId || job.Id >= nextId || job.Owner >= playerCount || (job.Good != GoodIds.Plank && job.Good != GoodIds.Stone)
                || job.SourceId < 1 || job.SourceId >= buildings.NextId || job.DestinationId < 1 || job.DestinationId >= buildings.NextId
                || job.State > JobState.Carrying || carrier < 0 || settlers.All[carrier].JobId != job.Id
                || settlers.All[carrier].Owner != job.Owner
                || (buildings.IndexOf(job.SourceId) >= 0 && buildings.StockOf(job.SourceId) == null))
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
        for (int i = 0; i < buildings.All.Count; i++)
        {
            var b = buildings.All[i];
            if (b.State != BuildingState.ConstructionSite) continue;
            if (b.DeliveredPlanks + pending[2 * i] > b.Definition.CostPlanks || b.DeliveredStone + pending[2 * i + 1] > b.Definition.CostStone)
                throw new InvalidDataException("More material on the way than a site needs");
        }
        reg.NextId = nextId;
        return reg;
    }
}
