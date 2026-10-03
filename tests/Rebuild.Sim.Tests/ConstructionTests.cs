using System.IO;
using System.Linq;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Core;
using Rebuild.Sim.Goods;
using Rebuild.Sim.MapGen;
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

    internal static void Run(Simulation sim, params Command[] commands) => sim.ExecuteTurn(new TurnBundle(sim.Turn, commands));

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

    internal static Building Get(Simulation sim, int id)
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

    /// <summary>Runs turns until the building is complete; fails after <paramref name="maxTurns"/>.</summary>
    internal static void RunUntilComplete(Simulation sim, int id, int maxTurns = 1000)
    {
        for (int turns = 0; Get(sim, id).State == BuildingState.ConstructionSite; turns++)
        {
            Assert.True(turns < maxTurns, "site never completed");
            Run(sim);
        }
        if (Get(sim, id).Definition.Production != null) RunUntilWorking(sim, id, maxTurns);
    }

    /// <summary>Runs one turn with every home's due counters cleared first: nobody eats, so no home goes Short.</summary>
    internal static void RunFed(Simulation sim)
    {
        for (int i = 0; i < sim.Buildings.All.Count; i++)
            if (sim.Buildings.NeedsAt(i) is { } n) n[0] = n[1] = n[Households.Heat] = 0;
        Run(sim);
    }

    /// <summary>Whether the production building has its worker inside.</summary>
    internal static bool HasWorker(Simulation sim, int id) =>
        sim.Buildings.TryGet(id, out var b) && b.State == BuildingState.Complete
        && sim.Settlers.All.Any(s => s.Kind == SettlerKind.Worker && s.WorkplaceId == id);

    /// <summary>Runs turns until the production building's worker is inside; fails after <paramref name="maxTurns"/>.</summary>
    internal static void RunUntilWorking(Simulation sim, int id, int maxTurns = 1000)
    {
        for (int turns = 0; !HasWorker(sim, id); turns++)
        {
            Assert.True(turns < maxTurns, "worker never arrived");
            Run(sim);
        }
    }

    /// <summary>Runs turns until no transport job is open; fails after <paramref name="maxTurns"/>.</summary>
    private static void RunUntilNoJobs(Simulation sim, int maxTurns = 1000)
    {
        for (int turns = 0; sim.Logistics.All.Count > 0; turns++)
        {
            Assert.True(turns < maxTurns, "jobs never finished");
            Run(sim);
        }
    }

    [Fact]
    public void A_site_is_supplied_built_and_completed()
    {
        var sim = Simulation.Create(TwoPlayers());
        var def = BuildingCatalog.All[BuildingIds.Sawmill]; // 3 planks + 2 stone
        int planks = CastleStock(sim, 0, GoodIds.Plank), stone = CastleStock(sim, 0, GoodIds.Stone);
        int id = Place(sim, 0, BuildingIds.Sawmill);
        int turns = 1;
        while (Get(sim, id).State == BuildingState.ConstructionSite)
        {
            var b = Get(sim, id);
            Assert.True(Construction.IsConsistent(b));
            Assert.True(b.WorkDone <= (b.DeliveredPlanks + b.DeliveredStone) * Construction.WorkTicksPerMaterial);
            Run(sim);
            Assert.True(++turns < 1000, "site never completed");
        }
        // Carriers have to walk first, so construction takes longer than the work alone.
        Assert.True(turns * Simulation.TicksPerTurn > Construction.TotalWork(def));
        Assert.Equal(new Building(id, BuildingIds.Sawmill, 0, Get(sim, id).X, Get(sim, id).Y, 0, BuildingState.Complete, 0), Get(sim, id));
        Assert.Equal(planks - def.CostPlanks, CastleStock(sim, 0, GoodIds.Plank));
        Assert.Equal(stone - def.CostStone, CastleStock(sim, 0, GoodIds.Stone));
        Assert.Null(sim.Buildings.StockOf(id));
        // The builder takes its hammer back and the sawmill's worker is on the way: the saw left the castle stock.
        Assert.Equal(new[] { (JobKind.Transport, (ushort)GoodIds.Hammer, id), (JobKind.Employ, (ushort)GoodIds.Saw, id) },
            sim.Logistics.All.Select(j => (j.Kind, j.Good, j.Kind == JobKind.Transport ? j.SourceId : j.DestinationId)));
        Assert.Equal(0, CastleStock(sim, 0, GoodIds.Saw));
    }

    /// <summary>Hammers of slot 0: castle stock, builders inside sites and hammers in open jobs.</summary>
    private static int Hammers(Simulation sim) =>
        CastleStock(sim, 0, GoodIds.Hammer)
        + sim.Settlers.All.Count(s => s.Kind == SettlerKind.Worker && sim.Buildings.TryGet(s.WorkplaceId, out var b) && b.State == BuildingState.ConstructionSite)
        + sim.Logistics.All.Count(j => j.Good == GoodIds.Hammer);

    private static bool HasBuilder(Simulation sim, int id) =>
        sim.Settlers.All.Any(s => s.Kind == SettlerKind.Worker && s.WorkplaceId == id);

    [Fact]
    public void A_site_is_built_only_by_a_builder_who_takes_the_hammer_back()
    {
        var sim = Simulation.Create(TwoPlayers());
        int hammers = CastleStock(sim, 0, GoodIds.Hammer);
        sim.Buildings.StockAt(0)![GoodIds.Hammer] = 0;
        int id = Place(sim, 0, BuildingIds.Woodcutter);
        RunTurns(sim, 100);
        var site = Get(sim, id);
        Assert.Equal(site.Definition.CostTotal, site.DeliveredPlanks + site.DeliveredStone); // materials arrive without a builder
        Assert.Equal(0, site.WorkDone);
        Assert.False(HasBuilder(sim, id));

        sim.Buildings.StockAt(0)![GoodIds.Hammer] = hammers;
        Run(sim);
        Assert.Equal(hammers - 1, CastleStock(sim, 0, GoodIds.Hammer)); // one builder fetches one hammer
        Assert.Single(sim.Logistics.All, j => j.Kind == JobKind.Employ && j.DestinationId == id && j.Good == GoodIds.Hammer);
        for (int turns = 0; Get(sim, id).State == BuildingState.ConstructionSite; turns++)
        {
            Assert.True(turns < 1000, "site never completed");
            Assert.Equal(hammers, Hammers(sim));
            Run(sim);
        }
        Assert.Contains(sim.Logistics.All, j => (j.Kind, j.Good, j.SourceId) == (JobKind.Transport, GoodIds.Hammer, id)); // the builder came out
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        while (sim.Logistics.All.Any(j => j.Good == GoodIds.Hammer)) Run(sim);
        Assert.Equal(hammers, CastleStock(sim, 0, GoodIds.Hammer)); // back in stock
    }

    [Fact]
    public void Sites_stalled_without_material_do_not_hold_the_hammers()
    {
        var sim = Simulation.Create(TwoPlayers());
        var stock = sim.Buildings.StockAt(0)!;
        stock[GoodIds.Stone] = 0;
        stock[GoodIds.Hammer] = 1;
        // A sawmill stalls for stone; the woodcutter placed after it (planks only) must still get the one hammer.
        int mill = Place(sim, 0, BuildingIds.Sawmill);
        int cutter = Place(sim, 0, BuildingIds.Woodcutter, 1);
        for (int turns = 0; Get(sim, cutter).State == BuildingState.ConstructionSite; turns++)
        {
            Assert.True(turns < 1000, "the planks-only site never got the hammer");
            Run(sim);
        }
        var site = Get(sim, mill);
        Assert.Equal(BuildingState.ConstructionSite, site.State);
        Assert.Equal(site.DeliveredPlanks * Construction.WorkTicksPerMaterial, site.WorkDone); // its planks were worked in
    }

    [Fact]
    public void Cancelling_a_site_sends_its_builder_out_with_the_hammer()
    {
        var sim = Simulation.Create(TwoPlayers());
        int hammers = CastleStock(sim, 0, GoodIds.Hammer);
        int id = Place(sim, 0, BuildingIds.Sawmill);
        for (int turns = 0; !HasBuilder(sim, id); turns++)
        {
            Assert.True(turns < 1000, "builder never arrived");
            Run(sim);
        }
        Assert.Equal(BuildingState.ConstructionSite, Get(sim, id).State);
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash()); // a builder inside a site loads
        Run(sim, BuildingCommands.Cancel(0, 1, id));
        Assert.False(sim.Buildings.TryGet(id, out _));
        Assert.DoesNotContain(sim.Settlers.All, s => s.Kind == SettlerKind.Worker);
        Assert.Equal(hammers, Hammers(sim));
        RunUntilNoJobs(sim);
        Assert.Equal(hammers, CastleStock(sim, 0, GoodIds.Hammer));
    }

    /// <summary>First spot of <paramref name="type"/> in slot 0's territory whose placement gives <paramref name="result"/> and that passes <paramref name="accept"/>.</summary>
    private static (int X, int Y) FindSpotWhere(Simulation sim, int type, PlacementResult result, System.Func<int, int, bool> accept)
    {
        for (int y = 0; y < sim.Map.Edge; y++)
            for (int x = 0; x < sim.Map.Edge; x++)
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, 0, type, x, y) == result && accept(x, y)) return (x, y);
        throw new Xunit.Sdk.XunitException($"No {result} spot for {BuildingCatalog.All[type].Id}");
    }

    /// <summary>Gives slot 0 a radius-30 claim around its start: beyond the flat start plateau, where land is uneven.</summary>
    private static void Widen(Simulation sim)
    {
        var s = sim.StartOf(0)!.Value;
        sim.Territory.AddClaim(0, s.X, s.Y, 30);
    }

    private static int Shovels(Simulation sim) =>
        CastleStock(sim, 0, GoodIds.Shovel)
        + sim.Settlers.All.Count(s => s.Kind == SettlerKind.Worker && sim.Buildings.TryGet(s.WorkplaceId, out var b) && b.State == BuildingState.ConstructionSite)
        + sim.Logistics.All.Count(j => j.Good == GoodIds.Shovel);

    [Fact]
    public void An_uneven_site_is_levelled_by_a_digger_before_it_is_built()
    {
        var sim = Simulation.Create(TwoPlayers());
        Widen(sim);
        int shovels = CastleStock(sim, 0, GoodIds.Shovel);
        var (x, y) = FindSpotWhere(sim, BuildingIds.Residence, PlacementResult.Ok, (x, y) => Construction.DigWork(sim.Map, BuildingIds.Residence, x, y) > 0);
        int work = Construction.DigWork(sim.Map, BuildingIds.Residence, x, y);
        int level = Construction.LevelOf(sim.Map, x, y, 2);
        Run(sim, BuildingCommands.Place(0, 0, BuildingIds.Residence, (ushort)x, (ushort)y));
        int id = sim.Buildings.All.Last().Id;
        Assert.Equal(work, Get(sim, id).DigLeft);
        Run(sim);
        Assert.Single(sim.Logistics.All, j => j.Kind == JobKind.Employ && j.DestinationId == id && j.Good == GoodIds.Shovel); // a digger at once
        bool saved = false;
        for (int turns = 0; Get(sim, id).DigLeft > 0; turns++)
        {
            Assert.True(turns < 1000, "site never levelled");
            Assert.Equal(0, Get(sim, id).WorkDone); // nothing is built before the site is level
            Assert.DoesNotContain(sim.Logistics.All, j => j.Kind == JobKind.Employ && j.Good == GoodIds.Hammer);
            Assert.Equal(shovels, Shovels(sim));
            if (!saved && HasBuilder(sim, id))
            {
                Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash()); // a digger inside a site loads
                saved = true;
            }
            Run(sim);
        }
        Assert.True(saved);
        for (int ty = y; ty < y + 2; ty++)
            for (int tx = x; tx < x + 2; tx++)
                Assert.Equal(level, sim.Map.Height[sim.Map.Index(tx, ty)]);
        Assert.Contains(sim.Logistics.All, j => (j.Kind, j.Good, j.SourceId) == (JobKind.Transport, GoodIds.Shovel, id)); // the digger came out
        Assert.NotEmpty(sim.MapChanges.Tiles);
        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash()); // levelled heights load
        Assert.Equal(sim.Map.Flags, loaded.Map.Flags);
        RunUntilComplete(sim, id);
        RunUntilNoJobs(sim);
        Assert.Equal(shovels, CastleStock(sim, 0, GoodIds.Shovel));
    }

    [Fact]
    public void Two_foresters_leave_shovels_for_diggers()
    {
        var sim = Simulation.Create(TwoPlayers());
        int a = Place(sim, 0, BuildingIds.Forester), b = Place(sim, 0, BuildingIds.Forester, 1);
        RunUntilComplete(sim, a);
        RunUntilComplete(sim, b); // both foresters keep a shovel
        Widen(sim);
        var (x, y) = FindSpotWhere(sim, BuildingIds.Residence, PlacementResult.Ok, (x, y) => Construction.DigWork(sim.Map, BuildingIds.Residence, x, y) > 0);
        Run(sim, BuildingCommands.Place(0, 2, BuildingIds.Residence, (ushort)x, (ushort)y));
        int id = sim.Buildings.All.Last().Id;
        for (int turns = 0; Get(sim, id).DigLeft > 0; turns++)
        {
            Assert.True(turns < 1000, "no shovel left for the digger");
            Run(sim);
        }
    }

    [Fact]
    public void Placement_accepts_gentle_slopes_but_not_steep_levelling()
    {
        var sim = Simulation.Create(TwoPlayers());
        var (fx, fy, _) = sim.StartOf(0)!.Value;
        Assert.Equal(0, Construction.DigWork(sim.Map, BuildingIds.Residence, fx + 4, fy + 4)); // the start plateau is flat
        Widen(sim);
        bool Sloped(int x, int y) => !sim.Map.IsBuildable(sim.Map.Index(x, y)) || !sim.Map.IsBuildable(sim.Map.Index(x + 1, y + 1));
        FindSpotWhere(sim, BuildingIds.Residence, PlacementResult.Ok, Sloped); // too steep to build on before levelling
        FindSpotWhere(sim, BuildingIds.Storehouse, PlacementResult.TooSteep, (_, _) => true);
    }

    [Fact]
    public void Height_changes_off_plains_are_rejected_on_load()
    {
        var sim = Simulation.Create(TwoPlayers());
        MapData Fresh() => MapGenerator.Generate(sim.Setup.Map).Map!;
        var map = Fresh();
        int plains = Enumerable.Range(0, map.TileCount).First(t => map.Terrain[t] == (byte)Terrain.Plains);
        int water = Enumerable.Range(0, map.TileCount).First(t => map.Terrain[t] == (byte)Terrain.Water);
        byte[] Change(int tile, byte height)
        {
            var w = new CanonicalWriter(16);
            w.WriteInt32(1);
            w.WriteInt32(tile);
            w.WriteByte(height);
            w.WriteByte(map.Object[tile]);
            w.WriteByte(map.Resource[tile]);
            w.WriteByte(map.Amount[tile]);
            return w.ToArray();
        }
        var levelled = Fresh();
        MapChanges.ReadFrom(new CanonicalReader(Change(plains, (byte)(map.Height[plains] + 1))), levelled);
        Assert.Equal(map.Height[plains] + 1, levelled.Height[plains]);
        Assert.Throws<InvalidDataException>(() => MapChanges.ReadFrom(new CanonicalReader(Change(water, (byte)(map.Height[water] + 1))), Fresh()));
    }

    [Fact]
    public void A_completed_tower_extends_the_territory()
    {
        var sim = Simulation.Create(TwoPlayers());
        int before = sim.Territory.TilesOwnedBy(0);
        int id = Place(sim, 0, BuildingIds.GuardTowerSmall);
        RunUntilComplete(sim, id);
        var tower = Get(sim, id);
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
        RunUntilComplete(sim, store);
        Assert.All(sim.Buildings.StockOf(store)!, n => Assert.Equal(0, n));
        int planks = CastleStock(sim, 0, GoodIds.Plank);
        int site = Place(sim, 0, BuildingIds.Woodcutter, 1);
        RunUntilComplete(sim, site);
        Assert.Equal(planks - BuildingCatalog.All[BuildingIds.Woodcutter].CostPlanks, CastleStock(sim, 0, GoodIds.Plank));
        Assert.All(sim.Buildings.StockOf(store)!.Where((_, g) => g != GoodIds.Hammer), n => Assert.Equal(0, n)); // a builder may bring its hammer here
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
        RunTurns(sim, 200);
        Assert.Equal((1, Construction.WorkTicksPerMaterial), (Get(sim, older).DeliveredPlanks, Get(sim, older).WorkDone));
        Assert.Equal((0, 0), (Get(sim, newer).DeliveredPlanks, Get(sim, newer).WorkDone));
        Assert.Equal(BuildingState.ConstructionSite, Get(sim, older).State);
        Assert.Equal(0, stock[GoodIds.Plank]);
        Assert.Empty(sim.Logistics.All);
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Cancelling_a_site_refunds_delivered_and_carried_materials()
    {
        var sim = Simulation.Create(TwoPlayers());
        int planks = CastleStock(sim, 0, GoodIds.Plank), stone = CastleStock(sim, 0, GoodIds.Stone);
        ushort seq = 0;
        // First cancel while units are carried (carriers walk together, so nothing is delivered yet), then while some are delivered.
        foreach (bool delivered in new[] { false, true })
        {
            int id = Place(sim, 0, BuildingIds.GuardTowerLarge, seq++); // 3 planks + 6 stone
            for (int turns = 0; delivered
                     ? Get(sim, id).DeliveredPlanks + Get(sim, id).DeliveredStone == 0
                     : !sim.Logistics.All.Any(j => j.DestinationId == id && j.State == JobState.Carrying); turns++)
            {
                Assert.True(turns < 1000, "nothing carried or delivered");
                Run(sim);
            }
            Run(sim, BuildingCommands.Cancel(0, seq++, id));
            Assert.Equal(0, sim.RejectedCommands);
            Assert.False(sim.Buildings.TryGet(id, out _));
            RunUntilNoJobs(sim); // reserved units go back to the stock, carried ones are brought to the castle
            Assert.Equal((planks, stone), (CastleStock(sim, 0, GoodIds.Plank), CastleStock(sim, 0, GoodIds.Stone)));
            Assert.All(sim.Settlers.All, s => Assert.Equal(0, s.JobId));
        }
    }

    [Fact]
    public void Demolish_removes_own_complete_buildings_and_their_claim()
    {
        var sim = Simulation.Create(TwoPlayers());
        int before = sim.Territory.TilesOwnedBy(0);
        int tower = Place(sim, 0, BuildingIds.GuardTowerSmall);
        RunUntilComplete(sim, tower);
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
        const int stockFlag = 8 + 17 + 9; // nextId, count, first castle's fields + progress + cycle + choice + dig
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
        int maxClaims = 0, cycles = 0, completed = 0, smiths = 0;
        bool planted = false;
        foreach (var bundle in log.Bundles)
        {
            sim.ExecuteTurn(bundle);
            maxClaims = System.Math.Max(maxClaims, sim.Territory.Claims.Count);
            cycles += sim.Buildings.All.Count(b => b.Cycle > 0);
            completed += sim.Buildings.All.Count(b => b.Type != BuildingIds.Castle && b.State == BuildingState.Complete);
            smiths += sim.Buildings.All.Count(b => b.Definition.Production is { HasChoice: true } && b.State == BuildingState.Complete);
            planted |= sim.MapChanges.Tiles.Any(t => sim.Map.Object[t] == (byte)Rebuild.Sim.MapGen.MapObject.Tree);
        }
        Assert.True(completed > 0, "no building was completed");
        Assert.True(sim.MapChanges.Tiles.Count > 0, "nothing was harvested");
        Assert.True(maxClaims > 2, "no tower was completed");
        Assert.True(cycles > 0, "no production cycle ran");
        Assert.True(planted, "no tree was planted");
        Assert.True(smiths > 0, "no smith was completed");
        Assert.NotEqual(ProductionQuotas.DefaultWeight, sim.Quotas.WeightOf(0, Rebuild.Sim.Goods.GoodIds.Hammer)); // quota commands applied
        Assert.All(sim.Buildings.All, b => Assert.True(Construction.IsConsistent(b)));
    }
}
