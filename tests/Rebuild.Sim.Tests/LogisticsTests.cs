using System.IO;
using System.Linq;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Goods;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Sim.World;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>Transport jobs: matching, carrying, conservation of goods, unreachable sites (docs/06-economy.md §4).</summary>
public class LogisticsTests
{
    private static MatchSetup TwoPlayers(ulong seed = 1) => new(
        new MapSpec(seed, MapSize.Small, 2), seed,
        new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Ai, 1, "rivermen") });

    private static void Run(Simulation sim, params Command[] commands) => ConstructionTests.Run(sim, commands);

    /// <summary>Planks or stone of a slot: storage stocks + delivered to sites + units in open jobs.</summary>
    private static int Units(Simulation sim, byte slot, int good)
    {
        int n = 0;
        for (int i = 0; i < sim.Buildings.All.Count; i++)
        {
            var b = sim.Buildings.All[i];
            if (b.Owner != slot) continue;
            n += sim.Buildings.StockOf(b.Id)?[good] ?? 0;
            n += good == GoodIds.Plank ? b.DeliveredPlanks : b.DeliveredStone;
        }
        return n + sim.Logistics.All.Count(j => j.Owner == slot && j.Good == good);
    }

    private static (int X, int Y) Spot(Simulation sim, byte slot, int type, System.Func<int, int, bool> accept)
    {
        var s = sim.StartOf(slot)!.Value;
        for (int y = s.Y - 30; y <= s.Y + 30; y++)
            for (int x = s.X - 30; x <= s.X + 30; x++)
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, slot, type, x, y) == PlacementResult.Ok && accept(x, y))
                    return (x, y);
        throw new Xunit.Sdk.XunitException($"No spot for {BuildingCatalog.All[type].Id}");
    }

    private static int Place(Simulation sim, byte slot, ushort type, (int X, int Y) at, ushort seq)
    {
        Run(sim, BuildingCommands.Place(slot, seq, type, at.X, at.Y));
        Assert.Equal(0, sim.RejectedCommands);
        return sim.Buildings.All.Last(b => b.Owner == slot).Id;
    }

    [Fact]
    public void Jobs_move_units_from_storage_to_sites_without_creating_or_losing_any()
    {
        var sim = Simulation.Create(TwoPlayers());
        int planks = Units(sim, 0, GoodIds.Plank), stone = Units(sim, 0, GoodIds.Stone);
        var ids = new[] { BuildingIds.Sawmill, BuildingIds.Woodcutter, BuildingIds.GuardTowerSmall }
            .Select((type, k) => PlaceAnywhere(sim, type, (ushort)k)).ToList();
        int spentPlanks = 0, spentStone = 0, maxJobs = 0;
        for (int turn = 0; ids.Any(id => sim.Buildings.TryGet(id, out var b) && b.State == BuildingState.ConstructionSite); turn++)
        {
            Assert.True(turn < 1000, "sites never completed");
            Run(sim);
            maxJobs = System.Math.Max(maxJobs, sim.Logistics.All.Count);
            spentPlanks = spentStone = 0;
            foreach (int id in ids)
            {
                var b = ConstructionTests.Get(sim, id);
                if (b.State != BuildingState.Complete) continue;
                spentPlanks += b.Definition.CostPlanks;
                spentStone += b.Definition.CostStone;
            }
            Assert.Equal(planks, Units(sim, 0, GoodIds.Plank) + spentPlanks);
            Assert.Equal(stone, Units(sim, 0, GoodIds.Stone) + spentStone);
            foreach (var job in sim.Logistics.All)
            {
                var carrier = sim.Settlers.All[sim.Settlers.IndexOf(job.CarrierId)];
                Assert.Equal((job.Id, job.Owner), (carrier.JobId, carrier.Owner));
            }
        }
        // The three sites need 7 planks + 5 stone; all are matched at once (30 idle carriers).
        Assert.Equal(12, maxJobs);
        Assert.Empty(sim.Logistics.All);
        Assert.All(sim.Settlers.All, s => Assert.Equal(0, s.JobId));
    }

    private static int PlaceAnywhere(Simulation sim, ushort type, ushort seq) =>
        Place(sim, 0, type, Spot(sim, 0, type, (_, _) => true), seq);

    [Fact]
    public void The_nearest_storage_by_sector_supplies_a_site()
    {
        var sim = Simulation.Create(TwoPlayers());
        var castle = sim.Buildings.All[0];
        int Sector(int x, int y, in Building b) => Logistics.SectorDistance(sim.Map.Edge, b.CenterY * sim.Map.Edge + b.CenterX, y * sim.Map.Edge + x);
        // A storehouse in another sector than the castle, then a site nearer to it than to the castle.
        int store = Place(sim, 0, BuildingIds.Storehouse, Spot(sim, 0, BuildingIds.Storehouse, (x, y) => Sector(x + 1, y + 1, castle) >= 1), 0);
        ConstructionTests.RunUntilComplete(sim, store);
        var house = ConstructionTests.Get(sim, store);
        var stock = sim.Buildings.StockAt(sim.Buildings.IndexOf(store))!;
        stock[GoodIds.Plank] = 5;
        var at = Spot(sim, 0, BuildingIds.Woodcutter, (x, y) => Sector(x + 1, y + 1, house) < Sector(x + 1, y + 1, castle));
        int site = Place(sim, 0, BuildingIds.Woodcutter, at, 1);
        Assert.All(sim.Logistics.All.Where(j => j.DestinationId == site), j => Assert.Equal(store, j.SourceId));
        Assert.Equal(2, sim.Logistics.All.Count(j => j.DestinationId == site));
        Assert.Equal(3, stock[GoodIds.Plank]); // reserved units leave the stock at once
        ConstructionTests.RunUntilComplete(sim, site);
        Assert.Equal(3, stock[GoodIds.Plank]);
    }

    [Fact]
    public void An_unreachable_site_is_backed_off_and_its_units_return()
    {
        var sim = Simulation.Create(TwoPlayers());
        int tower = Place(sim, 0, BuildingIds.GuardTowerSmall, Spot(sim, 0, BuildingIds.GuardTowerSmall, (_, _) => true), 0);
        ConstructionTests.RunUntilComplete(sim, tower);
        var claim = sim.Territory.Claims.Single(c => c.Id == ConstructionTests.Get(sim, tower).ClaimId);
        var castle = sim.Buildings.All[0];
        // A site inside the tower's claim but outside the castle's (r = 16) loses its territory when the tower goes.
        bool OutsideCastle(int x, int y)
        {
            for (int dy = -2; dy <= 3; dy++)
                for (int dx = -2; dx <= 3; dx++)
                {
                    int ex = x + dx - castle.CenterX, ey = y + dy - castle.CenterY;
                    if (ex * ex + ey * ey <= 18 * 18) return false;
                }
            return true;
        }
        int planks = Units(sim, 0, GoodIds.Plank), stone = Units(sim, 0, GoodIds.Stone);
        var at = Spot(sim, 0, BuildingIds.Woodcutter, OutsideCastle);
        Run(sim, BuildingCommands.Place(0, 1, BuildingIds.Woodcutter, at.X, at.Y), BuildingCommands.Demolish(0, 2, tower));
        Assert.Equal(0, sim.RejectedCommands);
        int site = sim.Buildings.All.Last().Id;
        Assert.NotEqual(0, sim.Territory.OwnerAt(at.X, at.Y)); // the site lies outside the territory now
        for (int turn = 0; !sim.Logistics.IsUnreachable(site); turn++)
        {
            Assert.True(turn < 500, "site never marked unreachable");
            Run(sim);
        }
        for (int i = 0; i < Logistics.UnreachableBackoffTicks / Simulation.TicksPerTurn - 10; i++)
        {
            Run(sim);
            Assert.True(sim.Logistics.IsUnreachable(site));
        }
        Assert.DoesNotContain(sim.Logistics.All, j => j.DestinationId == site);
        Assert.Equal((0, 0), (ConstructionTests.Get(sim, site).DeliveredPlanks, ConstructionTests.Get(sim, site).DeliveredStone));
        Assert.Equal((planks, stone), (Units(sim, 0, GoodIds.Plank), Units(sim, 0, GoodIds.Stone)));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Save_and_load_with_open_jobs_continue_identically()
    {
        var a = Simulation.Create(TwoPlayers(5));
        PlaceAnywhere(a, BuildingIds.GuardTowerLarge, 0);
        for (int i = 0; i < 3; i++) Run(a);
        Assert.Contains(a.Logistics.All, j => j.State == JobState.Carrying); // the carriers start at the castle door
        var b = Simulation.Load(a.Save());
        for (int i = 0; i < 300; i++)
        {
            Run(a);
            Run(b);
            Assert.Equal(a.ComputeHash(), b.ComputeHash());
        }
    }

    [Fact]
    public void Corrupt_jobs_are_rejected_on_load()
    {
        var sim = Simulation.Create(TwoPlayers(5));
        PlaceAnywhere(sim, BuildingIds.GuardTowerLarge, 0);
        Run(sim);
        Assert.NotEmpty(sim.Logistics.All);
        byte[] Section()
        {
            var w = new CanonicalWriter(4096);
            sim.Logistics.WriteTo(w);
            return w.ToArray();
        }
        Logistics Read(byte[] bytes) =>
            Logistics.ReadFrom(new CanonicalReader(bytes), sim.Map.Edge, sim.Players.Count, sim.Buildings, sim.Settlers);
        var good = Section();
        Read(good);
        const int first = 8; // next id, count
        var cases = new (int Offset, byte Value)[]
        {
            (first + 0, 0),     // id 0
            (first + 4, 7),     // unknown owner
            (first + 5, 0xEE),  // carrier that does not exist or has another job
            (first + 9, 0xEE),  // unknown good
            (first + 11, 0),    // source id 0
            (first + 11, 3),    // source is the construction site (no stock)
            (first + 15, 0xEE), // unknown destination
            (first + 19, 2),    // state
        };
        foreach (var (offset, value) in cases)
        {
            var bytes = (byte[])good.Clone();
            bytes[offset] = value;
            Assert.Throws<InvalidDataException>(() => Read(bytes));
        }
        // Dropping a job leaves its carrier pointing at nothing.
        var w = new CanonicalWriter(4096);
        w.WriteInt32(sim.Logistics.NextId);
        w.WriteInt32(0);
        w.WriteInt32(0);
        Assert.Throws<InvalidDataException>(() => Read(w.ToArray()));
    }
}
