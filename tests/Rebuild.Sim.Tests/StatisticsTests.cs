using System;
using System.IO;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Goods;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Sim.World;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>Production statistics: per player, per good, per-minute ring buffer of produced/consumed units (docs/06-economy.md §4).</summary>
public class StatisticsTests
{
    private static MatchSetup TwoPlayers() => new(
        new MapSpec(1, MapSize.Small, 2), 1,
        new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Ai, 1, "rivermen") });

    /// <summary>Advances a standalone statistics object tick by tick from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static void AdvanceTo(ProductionStatistics s, int from, int to)
    {
        for (int t = from + 1; t <= to; t++) s.Advance(t);
    }

    [Fact]
    public void Counts_go_into_the_running_minute_and_age_out_after_the_buffer()
    {
        const int m = ProductionStatistics.TicksPerMinute;
        var s = new ProductionStatistics(2, tick: 0);
        s.Produce(0, GoodIds.Log);
        s.Produce(0, GoodIds.Log);
        s.Consume(1, GoodIds.Plank);
        Assert.Equal(2, s.Produced(0, GoodIds.Log, 0));
        Assert.Equal(0, s.Produced(1, GoodIds.Log, 0));
        Assert.Equal(1, s.Consumed(1, GoodIds.Plank, 0));
        Assert.Equal(0, s.Produced(0, GoodIds.Log, 1)); // before match start

        AdvanceTo(s, 0, m - 1);
        Assert.Equal(0, s.CurrentMinute);
        s.Produce(0, GoodIds.Log);
        AdvanceTo(s, m - 1, m);
        Assert.Equal(1, s.CurrentMinute);
        Assert.Equal(0, s.Produced(0, GoodIds.Log, 0));
        Assert.Equal(3, s.Produced(0, GoodIds.Log, 1));
        Assert.Equal(1, s.Consumed(1, GoodIds.Plank, 1));

        // Minute 0 stays readable until minute 60 reuses its slot; totals keep everything.
        AdvanceTo(s, m, ProductionStatistics.Minutes * m - 1);
        Assert.Equal(3, s.Produced(0, GoodIds.Log, ProductionStatistics.Minutes - 1));
        AdvanceTo(s, ProductionStatistics.Minutes * m - 1, ProductionStatistics.Minutes * m);
        Assert.Equal(ProductionStatistics.Minutes, s.CurrentMinute);
        Assert.Equal(0, s.Produced(0, GoodIds.Log, 0));
        Assert.Equal(0, s.Produced(0, GoodIds.Log, ProductionStatistics.Minutes)); // outside the buffer
        Assert.Equal(3, s.TotalProduced(0, GoodIds.Log));
        Assert.Equal(1, s.TotalConsumed(1, GoodIds.Plank));
        Assert.Equal(0, s.Produced(0, GoodIds.Log, -1));
    }

    [Fact]
    public void Write_and_read_round_trip_after_the_buffer_wrapped()
    {
        const int m = ProductionStatistics.TicksPerMinute;
        var s = new ProductionStatistics(2, tick: 0);
        int tick = 0;
        for (int minute = 0; minute < ProductionStatistics.Minutes + 5; minute++)
        {
            for (int k = 0; k <= minute % 7; k++) s.Produce(minute % 2, GoodIds.Plank);
            s.Consume(0, GoodIds.Stone);
            AdvanceTo(s, tick, tick + m);
            tick += m;
        }
        var w = new CanonicalWriter(1 << 16);
        s.WriteTo(w);
        var loaded = ProductionStatistics.ReadFrom(new CanonicalReader(w.ToArray()), 2, tick);
        Assert.Equal(s.CurrentMinute, loaded.CurrentMinute);
        for (int p = 0; p < 2; p++)
            for (int ago = 0; ago <= ProductionStatistics.Minutes; ago++)
            {
                Assert.Equal(s.Produced(p, GoodIds.Plank, ago), loaded.Produced(p, GoodIds.Plank, ago));
                Assert.Equal(s.Consumed(p, GoodIds.Stone, ago), loaded.Consumed(p, GoodIds.Stone, ago));
            }
        Assert.Equal(s.TotalProduced(1, GoodIds.Plank), loaded.TotalProduced(1, GoodIds.Plank));
        var again = new CanonicalWriter(1 << 16);
        loaded.WriteTo(again);
        Assert.Equal(w.ToArray(), again.ToArray());
    }

    [Fact]
    public void Construction_materials_count_as_consumed_and_harvests_as_produced()
    {
        var sim = Simulation.Create(TwoPlayers());
        int id = ProductionTests.Woodcutter(sim, trees: 3);
        var def = BuildingCatalog.All[BuildingIds.Woodcutter];
        Assert.Equal(def.CostPlanks, sim.Statistics.TotalConsumed(0, GoodIds.Plank));
        Assert.Equal(def.CostStone, sim.Statistics.TotalConsumed(0, GoodIds.Stone));
        Assert.Equal(0, sim.Statistics.TotalConsumed(1, GoodIds.Plank));
        Assert.Equal(0, sim.Statistics.TotalProduced(0, GoodIds.Log));

        // Count finished cycles turn by turn for three minutes and compare with the statistics.
        int cycles = 0, lastCycle = 0;
        for (int turn = 0; turn < 3 * ProductionStatistics.TicksPerMinute / Simulation.TicksPerTurn; turn++)
        {
            ConstructionTests.Run(sim);
            sim.Buildings.TryGet(id, out var b);
            if (b.Cycle < lastCycle) cycles++; // a cycle ended within the turn
            lastCycle = b.Cycle;
        }
        Assert.True(cycles > 0);
        Assert.Equal(cycles, sim.Statistics.TotalProduced(0, GoodIds.Log));
        long sum = 0;
        for (int ago = 0; ago <= sim.Statistics.CurrentMinute; ago++) sum += sim.Statistics.Produced(0, GoodIds.Log, ago);
        Assert.Equal(cycles, sum);
        Assert.Equal(0, sim.Statistics.TotalProduced(1, GoodIds.Log));
    }

    [Fact]
    public void Save_and_load_keep_statistics_and_the_hash()
    {
        var sim = Simulation.Create(TwoPlayers());
        ProductionTests.Woodcutter(sim, trees: 3);
        for (int i = 0; i < 400; i++) ConstructionTests.Run(sim);
        Assert.True(sim.Statistics.TotalProduced(0, GoodIds.Log) > 0);
        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
        Assert.Equal(sim.Statistics.TotalProduced(0, GoodIds.Log), loaded.Statistics.TotalProduced(0, GoodIds.Log));
        for (int i = 0; i < 300; i++)
        {
            ConstructionTests.Run(sim);
            ConstructionTests.Run(loaded);
        }
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
    }

    /// <summary>Offset of the statistics block, the last part of a save.</summary>
    private static int StatisticsOffset(Simulation sim, byte[] save)
    {
        var w = new CanonicalWriter(1 << 16);
        sim.Statistics.WriteTo(w);
        return save.Length - w.Length;
    }

    [Fact]
    public void Load_rejects_negative_counts()
    {
        var sim = Simulation.Create(TwoPlayers());
        var save = sim.Save();
        // First value: player 0, slot 0, good 0, produced.
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(save.AsSpan(StatisticsOffset(sim, save)), -1);
        var e = Assert.Throws<InvalidDataException>(() => Simulation.Load(save));
        Assert.Contains("Negative production statistics", e.Message);
    }

    [Fact]
    public void Load_rejects_totals_below_the_per_minute_counts()
    {
        var sim = Simulation.Create(TwoPlayers());
        var save = sim.Save();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(save.AsSpan(StatisticsOffset(sim, save)), 5);
        var e = Assert.Throws<InvalidDataException>(() => Simulation.Load(save));
        Assert.Contains("totals below", e.Message);
    }

    [Fact]
    public void Load_rejects_a_negative_tick()
    {
        var sim = Simulation.Create(TwoPlayers());
        var save = sim.Save();
        // The tick directly follows the setup: find it as the first value of the state after the header.
        var w = new CanonicalWriter(1 << 16);
        sim.WriteState(w);
        int tickOffset = save.Length - w.Length;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(save.AsSpan(tickOffset), -7200);
        var e = Assert.Throws<InvalidDataException>(() => Simulation.Load(save));
        Assert.Contains("Negative tick", e.Message);
    }
}
