using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Sim.World;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>Calendar, season length lobby option and seasonal production speed (docs/12-needs-seasons-weather.md §2.1).</summary>
public class SeasonTests
{
    private static MatchSetup TwoPlayers(SeasonLength seasons, ulong seed = 1) => new(
        new MapSpec(seed, MapSize.Small, 2), seed,
        new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Ai, 1, "rivermen") }, seasons);

    [Fact]
    public void The_calendar_starts_in_spring_and_cycles_through_the_seasons()
    {
        Assert.Equal(new[] { 0, 2400, 3600, 5400 },
            new[] { SeasonLength.Off, SeasonLength.Short, SeasonLength.Normal, SeasonLength.Long }.Select(Calendar.SeasonTicks));
        const SeasonLength n = SeasonLength.Normal;
        Assert.Equal(Season.Spring, Calendar.SeasonAt(0, n));
        Assert.Equal(Season.Spring, Calendar.SeasonAt(3599, n));
        Assert.Equal(Season.Summer, Calendar.SeasonAt(3600, n));
        Assert.Equal(Season.Autumn, Calendar.SeasonAt(7200, n));
        Assert.Equal(Season.Winter, Calendar.SeasonAt(14399, n));
        Assert.Equal(Season.Spring, Calendar.SeasonAt(14400, n));
        Assert.Equal((0, 1, 2), (Calendar.YearAt(14399, n), Calendar.YearAt(14400, n), Calendar.YearAt(28800, n)));
        Assert.Equal((0, 1, 3599), (Calendar.TicksIntoSeason(3600, n), Calendar.TicksIntoSeason(3601, n), Calendar.TicksIntoSeason(7199, n)));
        // Without seasons it is summer forever.
        foreach (int tick in new[] { 0, 7200, int.MaxValue })
            Assert.Equal((Season.Summer, 0, tick), (Calendar.SeasonAt(tick, SeasonLength.Off), Calendar.YearAt(tick, SeasonLength.Off),
                Calendar.TicksIntoSeason(tick, SeasonLength.Off)));
        Assert.Equal(Season.Autumn, Calendar.SeasonAt(int.MaxValue, SeasonLength.Long)); // 2^31 - 1 ticks = year 99 420, no overflow
    }

    [Fact]
    public void Season_speeds_are_compiled_from_data()
    {
        var farm = BuildingCatalog.All[BuildingIds.Farm].Production!;
        Assert.Equal(new[] { 100, 100, 125, 0 }, farm.SeasonSpeed);
        Assert.Equal((300, 300, 240, 300), (farm.CycleTicksIn(Season.Spring), farm.CycleTicksIn(Season.Summer),
            farm.CycleTicksIn(Season.Autumn), farm.CycleTicksIn(Season.Winter)));
        Assert.False(farm.WorksIn(Season.Winter));
        Assert.True(farm.WorksIn(Season.Autumn));
        var fisher = BuildingCatalog.All[BuildingIds.Fisher].Production!;
        Assert.Equal(new[] { 100, 100, 100, 50 }, fisher.SeasonSpeed);
        Assert.Equal((150, 300, 300), (fisher.CycleTicksIn(Season.Summer), fisher.CycleTicksIn(Season.Winter), fisher.MaxCycleTicks));
        Assert.All(BuildingCatalog.All.Where(d => d.Production != null && d.Index != BuildingIds.Farm && d.Index != BuildingIds.Fisher),
            d => Assert.Equal(new[] { 100, 100, 100, 100 }, d.Production!.SeasonSpeed));
        // Rounded up: 7 ticks at 300 % → 3 ticks.
        var fast = new ProductionDefinition(System.Array.Empty<ushort[]>(), System.Array.Empty<int>(), 0, 7, HarvestSource.None, 0,
            seasonSpeed: new[] { 300, 25, 100, 0 });
        Assert.Equal((3, 28, 7, 7, 28), (fast.CycleTicksIn(Season.Spring), fast.CycleTicksIn(Season.Summer), fast.CycleTicksIn(Season.Autumn),
            fast.CycleTicksIn(Season.Winter), fast.MaxCycleTicks));
        foreach (var bad in new[] { new[] { 100, 100, 100 }, new[] { 100, 24, 100, 100 }, new[] { 401, 100, 100, 100 }, new[] { -1, 100, 100, 100 } })
            Assert.Throws<System.ArgumentException>(() => new ProductionDefinition(System.Array.Empty<ushort[]>(), System.Array.Empty<int>(), 0, 7,
                HarvestSource.None, 0, seasonSpeed: bad));
    }

    [Fact]
    public void The_season_length_is_part_of_the_setup_and_the_save()
    {
        Assert.Equal(SeasonLength.Normal, new MatchSetup(new MapSpec(1, MapSize.Small, 2), 1,
            new[] { new SlotInfo(SlotKind.Human, 0, "rivermen") }).Seasons);
        Assert.Throws<System.ArgumentException>(() => TwoPlayers((SeasonLength)4));
        foreach (var length in new[] { SeasonLength.Off, SeasonLength.Short, SeasonLength.Long })
        {
            var w = new CanonicalWriter(256);
            TwoPlayers(length).WriteTo(w);
            Assert.Equal(length, MatchSetup.ReadFrom(new CanonicalReader(w.ToArray())).Seasons);
        }
        var bytes = SetupBytes(TwoPlayers(SeasonLength.Short));
        bytes[^1] = 4;
        Assert.Throws<InvalidDataException>(() => MatchSetup.ReadFrom(new CanonicalReader(bytes)));

        var sim = Simulation.Create(TwoPlayers(SeasonLength.Long));
        for (int i = 0; i < 10; i++) ConstructionTests.Run(sim);
        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(SeasonLength.Long, loaded.Setup.Seasons);
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
    }

    private static byte[] SetupBytes(MatchSetup setup)
    {
        var w = new CanonicalWriter(256);
        setup.WriteTo(w);
        return w.ToArray();
    }

    /// <summary>Places a building at the first valid spot near slot 0's start where <paramref name="accept"/> holds, then completes and staffs it.</summary>
    private static int Build(Simulation sim, ushort type, ushort seq, System.Func<int, int, bool> accept)
    {
        var s = sim.StartOf(0)!.Value;
        for (int y = s.Y - 20; y <= s.Y + 20; y++)
            for (int x = s.X - 20; x <= s.X + 20; x++)
            {
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, 0, type, x, y) != PlacementResult.Ok || !accept(x, y)) continue;
                ConstructionTests.Run(sim, BuildingCommands.Place(0, seq, type, x, y));
                Assert.Equal(0, sim.RejectedCommands);
                int id = sim.Buildings.All.Last(b => b.Owner == 0).Id;
                ConstructionTests.RunUntilComplete(sim, id);
                return id;
            }
        throw new Xunit.Sdk.XunitException($"No spot for {BuildingCatalog.All[type].Id}");
    }

    private static int FishAround(Simulation sim, int x, int y)
    {
        var b = new Building(1, BuildingIds.Fisher, 0, x, y, 0, BuildingState.Complete, 0);
        int r = b.Definition.Production!.Radius, edge = sim.Map.Edge, n = 0;
        for (int ty = System.Math.Max(0, b.CenterY - r); ty <= System.Math.Min(edge - 1, b.CenterY + r); ty++)
            for (int tx = System.Math.Max(0, b.CenterX - r); tx <= System.Math.Min(edge - 1, b.CenterX + r); tx++)
            {
                int t = ty * edge + tx, dx = tx - b.CenterX, dy = ty - b.CenterY;
                if (dx * dx + dy * dy <= r * r && sim.Territory.OwnerAt(t) == 0 && Harvest.Matches(sim.Map, t, HarvestSource.Fish)) n += sim.Map.Amount[t];
            }
        return n;
    }

    [Fact]
    public void Farms_rest_in_winter_and_harvest_faster_in_autumn_and_fishers_slow_down_on_ice()
    {
        var sim = Simulation.Create(TwoPlayers(SeasonLength.Short));
        int season = Calendar.SeasonTicks(SeasonLength.Short);
        int farm = Build(sim, BuildingIds.Farm, 0, (x, y) => ProductionTests.ObjectsAround(sim, 0, BuildingIds.Farm, x, y) >= 4);
        Assert.Equal(Season.Spring, sim.Season);
        // Longest running cycle seen per season, and the cycles started in it (a turn whose cycle is lower than the last, or follows 0).
        var farmMax = new int[4];
        var farmStarts = new int[4];
        var fisherMax = new int[4];
        int fisher = 0, lastFarm = 0;
        bool winterIdle = false;
        while (sim.Tick < 5 * season)
        {
            if (fisher == 0 && sim.Tick >= 2 * season)
                fisher = Build(sim, BuildingIds.Fisher, 1, (x, y) => FishAround(sim, x, y) >= 30);
            var s = (int)Calendar.SeasonAt(sim.Tick, SeasonLength.Short);
            ConstructionTests.RunFed(sim); // a Short worker would slow the cycles measured here
            int cycle = ConstructionTests.Get(sim, farm).Cycle;
            farmMax[s] = System.Math.Max(farmMax[s], cycle);
            if (cycle > 0 && (lastFarm == 0 || cycle < lastFarm)) farmStarts[s]++;
            if (s == (int)Season.Winter && lastFarm > 0 && cycle == 0) winterIdle = true;
            if (winterIdle && s == (int)Season.Winter) Assert.Equal(0, cycle); // once the autumn cycle ended, no new one starts
            lastFarm = cycle;
            if (fisher != 0) fisherMax[s] = System.Math.Max(fisherMax[s], ConstructionTests.Get(sim, fisher).Cycle);
            if (sim.Tick == 4 * season - 50)
            {
                // A winter fisher cycle longer than the summer one survives save and load.
                Assert.InRange(ConstructionTests.Get(sim, fisher).Cycle, 0, 299);
                var loaded = Simulation.Load(sim.Save());
                Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
            }
        }
        Assert.True(winterIdle);
        Assert.InRange(farmMax[(int)Season.Summer], 298, 299);
        Assert.InRange(farmMax[(int)Season.Autumn], 238, 239); // a summer cycle past 240 ticks ends at the first autumn tick
        Assert.InRange(farmStarts[(int)Season.Autumn], 9, 10);   // 2400 / 240
        Assert.InRange(farmStarts[(int)Season.Summer], 7, 8);    // 2400 / 300
        Assert.Equal(0, farmStarts[(int)Season.Winter]);
        Assert.True(farmStarts[(int)Season.Spring] > 0); // back to work in the second spring
        Assert.InRange(fisherMax[(int)Season.Winter], 298, 299);
        Assert.InRange(fisherMax[(int)Season.Spring], 148, 149); // a winter cycle past 150 ticks ends at the first spring tick
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void A_work_cycle_longer_than_the_last_season_allows_is_rejected_on_load()
    {
        // A winter fisher cycle past 150 ticks is valid with seasons, invalid in an eternal summer.
        var sim = Simulation.Create(TwoPlayers(SeasonLength.Short));
        int season = Calendar.SeasonTicks(SeasonLength.Short);
        while (sim.Tick < 3 * season) ConstructionTests.RunFed(sim);
        int fisher = Build(sim, BuildingIds.Fisher, 0, (x, y) => FishAround(sim, x, y) >= 4);
        for (int turns = 0; ConstructionTests.Get(sim, fisher).Cycle <= 150; turns++)
        {
            Assert.True(turns < 400, "fisher never worked");
            ConstructionTests.RunFed(sim);
        }
        Assert.Equal(Season.Winter, sim.Season);
        var save = sim.Save();
        Assert.Equal(sim.ComputeHash(), Simulation.Load(save).ComputeHash());
        int seasonsByte = 4 + 2 + 14 + SetupBytes(sim.Setup).Length - 1; // magic, format, GameVersion, then the setup ending in its season length
        Assert.Equal((byte)SeasonLength.Short, save[seasonsByte]);
        save[seasonsByte] = (byte)SeasonLength.Off;
        Assert.Throws<InvalidDataException>(() => Simulation.Load(save));
    }
}
