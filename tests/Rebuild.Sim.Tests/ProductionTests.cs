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

    /// <summary>Harvest objects within the building type's radius around a footprint at (x, y) on the slot's territory.</summary>
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
                if (dx * dx + dy * dy <= r * r && sim.Map.Object[t] == (byte)def.Production.Harvest && sim.Territory.OwnerAt(t) == slot) n++;
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
        Assert.Equal((MapObject.Tree, 8, (ushort)GoodIds.Log, 0), (wood.Harvest, wood.Radius, wood.Output, wood.Inputs.Count));
        var saw = BuildingCatalog.All[BuildingIds.Sawmill].Production!;
        Assert.Equal((MapObject.None, (ushort)GoodIds.Plank), (saw.Harvest, saw.Output));
        Assert.Equal(new[] { (ushort)GoodIds.Log }, saw.Inputs);
        Assert.Equal(new[] { 1 }, saw.InputAmounts);
        Assert.Equal(0, saw.InputIndexOf(GoodIds.Log));
        Assert.Equal(-1, saw.InputIndexOf(GoodIds.Plank));
        Assert.Equal(MapObject.Stone, BuildingCatalog.All[BuildingIds.Stonecutter].Production!.Harvest);
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

        // Map changes: count(4) then per tile tile(4) object(1) amount(1).
        var changes = Encode(w => sim.MapChanges.WriteTo(w, sim.Map));
        MapData Fresh() => MapGenerator.Generate(sim.Setup.Map).Map!;
        MapChanges.ReadFrom(new CanonicalReader(changes), Fresh());
        int plain = Enumerable.Range(0, sim.Map.TileCount).First(t => sim.Map.Object[t] == (byte)MapObject.None && !sim.MapChanges.Tiles.Contains(t));
        var cases = new System.Action<byte[]>[]
        {
            b => System.BitConverter.GetBytes(plain).CopyTo(b, 4),          // tile that never held a tree or stone
            b => b[8] = (byte)MapObject.Tree,                                   // tree still standing
            b => b[8] = (byte)MapObject.Lair,                                   // tree turned into something else
            b => b[9] = 3,                                                      // removed object with an amount
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
