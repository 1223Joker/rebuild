using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Core;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.World;

public enum SettlerKind : byte
{
    /// <summary>Carries goods (docs/06-economy.md §3).</summary>
    Carrier = 0,
    /// <summary>
    /// Works inside the production building <see cref="Settler.HomeId"/> (a carrier that took the building's tool; standing
    /// at its door, never walking); comes out as a carrier bringing its tool back when the building is demolished
    /// (<see cref="Logistics.ReleaseWorker"/>).
    /// </summary>
    Worker = 1,
}

public enum SettlerState : byte
{
    /// <summary>Waits <see cref="Settler.WaitTicks"/>, then picks its next walk.</summary>
    Idle = 0,
    /// <summary>Follows its path one tile at a time.</summary>
    Walking = 1,
}

/// <summary>
/// A settler on the tile grid. <see cref="HomeId"/> is the building that spawned a carrier (it may since have been
/// demolished) or a worker's workplace. <see cref="Progress"/> counts sub-tile units (<see cref="Settlers.SubTile"/> per straight step)
/// walked towards the next path tile. <see cref="JobId"/> is the carrier's <see cref="TransportJob"/> (0 = none).
/// </summary>
public readonly record struct Settler(int Id, SettlerKind Kind, byte Owner, int HomeId, int Tile, SettlerState State, int Progress, int WaitTicks,
    int JobId = 0);

/// <summary>
/// Settlers and their movement system (docs/06-economy.md §3, §6), run every tick after construction.
/// Every <see cref="SpawnIntervalTicks"/> ticks each complete building with <c>carriers</c> in its data spawns one
/// carrier at its door while fewer than that many carriers call it home (the start castle starts full). Settlers walk
/// on walkable tiles of their owner's territory that no building covers, along A* paths (<see cref="Pathfinder"/>) at
/// <see cref="Speed"/> sub-tile units per tick; diagonal steps cost 14/10 of a straight step. Settlers never collide;
/// a settler covered by a newly placed building is put at that building's door. Workers stay inside their workplace
/// (they do not walk to their resources yet, ASSUMPTION) and come out as carriers taking their tool to a storage when it
/// is demolished (no building homes them then, ASSUMPTION).
/// Carriers with a transport job execute it via <see cref="Logistics.Advance"/>; idle carriers without one wander to a
/// random tile within <see cref="WanderRadius"/> of their home's door (ASSUMPTION placeholder until idle carriers
/// gather at storages).
/// Path searches run in settler-id order under a per-tick node-expansion budget; settlers left over retry next tick.
/// </summary>
public sealed class Settlers
{
    /// <summary>Ticks between two carrier spawns of one building (60 s).</summary>
    public const int SpawnIntervalTicks = 600;
    /// <summary>Sub-tile units per straight step.</summary>
    public const int SubTile = 256;
    /// <summary>Sub-tile units of a diagonal step (× 14/10, like the path costs).</summary>
    public const int DiagonalStep = SubTile * Pathfinder.DiagonalCost / Pathfinder.StraightCost;
    /// <summary>Walking speed in sub-tile units per tick (2.5 tiles/s; ASSUMPTION).</summary>
    public const int Speed = 64;
    /// <summary>Wander targets lie within this Chebyshev distance of the home door (placeholder behaviour).</summary>
    public const int WanderRadius = 6;
    /// <summary>Idle wait after a walk: <see cref="MinIdleTicks"/> + random [0, <see cref="IdleTicksRange"/>).</summary>
    public const int MinIdleTicks = 20;
    public const int IdleTicksRange = 40;
    /// <summary>Node-expansion limit of one path search; a search that hits it fails.</summary>
    public const int MaxExpansionsPerSearch = 2048;
    /// <summary>Node expansions all path searches of one tick may use (docs/06-economy.md §6).</summary>
    public const int ExpansionBudgetPerTick = 60000;

    private readonly int _edge;
    /// <summary>Live settlers in ascending id order.</summary>
    private readonly List<Settler> _settlers = new();
    /// <summary>Remaining path per settler, parallel to <see cref="_settlers"/>, stored reversed (next tile last).</summary>
    private readonly List<List<int>> _paths = new();

