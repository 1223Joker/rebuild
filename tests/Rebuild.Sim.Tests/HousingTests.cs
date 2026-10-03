using System.Linq;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Goods;
using Rebuild.Sim.Match;
using Rebuild.Sim.World;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>Beds, workers living in homes and homeless settlers (docs/12-needs-seasons-weather.md §1.1).</summary>
public class HousingTests
{
    private static MatchSetup TwoPlayers() => new(
        new MapSpec(1, MapSize.Small, 2), 1,
        new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Ai, 1, "rivermen") });

    private static void Run(Simulation sim, params Command[] commands) => ConstructionTests.Run(sim, commands);

    private static void RunTicks(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks / Simulation.TicksPerTurn; i++) Run(sim);
    }

    private static int Homed(Simulation sim, int home) => sim.Settlers.All.Count(s => s.HomeId == home);

    /// <summary>Places a residence at the first valid spot near slot 0's start and runs until it is complete.</summary>
    private static int Residence(Simulation sim, ushort seq)
    {
        var s = sim.StartOf(0)!.Value;
        for (int y = s.Y - 14; y <= s.Y + 14; y++)
            for (int x = s.X - 14; x <= s.X + 14; x++)
            {
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, 0, BuildingIds.Residence, x, y) != PlacementResult.Ok) continue;
                Run(sim, BuildingCommands.Place(0, seq, BuildingIds.Residence, x, y));
                int id = sim.Buildings.All.Last().Id;
                ConstructionTests.RunUntilComplete(sim, id);
                return id;
            }
        throw new Xunit.Sdk.XunitException("No spot for a residence");
    }

    [Fact]
    public void Settlers_of_a_demolished_home_move_into_free_beds_or_leave_after_looking_for_one()
    {
        var sim = Simulation.Create(TwoPlayers());
        sim.Buildings.StockAt(0)![GoodIds.Water] += 100;
        int a = Residence(sim, 0), b = Residence(sim, 1);
        while (Homed(sim, a) < 2) Run(sim);
        int movers = Homed(sim, a), atB = Homed(sim, b);
        Run(sim, BuildingCommands.Demolish(0, 2, a));
        Assert.Equal(0, sim.RejectedCommands);
        Assert.Equal((0, atB + movers), (Homed(sim, a), Homed(sim, b)));
        Assert.All(sim.Settlers.All, s => Assert.Equal(0, s.HomelessTicks));

        // The castle is full (30 beds) and no other home is left: b's settlers look for a bed, then leave.
        int castle = sim.Buildings.All[0].Id;
        Assert.Equal(BuildingCatalog.All[BuildingIds.Castle].Beds, Homed(sim, castle));
        var homeless = sim.Settlers.All.Where(s => s.HomeId == b).Select(s => s.Id).ToArray();
        Run(sim, BuildingCommands.Demolish(0, 3, b));
        RunTicks(sim, Households.HomelessTicks - 20);
        Assert.All(homeless, id => Assert.InRange(sim.Settlers.All[sim.Settlers.IndexOf(id)].HomelessTicks, 1, Households.HomelessTicks));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        RunTicks(sim, 400); // time to finish a job (a carrier with one leaves when it is done)
        Assert.All(homeless, id => Assert.Equal(-1, sim.Settlers.IndexOf(id)));
        Assert.Equal(BuildingCatalog.All[BuildingIds.Castle].Beds, Homed(sim, castle));
    }

    [Fact]
    public void Workers_take_beds_and_a_homeless_worker_takes_a_free_bed_or_brings_its_tool_back()
    {
        var sim = Simulation.Create(TwoPlayers());
        var stock = sim.Buildings.StockAt(0)!;
        int saws = stock[GoodIds.Saw];
        stock[GoodIds.Log] += 20;
        int castle = sim.Buildings.All[0].Id;
        var s0 = sim.StartOf(0)!.Value;
        int mill = -1;
        for (int y = s0.Y - 14; y <= s0.Y + 14 && mill < 0; y++)
            for (int x = s0.X - 14; x <= s0.X + 14 && mill < 0; x++)
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, 0, BuildingIds.Sawmill, x, y) == PlacementResult.Ok)
                {
                    Run(sim, BuildingCommands.Place(0, 0, BuildingIds.Sawmill, x, y));
                    mill = sim.Buildings.All.Last().Id;
                }
        ConstructionTests.RunUntilWorking(sim, mill);
        int index = sim.Settlers.All.ToList().FindIndex(s => s.WorkplaceId == mill);
        var worker = sim.Settlers.All[index];
        Assert.Equal(castle, worker.HomeId); // the worker keeps its castle bed: the castle is full with 29 carriers
        RunTicks(sim, Settlers.SpawnIntervalTicks);
        Assert.Equal(BuildingCatalog.All[BuildingIds.Castle].Beds, Homed(sim, castle));

        // A homeless worker takes the free castle bed at once.
        sim.Settlers.Replace(index, worker with { HomeId = mill });
        Run(sim);
        Assert.Equal((castle, 0), (sim.Settlers.All[index].HomeId, sim.Settlers.All[index].HomelessTicks));

        // With every bed taken it keeps working, then leaves its workplace and brings the saw back.
        sim.Settlers.Spawn(0, castle, worker.Tile);
        sim.Settlers.Replace(index, sim.Settlers.All[index] with { HomeId = mill });
        RunTicks(sim, Households.HomelessTicks - 10);
        Assert.Equal(SettlerKind.Worker, sim.Settlers.All[index].Kind);
        RunTicks(sim, 20);
        var freed = sim.Settlers.All[sim.Settlers.IndexOf(worker.Id)];
        Assert.Equal((SettlerKind.Carrier, 0), (freed.Kind, freed.WorkplaceId));
        Assert.Equal(GoodIds.Saw, sim.Logistics.All.Single(j => j.CarrierId == worker.Id).Good);
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        for (int turns = 0; sim.Settlers.IndexOf(worker.Id) >= 0; turns++)
        {
            Assert.True(turns < 500, "the homeless carrier never left");
            Run(sim);
        }
        Assert.True(stock[GoodIds.Saw] >= saws - 1, "the saw came back"); // a new worker may have taken it again
    }

    [Fact]
    public void A_home_in_crisis_with_only_workers_left_sends_a_worker_out()
    {
        var sim = Simulation.Create(TwoPlayers());
        var stock = sim.Buildings.StockAt(0)!;
        int castle = sim.Buildings.All[0].Id;
        var s0 = sim.StartOf(0)!.Value;
        int mill = -1;
        for (int y = s0.Y - 14; y <= s0.Y + 14 && mill < 0; y++)
            for (int x = s0.X - 14; x <= s0.X + 14 && mill < 0; x++)
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, 0, BuildingIds.Sawmill, x, y) == PlacementResult.Ok)
                {
                    Run(sim, BuildingCommands.Place(0, 0, BuildingIds.Sawmill, x, y));
                    mill = sim.Buildings.All.Last().Id;
                }
        ConstructionTests.RunUntilWorking(sim, mill);
        int worker = sim.Settlers.All.Single(s => s.WorkplaceId == mill).Id;
        while (sim.Settlers.All.Any(s => s.Owner == 0 && s.Id != worker)) // only the worker is left in the castle
        {
            for (int i = sim.Settlers.All.Count - 1; i >= 0; i--)
                if (sim.Settlers.All[i] is { Owner: 0, JobId: 0 } s && s.Id != worker) sim.Settlers.RemoveAt(i);
            Run(sim);
        }
        stock[GoodIds.Water] = 0;
        for (int turns = 0; sim.Settlers.All[sim.Settlers.IndexOf(worker)].Kind == SettlerKind.Worker; turns++)
        {
            Assert.True(turns < 5000, "the worker never left the starving castle");
            Run(sim);
        }
        Assert.Equal(NeedState.Crisis, Households.StateAt(sim.Buildings, 0));
        Assert.Equal((castle, 0), (sim.Settlers.All[sim.Settlers.IndexOf(worker)].HomeId, sim.Settlers.All[sim.Settlers.IndexOf(worker)].WorkplaceId));
    }
}
