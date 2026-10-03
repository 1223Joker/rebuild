using System.IO;
using System.Linq;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Core;
using Rebuild.Sim.Goods;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Sim.World;
using Rebuild.Tools;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>Goods data, castle stock, construction, CancelConstruction refunds and Demolish (docs/06-economy.md §2–§5).</summary>
public class ConstructionTests
{
    private static MatchSetup TwoPlayers(ulong seed = 1) => new(
        new MapSpec(seed, MapSize.Small, 2), seed,
        new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Ai, 1, "rivermen") });

    private static (int X, int Y) FindSpot(Simulation sim, byte slot, int type, int skip = 0)
    {
        var s = sim.StartOf(slot)!.Value;
        for (int y = s.Y - 16; y <= s.Y + 16; y++)
            for (int x = s.X - 16; x <= s.X + 16; x++)
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, slot, type, x, y) == PlacementResult.Ok && skip-- == 0)
                    return (x, y);
        throw new Xunit.Sdk.XunitException($"No spot for {BuildingCatalog.All[type].Id} near slot {slot}");
    }

    private static void Run(Simulation sim, params Command[] commands) => sim.ExecuteTurn(new TurnBundle(sim.Turn, commands));

    private static void RunTurns(Simulation sim, int turns)
    {
        for (int i = 0; i < turns; i++) Run(sim);
    }

    private static int Place(Simulation sim, byte slot, ushort type, ushort seq = 0)
    {
        var (x, y) = FindSpot(sim, slot, type);
        int rejected = sim.RejectedCommands;
        Run(sim, BuildingCommands.Place(slot, seq, type, x, y));
        Assert.Equal(rejected, sim.RejectedCommands);
        return sim.Buildings.All.Last(b => b.Owner == slot).Id;
    }

    private static Building Get(Simulation sim, int id)
    {
        Assert.True(sim.Buildings.TryGet(id, out var b));
        return b;
    }

    private static int CastleStock(Simulation sim, byte slot, int good) =>
        sim.Buildings.StockOf(sim.Buildings.All.First(b => b.Owner == slot && b.Type == BuildingIds.Castle).Id)![good];

    [Fact]
    public void Goods_are_compiled_from_data()
    {
        Assert.Equal(28, GoodCatalog.All.Count);
        Assert.Equal(GoodIds.Plank, GoodCatalog.IndexOf("plank"));
        Assert.Equal(GoodIds.Stone, GoodCatalog.IndexOf("stone"));
        Assert.Equal(-1, GoodCatalog.IndexOf("wine"));
        Assert.Equal(GoodCatalog.All.Count, GoodCatalog.All.Select(g => g.Id).Distinct().Count());
        Assert.All(GoodCatalog.All, g => Assert.InRange(g.StartStock, 0, 10000));
        Assert.True(GoodCatalog.All[GoodIds.Plank].StartStock > 0);
        Assert.NotEqual(0UL, GoodCatalog.DataHash);
        Assert.Equal(GameVersion.CombinedDataHash, GameVersion.Current.DataHash);
        Assert.True(BuildingCatalog.All[BuildingIds.Castle].IsStorage);
        Assert.True(BuildingCatalog.All[BuildingIds.Storehouse].IsStorage);
        Assert.Equal(2, BuildingCatalog.All.Count(b => b.IsStorage));
    }

    [Fact]
    public void Start_castles_hold_the_start_stock()
    {
        var sim = Simulation.Create(TwoPlayers());
        foreach (var castle in sim.Buildings.All)
            Assert.Equal(GoodCatalog.All.Select(g => g.StartStock), sim.Buildings.StockOf(castle.Id)!);
    }

    [Fact]
    public void A_site_is_supplied_built_and_completed()
    {
        var sim = Simulation.Create(TwoPlayers());
        var def = BuildingCatalog.All[BuildingIds.Sawmill]; // 3 planks + 2 stone
        int planks = CastleStock(sim, 0, GoodIds.Plank), stone = CastleStock(sim, 0, GoodIds.Stone);
        int id = Place(sim, 0, BuildingIds.Sawmill);
        // Turn 0, ticks 0 and 1: one plank arrives at tick 0, two work ticks.
        Assert.Equal((1, 0, 2), (Get(sim, id).DeliveredPlanks, Get(sim, id).DeliveredStone, Get(sim, id).WorkDone));

        int turns = 1;
        while (Get(sim, id).State == BuildingState.ConstructionSite)
        {
            var b = Get(sim, id);
            Assert.True(Construction.IsConsistent(b));
            Assert.True(b.WorkDone <= (b.DeliveredPlanks + b.DeliveredStone) * Construction.WorkTicksPerMaterial);
            Run(sim);
            Assert.True(++turns < 500, "site never completed");
        }
        // Materials arrive faster (1 per 10 ticks) than they are worked in (20 ticks each): total work bounds the time.
        Assert.Equal(Construction.TotalWork(def), turns * Simulation.TicksPerTurn);
        Assert.Equal(new Building(id, BuildingIds.Sawmill, 0, Get(sim, id).X, Get(sim, id).Y, 0, BuildingState.Complete, 0), Get(sim, id));
        Assert.Equal(planks - def.CostPlanks, CastleStock(sim, 0, GoodIds.Plank));
        Assert.Equal(stone - def.CostStone, CastleStock(sim, 0, GoodIds.Stone));
        Assert.Null(sim.Buildings.StockOf(id));
    }

    [Fact]
    public void A_completed_tower_extends_the_territory()
    {
        var sim = Simulation.Create(TwoPlayers());
        int before = sim.Territory.TilesOwnedBy(0);
        int id = Place(sim, 0, BuildingIds.GuardTowerSmall);
        RunTurns(sim, Construction.TotalWork(BuildingCatalog.All[BuildingIds.GuardTowerSmall]) / Simulation.TicksPerTurn);
        var tower = Get(sim, id);
        Assert.Equal(BuildingState.Complete, tower.State);
        var claim = sim.Territory.Claims.Single(c => c.Id == tower.ClaimId);
        Assert.Equal((0, tower.CenterX, tower.CenterY, 8), (claim.Owner, claim.X, claim.Y, claim.Radius));
        Assert.True(sim.Territory.TilesOwnedBy(0) > before, "the tower near the border should add tiles");
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void A_completed_storehouse_gets_an_empty_stock_and_the_castle_keeps_supplying()
    {
        var sim = Simulation.Create(TwoPlayers());
        int store = Place(sim, 0, BuildingIds.Storehouse);
        RunTurns(sim, Construction.TotalWork(BuildingCatalog.All[BuildingIds.Storehouse]) / Simulation.TicksPerTurn);
        Assert.Equal(BuildingState.Complete, Get(sim, store).State);
        Assert.All(sim.Buildings.StockOf(store)!, n => Assert.Equal(0, n));
        while (sim.Tick % Construction.SupplyIntervalTicks != 0) Run(sim); // next turn starts with a supply tick
        int planks = CastleStock(sim, 0, GoodIds.Plank);
        int site = Place(sim, 0, BuildingIds.Woodcutter, 1);
        Assert.Equal(1, Get(sim, site).DeliveredPlanks);
        Assert.Equal(planks - 1, CastleStock(sim, 0, GoodIds.Plank));
    }

    [Fact]
    public void Sites_stall_without_materials_and_older_sites_are_served_first()
    {
        var sim = Simulation.Create(TwoPlayers());
        var castle = sim.Buildings.All[0];
        var stock = sim.Buildings.StockAt(sim.Buildings.IndexOf(castle.Id))!;
        stock[GoodIds.Plank] = 1;
        var a = FindSpot(sim, 0, BuildingIds.Woodcutter);
        var b = FindSpot(sim, 0, BuildingIds.Forester, 40);
        Run(sim, BuildingCommands.Place(0, 0, BuildingIds.Woodcutter, a.X, a.Y), BuildingCommands.Place(0, 1, BuildingIds.Forester, b.X, b.Y));
        Assert.Equal(0, sim.RejectedCommands);
        int older = sim.Buildings.All[^2].Id, newer = sim.Buildings.All[^1].Id;
        RunTurns(sim, 100);
        Assert.Equal((1, Construction.WorkTicksPerMaterial), (Get(sim, older).DeliveredPlanks, Get(sim, older).WorkDone));
        Assert.Equal((0, 0), (Get(sim, newer).DeliveredPlanks, Get(sim, newer).WorkDone));
        Assert.Equal(BuildingState.ConstructionSite, Get(sim, older).State);
        Assert.Equal(0, stock[GoodIds.Plank]);
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Cancelling_a_site_refunds_delivered_materials()
    {
        var sim = Simulation.Create(TwoPlayers());
        int planks = CastleStock(sim, 0, GoodIds.Plank), stone = CastleStock(sim, 0, GoodIds.Stone);
        int id = Place(sim, 0, BuildingIds.GuardTowerLarge); // 3 planks + 6 stone
        RunTurns(sim, 29); // ticks 0–59 incl. the placement turn: 3 planks + 3 stone delivered
        Assert.Equal((3, 3), (Get(sim, id).DeliveredPlanks, Get(sim, id).DeliveredStone));
        Assert.Equal(stone - 3, CastleStock(sim, 0, GoodIds.Stone));
        Run(sim, BuildingCommands.Cancel(0, 1, id));
        Assert.Equal(0, sim.RejectedCommands);
        Assert.False(sim.Buildings.TryGet(id, out _));
        Assert.Equal((planks, stone), (CastleStock(sim, 0, GoodIds.Plank), CastleStock(sim, 0, GoodIds.Stone)));
    }

    [Fact]
    public void Demolish_removes_own_complete_buildings_and_their_claim()
    {
        var sim = Simulation.Create(TwoPlayers());
        int before = sim.Territory.TilesOwnedBy(0);
        int tower = Place(sim, 0, BuildingIds.GuardTowerSmall);
        RunTurns(sim, Construction.TotalWork(BuildingCatalog.All[BuildingIds.GuardTowerSmall]) / Simulation.TicksPerTurn);
        Assert.Equal(BuildingState.Complete, Get(sim, tower).State);
        int claims = sim.Territory.Claims.Count;
        int ownCastle = sim.Buildings.All[0].Id, enemyCastle = sim.Buildings.All[1].Id;
        int site = Place(sim, 0, BuildingIds.Woodcutter, 1);
        int rejected = sim.RejectedCommands;

        Run(sim,
            BuildingCommands.Demolish(1, 0, tower),       // not the owner
            BuildingCommands.Demolish(0, 3, ownCastle),   // castle
            BuildingCommands.Demolish(0, 4, enemyCastle), // enemy
            BuildingCommands.Demolish(0, 5, site),        // construction site (cancel it instead)
            BuildingCommands.Demolish(0, 6, 999),         // unknown
            new Command(CommandType.Demolish, 0, 0, 7, new byte[] { 1, 2 })); // bad payload
        Assert.Equal(rejected + 6, sim.RejectedCommands);
        Assert.Equal(claims, sim.Territory.Claims.Count);

        Run(sim, BuildingCommands.Demolish(0, 8, tower));
        Assert.Equal(rejected + 6, sim.RejectedCommands);
        Assert.False(sim.Buildings.TryGet(tower, out _));
        Assert.Equal(claims - 1, sim.Territory.Claims.Count);
        Assert.Equal(before, sim.Territory.TilesOwnedBy(0));
        Assert.True(sim.Buildings.TryGet(site, out _));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Inconsistent_construction_state_is_rejected_on_load()
    {
        var site = new Building(5, BuildingIds.Sawmill, 0, 10, 10, 0, BuildingState.ConstructionSite, 0, 3, 1, 80);
        Assert.True(Construction.IsConsistent(site));
        Assert.False(Construction.IsConsistent(site with { DeliveredPlanks = 4 }));                 // more than the cost
        Assert.False(Construction.IsConsistent(site with { DeliveredPlanks = 2 }));                 // stone before all planks
        Assert.False(Construction.IsConsistent(site with { WorkDone = 81 }));                       // work ahead of materials
        Assert.False(Construction.IsConsistent(site with { ClaimId = 3 }));                         // sites claim nothing
        Assert.False(Construction.IsConsistent(site with { DeliveredStone = 2, WorkDone = 100 }));  // should be complete
        var tower = new Building(6, BuildingIds.GuardTowerSmall, 0, 10, 10, 0, BuildingState.Complete, 4);
        Assert.True(Construction.IsConsistent(tower));
        Assert.False(Construction.IsConsistent(tower with { ClaimId = 0 }));                        // complete tower without claim
        Assert.False(Construction.IsConsistent(tower with { WorkDone = 1 }));

        // A stock on a site, or a missing castle stock, fails the load.
        var sim = Simulation.Create(TwoPlayers());
        var w = new CanonicalWriter(1024);
        sim.Buildings.WriteTo(w);
        var bytes = w.ToArray();
        const int stockFlag = 8 + 17 + 4; // nextId, count, first castle's fields + progress
        Assert.Equal(1, bytes[stockFlag]);
        foreach (byte flag in new byte[] { 0, 2 })
        {
            var bad = (byte[])bytes.Clone();
            bad[stockFlag] = flag;
            Assert.Throws<InvalidDataException>(() =>
                BuildingRegistry.ReadFrom(new CanonicalReader(bad), sim.Map.Edge, sim.Players.Count, sim.Territory));
        }
        var negative = (byte[])bytes.Clone();
        negative[stockFlag + 4] = 0xFF; // high byte of good 0 → negative count
        Assert.Throws<InvalidDataException>(() =>
            BuildingRegistry.ReadFrom(new CanonicalReader(negative), sim.Map.Edge, sim.Players.Count, sim.Territory));
    }

    [Fact]
    public void Build_script_completes_and_demolishes_buildings()
    {
        var log = SampleLogs.BuildScript(1, 600);
        var sim = Simulation.Create(log.Setup);
        int maxClaims = 0;
        foreach (var bundle in log.Bundles)
        {
            sim.ExecuteTurn(bundle);
            maxClaims = System.Math.Max(maxClaims, sim.Territory.Claims.Count);
        }
        Assert.Contains(sim.Buildings.All, b => b.Type != BuildingIds.Castle && b.State == BuildingState.Complete);
        Assert.True(maxClaims > 2, "no tower was completed");
        Assert.All(sim.Buildings.All, b => Assert.True(Construction.IsConsistent(b)));
    }
}