    public Settlers(int edge)
    {
        _edge = edge;
        NextId = 1;
    }

    public int NextId { get; private set; }
    public IReadOnlyList<Settler> All => _settlers;

    /// <summary>Remaining path of the settler at list index <paramref name="index"/>, next tile first.</summary>
    public IReadOnlyList<int> PathAt(int index)
    {
        var path = new List<int>(_paths[index]);
        path.Reverse();
        return path;
    }

    /// <summary>Index in <see cref="All"/> of settler <paramref name="id"/>, or -1.</summary>
    public int IndexOf(int id)
    {
        int lo = 0, hi = _settlers.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int cur = _settlers[mid].Id;
            if (cur == id) return mid;
            if (cur < id) lo = mid + 1;
            else hi = mid - 1;
        }
        return -1;
    }

    /// <summary>Replaces the settler at list index <paramref name="index"/> (same id; systems only).</summary>
    internal void Replace(int index, in Settler s) => _settlers[index] = s;

    /// <summary>Gives the idle carrier at list index <paramref name="index"/> a transport job; it starts on its next step.</summary>
    internal void AssignJob(int index, int jobId) => _settlers[index] = _settlers[index] with { JobId = jobId, WaitTicks = 0 };

    /// <summary>Whether each building, by list index, has its worker inside.</summary>
    internal bool[] Working(BuildingRegistry buildings)
    {
        var working = new bool[buildings.All.Count];
        foreach (var s in _settlers)
        {
            if (s.Kind != SettlerKind.Worker) continue;
            int index = buildings.IndexOf(s.HomeId);
            if (index >= 0) working[index] = true;
        }
        return working;
    }

    /// <summary>Number of settlers a slot owns.</summary>
    public int CountOwnedBy(byte owner)
    {
        int n = 0;
        foreach (var s in _settlers)
            if (s.Owner == owner) n++;
        return n;
    }

    /// <summary>Adds an idle carrier and returns its id.</summary>
    public int Spawn(byte owner, int homeId, int tile)
    {
        if ((uint)tile >= (uint)(_edge * _edge)) throw new System.ArgumentOutOfRangeException(nameof(tile));
        var s = new Settler(NextId++, SettlerKind.Carrier, owner, homeId, tile, SettlerState.Idle, 0, 0);
        _settlers.Add(s);
        _paths.Add(new List<int>());
        return s.Id;
    }

    /// <summary>
    /// Door of a building, where its carriers spawn and settlers covered by it are put: the margin tile below the
    /// bottom-centre of the footprint when that is a free walkable map tile, else the first such tile of the margin
    /// ring in row-major order, else -1 (no carriers spawn there).
    /// </summary>
    public static int DoorOf(in Building b, MapData map, BuildingRegistry buildings)
    {
        int side = b.Definition.Side;
        int door = DoorCandidate(b.X + side / 2, b.Y + side, map, buildings);
        if (door >= 0) return door;
        for (int y = b.Y - 1; y <= b.Y + side; y++)
            for (int x = b.X - 1; x <= b.X + side; x++)
            {
                if (x >= b.X && x < b.X + side && y >= b.Y && y < b.Y + side) continue;
                int t = DoorCandidate(x, y, map, buildings);
                if (t >= 0) return t;
            }
        return -1;
    }

    private static int DoorCandidate(int x, int y, MapData map, BuildingRegistry buildings)
    {
        if ((uint)x >= (uint)map.Edge || (uint)y >= (uint)map.Edge) return -1;
        int t = y * map.Edge + x;
        return map.IsWalkable(t) && buildings.AtTile(t) == 0 ? t : -1;
    }

    /// <summary>Whether a settler of <paramref name="owner"/> may enter the tile.</summary>
    public static bool IsPassable(MapData map, Territory territory, BuildingRegistry buildings, byte owner, int tile) =>
        map.IsWalkable(tile) && buildings.AtTile(tile) == 0 && territory.OwnerAt(tile) == owner;

    /// <summary>Runs one tick of spawning and movement. <paramref name="tick"/> is the tick being simulated.</summary>
    public void Step(int tick, MapData map, Territory territory, BuildingRegistry buildings, Logistics logistics,
        ProductionStatistics statistics, Pcg32 rng, Pathfinder pathfinder)
    {
        if (tick % SpawnIntervalTicks == 0) Refill(map, buildings);
        int budget = ExpansionBudgetPerTick;
        for (int i = 0; i < _settlers.Count; i++)
        {
            var s = _settlers[i];
            var path = _paths[i];
            if (s.Kind == SettlerKind.Worker) continue; // inside its workplace
            int cover = buildings.AtTile(s.Tile);
            if (cover != 0 && buildings.TryGet(cover, out var covering))
            {
                // A building was placed on the settler: put it at that building's door (it stays if there is none).
                int door = DoorOf(covering, map, buildings);
                if (door >= 0)
                {
                    path.Clear();
                    s = s with { Tile = door, State = SettlerState.Idle, Progress = 0, WaitTicks = 0 };
                }
            }
            if (s.State == SettlerState.Walking)
            {
                int next = path[path.Count - 1];
                bool diagonal = next % _edge != s.Tile % _edge && next / _edge != s.Tile / _edge;
                int cost = diagonal ? DiagonalStep : SubTile;
                int progress = s.Progress + Speed;
                if (progress < cost)
                {
                    s = s with { Progress = progress };
                }
                else if (!IsPassable(map, territory, buildings, s.Owner, next))
                {
                    // Blocked by a new building or lost territory: stop and plan again after a short wait.
                    path.Clear();
                    s = s with { State = SettlerState.Idle, Progress = 0, WaitTicks = MinIdleTicks };
                }
                else
                {
                    path.RemoveAt(path.Count - 1);
                    s = s with { Tile = next, Progress = progress - cost };
                    if (path.Count == 0)
                    {
                        // A carrier on a job acts at the target next tick; others rest before wandering again.
                        int wait = s.JobId != 0 ? 0 : MinIdleTicks + rng.NextInt(IdleTicksRange);
                        s = s with { State = SettlerState.Idle, Progress = 0, WaitTicks = wait };
                    }
                }
            }
            else if (s.WaitTicks > 0)
            {
                s = s with { WaitTicks = s.WaitTicks - 1 };
            }
            else if (s.JobId != 0)
            {
                s = logistics.Advance(tick, s, path, map, territory, buildings, statistics, pathfinder, ref budget);
            }
            else if (budget > 0)
            {
                s = Wander(s, path, map, territory, buildings, rng, pathfinder, out int expansions);
                budget -= expansions;
            }
            _settlers[i] = s;
        }
    }

    private Settler Wander(Settler s, List<int> path, MapData map, Territory territory, BuildingRegistry buildings, Pcg32 rng,
        Pathfinder pathfinder, out int expansions)
    {
        int anchor = buildings.TryGet(s.HomeId, out var home) ? DoorOf(home, map, buildings) : s.Tile;
        int dx = rng.NextInt(2 * WanderRadius + 1) - WanderRadius;
        int dy = rng.NextInt(2 * WanderRadius + 1) - WanderRadius;
        int x = anchor % _edge + dx, y = anchor / _edge + dy;
        int cost = -1;
        expansions = 0;
        if ((uint)x < (uint)_edge && (uint)y < (uint)_edge)
        {
            byte owner = s.Owner;
            cost = pathfinder.FindPath(t => IsPassable(map, territory, buildings, owner, t), s.Tile, y * _edge + x,
                MaxExpansionsPerSearch, path);
            expansions = pathfinder.Expansions;
        }
        if (cost <= 0)
        {
            path.Clear();
            return s with { WaitTicks = MinIdleTicks };
        }
        path.Reverse();
        return s with { State = SettlerState.Walking, Progress = 0 };
    }

    /// <summary>Spawns one carrier at every complete building homing fewer carriers than its data allows.</summary>
    private void Refill(MapData map, BuildingRegistry buildings)
    {
        var all = buildings.All;
        var homed = new int[all.Count];
        foreach (var s in _settlers)
        {
            int index = s.Kind == SettlerKind.Carrier ? buildings.IndexOf(s.HomeId) : -1;
            if (index >= 0) homed[index]++;
        }
        for (int i = 0; i < all.Count; i++)
        {
            var b = all[i];
            if (b.State != BuildingState.Complete || homed[i] >= b.Definition.Carriers) continue;
            int door = DoorOf(b, map, buildings);
            if (door >= 0) Spawn(b.Owner, b.Id, door);
        }
    }

    /// <summary>Canonical state: next id, then every settler with its remaining path (next tile first).</summary>
    public void WriteTo(CanonicalWriter w)
    {
        w.WriteInt32(NextId);
        w.WriteInt32(_settlers.Count);
        for (int i = 0; i < _settlers.Count; i++)
        {
            var s = _settlers[i];
            w.WriteInt32(s.Id);
            w.WriteByte((byte)s.Kind);
            w.WriteByte(s.Owner);
            w.WriteInt32(s.HomeId);
            w.WriteInt32(s.Tile);
            w.WriteByte((byte)s.State);
            w.WriteUInt16((ushort)s.Progress);
            w.WriteUInt16((ushort)s.WaitTicks);
            w.WriteInt32(s.JobId);
            var path = _paths[i];
            w.WriteInt32(path.Count);
            for (int k = path.Count - 1; k >= 0; k--) w.WriteInt32(path[k]);
        }
    }

    /// <summary>Reads and validates settlers against the map size, player count and building ids.</summary>
    public static Settlers ReadFrom(CanonicalReader r, int edge, int playerCount, int nextBuildingId)
    {
        var reg = new Settlers(edge);
        int nextId = r.ReadInt32();
        if (nextId < 1) throw new InvalidDataException("Invalid next settler id");
        int count = r.ReadInt32();
        if (count < 0 || count >= nextId) throw new InvalidDataException("Invalid settler count");
        int tiles = edge * edge, lastId = 0;
        for (int i = 0; i < count; i++)
        {
            var s = new Settler(r.ReadInt32(), (SettlerKind)r.ReadByte(), r.ReadByte(), r.ReadInt32(), r.ReadInt32(),
                (SettlerState)r.ReadByte(), r.ReadUInt16(), r.ReadUInt16(), r.ReadInt32());
            if (s.Id <= lastId || s.Id >= nextId || s.Kind > SettlerKind.Worker || s.Owner >= playerCount
                || s.HomeId < 1 || s.HomeId >= nextBuildingId || (uint)s.Tile >= (uint)tiles || s.State > SettlerState.Walking || s.JobId < 0)
                throw new InvalidDataException("Invalid settler");
            int length = r.ReadInt32();
            if (length < 0 || length > tiles) throw new InvalidDataException("Invalid settler path length");
            var path = new List<int>(length);
            int prev = s.Tile;
            for (int k = 0; k < length; k++)
            {
                int t = r.ReadInt32();
                if ((uint)t >= (uint)tiles || t == prev || System.Math.Abs(t % edge - prev % edge) > 1
                    || System.Math.Abs(t / edge - prev / edge) > 1)
                    throw new InvalidDataException("Settler path is not a chain of neighbouring tiles");
                path.Add(t);
                prev = t;
            }
            bool walking = s.State == SettlerState.Walking;
            if (walking != (length > 0)
                || (walking ? s.Progress >= DiagonalStep || s.WaitTicks != 0 : s.Progress != 0 || s.WaitTicks > MinIdleTicks + IdleTicksRange))
                throw new InvalidDataException("Settler state does not match its path");
            if (s.Kind == SettlerKind.Worker && (walking || s.WaitTicks != 0 || s.JobId != 0))
                throw new InvalidDataException("A worker neither walks, waits nor carries");
            path.Reverse();
            lastId = s.Id;
            reg._settlers.Add(s);
            reg._paths.Add(path);
        }
        reg.NextId = nextId;
        return reg;
    }
}
