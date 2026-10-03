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

    /// <summary>Tiles offering the harvest source within the building type's radius around a footprint at (x, y) on the slot's territory.</summary>
    private static int ObjectsAround(Simulation sim, byte slot, int type, int x, int y)
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
    private static int Woodcutter(Simulation sim, int trees, ushort seq = 0)
    {
        int id = Place(sim, 0, BuildingIds.Woodcutter, seq, (x, y) => ObjectsAround(sim, 0, BuildingIds.Woodcutter, x, y) >= trees);
        ConstructionTests.RunUntilComplete(sim, id);
        return id;
    }

    private static int CastleIndex(Simulation sim) => sim.Buildings.All.ToList().FindIndex(b => b.Owner == 0 && b.Type == BuildingIds.Castle);

    /// <summary>Units of a good the slot holds: storage stocks, production piles and transport jobs.</summary>
    private static int Units(Simulation sim, byte slot, int good)
    {
        int n = 0;
        foreach (var b in sim.Buildings.All.Where(b => b.Owner == slot))
        {
            n += sim.Buildings.StockOf(b.Id)?[good] ?? 0;
            var piles = sim.Buildings.PilesOf(b.Id);
            if (piles == null) continue;
            var p = b.Definition.Production!;
            for (int k = 0; k < p.Inputs.Count; k++)
                if (p.Inputs[k] == good) n += piles[k];
            if (p.Output == good) n += piles[^1];
        }
        return n + sim.Logistics.All.Count(j => j.Owner == slot && j.Good == good);
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
        Assert.InRange(felled, 9, 10); // one cycle per CycleTicks; the first starts with the first tick
        Assert.Equal(felled, Units(sim, 0, GoodIds.Log));
        Assert.Equal(felled, sim.MapChanges.Tiles.Count);
        Assert.All(sim.MapChanges.Tiles, t => Assert.Equal((byte)MapObject.None, sim.Map.Object[t]));
        Assert.InRange(sim.Buildings.StockAt(castle)![GoodIds.Log], felled - 2, felled); // overflow carried to the castle
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
        RunTicks(sim, cycle * amount + 2);
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
        RunTicks(sim, cycle + 2);
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

        // Map changes: count(4) then per tile tile(4) object(1) resource(1) amount(1).
        var changes = Encode(w => sim.MapChanges.WriteTo(w, sim.Map));
        MapData Fresh() => MapGenerator.Generate(sim.Setup.Map).Map!;
        MapChanges.ReadFrom(new CanonicalReader(changes), Fresh());
        int plain = Enumerable.Range(0, sim.Map.TileCount).First(t => sim.Map.Object[t] == (byte)MapObject.None && !sim.MapChanges.Tiles.Contains(t));
        var cases = new System.Action<byte[]>[]
        {
            b => System.BitConverter.GetBytes(plain).CopyTo(b, 4),          // tile that never held a tree or stone
            b => b[8] = (byte)MapObject.Tree,                                   // tree still standing
            b => b[8] = (byte)MapObject.Lair,                                   // tree turned into something else
            b => b[10] = 3,                                                     // removed object with an amount
            b => b[9] = (byte)Resource.Fish,                                    // resource appeared
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
        int fish = Units(sim, 0, GoodIds.Fish);
        int cycle = b.Definition.Production!.CycleTicks;
        RunTicks(sim, cycle + 2);
        Assert.Equal((byte)Resource.Fish, sim.Map.Resource[nearest]);
        Assert.Equal(amount - 1, sim.Map.Amount[nearest]);
        Assert.Contains(nearest, sim.MapChanges.Tiles);
        // A partly fished tile survives save and load.
        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
        Assert.Equal(sim.Map.Resource, loaded.Map.Resource);
        Assert.Equal(sim.Map.Amount, loaded.Map.Amount);
        RunTicks(sim, cycle * (amount - 1));
        Assert.Equal(((byte)Resource.None, (byte)0), (sim.Map.Resource[nearest], sim.Map.Amount[nearest]));
        Assert.Equal(fish + amount, Units(sim, 0, GoodIds.Fish));
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
        int meat = Units(sim, 0, GoodIds.Meat);
        RunTicks(sim, 2 * b.Definition.Production!.CycleTicks + 2);
        Assert.Equal(game - 2, ObjectsAround(sim, 0, BuildingIds.Hunter, b.X, b.Y));
        Assert.Equal(meat + 2, Units(sim, 0, GoodIds.Meat));
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
        int bread = Units(sim, 0, GoodIds.Bread);
        int grainCycle = ConstructionTests.Get(sim, farm).Definition.Production!.CycleTicks;
        RunTicks(sim, 8 * grainCycle);
        int made = Units(sim, 0, GoodIds.Bread) - bread;
        Assert.InRange(made, 4, 8); // at most one loaf per grain; the chain needs a few cycles to fill
        Assert.Empty(sim.MapChanges.Tiles); // fertile land and water are not used up
        Assert.True(Units(sim, 0, GoodIds.Water) > 0);
        Assert.InRange(sim.Buildings.PilesOf(bakery)![1] + sim.Logistics.All.Count(j => j.DestinationId == bakery && j.Good == GoodIds.Water),
            0, Production.InputTarget);
        Assert.True(sim.Buildings.TryGet(water, out _) && sim.Buildings.TryGet(mill, out _));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Theory]
    [InlineData(BuildingIds.PigFarm, GoodIds.Grain, GoodIds.Water, GoodIds.Pig)]
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
        Assert.Equal(10 - 6, Units(sim, 0, a));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Corrupt_fish_changes_are_rejected_on_load()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Place(sim, 0, BuildingIds.Fisher, 0, (x, y) => ObjectsAround(sim, 0, BuildingIds.Fisher, x, y) >= 1);
        ConstructionTests.RunUntilComplete(sim, id);
        RunTicks(sim, ConstructionTests.Get(sim, id).Definition.Production!.CycleTicks + 2);
        Assert.Single(sim.MapChanges.Tiles);
        var w = new CanonicalWriter(64);
        sim.MapChanges.WriteTo(w, sim.Map);
        var changes = w.ToArray();
        MapData Fresh() => MapGenerator.Generate(sim.Setup.Map).Map!;
        MapChanges.ReadFrom(new CanonicalReader(changes), Fresh());
        var cases = new System.Action<byte[]>[]
        {
            b => b[10] = 0,                                // fish left but amount 0
            b => b[10] = 200,                              // more fish than generated
            b => b[9] = (byte)Resource.Coal,               // fish turned into coal
            b => b[8] = (byte)MapObject.Tree,              // tree appeared on water
            b => { b[9] = (byte)Resource.None; b[10] = 1; }, // fish gone but an amount left
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
}
