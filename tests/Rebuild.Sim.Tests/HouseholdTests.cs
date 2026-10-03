using System.IO;
using System.Linq;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Goods;
using Rebuild.Sim.Match;
using Rebuild.Sim.World;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>Food, water and heat of homes, pantries and shortage states (docs/12-needs-seasons-weather.md §1.2–1.3, §2.2).</summary>
public class HouseholdTests
{
    private static MatchSetup TwoPlayers(SeasonLength seasons = SeasonLength.Normal) => new(
        new MapSpec(1, MapSize.Small, 2), 1,
        new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Ai, 1, "rivermen") }, seasons);

    private static void RunTicks(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks / Simulation.TicksPerTurn; i++) ConstructionTests.Run(sim);
    }

    private static int CastleCarriers(Simulation sim) =>
        sim.Settlers.All.Count(s => s.Kind == SettlerKind.Carrier && s.HomeId == sim.Buildings.All[0].Id);

    /// <summary>Places a building of player 0 on the first free spot near its castle and runs until it is complete.</summary>
    private static int Build(Simulation sim, ushort type)
    {
        var s0 = sim.StartOf(0)!.Value;
        (int X, int Y) spot = (-1, -1);
        for (int y = s0.Y - 14; y <= s0.Y + 14 && spot.X < 0; y++)
            for (int x = s0.X - 14; x <= s0.X + 14 && spot.X < 0; x++)
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, 0, type, x, y) == PlacementResult.Ok)
                    spot = (x, y);
        ConstructionTests.Run(sim, BuildingCommands.Place(0, 0, type, spot.X, spot.Y));
        int id = sim.Buildings.All.Last().Id;
        ConstructionTests.RunUntilComplete(sim, id);
        return id;
    }

    [Fact]
    public void The_castle_fetches_food_and_water_from_a_storehouse()
    {
        var sim = Simulation.Create(TwoPlayers());
        int storehouse = Build(sim, BuildingIds.Storehouse);
        var castle = sim.Buildings.StockAt(0)!;
        var store = sim.Buildings.StockAt(sim.Buildings.IndexOf(storehouse))!;
        // All food and water is in the storehouse: the castle fetches up to its target of each need (30 beds / 4 = 7).
        Assert.Equal(7, Households.StockTarget(sim.Buildings.All[0]));
        foreach (ushort g in Households.FoodGoods.Concat(Households.WaterGoods))
        {
            store[g] += castle[g] + 20;
            castle[g] = 0;
        }
        RunTicks(sim, 600);
        Assert.Equal(NeedState.Supplied, Households.StateAt(sim.Buildings, 0));
        int food = Households.FoodGoods.Sum(g => castle[g]);
        Assert.InRange(food, 1, 7);
        Assert.InRange(castle[GoodIds.Water], 1, 7);
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        // It stays supplied while the storehouse has some: 3 600 ticks eat 18 food and 22 water.
        RunTicks(sim, 3600);
        Assert.Equal(NeedState.Supplied, Households.StateAt(sim.Buildings, 0));
    }

    [Fact]
    public void The_castle_eats_and_drinks_from_its_stock()
    {
        var sim = Simulation.Create(TwoPlayers());
        var stock = sim.Buildings.StockAt(0)!;
        Assert.Equal((10, 10, 10, 30), (stock[GoodIds.Fish], stock[GoodIds.Meat], stock[GoodIds.Bread], stock[GoodIds.Water]));
        // 30 carriers: one water per 4 800 / 30 = 160 ticks, one food per 6 000 / 30 = 200 ticks.
        RunTicks(sim, 200);
        Assert.Equal((9, 10, 10, 29), (stock[GoodIds.Fish], stock[GoodIds.Meat], stock[GoodIds.Bread], stock[GoodIds.Water]));
        Assert.Equal(new[] { 0, 30 * 200 - Households.WaterTicks, 0, 0, 0, 0 }, sim.Buildings.NeedsOf(sim.Buildings.All[0].Id));
        // The food good held most is eaten next (ties: data order): meat, then bread, then fish again.
        RunTicks(sim, 400);
        Assert.Equal((9, 9, 9), (stock[GoodIds.Fish], stock[GoodIds.Meat], stock[GoodIds.Bread]));
        Assert.Equal(1, sim.Statistics.TotalConsumed(0, GoodIds.Fish));
        Assert.Equal(3, sim.Statistics.TotalConsumed(0, GoodIds.Water)); // 600 ticks: 3 × 160 + 120
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Without_water_a_home_goes_short_then_into_crisis_and_recovers_when_water_arrives()
    {
        var sim = Simulation.Create(TwoPlayers());
        var stock = sim.Buildings.StockAt(0)!;
        stock[GoodIds.Water] = 0;
        // The first water is due at tick 160 and stays unpaid.
        RunTicks(sim, 160 + Households.ShortTicks[1]);
        Assert.Equal(NeedState.Short, Households.StateAt(sim.Buildings, 0));
        Assert.Equal(NeedState.Supplied, Households.StateAt(sim.Buildings, 1)); // the other castle still has water
        RunTicks(sim, Households.CrisisTicks[1] - 2);
        Assert.Equal((NeedState.Short, 30), (Households.StateAt(sim.Buildings, 0), CastleCarriers(sim)));
        RunTicks(sim, 2); // enters Crisis: the first occupant leaves at once, the next one 60 s later
        Assert.Equal((NeedState.Crisis, 29), (Households.StateAt(sim.Buildings, 0), CastleCarriers(sim)));
        int lastId = sim.Settlers.All.Where(s => s.Owner == 0 && s.JobId == 0).Max(s => s.Id);
        RunTicks(sim, Households.LeaveIntervalTicks);
        Assert.Equal(28, CastleCarriers(sim));
        Assert.DoesNotContain(sim.Settlers.All, s => s.Id == lastId); // the highest id without a job leaves
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        // No carrier spawned while short (spawn ticks 600 … 2 400 passed); one water ends the shortage at once and refilling resumes.
        RunTicks(sim, Settlers.SpawnIntervalTicks - 2);
        Assert.Equal(28, CastleCarriers(sim));
        stock[GoodIds.Water] = 50;
        RunTicks(sim, 2);
        Assert.Equal(NeedState.Supplied, Households.StateAt(sim.Buildings, 0));
        Assert.Equal(49, stock[GoodIds.Water]);
        RunTicks(sim, Settlers.SpawnIntervalTicks);
        Assert.Equal(29, CastleCarriers(sim));
    }

    [Fact]
    public void A_short_home_walks_and_works_slower_and_its_workers_stop_in_crisis()
    {
        var sim = Simulation.Create(TwoPlayers());
        sim.Buildings.StockAt(0)![GoodIds.Water] += 100;
        int cutter = ProductionTests.Woodcutter(sim, trees: 14);
        Assert.Equal(sim.Buildings.All[0].Id, sim.Settlers.All.Single(s => s.WorkplaceId == cutter).HomeId); // worker lives in the castle
        Assert.Null(sim.Buildings.NeedsOf(cutter)); // production buildings need no heating (user 2026-10-03)
        for (int turns = 0; ConstructionTests.Get(sim, cutter).Cycle is 0 or > 10; turns++)
        {
            Assert.True(turns < 200, "woodcutter never worked");
            ConstructionTests.Run(sim);
        }
        int Advance(int ticks)
        {
            int before = ConstructionTests.Get(sim, cutter).Cycle;
            RunTicks(sim, ticks);
            return ConstructionTests.Get(sim, cutter).Cycle - before;
        }
        Assert.Equal(40, Advance(40));
        // Water runs out and has been unpaid long enough: Short (water stays due while unpaid, so the state holds).
        var needs = sim.Buildings.NeedsAt(0)!;
        sim.Buildings.StockAt(0)![GoodIds.Water] = 0;
        needs[1] = Households.WaterTicks;
        needs[4] = Households.ShortTicks[1];
        Assert.Equal(30, Advance(40)); // work −25 %: every 4th tick skipped
        Assert.Equal(NeedState.Short, Households.StateAt(sim.Buildings, 0));
        // Its carriers walk −15 %.
        var walker = sim.Settlers.All.First(s => s.Owner == 0 && s.State == SettlerState.Walking && s.Progress + 2 * Settlers.Speed < Settlers.SubTile);
        ConstructionTests.Run(sim);
        Assert.Equal(walker.Progress + 2 * Settlers.ShortSpeed, sim.Settlers.All[sim.Settlers.IndexOf(walker.Id)].Progress);
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        // Crisis: the running cycle stops.
        needs[4] = Households.ShortTicks[1] + Households.CrisisTicks[1] + 1;
        Assert.Equal(0, Advance(40));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void A_residence_pantry_is_filled_by_carriers_and_eaten_from()
    {
        var sim = Simulation.Create(TwoPlayers());
        sim.Buildings.StockAt(0)![GoodIds.Water] += 100; // enough for the castle for the whole test
        int residence = Build(sim, BuildingIds.Residence);
        Assert.Equal(new[] { 0, 0, 0 }, sim.Buildings.PilesOf(residence));
        Assert.Equal(new int[Households.CounterCount], sim.Buildings.NeedsOf(residence));
        long eaten = sim.Statistics.TotalConsumed(0, GoodIds.Water);
        RunTicks(sim, 300);
        // Food and water piles are full (fuel waits for autumn); water handed over counts as consumed.
        Assert.Equal(new[] { Households.PantryTarget, Households.PantryTarget, 0 }, sim.Buildings.PilesOf(residence));
        Assert.True(sim.Statistics.TotalConsumed(0, GoodIds.Water) >= eaten + Households.PantryTarget);
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        // Its first carrier (tick 600 + spawns) eats; the pantry is refilled.
        RunTicks(sim, Settlers.SpawnIntervalTicks + Households.WaterTicks);
        Assert.True(sim.Settlers.All.Count(s => s.HomeId == residence) >= 2);
        Assert.Equal(NeedState.Supplied, Households.StateAt(sim.Buildings, sim.Buildings.IndexOf(residence)));
        Assert.True(sim.Buildings.NeedsOf(residence)![1] < Households.WaterTicks);
        var piles = sim.Buildings.PilesOf(residence)!;
        Assert.All(piles, n => Assert.InRange(n, 0, Households.PantryTarget));
        sim.Buildings.PilesAt(sim.Buildings.IndexOf(residence))![0] = Households.PantryTarget + 1;
        Assert.Throws<InvalidDataException>(() => Simulation.Load(sim.Save()));
    }

    [Fact]
    public void Inconsistent_need_counters_and_pantries_fail_to_load()
    {
        var sim = Simulation.Create(TwoPlayers());
        RunTicks(sim, 20);
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        var needs = sim.Buildings.NeedsAt(0)!;
        foreach (var bad in new[]
                 {
                     new[] { -1, 0, 0, 0, 0, 0 },
                     new[] { Households.FoodTicks + 1, 0, 0, 0, 0, 0 },
                     new[] { 0, 5, 0, 0, 1, 0 }, // unpaid water while none is due
                     new[] { 0, Households.WaterTicks, 0, 0, -1, 0 },
                     new[] { 0, 0, 601, 0, 0, 0 }, // castle (L): one fuel per 600 ticks
                     new[] { 0, 0, 5, 0, 0, 1 }, // cold while no fuel is due
                 })
        {
            var saved = needs.ToArray();
            bad.CopyTo(needs, 0);
            Assert.Throws<InvalidDataException>(() => Simulation.Load(sim.Save()));
            saved.CopyTo(needs, 0);
        }
        new[] { 0, Households.WaterTicks, 0, 0, 7, 0 }.CopyTo(needs, 0); // water due and unpaid for 7 ticks
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void The_castle_burns_fuel_in_winter_gets_cold_without_it_and_warms_up_in_spring()
    {
        var sim = Simulation.Create(TwoPlayers(SeasonLength.Short)); // winter = ticks 7 200 … 9 599
        var stock = sim.Buildings.StockAt(0)!;
        var needs = sim.Buildings.NeedsAt(0)!;
        stock[GoodIds.Fish] += 100;
        stock[GoodIds.Water] += 100;
        stock[GoodIds.Log] = 1;
        RunTicks(sim, 7200);
        Assert.Equal((Season.Winter, 1, 0), (sim.Season, stock[GoodIds.Log], needs[Households.Heat]));
        // The castle (L) burns one fuel per 600 ticks.
        Assert.Equal(600, Households.Period(sim.Buildings.All[0], Households.Heat));
        RunTicks(sim, 600);
        Assert.Equal((0, 0), (stock[GoodIds.Log], needs[Households.Heat]));
        Assert.Equal(1, sim.Statistics.TotalConsumed(0, GoodIds.Log));
        // No fuel: the next one stays unpaid and the castle goes Short after 60 s.
        RunTicks(sim, 600 + Households.ShortTicks[Households.Heat]);
        Assert.Equal(NeedState.Short, Households.StateAt(sim.Buildings, 0));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        // One coal warms it at once.
        stock[GoodIds.Coal] = 1;
        RunTicks(sim, 2);
        Assert.Equal((NeedState.Supplied, 0), (Households.StateAt(sim.Buildings, 0), stock[GoodIds.Coal]));
        // Cold again until spring, whose first tick ends it.
        RunTicks(sim, 9600 - sim.Tick + 2);
        Assert.Equal((Season.Spring, NeedState.Supplied, 0, 0),
            (sim.Season, Households.StateAt(sim.Buildings, 0), needs[Households.Heat], needs[Households.NeedCount + Households.Heat]));
    }

    [Fact]
    public void A_residence_stockpiles_fuel_from_autumn_and_settlers_walk_slower_in_winter()
    {
        var sim = Simulation.Create(TwoPlayers(SeasonLength.Short));
        var stock = sim.Buildings.StockAt(0)!;
        stock[GoodIds.Fish] += 100;
        stock[GoodIds.Water] += 150;
        stock[GoodIds.Log] = 20;
        int residence = Build(sim, BuildingIds.Residence);
        int index = sim.Buildings.IndexOf(residence);
        Assert.Equal(1200, Households.Period(sim.Buildings.All[index], Households.Heat)); // S: one fuel per 2 min
        RunTicks(sim, 300);
        Assert.Equal(0, sim.Buildings.PilesOf(residence)![Households.Heat]); // no fuel in spring and summer
        RunTicks(sim, 4800 - sim.Tick + 300);
        Assert.Equal(Season.Autumn, sim.Season);
        Assert.Equal(Households.PantryTarget, sim.Buildings.PilesOf(residence)![Households.Heat]);
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        // Winter: supplied settlers walk −20 % on snow and the residence burns its fuel.
        RunTicks(sim, 7200 - sim.Tick);
        Assert.Equal(Season.Winter, sim.Season);
        var walker = sim.Settlers.All.First(s => s.Owner == 0 && s.State == SettlerState.Walking && s.Progress + 2 * Settlers.Speed < Settlers.SubTile
            && Households.HomeState(sim.Buildings, s.HomeId) == NeedState.Supplied);
        ConstructionTests.Run(sim);
        Assert.Equal(walker.Progress + 2 * (Settlers.Speed * Settlers.WinterPercent / 100), sim.Settlers.All[sim.Settlers.IndexOf(walker.Id)].Progress);
        RunTicks(sim, 2400 - 2);
        Assert.Equal(NeedState.Supplied, Households.StateAt(sim.Buildings, index));
        Assert.True(sim.Statistics.TotalConsumed(0, GoodIds.Log) >= 2 + 2); // pantry hand-overs + castle burns
    }
}
