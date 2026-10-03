using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Goods;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Sim.World;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>Production buildings: work cycles, harvesting, piles, input requests and output overflow (docs/06-economy.md §4).</summary>
public class ProductionTests
{
    private static MatchSetup TwoPlayers(ulong seed = 1) => new(
        new MapSpec(seed, MapSize.Small, 2), seed,
        new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Ai, 1, "rivermen") });

    private static void Run(Simulation sim, params Command[] commands) => ConstructionTests.Run(sim, commands);

    private static void RunTicks(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks / Simulation.TicksPerTurn; i++) Run(sim);
    }

    /// <summary>Runs turns until <paramref name="done"/> holds; fails after <paramref name="maxTicks"/>.</summary>
    private static void RunUntil(Simulation sim, System.Func<bool> done, int maxTicks)
    {
        for (int t = 0; !done(); t += Simulation.TicksPerTurn)
        {
            Assert.True(t < maxTicks, "condition not reached in time");
            Run(sim);
        }
    }

    /// <summary>Tiles offering the harvest source within the building type's radius around a footprint at (x, y) on the slot's territory.</summary>
    internal static int ObjectsAround(Simulation sim, byte slot, int type, int x, int y)
    {
        var def = BuildingCatalog.All[type];
        var b = new Building(1, (ushort)type, slot, x, y, 0, BuildingState.Complete, 0);
        int n = 0, r = def.Production!.Radius, edge = sim.Map.Edge;
        for (int ty = b.CenterY - r; ty <= b.CenterY + r; ty++)
            for (int tx = b.CenterX - r; tx <= b.CenterX + r; tx++)
            {
                if ((uint)tx >= (uint)edge || (uint)ty >= (uint)edge) continue;
                int dx = tx - b.CenterX, dy = ty - b.CenterY, t = ty * edge + tx;
                if (dx * dx + dy * dy <= r * r && Harvest.Matches(sim.Map, t, def.Production.Harvest) && sim.Territory.OwnerAt(t) == slot) n++;
            }
        return n;
    }

    private static int Place(Simulation sim, byte slot, ushort type, ushort seq, System.Func<int, int, bool> accept)
    {
        int id = TryPlace(sim, slot, type, seq, accept);
        return id > 0 ? id : throw new Xunit.Sdk.XunitException($"No spot for {BuildingCatalog.All[type].Id}");
    }

    /// <summary>Places a building at the first accepted valid spot near the slot's start; -1 if there is none.</summary>
    private static int TryPlace(Simulation sim, byte slot, ushort type, ushort seq, System.Func<int, int, bool> accept)
    {
        var s = sim.StartOf(slot)!.Value;
        for (int y = s.Y - 20; y <= s.Y + 20; y++)
            for (int x = s.X - 20; x <= s.X + 20; x++)
            {
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, slot, type, x, y) != PlacementResult.Ok || !accept(x, y)) continue;
                Run(sim, BuildingCommands.Place(slot, seq, type, x, y));
                Assert.Equal(0, sim.RejectedCommands);
                return sim.Buildings.All.Last(b => b.Owner == slot).Id;
            }
        return -1;
    }

    /// <summary>Places and completes a woodcutter with at least <paramref name="trees"/> trees in reach.</summary>
    internal static int Woodcutter(Simulation sim, int trees, ushort seq = 0)
    {
        int id = Place(sim, 0, BuildingIds.Woodcutter, seq, (x, y) => ObjectsAround(sim, 0, BuildingIds.Woodcutter, x, y) >= trees);
        ConstructionTests.RunUntilComplete(sim, id);
        return id;
    }

    private static int CastleIndex(Simulation sim) => sim.Buildings.All.ToList().FindIndex(b => b.Owner == 0 && b.Type == BuildingIds.Castle);

    /// <summary>Units of a good the slot holds: storage stocks, production piles, transport jobs and field workers carrying it home (not piles of alternative goods).</summary>
    private static int Units(Simulation sim, byte slot, int good)
    {
        int n = 0;
        foreach (var b in sim.Buildings.All.Where(b => b.Owner == slot))
        {
            n += sim.Buildings.StockOf(b.Id)?[good] ?? 0;
            var piles = sim.Buildings.PilesOf(b.Id);
            if (piles == null || b.Definition.Production is not { } p) continue;
            for (int k = 0; k < p.Inputs.Count; k++)
                if (p.Alternatives[k].Count == 1 && p.Inputs[k] == good) n += piles[k];
            int o = p.OutputIndexOf(good);
            if (o >= 0) n += piles[p.Inputs.Count + o];
        }
        return n + sim.Logistics.All.Count(j => j.Owner == slot && j.Good == good)
            + sim.Settlers.All.Count(s => s.Owner == slot && s.Laden && sim.Buildings.TryGet(s.WorkplaceId, out var w) && w.Definition.Production!.Output == good);
    }

    [Fact]
    public void Production_is_compiled_from_data()
    {
        var wood = BuildingCatalog.All[BuildingIds.Woodcutter].Production!;
        Assert.Equal((HarvestSource.Tree, 8, (ushort)GoodIds.Log, 0), (wood.Harvest, wood.Radius, wood.Output, wood.Inputs.Count));
        var saw = BuildingCatalog.All[BuildingIds.Sawmill].Production!;
        Assert.Equal((HarvestSource.None, (ushort)GoodIds.Plank), (saw.Harvest, saw.Output));
        Assert.Equal(new[] { (ushort)GoodIds.Log }, saw.Inputs);
        Assert.Equal(new[] { 1 }, saw.InputAmounts);
        Assert.Equal(0, saw.InputIndexOf(GoodIds.Log));
        Assert.Equal(-1, saw.InputIndexOf(GoodIds.Plank));
        Assert.Equal(HarvestSource.Stone, BuildingCatalog.All[BuildingIds.Stonecutter].Production!.Harvest);
        Assert.Null(BuildingCatalog.All[BuildingIds.Castle].Production);
        Assert.All(BuildingCatalog.All.Where(d => d.Production != null), d => Assert.False(d.IsStorage));
    }

    [Fact]
    public void A_woodcutter_fells_trees_and_its_logs_reach_the_castle()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Woodcutter(sim, trees: 12);
        var cutter = ConstructionTests.Get(sim, id);
        Assert.Equal(new[] { 0 }, sim.Buildings.PilesOf(id));
        int trees = ObjectsAround(sim, 0, BuildingIds.Woodcutter, cutter.X, cutter.Y);
        int castle = CastleIndex(sim);
        Assert.Equal(0, sim.Buildings.StockAt(castle)![GoodIds.Log]);
        int cycle = cutter.Definition.Production!.CycleTicks;
        RunTicks(sim, 10 * cycle);
        int felled = trees - ObjectsAround(sim, 0, BuildingIds.Woodcutter, cutter.X, cutter.Y);
        Assert.InRange(felled, 6, 10); // at most one cycle per CycleTicks; walking to the trees and back costs time
        Assert.Equal(felled, Units(sim, 0, GoodIds.Log));
        Assert.Equal(felled, sim.MapChanges.Tiles.Count);
        Assert.All(sim.MapChanges.Tiles, t => Assert.Equal((byte)MapObject.None, sim.Map.Object[t]));
        Assert.InRange(sim.Buildings.StockAt(castle)![GoodIds.Log], felled - 3, felled); // overflow carried to the castle
        // Map changes, piles and cycles survive save and load and the runs stay identical.
        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
        Assert.Equal(sim.Map.Object, loaded.Map.Object);
        Assert.Equal(sim.Map.Flags, loaded.Map.Flags);
        RunTicks(sim, 400);
        RunTicks(loaded, 400);
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
    }

    [Fact]
    public void A_woodcutter_walks_to_the_tree_and_carries_the_log_home()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Woodcutter(sim, trees: 12);
        var cutter = ConstructionTests.Get(sim, id);
        int door = Settlers.DoorOf(cutter, sim.Map, sim.Buildings);
        Settler Worker() => sim.Settlers.All.Single(s => s.WorkplaceId == id);
        int trees = ObjectsAround(sim, 0, BuildingIds.Woodcutter, cutter.X, cutter.Y);
        int cycle = cutter.Definition.Production!.CycleTicks;
        // Out to the nearest tree: the cycle waits while the worker walks.
        RunUntil(sim, () => sim.Settlers.All.Any(s => s.WorkplaceId == id) && Worker().State == SettlerState.Walking, 2000);
        int tree = Production.FindHarvest(sim.Map, sim.Territory, ConstructionTests.Get(sim, id), cutter.Definition.Production);
        int spot = Production.WorkSpot(sim.Map, sim.Territory, sim.Buildings, 0, tree, door);
        Assert.NotEqual(door, spot); // the test needs a real walk
        Assert.Equal(1, ConstructionTests.Get(sim, id).Cycle);
        RunUntil(sim, () => Worker().State == SettlerState.Idle, cycle);
        Assert.Equal(spot, Worker().Tile);
        // Felled at the end of the cycle; the log is carried, not piled yet.
        RunUntil(sim, () => Worker().Laden, cycle);
        Assert.Equal(trees - 1, ObjectsAround(sim, 0, BuildingIds.Woodcutter, cutter.X, cutter.Y));
        Assert.Equal(new[] { 0 }, sim.Buildings.PilesOf(id));
        Assert.Equal(0, sim.Statistics.TotalProduced(0, GoodIds.Log));
        // A save on the way home loads and runs on identically.
        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
        RunUntil(sim, () => !Worker().Laden, cycle);
        Assert.Equal((door, 1L), (Worker().Tile, sim.Statistics.TotalProduced(0, GoodIds.Log)));
        Assert.Equal(1, Units(sim, 0, GoodIds.Log));
        while (loaded.Tick < sim.Tick) Run(loaded);
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
        // Released mid-cycle in the field (homeless or crisis; the workplace stays): the cycle stops with it.
        RunUntil(sim, () => ConstructionTests.Get(sim, id).Cycle > 1, 3 * cycle);
        sim.Logistics.ReleaseWorker(sim.Buildings, sim.Settlers, id);
        Assert.Equal(0, ConstructionTests.Get(sim, id).Cycle);
        Assert.DoesNotContain(sim.Settlers.All, s => s.WorkplaceId == id);
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Field_workers_are_planters_and_harvesters_of_objects_and_fish()
    {
        var walkers = BuildingCatalog.All.Where(d => d.Production is { } p && Production.WalksOut(p)).Select(d => d.Id).OrderBy(n => n, System.StringComparer.Ordinal);
        Assert.Equal(new[] { "fisher", "forester", "hunter", "stonecutter", "woodcutter" }, walkers);
    }

    [Fact]
    public void Storage_capacities_are_compiled_from_data()
    {
        Assert.Equal(500, BuildingCatalog.All[BuildingIds.Castle].StorageCapacity);
        Assert.Equal(300, BuildingCatalog.All[BuildingIds.Storehouse].StorageCapacity);
        Assert.All(BuildingCatalog.All.Where(d => !d.IsStorage), d => Assert.Equal(0, d.StorageCapacity));
        Assert.True(GoodCatalog.All.Sum(g => g.StartStock) < BuildingCatalog.All[BuildingIds.Castle].StorageCapacity);
    }

    [Fact]
    public void A_woodcutter_works_until_its_pile_is_full_when_no_storage_has_room()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Woodcutter(sim, trees: 14);
        var stock = sim.Buildings.StockAt(CastleIndex(sim))!;
        foreach (int g in Food.Append(GoodIds.Water)) stock[g] = 0; // nothing eaten makes room (RunFed)
        int capacity = BuildingCatalog.All[BuildingIds.Castle].StorageCapacity;
        stock[GoodIds.Stone] += capacity - 2 - stock.Sum(); // room for two more units
        int cycle = ConstructionTests.Get(sim, id).Definition.Production!.CycleTicks;
        for (int i = 0; i < 16 * cycle / Simulation.TicksPerTurn; i++) ConstructionTests.RunFed(sim);
        // Two logs reached the castle; the rest filled the pile, then the woodcutter paused.
        Assert.Equal(capacity, stock.Sum());
        Assert.Equal(2, stock[GoodIds.Log]);
        Assert.Equal(new[] { Production.OutputCap }, sim.Buildings.PilesOf(id));
        Assert.Equal(0, ConstructionTests.Get(sim, id).Cycle);
        Assert.DoesNotContain(sim.Logistics.All, j => j.Kind == JobKind.Transport);
        // Room in storage again: the overflow resumes and the woodcutter works on.
        stock[GoodIds.Stone] -= 5;
        for (int i = 0; i < 3 * cycle / Simulation.TicksPerTurn; i++) ConstructionTests.RunFed(sim);
        Assert.Equal(7, stock[GoodIds.Log]);
        Assert.Equal(capacity, stock.Sum());
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Woodcutter_and_sawmill_turn_trees_into_planks()
    {
        var sim = Simulation.Create(TwoPlayers(3));
        int cutter = Woodcutter(sim, trees: 12);
        int mill = Place(sim, 0, BuildingIds.Sawmill, 1, (_, _) => true);
        ConstructionTests.RunUntilComplete(sim, mill);
        int planks = Units(sim, 0, GoodIds.Plank);
        int treesBefore = sim.MapChanges.Tiles.Count;
        RunTicks(sim, 3000);
        int planksMade = Units(sim, 0, GoodIds.Plank) - planks;
        Assert.True(planksMade >= 10, $"only {planksMade} planks");
        // Every felled tree is a log, every plank used one log.
        Assert.Equal(sim.MapChanges.Tiles.Count, Units(sim, 0, GoodIds.Log) + planksMade + Consumed(sim, mill));
        Assert.True(sim.MapChanges.Tiles.Count > treesBefore);
        Assert.InRange(sim.Buildings.PilesOf(mill)![0] + sim.Logistics.All.Count(j => j.DestinationId == mill), 0, Production.InputTarget);
        Assert.True(sim.Buildings.TryGet(cutter, out _));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    /// <summary>Logs consumed by a sawmill cycle that has not produced its plank yet.</summary>
    private static int Consumed(Simulation sim, int mill) => ConstructionTests.Get(sim, mill).Cycle > 0 ? 1 : 0;

    [Fact]
    public void A_sawmill_takes_logs_from_storage_up_to_the_input_target()
    {
        var sim = Simulation.Create(TwoPlayers());
        int mill = Place(sim, 0, BuildingIds.Sawmill, 0, (_, _) => true);
        ConstructionTests.RunUntilComplete(sim, mill);
        int castle = CastleIndex(sim);
        sim.Buildings.StockAt(castle)![GoodIds.Log] = 20;
        int planks = Units(sim, 0, GoodIds.Plank);
        int maxOnTheWay = 0;
        for (int turn = 0; turn < 600; turn++)
        {
            Run(sim);
            int onTheWay = sim.Buildings.PilesOf(mill)![0] + sim.Logistics.All.Count(j => j.DestinationId == mill);
            Assert.InRange(onTheWay, 0, Production.InputTarget);
            maxOnTheWay = System.Math.Max(maxOnTheWay, onTheWay);
        }
        Assert.Equal(Production.InputTarget, maxOnTheWay);
        int made = Units(sim, 0, GoodIds.Plank) - planks;
        Assert.Equal(20, Units(sim, 0, GoodIds.Log) + made + Consumed(sim, mill));
        Assert.True(made >= 15, $"only {made} planks");
    }

    [Fact]
    public void A_stonecutter_takes_stone_units_until_the_outcrop_is_gone()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Place(sim, 0, BuildingIds.Stonecutter, 0, (x, y) => ObjectsAround(sim, 0, BuildingIds.Stonecutter, x, y) >= 1);
        ConstructionTests.RunUntilComplete(sim, id);
        var b = ConstructionTests.Get(sim, id);
        int nearest = Production.FindHarvest(sim.Map, sim.Territory, b, b.Definition.Production!);
        int amount = System.Math.Max((int)sim.Map.Amount[nearest], 1);
        int stone = Units(sim, 0, GoodIds.Stone);
        int cycle = b.Definition.Production!.CycleTicks;
        RunUntil(sim, () => Units(sim, 0, GoodIds.Stone) == stone + amount, 3 * cycle * amount);
        Assert.Equal((byte)MapObject.None, sim.Map.Object[nearest]);
        Assert.Contains(nearest, sim.MapChanges.Tiles);
        Assert.Equal(stone + amount, Units(sim, 0, GoodIds.Stone));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Without_a_storage_the_output_pile_fills_up_and_production_pauses()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Woodcutter(sim, trees: 3);
        sim.Buildings.Remove(sim.Buildings.All[CastleIndex(sim)].Id);
        sim.Buildings.PilesAt(sim.Buildings.IndexOf(id))![0] = Production.OutputCap - 1;
        int cycle = ConstructionTests.Get(sim, id).Definition.Production!.CycleTicks;
        RunUntil(sim, () => sim.Buildings.PilesOf(id)![0] == Production.OutputCap, 2 * cycle);
        Assert.Equal(new[] { Production.OutputCap }, sim.Buildings.PilesOf(id));
        int changes = sim.MapChanges.Tiles.Count;
        RunTicks(sim, 3 * cycle);
        Assert.Equal(new[] { Production.OutputCap }, sim.Buildings.PilesOf(id));
        Assert.Equal(0, ConstructionTests.Get(sim, id).Cycle);
        Assert.Equal(changes, sim.MapChanges.Tiles.Count);
        Assert.Empty(sim.Logistics.All);
    }

    [Fact]
    public void A_woodcutter_without_trees_in_reach_idles()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Place(sim, 0, BuildingIds.Woodcutter, 0, (x, y) => ObjectsAround(sim, 0, BuildingIds.Woodcutter, x, y) == 0);
        ConstructionTests.RunUntilComplete(sim, id);
        RunTicks(sim, 400);
        Assert.Equal(new[] { 0 }, sim.Buildings.PilesOf(id));
        Assert.Equal(0, ConstructionTests.Get(sim, id).Cycle);
        Assert.Empty(sim.MapChanges.Tiles);
    }

    [Fact]
    public void Corrupt_production_state_is_rejected_on_load()
    {
        var mill = new Building(5, BuildingIds.Sawmill, 0, 10, 10, 0, BuildingState.Complete, 0, Cycle: 59);
        Assert.True(Construction.IsConsistent(mill));
        Assert.False(Construction.IsConsistent(mill with { Cycle = 60 }));                               // cycle ≥ cycle ticks
        Assert.False(Construction.IsConsistent(mill with { State = BuildingState.ConstructionSite }));   // sites run no cycle
        Assert.False(Construction.IsConsistent(new Building(6, BuildingIds.Storehouse, 0, 10, 10, 0, BuildingState.Complete, 0, Cycle: 1)));

        var sim = Simulation.Create(TwoPlayers());
        int id = Woodcutter(sim, trees: 3);
        RunTicks(sim, 400);
        Assert.NotEmpty(sim.MapChanges.Tiles);
        byte[] Encode(System.Action<CanonicalWriter> write)
        {
            var w = new CanonicalWriter(1024);
            write(w);
            return w.ToArray();
        }

        // Map changes: count(4) then per tile tile(4) height(1) object(1) resource(1) amount(1).
        var changes = Encode(w => sim.MapChanges.WriteTo(w, sim.Map));
        MapData Fresh() => MapGenerator.Generate(sim.Setup.Map).Map!;
        MapChanges.ReadFrom(new CanonicalReader(changes), Fresh());
        int plain = Enumerable.Range(0, sim.Map.TileCount).First(t => sim.Map.Object[t] == (byte)MapObject.None && !sim.MapChanges.Tiles.Contains(t));
        var cases = new System.Action<byte[]>[]
        {
            b => { System.BitConverter.GetBytes(plain).CopyTo(b, 4); b[8] = sim.Map.Height[plain]; }, // tile that never held a tree or stone
            b => { b[9] = (byte)MapObject.Tree; b[11] = 1; },                   // tree back with an amount
            b => b[9] = (byte)MapObject.Lair,                                   // tree turned into something else
            b => b[11] = 3,                                                     // removed object with an amount
            b => b[10] = (byte)Resource.Fish,                                    // resource appeared
            b => System.BitConverter.GetBytes(sim.Map.TileCount).CopyTo(b, 4), // outside the map
        };
        foreach (var corrupt in cases)
        {
            var bad = (byte[])changes.Clone();
            corrupt(bad);
            Assert.Throws<InvalidDataException>(() => MapChanges.ReadFrom(new CanonicalReader(bad), Fresh()));
        }

        // A woodcutter's output pile over the cap.
        var buildings = Encode(sim.Buildings.WriteTo);
        var over = (byte[])buildings.Clone();
        over[^1] = Production.OutputCap + 1; // the woodcutter is the last building; its output pile is the last byte
        Assert.Equal(sim.Buildings.PilesOf(id)![0], buildings[^1]);
        Assert.Throws<InvalidDataException>(() =>
            BuildingRegistry.ReadFrom(new CanonicalReader(over), sim.Map.Edge, sim.Players.Count, sim.Territory));
    }

    [Fact]
    public void Food_and_metal_production_is_compiled_from_data()
    {
        (HarvestSource, int, ushort) Source(ushort type)
        {
            var p = BuildingCatalog.All[type].Production!;
            Assert.Empty(p.Inputs);
            return (p.Harvest, p.Radius, p.Output);
        }
        Assert.Equal((HarvestSource.Fish, 6, (ushort)GoodIds.Fish), Source(BuildingIds.Fisher));
        Assert.Equal((HarvestSource.Game, 10, (ushort)GoodIds.Meat), Source(BuildingIds.Hunter));
        Assert.Equal((HarvestSource.Fertile, 4, (ushort)GoodIds.Grain), Source(BuildingIds.Farm));
        Assert.Equal((HarvestSource.Water, 4, (ushort)GoodIds.Water), Source(BuildingIds.Waterworks));

        void Chain(ushort type, ushort output, params int[] inputs)
        {
            var p = BuildingCatalog.All[type].Production!;
            Assert.Equal((HarvestSource.None, 0, output), (p.Harvest, p.Radius, p.Output));
            Assert.Equal(inputs.Select(g => (ushort)g), p.Inputs);
            Assert.All(p.InputAmounts, a => Assert.Equal(1, a));
        }
        Chain(BuildingIds.Mill, GoodIds.Flour, GoodIds.Grain);
        Chain(BuildingIds.Bakery, GoodIds.Bread, GoodIds.Flour, GoodIds.Water);
        Chain(BuildingIds.PigFarm, GoodIds.Pig, GoodIds.Grain, GoodIds.Water);
        Chain(BuildingIds.Slaughterhouse, GoodIds.Meat, GoodIds.Pig);
        Chain(BuildingIds.IronSmelter, GoodIds.Iron, GoodIds.IronOre, GoodIds.Coal);
        Chain(BuildingIds.GoldSmelter, GoodIds.Gold, GoodIds.GoldOre, GoodIds.Coal);
        Assert.True(Harvest.IsConsumed(HarvestSource.Fish) && Harvest.IsConsumed(HarvestSource.Game));
        Assert.False(Harvest.IsConsumed(HarvestSource.Water) || Harvest.IsConsumed(HarvestSource.Fertile) || Harvest.IsConsumed(HarvestSource.None));
    }

    [Fact]
    public void A_fisher_catches_fish_until_the_water_is_empty()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Place(sim, 0, BuildingIds.Fisher, 0, (x, y) => ObjectsAround(sim, 0, BuildingIds.Fisher, x, y) >= 1);
        ConstructionTests.RunUntilComplete(sim, id);
        var b = ConstructionTests.Get(sim, id);
        int nearest = Production.FindHarvest(sim.Map, sim.Territory, b, b.Definition.Production!);
        Assert.Equal((byte)Terrain.Water, sim.Map.Terrain[nearest]);
        int amount = sim.Map.Amount[nearest];
        Assert.True(amount >= 2);
        int fish = Units(sim, 0, GoodIds.Fish) + Eaten(sim, GoodIds.Fish);
        int cycle = b.Definition.Production!.CycleTicks;
        RunUntil(sim, () => sim.Map.Amount[nearest] < amount, 2 * cycle);
        Assert.Equal((byte)Resource.Fish, sim.Map.Resource[nearest]);
        Assert.Equal(amount - 1, sim.Map.Amount[nearest]);
        int door = Settlers.DoorOf(b, sim.Map, sim.Buildings);
        int shore = Production.WorkSpot(sim.Map, sim.Territory, sim.Buildings, 0, nearest, door);
        Assert.NotEqual(nearest, shore); // water is not walkable: the fisher stands next to it
        Assert.True(System.Math.Abs(shore % sim.Map.Edge - nearest % sim.Map.Edge) <= 1 && System.Math.Abs(shore / sim.Map.Edge - nearest / sim.Map.Edge) <= 1);
        Assert.Contains(nearest, sim.MapChanges.Tiles);
        // A partly fished tile survives save and load.
        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
        Assert.Equal(sim.Map.Resource, loaded.Map.Resource);
        Assert.Equal(sim.Map.Amount, loaded.Map.Amount);
        RunUntil(sim, () => Units(sim, 0, GoodIds.Fish) + Eaten(sim, GoodIds.Fish) == fish + amount, 2 * cycle * amount);
        Assert.Equal(((byte)Resource.None, (byte)0), (sim.Map.Resource[nearest], sim.Map.Amount[nearest]));
        Assert.Equal(fish + amount, Units(sim, 0, GoodIds.Fish) + Eaten(sim, GoodIds.Fish));
        loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
        Assert.Equal(sim.Map.Resource, loaded.Map.Resource);
    }

    [Fact]
    public void A_hunter_hunts_game_into_meat()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Place(sim, 0, BuildingIds.Hunter, 0, (x, y) => ObjectsAround(sim, 0, BuildingIds.Hunter, x, y) >= 2);
        ConstructionTests.RunUntilComplete(sim, id);
        var b = ConstructionTests.Get(sim, id);
        int game = ObjectsAround(sim, 0, BuildingIds.Hunter, b.X, b.Y);
        int meat = Units(sim, 0, GoodIds.Meat) + Eaten(sim, GoodIds.Meat);
        RunUntil(sim, () => Units(sim, 0, GoodIds.Meat) + Eaten(sim, GoodIds.Meat) == meat + 2, 4 * b.Definition.Production!.CycleTicks);
        Assert.Equal(game - 2, ObjectsAround(sim, 0, BuildingIds.Hunter, b.X, b.Y));
        Assert.Equal(meat + 2, Units(sim, 0, GoodIds.Meat) + Eaten(sim, GoodIds.Meat));
        Assert.All(sim.MapChanges.Tiles, t => Assert.Equal((byte)MapObject.None, sim.Map.Object[t]));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Farm_waterworks_mill_and_bakery_turn_fertile_land_and_water_into_bread()
    {
        var sim = Simulation.Create(TwoPlayers());
        ushort seq = 0;
        int Build(ushort type, System.Func<int, int, bool> accept)
        {
            int id = Place(sim, 0, type, seq++, accept);
            ConstructionTests.RunUntilComplete(sim, id);
            return id;
        }
        int farm = Build(BuildingIds.Farm, (x, y) => ObjectsAround(sim, 0, BuildingIds.Farm, x, y) >= 1);
        int water = Build(BuildingIds.Waterworks, (x, y) => ObjectsAround(sim, 0, BuildingIds.Waterworks, x, y) >= 1);
        int mill = Build(BuildingIds.Mill, (_, _) => true);
        int bakery = Build(BuildingIds.Bakery, (_, _) => true);
        int bread = Units(sim, 0, GoodIds.Bread) + Eaten(sim, GoodIds.Bread);
        int grainCycle = ConstructionTests.Get(sim, farm).Definition.Production!.CycleTicks;
        RunTicks(sim, 8 * grainCycle);
        int made = Units(sim, 0, GoodIds.Bread) + Eaten(sim, GoodIds.Bread) - bread;
        Assert.InRange(made, 4, (int)sim.Statistics.TotalProduced(0, GoodIds.Grain)); // at most one loaf per grain; the chain needs a few cycles to fill
        Assert.Empty(sim.MapChanges.Tiles); // fertile land and water are not used up
        Assert.True(Units(sim, 0, GoodIds.Water) > 0);
        Assert.InRange(sim.Buildings.PilesOf(bakery)![1] + sim.Logistics.All.Count(j => j.DestinationId == bakery && j.Good == GoodIds.Water),
            0, Production.InputTarget);
        Assert.True(sim.Buildings.TryGet(water, out _) && sim.Buildings.TryGet(mill, out _));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Theory]
    [InlineData(BuildingIds.PigFarm, GoodIds.Water, GoodIds.Grain, GoodIds.Pig)]
    [InlineData(BuildingIds.IronSmelter, GoodIds.IronOre, GoodIds.Coal, GoodIds.Iron)]
    [InlineData(BuildingIds.GoldSmelter, GoodIds.GoldOre, GoodIds.Coal, GoodIds.Gold)]
    public void A_two_input_building_consumes_one_of_each_input_per_unit(ushort type, int a, int b, int output)
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Place(sim, 0, type, 0, (_, _) => true);
        ConstructionTests.RunUntilComplete(sim, id);
        var stock = sim.Buildings.StockAt(CastleIndex(sim))!;
        stock[a] = 10;
        stock[b] = 6; // the scarcer input limits the output
        int before = Units(sim, 0, output);
        RunTicks(sim, 10 * BuildingCatalog.All[type].Production!.CycleTicks);
        int made = Units(sim, 0, output) - before;
        int running = Consumed(sim, id);
        Assert.Equal(6, made + running);
        Assert.Equal(0, Units(sim, 0, b));
        if (a != GoodIds.Water) Assert.Equal(10 - 6, Units(sim, 0, a)); // the castle drinks water too
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Corrupt_fish_changes_are_rejected_on_load()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Place(sim, 0, BuildingIds.Fisher, 0, (x, y) => ObjectsAround(sim, 0, BuildingIds.Fisher, x, y) >= 1);
        ConstructionTests.RunUntilComplete(sim, id);
        RunUntil(sim, () => sim.MapChanges.Tiles.Count > 0, 2 * ConstructionTests.Get(sim, id).Definition.Production!.CycleTicks);
        Assert.Single(sim.MapChanges.Tiles);
        var w = new CanonicalWriter(64);
        sim.MapChanges.WriteTo(w, sim.Map);
        var changes = w.ToArray();
        MapData Fresh() => MapGenerator.Generate(sim.Setup.Map).Map!;
        MapChanges.ReadFrom(new CanonicalReader(changes), Fresh());
        var cases = new System.Action<byte[]>[]
        {
            b => b[11] = 0,                                // fish left but amount 0
            b => b[11] = 200,                              // more fish than generated
            b => b[10] = (byte)Resource.Coal,               // fish turned into coal
            b => b[9] = (byte)MapObject.Tree,              // tree appeared on water
            b => { b[10] = (byte)Resource.None; b[11] = 1; }, // fish gone but an amount left
        };
        foreach (var corrupt in cases)
        {
            var bad = (byte[])changes.Clone();
            corrupt(bad);
            Assert.Throws<InvalidDataException>(() => MapChanges.ReadFrom(new CanonicalReader(bad), Fresh()));
        }
    }

    [Fact]
    public void Overflow_for_an_unreachable_storage_goes_to_another_storage()
    {
        // The first seed whose start area fits the layout below.
        for (ulong seed = 1; seed <= 40; seed++)
            if (OverflowAfterTerritoryLoss(seed)) return;
        Assert.Fail("no seed fits the layout");
    }

    private static bool OverflowAfterTerritoryLoss(ulong seed)
    {
        var sim = Simulation.Create(TwoPlayers(seed));
        var castle = sim.Buildings.All[CastleIndex(sim)];
        int edge = sim.Map.Edge;
        int Sector(int x, int y, int cx, int cy) => Logistics.SectorDistance(edge, y * edge + x, cy * edge + cx);
        bool Within(int x, int y, int side, int r2, bool inside)
        {
            for (int dy = -1; dy <= side; dy++)
                for (int dx = -1; dx <= side; dx++)
                {
                    int ex = x + dx - castle.CenterX, ey = y + dy - castle.CenterY;
                    if ((ex * ex + ey * ey <= r2) != inside) return false;
                }
            return true;
        }
        // A tower extends the territory; a storehouse only inside the tower's claim; a woodcutter inside the castle's
        // claim but nearer (by sector) to the storehouse. Demolishing the tower cuts the storehouse off.
        int tower = TryPlace(sim, 0, BuildingIds.GuardTowerSmall, 0, (x, y) => Within(x, y, 2, 12 * 12, inside: false));
        if (tower < 0) return false;
        ConstructionTests.RunUntilComplete(sim, tower);
        int store = TryPlace(sim, 0, BuildingIds.Storehouse, 1, (x, y) => Within(x, y, 3, 17 * 17, inside: false));
        if (store < 0) return false;
        var house = sim.Buildings.All.Last();
        int cutter = TryPlace(sim, 0, BuildingIds.Woodcutter, 2, (x, y) => Within(x, y, 2, 15 * 15, inside: true)
            && Sector(x + 1, y + 1, house.CenterX, house.CenterY) < Sector(x + 1, y + 1, castle.CenterX, castle.CenterY));
        if (cutter < 0) return false;
        ConstructionTests.RunUntilComplete(sim, store);
        ConstructionTests.RunUntilComplete(sim, cutter);
        while (sim.Logistics.All.Any(j => j.Kind == JobKind.Transport)) Run(sim); // the builders' hammers are back
        Run(sim, BuildingCommands.Demolish(0, 3, tower));
        Assert.Equal(0, sim.RejectedCommands);
        Assert.NotEqual(0, sim.Territory.OwnerAt(house.CenterX, house.CenterY));
        int logs = Units(sim, 0, GoodIds.Log);
        sim.Buildings.PilesAt(sim.Buildings.IndexOf(cutter))![0] += 3;
        Run(sim);
        Assert.Equal(3, sim.Logistics.All.Count(j => j.SourceId == cutter && j.DestinationId == store));
        for (int turn = 0; sim.Logistics.All.Count > 0; turn++)
        {
            Assert.True(turn < 500, "jobs never finished");
            Run(sim);
        }
        Assert.True(sim.Logistics.IsUnreachable(store));
        Assert.Equal(logs + 3, Units(sim, 0, GoodIds.Log)); // nothing lost: the units went to the castle
        Assert.Equal(0, sim.Buildings.StockOf(store)![GoodIds.Log]);
        return true;
    }

    /// <summary>Food goods; mines no longer eat any (user, 2026-10-03).</summary>
    private static readonly int[] Food = { GoodIds.Fish, GoodIds.Meat, GoodIds.Bread };

    /// <summary>Units of a good slot 0 consumed so far; in these tests only the castle eats them (<see cref="Households"/>).</summary>
    private static int Eaten(Simulation sim, int good) => (int)sim.Statistics.TotalConsumed(0, good);

    /// <summary>Places and completes a mine with its deposit in reach on the first seed whose start area allows it.</summary>
    private static (Simulation Sim, int Id) Mine(ushort type)
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var sim = Simulation.Create(TwoPlayers(seed));
            int id = TryPlace(sim, 0, type, 0, (x, y) => ObjectsAround(sim, 0, type, x, y) >= 4);
            if (id < 0) continue;
            ConstructionTests.RunUntilComplete(sim, id);
            return (sim, id);
        }
        throw new Xunit.Sdk.XunitException($"No seed fits a {BuildingCatalog.All[type].Id}");
    }

    [Fact]
    public void Mines_are_compiled_from_data()
    {
        foreach (var (type, harvest, output) in new[]
        {
            (BuildingIds.CoalMine, HarvestSource.Coal, GoodIds.Coal),
            (BuildingIds.IronMine, HarvestSource.IronOre, GoodIds.IronOre),
            (BuildingIds.GoldMine, HarvestSource.GoldOre, GoodIds.GoldOre),
        })
        {
            var def = BuildingCatalog.All[type];
            var p = def.Production!;
            Assert.Equal(BuildingTerrain.Mountain, def.Terrain);
            Assert.Equal((harvest, 3, (ushort)output), (p.Harvest, p.Radius, p.Output));
            Assert.Empty(p.Inputs); // no work ration (user, 2026-10-03)
            Assert.Equal((ushort)GoodIds.Pickaxe, p.Tool);
            Assert.True(Harvest.IsConsumed(harvest) && Harvest.IsResource(harvest));
        }
        Assert.Equal(new[] { (ushort)GoodIds.Log }, BuildingCatalog.All[BuildingIds.Sawmill].Production!.Alternatives.Single());
        Assert.False(Harvest.IsResource(HarvestSource.Stone) || Harvest.IsResource(HarvestSource.Water));
    }

    [Theory]
    [InlineData(BuildingIds.CoalMine, GoodIds.Coal, Resource.Coal)]
    [InlineData(BuildingIds.IronMine, GoodIds.IronOre, Resource.Iron)]
    public void A_mine_digs_its_deposit_without_food(ushort type, int output, Resource ore)
    {
        var (sim, id) = Mine(type);
        int food = Food.Sum(g => Units(sim, 0, g) + Eaten(sim, g));
        var b = ConstructionTests.Get(sim, id);
        var p = b.Definition.Production!;
        int nearest = Production.FindHarvest(sim.Map, sim.Territory, b, p);
        Assert.Equal(((byte)ore, (byte)Terrain.Mountain), (sim.Map.Resource[nearest], sim.Map.Terrain[nearest]));
        int amount = sim.Map.Amount[nearest];
        Assert.True(amount >= 2);
        int before = Units(sim, 0, output);
        for (int turn = 0; turn < 4 * p.CycleTicks / Simulation.TicksPerTurn; turn++)
        {
            Run(sim);
            Assert.DoesNotContain(sim.Logistics.All, j => j.DestinationId == id && j.Kind == JobKind.Transport);
        }
        // Every dug unit came from the deposit, nearest tile first; no food was touched.
        int dug = Units(sim, 0, output) - before;
        Assert.InRange(dug, 2, 4);
        Assert.Equal(food, Food.Sum(g => Units(sim, 0, g) + Eaten(sim, g)));
        Assert.Equal(System.Math.Max(0, amount - dug), sim.Map.Amount[nearest]);
        Assert.Contains(nearest, sim.MapChanges.Tiles);
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.Map.Resource, loaded.Map.Resource);
        Assert.Equal(sim.Map.Amount, loaded.Map.Amount);
    }

    [Fact]
    public void A_mine_stops_when_its_deposit_is_exhausted()
    {
        var (sim, id) = Mine(BuildingIds.CoalMine);
        var b = ConstructionTests.Get(sim, id);
        var p = b.Definition.Production!;
        // Shrink the deposit in reach to one unit per tile (map edits do not survive a load; this test never loads).
        var tiles = new System.Collections.Generic.List<int>();
        for (int t = 0; t < sim.Map.TileCount; t++)
        {
            int dx = t % sim.Map.Edge - b.CenterX, dy = t / sim.Map.Edge - b.CenterY;
            if (dx * dx + dy * dy <= p.Radius * p.Radius && sim.Territory.OwnerAt(t) == 0 && Harvest.Matches(sim.Map, t, p.Harvest))
            {
                sim.Map.Amount[t] = 1;
                tiles.Add(t);
            }
        }
        Assert.InRange(tiles.Count, 4, 30);
        int before = Units(sim, 0, GoodIds.Coal);
        RunTicks(sim, (tiles.Count + 3) * p.CycleTicks);
        Assert.Equal(tiles.Count, Units(sim, 0, GoodIds.Coal) - before);
        Assert.All(tiles, t => Assert.Equal(((byte)Resource.None, (byte)0), (sim.Map.Resource[t], sim.Map.Amount[t])));
        Assert.Equal(-1, Production.FindHarvest(sim.Map, sim.Territory, b, p));
        Assert.Equal(0, ConstructionTests.Get(sim, id).Cycle); // idle: no cycle starts without ore
    }

    [Fact]
    public void Corrupt_ore_changes_are_rejected_on_load()
    {
        var (sim, _) = Mine(BuildingIds.CoalMine);
        while (sim.MapChanges.Tiles.Count == 0) Run(sim); // the first cycle starts once the miner is inside
        Assert.Single(sim.MapChanges.Tiles);
        var w = new CanonicalWriter(64);
        sim.MapChanges.WriteTo(w, sim.Map);
        var changes = w.ToArray();
        MapData Fresh() => MapGenerator.Generate(sim.Setup.Map).Map!;
        MapChanges.ReadFrom(new CanonicalReader(changes), Fresh());
        var cases = new System.Action<byte[]>[]
        {
            b => b[11] = 0,                                  // coal left but amount 0
            b => b[11] = 200,                                // more coal than generated
            b => b[10] = (byte)Resource.Gold,                 // coal turned into gold
            b => b[9] = (byte)MapObject.Stone,               // stone appeared on the deposit
            b => { b[10] = (byte)Resource.None; b[11] = 1; }, // coal gone but an amount left
        };
        foreach (var corrupt in cases)
        {
            var bad = (byte[])changes.Clone();
            corrupt(bad);
            Assert.Throws<InvalidDataException>(() => MapChanges.ReadFrom(new CanonicalReader(bad), Fresh()));
        }
    }

    /// <summary>Free tiles the forester could plant on from a footprint at (x, y) (<see cref="Production.FindPlantSite"/> rules).</summary>
    private static int PlantSites(Simulation sim, int x, int y)
    {
        var p = BuildingCatalog.All[BuildingIds.Forester].Production!;
        var b = new Building(1, BuildingIds.Forester, 0, x, y, 0, BuildingState.Complete, 0);
        return Production.FindPlantSite(sim.Map, sim.Territory, sim.Buildings, b, p) < 0 ? 0 : 1;
    }

    private static int Forester(Simulation sim, ushort seq = 0)
    {
        int id = Place(sim, 0, BuildingIds.Forester, seq, (x, y) => PlantSites(sim, x, y) > 0);
        ConstructionTests.RunUntilComplete(sim, id);
        return id;
    }

    /// <summary>Planted trees: changed tiles that now hold a tree.</summary>
    private static List<int> Planted(Simulation sim) =>
        sim.MapChanges.Tiles.Where(t => sim.Map.Object[t] == (byte)MapObject.Tree).ToList();

    [Fact]
    public void The_forester_is_compiled_from_data()
    {
        var p = BuildingCatalog.All[BuildingIds.Forester].Production!;
        Assert.Equal((MapObject.Tree, HarvestSource.None, 6, ProductionDefinition.NoOutput, 0),
            (p.Plant, p.Harvest, p.Radius, p.Output, p.Inputs.Count));
        Assert.All(BuildingCatalog.All.Where(d => d.Production != null && d.Id != "forester"),
            d => Assert.Equal(MapObject.None, d.Production!.Plant));
        Assert.Throws<System.ArgumentException>(() =>
            new ProductionDefinition(System.Array.Empty<ushort[]>(), System.Array.Empty<int>(), GoodIds.Log, 10, HarvestSource.None, 6, MapObject.Tree));
        Assert.Throws<System.ArgumentException>(() =>
            new ProductionDefinition(System.Array.Empty<ushort[]>(), System.Array.Empty<int>(), ProductionDefinition.NoOutput, 10, HarvestSource.None, 0));
    }

    [Fact]
    public void A_forester_plants_spaced_trees_in_reach()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Forester(sim);
        var forester = ConstructionTests.Get(sim, id);
        var p = forester.Definition.Production!;
        Assert.Empty(sim.MapChanges.Tiles);
        RunTicks(sim, 10 * p.CycleTicks);
        var planted = Planted(sim);
        Assert.InRange(planted.Count, 6, 10); // at most one tree per cycle; walking to the site and back costs time
        Assert.Equal(planted.Count, sim.MapChanges.Tiles.Count);
        int edge = sim.Map.Edge;
        foreach (int t in planted)
        {
            int x = t % edge, y = t / edge, dx = x - forester.CenterX, dy = y - forester.CenterY;
            Assert.True(dx * dx + dy * dy <= p.Radius * p.Radius);
            Assert.Equal(0, sim.Territory.OwnerAt(t));
            Assert.False(sim.Map.IsBuildable(t)); // a tree blocks building
            Assert.True(sim.Map.IsWalkable(t));   // but not walking
            for (int ny = y - 1; ny <= y + 1; ny++)
                for (int nx = x - 1; nx <= x + 1; nx++)
                {
                    int n = ny * edge + nx;
                    Assert.Equal(0, sim.Buildings.AtTile(n));
                    if (n != t) Assert.NotEqual((byte)MapObject.Tree, sim.Map.Object[n]);
                }
        }
        Assert.Equal(new[] { 0 }, sim.Buildings.PilesOf(id)); // no output pile to carry away
        Assert.DoesNotContain(sim.Logistics.All, j => j.SourceId == id);
        // Planted trees survive save and load and the runs stay identical.
        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
        Assert.Equal(sim.Map.Object, loaded.Map.Object);
        Assert.Equal(sim.Map.Flags, loaded.Map.Flags);
        RunTicks(sim, 400);
        RunTicks(loaded, 400);
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
    }

    [Fact]
    public void A_forester_idles_once_no_free_tile_is_left()
    {
        var sim = Simulation.Create(TwoPlayers(2));
        int id = Forester(sim);
        var forester = ConstructionTests.Get(sim, id);
        int cycle = forester.Definition.Production!.CycleTicks;
        for (int i = 0; i < 200 && PlantSites(sim, forester.X, forester.Y) > 0; i++) RunTicks(sim, cycle);
        Assert.Equal(0, PlantSites(sim, forester.X, forester.Y));
        RunTicks(sim, cycle); // a cycle that ran when the last site went yields nothing and none starts
        int trees = Planted(sim).Count;
        Assert.True(trees > 5);
        RunTicks(sim, 4 * cycle);
        Assert.Equal(trees, Planted(sim).Count);
        Assert.Equal(0, ConstructionTests.Get(sim, id).Cycle);
    }

    [Fact]
    public void A_woodcutter_fells_what_a_forester_plants()
    {
        var sim = Simulation.Create(TwoPlayers());
        int forester = Forester(sim);
        var f = ConstructionTests.Get(sim, forester);
        int cycle = f.Definition.Production!.CycleTicks;
        RunTicks(sim, 8 * cycle);
        var planted = Planted(sim);
        Assert.NotEmpty(planted);
        // A woodcutter whose reach holds planted trees only.
        int edge = sim.Map.Edge;
        int cutter = Place(sim, 0, BuildingIds.Woodcutter, 1, (x, y) =>
        {
            int trees = ObjectsAround(sim, 0, BuildingIds.Woodcutter, x, y);
            var b = new Building(1, BuildingIds.Woodcutter, 0, x, y, 0, BuildingState.Complete, 0);
            int r = b.Definition.Production!.Radius;
            int mine = planted.Count(t => (t % edge - b.CenterX) * (t % edge - b.CenterX) + (t / edge - b.CenterY) * (t / edge - b.CenterY) <= r * r);
            return trees > 0 && trees == mine;
        });
        ConstructionTests.RunUntilComplete(sim, cutter);
        int before = Units(sim, 0, GoodIds.Log);
        RunTicks(sim, 6 * BuildingCatalog.All[BuildingIds.Woodcutter].Production!.CycleTicks);
        Assert.True(Units(sim, 0, GoodIds.Log) > before);
        Assert.Contains(sim.MapChanges.Tiles, t => sim.Map.Object[t] == (byte)MapObject.None); // a planted tree was felled
        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
    }

    [Fact]
    public void Corrupt_planted_trees_are_rejected_on_load()
    {
        var sim = Simulation.Create(TwoPlayers());
        Forester(sim);
        while (sim.MapChanges.Tiles.Count == 0) Run(sim);
        Assert.Single(sim.MapChanges.Tiles);
        int tile = sim.MapChanges.Tiles[0];
        var w = new CanonicalWriter(64);
        sim.MapChanges.WriteTo(w, sim.Map);
        var changes = w.ToArray();
        MapData Fresh() => MapGenerator.Generate(sim.Setup.Map).Map!;
        var map = Fresh();
        Assert.Equal((byte)MapObject.None, map.Object[tile]);
        MapChanges.ReadFrom(new CanonicalReader(changes), map);
        Assert.Equal((byte)MapObject.Tree, map.Object[tile]);
        Assert.False(map.IsBuildable(tile));
        int Find(System.Func<MapData, int, bool> test) => Enumerable.Range(0, map.TileCount).First(t => test(map, t));
        int water = Find((m, t) => m.Terrain[t] == (byte)Terrain.Water && m.Resource[t] == (byte)Resource.None);
        int mountain = Find((m, t) => m.Terrain[t] == (byte)Terrain.Mountain && m.Object[t] == (byte)MapObject.None && m.Resource[t] == (byte)Resource.None);
        int steep = Find((m, t) => m.Terrain[t] == (byte)Terrain.Plains && m.Object[t] == (byte)MapObject.None
            && m.Resource[t] == (byte)Resource.None && !m.IsBuildable(t));
        int deposit = Find((m, t) => m.Terrain[t] == (byte)Terrain.Mountain && m.Resource[t] != (byte)Resource.None);
        var cases = new System.Action<byte[]>[]
        {
            b => b[11] = 1, // planted tree with an amount
            b => b[10] = (byte)Resource.Fish, // resource appeared with it
            b => b[9] = (byte)MapObject.Game, // game appeared instead
            b => { System.BitConverter.GetBytes(water).CopyTo(b, 4); b[8] = map.Height[water]; }, // tree in the water
            b => { System.BitConverter.GetBytes(mountain).CopyTo(b, 4); b[8] = map.Height[mountain]; }, // tree on a mountain
            b => { System.BitConverter.GetBytes(deposit).CopyTo(b, 4); b[8] = map.Height[deposit]; }, // tree on an ore deposit
            b => { System.BitConverter.GetBytes(steep).CopyTo(b, 4); b[8] = map.Height[steep]; }, // tree on a slope too steep to plant on
        };
        foreach (var corrupt in cases)
        {
            var bad = (byte[])changes.Clone();
            corrupt(bad);
            Assert.Throws<InvalidDataException>(() => MapChanges.ReadFrom(new CanonicalReader(bad), Fresh()));
        }

        // A forester's output pile holds nothing: the forester is the last building, its output pile the last byte.
        var bw = new CanonicalWriter(1024);
        sim.Buildings.WriteTo(bw);
        var buildings = bw.ToArray();
        Assert.Equal(BuildingIds.Forester, sim.Buildings.All[^1].Type);
        BuildingRegistry.ReadFrom(new CanonicalReader(buildings), sim.Map.Edge, sim.Players.Count, sim.Territory);
        buildings[^1] = 1;
        Assert.Throws<InvalidDataException>(() =>
            BuildingRegistry.ReadFrom(new CanonicalReader(buildings), sim.Map.Edge, sim.Players.Count, sim.Territory));
    }
}
