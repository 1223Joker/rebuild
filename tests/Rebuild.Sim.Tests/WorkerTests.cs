using System;
using System.Buffers.Binary;
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

/// <summary>Workers: a carrier fetches the building's tool and becomes its worker; production needs one (docs/06-economy.md §3).</summary>
public class WorkerTests
{
    private static MatchSetup TwoPlayers(ulong seed = 1) => new(
        new MapSpec(seed, MapSize.Small, 2), seed,
        new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Ai, 1, "rivermen") });

    private static void Run(Simulation sim, params Command[] commands) => ConstructionTests.Run(sim, commands);

    private static void RunTicks(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks / Simulation.TicksPerTurn; i++) Run(sim);
    }

    /// <summary>Places a building at the first accepted valid spot near slot 0's start and runs until the site is complete (not until its worker arrives).</summary>
    private static int Build(Simulation sim, ushort type, ushort seq, System.Func<int, int, bool>? accept = null)
    {
        var s = sim.StartOf(0)!.Value;
        for (int y = s.Y - 20; y <= s.Y + 20; y++)
            for (int x = s.X - 20; x <= s.X + 20; x++)
            {
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, 0, type, x, y) != PlacementResult.Ok
                    || !(accept?.Invoke(x, y) ?? true)) continue;
                Run(sim, BuildingCommands.Place(0, seq, type, x, y));
                Assert.Equal(0, sim.RejectedCommands);
                int id = sim.Buildings.All.Last(b => b.Owner == 0).Id;
                for (int turns = 0; ConstructionTests.Get(sim, id).State == BuildingState.ConstructionSite; turns++)
                {
                    Assert.True(turns < 1000, "site never completed");
                    Run(sim);
                }
                return id;
            }
        throw new Xunit.Sdk.XunitException($"No spot for {BuildingCatalog.All[type].Id}");
    }

    private static int Woodcutter(Simulation sim, ushort seq) =>
        Build(sim, BuildingIds.Woodcutter, seq, (x, y) => ProductionTests.ObjectsAround(sim, 0, BuildingIds.Woodcutter, x, y) >= 6);

    private static int[] CastleStock(Simulation sim) => sim.Buildings.StockAt(sim.Buildings.IndexOf(1))!;

    private static Settler WorkerOf(Simulation sim, int id) => sim.Settlers.All.Single(s => s.Kind == SettlerKind.Worker && s.HomeId == id);

    private static TransportJob[] EmployJobs(Simulation sim) => sim.Logistics.All.Where(j => j.Kind == JobKind.Employ).ToArray();

    [Fact]
    public void Tools_are_compiled_from_data()
    {
        ushort Tool(ushort type) => BuildingCatalog.All[type].Production!.Tool;
        Assert.Equal(GoodIds.Axe, Tool(BuildingIds.Woodcutter));
        Assert.Equal(GoodIds.Shovel, Tool(BuildingIds.Forester));
        Assert.Equal(GoodIds.Saw, Tool(BuildingIds.Sawmill));
        Assert.Equal(GoodIds.FishingRod, Tool(BuildingIds.Fisher));
        Assert.Equal(GoodIds.Bucket, Tool(BuildingIds.Waterworks));
        Assert.All(new[] { BuildingIds.Stonecutter, BuildingIds.CoalMine, BuildingIds.IronMine, BuildingIds.GoldMine },
            t => Assert.Equal(GoodIds.Pickaxe, Tool(t)));
        Assert.All(new[] { BuildingIds.Toolsmith, BuildingIds.Weaponsmith }, t => Assert.Equal(GoodIds.Hammer, Tool(t)));
        Assert.All(new[] { BuildingIds.Mill, BuildingIds.Bakery, BuildingIds.PigFarm, BuildingIds.IronSmelter, BuildingIds.GoldSmelter },
            t => Assert.Equal(ProductionDefinition.NoTool, Tool(t)));
        // Every tool is a good some toolsmith cycle can make.
        var toolsmith = BuildingCatalog.All[BuildingIds.Toolsmith].Production!;
        foreach (var d in BuildingCatalog.All.Where(d => d.Production is { Tool: not ProductionDefinition.NoTool }))
            Assert.True(toolsmith.OutputIndexOf(d.Production!.Tool) >= 0, d.Id);
    }

    [Fact]
    public void A_carrier_fetches_the_tool_and_becomes_the_worker()
    {
        var sim = Simulation.Create(TwoPlayers());
        int axes = CastleStock(sim)[GoodIds.Axe];
        int id = Woodcutter(sim, 0);
        Assert.False(ConstructionTests.HasWorker(sim, id));
        Run(sim);
        var job = EmployJobs(sim).Single();
        Assert.Equal(((ushort)GoodIds.Axe, 1, id, JobState.ToPickup), (job.Good, job.SourceId, job.DestinationId, job.State));
        Assert.Equal(axes - 1, CastleStock(sim)[GoodIds.Axe]); // reserved at once
        int turns = 0;
        while (!ConstructionTests.HasWorker(sim, id))
        {
            Assert.Equal(0, ConstructionTests.Get(sim, id).Cycle); // no work without the worker
            Assert.True(++turns < 500, "worker never arrived");
            Run(sim);
        }
        var worker = WorkerOf(sim, id);
        Assert.Equal(job.CarrierId, worker.Id);
        Assert.Equal(Settlers.DoorOf(ConstructionTests.Get(sim, id), sim.Map, sim.Buildings), worker.Tile);
        Assert.Equal((SettlerState.Idle, 0), (worker.State, worker.JobId));
        Assert.Empty(EmployJobs(sim));
        Assert.Equal(1, sim.Statistics.TotalConsumed(0, GoodIds.Axe)); // the tool is used up
        Assert.Equal(axes - 1, CastleStock(sim)[GoodIds.Axe]);
        Run(sim);
        Assert.True(ConstructionTests.Get(sim, id).Cycle > 0, "the cycle starts once the worker is inside");
        // The worker stays put; the castle refills the carrier it lost.
        RunTicks(sim, Settlers.SpawnIntervalTicks);
        Assert.Equal(worker, WorkerOf(sim, id));
        Assert.Equal(BuildingCatalog.All[BuildingIds.Castle].Carriers,
            sim.Settlers.All.Count(s => s.Kind == SettlerKind.Carrier && s.HomeId == 1));
        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
        RunTicks(sim, 300);
        RunTicks(loaded, 300);
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
    }

    [Fact]
    public void A_worker_without_a_tool_walks_straight_in()
    {
        var sim = Simulation.Create(TwoPlayers());
        var stock = CastleStock(sim).ToArray();
        int id = Build(sim, BuildingIds.Mill, 0);
        Run(sim);
        var job = EmployJobs(sim).Single();
        Assert.Equal((ProductionDefinition.NoTool, id, id, JobState.Carrying), (job.Good, job.SourceId, job.DestinationId, job.State));
        // Mid-walk saves keep the tool-less job.
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        ConstructionTests.RunUntilWorking(sim, id);
        Assert.Equal(stock.Where((_, g) => g != GoodIds.Plank && g != GoodIds.Stone),
            CastleStock(sim).Where((_, g) => g != GoodIds.Plank && g != GoodIds.Stone));
        Assert.Equal(0, sim.Statistics.TotalConsumed(0, GoodIds.Axe));
    }

    [Fact]
    public void Without_the_tool_in_stock_a_building_waits_for_one()
    {
        var sim = Simulation.Create(TwoPlayers());
        Assert.Equal(1, CastleStock(sim)[GoodIds.Saw]);
        int first = Build(sim, BuildingIds.Sawmill, 0);
        int second = Build(sim, BuildingIds.Sawmill, 1);
        ConstructionTests.RunUntilWorking(sim, first);
        RunTicks(sim, 600);
        Assert.False(ConstructionTests.HasWorker(sim, second));
        Assert.Empty(EmployJobs(sim));
        Assert.Equal(0, CastleStock(sim)[GoodIds.Saw]);
        CastleStock(sim)[GoodIds.Saw]++; // as if a toolsmith had delivered one
        ConstructionTests.RunUntilWorking(sim, second);
        Assert.Equal(0, CastleStock(sim)[GoodIds.Saw]);
        Assert.Equal(2, sim.Statistics.TotalConsumed(0, GoodIds.Saw));
    }

    [Fact]
    public void Demolishing_the_workplace_frees_the_worker_as_a_carrier()
    {
        var sim = Simulation.Create(TwoPlayers());
        int axes = CastleStock(sim)[GoodIds.Axe];
        int id = Woodcutter(sim, 0);
        ConstructionTests.RunUntilWorking(sim, id);
        int worker = WorkerOf(sim, id).Id;
        Run(sim, BuildingCommands.Demolish(0, 1, id));
        Assert.Equal(0, sim.RejectedCommands);
        var s = sim.Settlers.All[sim.Settlers.IndexOf(worker)];
        Assert.Equal((SettlerKind.Carrier, id), (s.Kind, s.HomeId));
        Assert.DoesNotContain(sim.Settlers.All, x => x.Kind == SettlerKind.Worker);
        Assert.Equal(axes - 1, CastleStock(sim)[GoodIds.Axe]); // the tool is not refunded (ASSUMPTION)
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
        // The freed carrier takes transport jobs again.
        Build(sim, BuildingIds.GuardTowerSmall, 2);
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void A_tool_on_its_way_to_a_demolished_building_goes_back_to_storage()
    {
        foreach (var state in new[] { JobState.ToPickup, JobState.Carrying })
        {
            var sim = Simulation.Create(TwoPlayers());
            int axes = CastleStock(sim)[GoodIds.Axe];
            int id = Woodcutter(sim, 0);
            Run(sim);
            int carrier = EmployJobs(sim).Single().CarrierId;
            for (int turns = 0; EmployJobs(sim).Single().State != state; turns++)
            {
                Assert.True(turns < 100, "the axe was never picked up");
                Run(sim);
            }
            Run(sim, BuildingCommands.Demolish(0, 1, id));
            for (int turns = 0; sim.Logistics.All.Any(j => j.CarrierId == carrier); turns++)
            {
                Assert.True(turns < 500, "the tool never came back");
                Run(sim);
            }
            Assert.Equal(axes, CastleStock(sim)[GoodIds.Axe]);
            Assert.Equal(0, sim.Statistics.TotalConsumed(0, GoodIds.Axe));
            Assert.DoesNotContain(sim.Settlers.All, x => x.Kind == SettlerKind.Worker);
        }
    }

    /// <summary>Byte offset of the settler at list index <paramref name="index"/> in the settlers section.</summary>
    private static int SettlerOffset(Simulation sim, int index)
    {
        int offset = 8; // next id, count
        for (int i = 0; i < index; i++) offset += 27 + 4 * sim.Settlers.PathAt(i).Count;
        return offset;
    }

    [Fact]
    public void Corrupt_workers_are_rejected_on_load()
    {
        var sim = Simulation.Create(TwoPlayers());
        int a = Woodcutter(sim, 0), b = Woodcutter(sim, 1);
        // A cancelled site leaves a dead building id.
        var start = sim.StartOf(0)!.Value;
        int dead = 0;
        for (int y = start.Y - 20; y <= start.Y + 20 && dead == 0; y++)
            for (int x = start.X - 20; x <= start.X + 20 && dead == 0; x++)
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, 0, BuildingIds.GuardTowerSmall, x, y) == PlacementResult.Ok)
                {
                    Run(sim, BuildingCommands.Place(0, 2, BuildingIds.GuardTowerSmall, x, y));
                    dead = sim.Buildings.All[^1].Id;
                    Run(sim, BuildingCommands.Cancel(0, 3, dead));
                }
        Assert.Equal(0, sim.RejectedCommands);
        Assert.False(sim.Buildings.TryGet(dead, out _));
        ConstructionTests.RunUntilWorking(sim, a);
        ConstructionTests.RunUntilWorking(sim, b);
        Run(sim);
        Assert.True(ConstructionTests.Get(sim, a).Cycle > 0);
        var w = new CanonicalWriter(1 << 16);
        sim.Settlers.WriteTo(w);
        var good = w.ToArray();
        void Load(byte[] settlerBytes)
        {
            var settlers = Settlers.ReadFrom(new CanonicalReader(settlerBytes), sim.Map.Edge, sim.Players.Count, sim.Buildings.NextId);
            var jobs = new CanonicalWriter(1 << 16);
            sim.Logistics.WriteTo(jobs);
            Logistics.ReadFrom(new CanonicalReader(jobs.ToArray()), sim.Map.Edge, sim.Players.Count, sim.Buildings, settlers);
        }
        Load(good);
        int atA = SettlerOffset(sim, sim.Settlers.IndexOf(WorkerOf(sim, a).Id));
        int atB = SettlerOffset(sim, sim.Settlers.IndexOf(WorkerOf(sim, b).Id));
        byte[] With(int offset, int value, int size, bool workerOfB = false)
        {
            int at = workerOfB ? atB : atA;
            var bytes = (byte[])good.Clone();
            if (size == 1) bytes[at + offset] = (byte)value;
            else if (size == 2) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + offset), (ushort)value);
            else BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + offset), value);
            return bytes;
        }
        var cases = new (byte[] Bytes, string Message)[]
        {
            (With(4, 2, 1), "Invalid settler"),
            (With(17, 5, 2), "A worker neither walks, waits nor carries"),
            (With(6, 1, 4), "Worker of a building that takes none"), // the castle
            (With(6, a, 4, workerOfB: true), "More than one worker for a building"),
            (With(6, dead, 4), "Work cycle running without its worker"),
        };
        foreach (var (bytes, message) in cases)
            Assert.Equal(message, Assert.Throws<InvalidDataException>(() => Load(bytes)).Message);
    }

    [Fact]
    public void Corrupt_worker_jobs_are_rejected_on_load()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = Build(sim, BuildingIds.Mill, 0);
        Run(sim);
        Assert.Single(sim.Logistics.All);
        var w = new CanonicalWriter(4096);
        sim.Logistics.WriteTo(w);
        var good = w.ToArray();
        Logistics Read(byte[] bytes) =>
            Logistics.ReadFrom(new CanonicalReader(bytes), sim.Map.Edge, sim.Players.Count, sim.Buildings, sim.Settlers);
        Read(good);
        const int first = 8; // next id, count
        var cases = new (int Offset, byte Value)[]
        {
            (first + 11, 1),    // a tool-less worker job sourced at the castle
            (first + 19, 0),    // a tool-less worker job waiting for a pickup
            (first + 20, 2),    // unknown kind
            (first + 20, 0),    // a transport of the non-good NoTool
        };
        foreach (var (offset, value) in cases)
        {
            var bytes = (byte[])good.Clone();
            bytes[offset] = value;
            Assert.Throws<InvalidDataException>(() => Read(bytes));
        }
        Assert.Equal(id, sim.Logistics.All[0].DestinationId);
    }
}
