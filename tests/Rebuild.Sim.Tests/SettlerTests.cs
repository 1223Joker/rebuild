using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Core;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Sim.World;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>A* pathfinding, carrier spawning and settler movement (docs/06-economy.md §3, §6).</summary>
public class SettlerTests
{
    private static MatchSetup TwoPlayers(ulong seed = 1) => new(
        new MapSpec(seed, MapSize.Small, 2), seed,
        new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Ai, 1, "rivermen") });

    private static void RunTicks(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks / Simulation.TicksPerTurn; i++) sim.ExecuteTurn(new TurnBundle(sim.Turn, System.Array.Empty<Command>()));
    }

    // ---- pathfinder -------------------------------------------------------------------------------------

    [Fact]
    public void Straight_and_diagonal_paths_have_octile_cost()
    {
        var pf = new Pathfinder(16);
        var path = new List<int>();
        Assert.Equal(50, pf.FindPath(_ => true, 0, 5, 1000, path));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, path);
        Assert.Equal(3 * 14 + 2 * 10, pf.FindPath(_ => true, 0, 5 * 16 + 3, 1000, path));
        Assert.Equal(5, path.Count);
        Assert.Equal(5 * 16 + 3, path[^1]);
        Assert.Equal(0, pf.FindPath(_ => true, 7, 7, 1000, path));
        Assert.Empty(path);
    }

    [Fact]
    public void Paths_avoid_blocked_tiles_and_never_cut_corners()
    {
        // 3×3 grid, centre blocked: (0,0) → (2,2) must go around the edge (4 straight steps).
        var pf = new Pathfinder(3);
        var path = new List<int>();
        Assert.Equal(40, pf.FindPath(t => t != 4, 0, 8, 1000, path));
        // Only (1,0) blocked: the diagonal (0,0) → (1,1) would cut its corner, so the path is 0 → 3 → 7.
        Assert.Equal(10 + 14, pf.FindPath(t => t != 1, 0, 7, 1000, path));
        Assert.Equal(new[] { 3, 7 }, path);
        Assert.Equal(-1, pf.FindPath(t => t != 8, 0, 8, 1000, path));
        Assert.Empty(path);
    }

    [Fact]
    public void Searches_fail_when_the_expansion_limit_is_hit()
    {
        var pf = new Pathfinder(64);
        var path = new List<int>();
        Assert.Equal(-1, pf.FindPath(_ => true, 0, 63 * 64 + 63, 10, path));
        Assert.Equal(10, pf.Expansions);
        Assert.True(pf.FindPath(_ => true, 0, 63 * 64 + 63, 100000, path) > 0);
    }

    [Fact]
    public void Astar_cost_equals_dijkstra_on_random_grids()
    {
        var rng = new Pcg32(42, 7);
        var pf = new Pathfinder(24);
        var path = new List<int>();
        for (int round = 0; round < 200; round++)
        {
            var open = new bool[24 * 24];
            for (int i = 0; i < open.Length; i++) open[i] = rng.NextInt(100) >= 30;
            int start = rng.NextInt(open.Length);
            int goal = rng.NextInt(open.Length);
            int cost = pf.FindPath(t => open[t], start, goal, 1 << 20, path);
            Assert.Equal(Dijkstra(open, 24, start, goal), cost);
            if (cost > 0)
            {
                // The path is a chain of passable neighbours ending at the goal and its step costs add up.
                int prev = start, sum = 0;
                foreach (int t in path)
                {
                    Assert.True(open[t]);
                    int dx = System.Math.Abs(t % 24 - prev % 24), dy = System.Math.Abs(t / 24 - prev / 24);
                    Assert.True(dx <= 1 && dy <= 1 && dx + dy > 0);
                    sum += dx + dy == 2 ? 14 : 10;
                    prev = t;
                }
                Assert.Equal(goal, prev);
                Assert.Equal(cost, sum);
            }
        }
    }

    /// <summary>Reference: Dijkstra with the same moves (8 neighbours, no corner cutting, start exempt).</summary>
    private static int Dijkstra(bool[] open, int edge, int start, int goal)
    {
        if (start == goal) return 0;
        if (!open[goal]) return -1;
        var dist = Enumerable.Repeat(int.MaxValue, open.Length).ToArray();
        var done = new bool[open.Length];
        dist[start] = 0;
        while (true)
        {
            int best = -1;
            for (int i = 0; i < open.Length; i++)
                if (!done[i] && dist[i] != int.MaxValue && (best < 0 || dist[i] < dist[best])) best = i;
            if (best < 0) return -1;
            if (best == goal) return dist[goal];
            done[best] = true;
            int x = best % edge, y = best / edge;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= edge || ny >= edge) continue;
                    int n = ny * edge + nx;
                    if (!open[n]) continue;
                    if (dx != 0 && dy != 0 && (!open[y * edge + nx] || !open[ny * edge + x])) continue;
                    int d = dist[best] + (dx != 0 && dy != 0 ? 14 : 10);
                    if (d < dist[n]) dist[n] = d;
                }
        }
    }

    // ---- settlers ---------------------------------------------------------------------------------------

    [Fact]
    public void Start_castles_spawn_their_carriers_at_the_door()
    {
        var sim = Simulation.Create(TwoPlayers());
        int perCastle = BuildingCatalog.All[BuildingIds.Castle].Carriers;
        Assert.Equal(30, perCastle);
        Assert.Equal(10, BuildingCatalog.All[BuildingIds.Residence].Carriers);
        Assert.Equal(2 * perCastle, sim.Settlers.All.Count);
        foreach (var castle in sim.Buildings.All)
        {
            int door = Settlers.DoorOf(castle, sim.Map, sim.Buildings);
            Assert.Equal(castle.Y + 4, door / sim.Map.Edge);
            var own = sim.Settlers.All.Where(s => s.Owner == castle.Owner).ToList();
            Assert.Equal(perCastle, own.Count);
            Assert.All(own, s => Assert.Equal((castle.Id, door, SettlerState.Idle), (s.HomeId, s.Tile, s.State)));
        }
    }

    [Fact]
    public void Carriers_walk_inside_own_territory_and_never_onto_buildings()
    {
        var sim = Simulation.Create(TwoPlayers(3));
        var moved = new HashSet<int>();
        var start = sim.Settlers.All.ToDictionary(s => s.Id, s => s.Tile);
        for (int turn = 0; turn < 300; turn++)
        {
            RunTicks(sim, Simulation.TicksPerTurn);
            for (int i = 0; i < sim.Settlers.All.Count; i++)
            {
                var s = sim.Settlers.All[i];
                Assert.Equal(s.Owner, sim.Territory.OwnerAt(s.Tile));
                Assert.Equal(0, sim.Buildings.AtTile(s.Tile));
                if (s.Tile != start[s.Id]) moved.Add(s.Id);
                if (s.State == SettlerState.Walking)
                {
                    var path = sim.Settlers.PathAt(i);
                    Assert.NotEmpty(path);
                    Assert.All(path, t => Assert.True(Settlers.IsPassable(sim.Map, sim.Territory, sim.Buildings, s.Owner, t)));
                }
            }
        }
        Assert.Equal(sim.Settlers.All.Count, moved.Count);
    }

    [Fact]
    public void Walkers_advance_at_most_one_tile_per_turn_and_keep_bounded_progress()
    {
        // Speed 64 per tick: a straight step takes 4 ticks, a diagonal one 358 / 64 → 6 ticks, so a 2-tick turn moves ≤ 1 tile.
        Assert.Equal(358, Settlers.DiagonalStep);
        var sim = Simulation.Create(TwoPlayers());
        var last = sim.Settlers.All.ToDictionary(s => s.Id, s => s.Tile);
        int steps = 0;
        for (int turn = 0; turn < 200; turn++)
        {
            RunTicks(sim, Simulation.TicksPerTurn);
            foreach (var s in sim.Settlers.All)
            {
                int prev = last[s.Id], edge = sim.Map.Edge;
                Assert.True(System.Math.Abs(s.Tile % edge - prev % edge) <= 1 && System.Math.Abs(s.Tile / edge - prev / edge) <= 1);
                Assert.InRange(s.Progress, 0, Settlers.DiagonalStep - 1);
                if (s.Tile != prev) steps++;
                last[s.Id] = s.Tile;
            }
        }
        // 60 carriers × 400 ticks; idle waits of 2–6 s keep them well below the 2.5 tiles/s maximum.
        Assert.InRange(steps, 60, 60 * 400 / 4);
    }

    [Fact]
    public void A_carrier_covered_by_a_new_building_is_put_at_its_door()
    {
        var sim = Simulation.Create(TwoPlayers(3));
        RunTicks(sim, 200);
        // Find an idle carrier of slot 0 standing where a storehouse centred on it may be placed.
        foreach (var c in sim.Settlers.All.Where(s => s.Owner == 0))
        {
            int x = c.Tile % sim.Map.Edge - 1, y = c.Tile / sim.Map.Edge - 1;
            if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, 0, BuildingIds.Storehouse, x, y) != PlacementResult.Ok) continue;
            int id = c.Id;
            sim.ExecuteTurn(new TurnBundle(sim.Turn, new[] { BuildingCommands.Place(0, 0, BuildingIds.Storehouse, x, y) }));
            Assert.Equal(0, sim.RejectedCommands);
            Assert.True(sim.Buildings.TryGet(sim.Buildings.All.Last().Id, out var site));
            var moved = sim.Settlers.All.Single(s => s.Id == id);
            Assert.Equal(Settlers.DoorOf(site, sim.Map, sim.Buildings), moved.Tile);
            Assert.All(sim.Settlers.All, s => Assert.Equal(0, sim.Buildings.AtTile(s.Tile)));
            return;
        }
        throw new Xunit.Sdk.XunitException("No carrier on a free storehouse spot");
    }

    [Fact]
    public void A_completed_residence_spawns_one_carrier_per_interval_up_to_its_capacity()
    {
        var sim = Simulation.Create(TwoPlayers());
        var s0 = sim.StartOf(0)!.Value;
        (int X, int Y) spot = (-1, -1);
        for (int y = s0.Y - 14; y <= s0.Y + 14 && spot.X < 0; y++)
            for (int x = s0.X - 14; x <= s0.X + 14 && spot.X < 0; x++)
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, 0, BuildingIds.Residence, x, y) == PlacementResult.Ok)
                    spot = (x, y);
        sim.ExecuteTurn(new TurnBundle(sim.Turn, new[] { BuildingCommands.Place(0, 0, BuildingIds.Residence, spot.X, spot.Y) }));
        int residence = sim.Buildings.All.Last().Id;
        RunTicks(sim, Settlers.SpawnIntervalTicks); // carriers bring 4 units, each worked in for 20 ticks
        Assert.True(sim.Buildings.TryGet(residence, out var b) && b.State == BuildingState.Complete);
        int Homed() => sim.Settlers.All.Count(s => s.HomeId == residence);
        int spawned = Homed();
        Assert.Equal(1, spawned); // tick 600
        RunTicks(sim, 9 * Settlers.SpawnIntervalTicks);
        Assert.Equal(10, Homed());
        RunTicks(sim, 2 * Settlers.SpawnIntervalTicks);
        Assert.Equal(10, Homed());
        Assert.Equal(30, sim.Settlers.All.Count(s => s.HomeId == sim.Buildings.All[0].Id)); // the castle stays full
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Save_and_load_mid_walk_continue_identically()
    {
        var a = Simulation.Create(TwoPlayers(5));
        RunTicks(a, 50);
        Assert.Contains(a.Settlers.All, s => s.State == SettlerState.Walking);
        var b = Simulation.Load(a.Save());
        for (int i = 0; i < 100; i++)
        {
            RunTicks(a, Simulation.TicksPerTurn);
            RunTicks(b, Simulation.TicksPerTurn);
            Assert.Equal(a.ComputeHash(), b.ComputeHash());
        }
    }

    [Fact]
    public void Corrupt_settler_state_is_rejected_on_load()
    {
        var sim = Simulation.Create(TwoPlayers(5));
        RunTicks(sim, 50);
        int walker = sim.Settlers.All.ToList().FindIndex(s => s.State == SettlerState.Walking);
        Assert.True(walker >= 0);
        var save = sim.Save();
        // Settlers are the last section; locate the walker's first path tile and break the chain.
        var w = new CanonicalWriter(65536);
        sim.Settlers.WriteTo(w);
        int section = save.Length - w.Length;
        int offset = section + 8;
        for (int i = 0; i < walker; i++) offset += 23 + 4 + 4 * sim.Settlers.PathAt(i).Count;
        int firstPathTile = offset + 23 + 4;
        var broken = (byte[])save.Clone();
        System.BitConverter.GetBytes(sim.Settlers.All[walker].Tile + 5).CopyTo(broken, firstPathTile);
        Assert.Throws<InvalidDataException>(() => Simulation.Load(broken));
        var badOwner = (byte[])save.Clone();
        badOwner[offset + 5] = 9; // owner byte (id 4, kind 1)
        Assert.Throws<InvalidDataException>(() => Simulation.Load(badOwner));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(save).ComputeHash());
    }
}
