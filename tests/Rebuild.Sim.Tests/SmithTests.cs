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

/// <summary>Toolsmith and weaponsmith: output choice by per-player quota (docs/06-economy.md §4).</summary>
public class SmithTests
{
    private static readonly int[] Tools =
    {
        GoodIds.Axe, GoodIds.Saw, GoodIds.Pickaxe, GoodIds.Shovel, GoodIds.Hammer, GoodIds.Scythe, GoodIds.FishingRod,
        GoodIds.HuntingBow, GoodIds.Cleaver, GoodIds.Bucket,
    };
    private static readonly int[] Weapons = { GoodIds.Sword, GoodIds.Spear, GoodIds.Bow };

    private static MatchSetup TwoPlayers() => new(
        new MapSpec(1, MapSize.Small, 2), 1,
        new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Ai, 1, "rivermen") });

    private static void RunTicks(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks / Simulation.TicksPerTurn; i++) ConstructionTests.Run(sim);
    }

    /// <summary>Places and completes a smith of slot 0 near its start; returns the simulation, its id and the castle stock.</summary>
    private static (Simulation Sim, int Id, int[] Stock) Smith(ushort type)
    {
        var sim = Simulation.Create(TwoPlayers());
        var s = sim.StartOf(0)!.Value;
        for (int y = s.Y - 20; y <= s.Y + 20; y++)
            for (int x = s.X - 20; x <= s.X + 20; x++)
            {
                if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, 0, type, x, y) != PlacementResult.Ok) continue;
                ConstructionTests.Run(sim, BuildingCommands.Place(0, 0, type, x, y));
                int id = sim.Buildings.All.Last(b => b.Owner == 0).Id;
                ConstructionTests.RunUntilComplete(sim, id);
                int castle = sim.Buildings.All.ToList().FindIndex(b => b.Owner == 0 && b.Type == BuildingIds.Castle);
                var stock = sim.Buildings.StockAt(castle)!;
                foreach (int g in Tools.Concat(Weapons)) stock[g] = 0; // count only what the smith makes
                return (sim, id, stock);
            }
        throw new Xunit.Sdk.XunitException("No spot for the smith");
    }

    /// <summary>Units of a good in slot 0's stocks, piles and transport jobs.</summary>
    private static int Units(Simulation sim, int good)
    {
        int n = sim.Logistics.All.Count(j => j.Owner == 0 && j.Good == good);
        foreach (var b in sim.Buildings.All.Where(b => b.Owner == 0))
        {
            n += sim.Buildings.StockOf(b.Id)?[good] ?? 0;
            if (b.Definition.Production is { } p && sim.Buildings.PilesOf(b.Id) is { } piles && p.OutputIndexOf(good) >= 0)
                n += piles[p.Inputs.Count + p.OutputIndexOf(good)];
        }
        return n;
    }

    [Fact]
    public void Smiths_are_compiled_from_data()
    {
        var tool = BuildingCatalog.All[BuildingIds.Toolsmith].Production!;
        Assert.Equal(new[] { (ushort)GoodIds.Iron, (ushort)GoodIds.Plank }, tool.Inputs);
        Assert.Equal(Tools.Select(g => (ushort)g), tool.Outputs);
        Assert.Equal((ushort)GoodIds.Axe, tool.Output);
        Assert.True(tool.HasChoice);
        var weapon = BuildingCatalog.All[BuildingIds.Weaponsmith].Production!;
        Assert.Equal(new[] { (ushort)GoodIds.Iron, (ushort)GoodIds.Coal }, weapon.Inputs);
        Assert.Equal(Weapons.Select(g => (ushort)g), weapon.Outputs);
        Assert.False(BuildingCatalog.All[BuildingIds.Sawmill].Production!.HasChoice);
        Assert.Equal(-1, BuildingCatalog.All[BuildingIds.Forester].Production!.OutputIndexOf(ProductionDefinition.NoOutput));

        foreach (int g in Tools.Concat(Weapons)) Assert.True(ProductionQuotas.IsQuotaGood(g));
        Assert.False(ProductionQuotas.IsQuotaGood(GoodIds.Plank));
        Assert.Throws<System.ArgumentException>(() => new ProductionDefinition(System.Array.Empty<ushort[]>(), System.Array.Empty<int>(),
            (ushort)GoodIds.Axe, 10, HarvestSource.None, 0, outputs: new[] { (ushort)GoodIds.Axe }));                // one choice
        Assert.Throws<System.ArgumentException>(() => new ProductionDefinition(System.Array.Empty<ushort[]>(), System.Array.Empty<int>(),
            (ushort)GoodIds.Axe, 10, HarvestSource.None, 0, outputs: new[] { (ushort)GoodIds.Saw, (ushort)GoodIds.Axe })); // output not first
        Assert.Throws<System.ArgumentException>(() => new ProductionDefinition(System.Array.Empty<ushort[]>(), System.Array.Empty<int>(),
            (ushort)GoodIds.Axe, 10, HarvestSource.None, 0, outputs: new[] { (ushort)GoodIds.Axe, (ushort)GoodIds.Axe })); // duplicate
    }

    [Fact]
    public void Round_robin_follows_the_weights_evenly_interleaved()
    {
        var q = new ProductionQuotas(1);
        var p = BuildingCatalog.All[BuildingIds.Weaponsmith].Production!;
        // Default: equal weights, data order.
        Assert.Equal(new[] { 0, 1, 2, 0, 1, 2 }, Enumerable.Range(0, 6).Select(_ => q.Pick(0, p)));
        // 3 : 1 : 0 — swords three times as often, interleaved, never a bow.
        q.Set(0, GoodIds.Sword, 3);
        q.Set(0, GoodIds.Spear, 1);
        q.Set(0, GoodIds.Bow, 0);
        var picks = Enumerable.Range(0, 8).Select(_ => q.Pick(0, p)).ToArray();
        Assert.Equal(new[] { 0, 0, 1, 0, 0, 0, 1, 0 }, picks);
        Assert.Equal(0, q.CreditOf(0, GoodIds.Bow));
        // All zero: nothing to make.
        q.Set(0, GoodIds.Sword, 0);
        q.Set(0, GoodIds.Spear, 0);
        Assert.Equal(-1, q.Pick(0, p));
        // Single-output buildings never consult the quotas.
        Assert.Equal(0, q.Pick(0, BuildingCatalog.All[BuildingIds.Sawmill].Production!));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => q.Set(0, GoodIds.Plank, 1));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => q.Set(0, GoodIds.Axe, ProductionQuotas.MaxWeight + 1));
    }

    [Fact]
    public void A_toolsmith_makes_tools_in_quota_proportion()
    {
        var (sim, id, stock) = Smith(BuildingIds.Toolsmith);
        // Only hammers and axes, 2 : 1.
        ushort seq = 1;
        foreach (int g in Tools)
            ConstructionTests.Run(sim, EconomyCommands.Quota(0, seq++, (ushort)g, (byte)(g == GoodIds.Hammer ? 2 : g == GoodIds.Axe ? 1 : 0)));
        Assert.Equal(0, sim.RejectedCommands);
        stock[GoodIds.Iron] = 6;
        stock[GoodIds.Plank] = 20;
        RunTicks(sim, 10 * BuildingCatalog.All[BuildingIds.Toolsmith].Production!.CycleTicks);
        Assert.Equal(0, Units(sim, GoodIds.Iron));
        Assert.Equal(4, Units(sim, GoodIds.Hammer));
        Assert.Equal(2, Units(sim, GoodIds.Axe));
        foreach (int g in Tools.Where(g => g != GoodIds.Hammer && g != GoodIds.Axe)) Assert.Equal(0, Units(sim, g));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void A_weaponsmith_with_every_weight_zero_idles_until_a_quota_is_set()
    {
        var (sim, id, stock) = Smith(BuildingIds.Weaponsmith);
        ushort seq = 1;
        foreach (int g in Weapons) ConstructionTests.Run(sim, EconomyCommands.Quota(0, seq++, (ushort)g, 0));
        stock[GoodIds.Iron] = 3;
        stock[GoodIds.Coal] = 3;
        int cycle = BuildingCatalog.All[BuildingIds.Weaponsmith].Production!.CycleTicks;
        RunTicks(sim, 3 * cycle);
        Assert.Equal(0, ConstructionTests.Get(sim, id).Cycle);
        Assert.Equal(Production.InputTarget - 1, sim.Buildings.PilesOf(id)![0]); // inputs delivered (3 of 3 iron) but untouched
        Assert.Equal(0, Weapons.Sum(g => Units(sim, g)));

        ConstructionTests.Run(sim, EconomyCommands.Quota(0, seq++, (ushort)GoodIds.Spear, 5));
        RunTicks(sim, 4 * cycle);
        Assert.Equal(3, Units(sim, GoodIds.Spear));
        Assert.Equal(0, Units(sim, GoodIds.Sword) + Units(sim, GoodIds.Bow));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Smith_output_piles_are_carried_to_the_castle_by_good()
    {
        var (sim, id, stock) = Smith(BuildingIds.Weaponsmith);
        stock[GoodIds.Iron] = 6;
        stock[GoodIds.Coal] = 6;
        RunTicks(sim, 8 * BuildingCatalog.All[BuildingIds.Weaponsmith].Production!.CycleTicks);
        // Equal default weights: two of each weapon, all of it in the castle by now.
        foreach (int g in Weapons)
        {
            Assert.Equal(2, Units(sim, g));
            Assert.Equal(2, stock[g]);
        }
    }

    [Fact]
    public void Quota_commands_are_validated()
    {
        var sim = Simulation.Create(TwoPlayers());
        ConstructionTests.Run(sim,
            EconomyCommands.Quota(0, 0, (ushort)GoodIds.Sword, 7),
            EconomyCommands.Quota(1, 0, (ushort)GoodIds.Bucket, 0),
            EconomyCommands.Quota(0, 1, (ushort)GoodIds.Plank, 1),                                     // no quota good
            EconomyCommands.Quota(0, 2, (ushort)GoodIds.Axe, ProductionQuotas.MaxWeight + 1),          // weight too high
            EconomyCommands.Quota(0, 3, (ushort)GoodCatalog.All.Count, 1),                             // unknown good
            new Command(CommandType.SetToolProductionQuota, 0, 0, 4, new byte[] { 1, 2 }),             // short payload
            EconomyCommands.Quota(5, 0, (ushort)GoodIds.Axe, 1));                                      // unknown slot
        Assert.Equal(5, sim.RejectedCommands);
        Assert.Equal(7, sim.Quotas.WeightOf(0, GoodIds.Sword));
        Assert.Equal(ProductionQuotas.DefaultWeight, sim.Quotas.WeightOf(1, GoodIds.Sword));
        Assert.Equal(0, sim.Quotas.WeightOf(1, GoodIds.Bucket));
        Assert.Equal(ProductionQuotas.DefaultWeight, sim.Quotas.WeightOf(0, GoodIds.Axe));
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Fact]
    public void Corrupt_smith_state_is_rejected_on_load()
    {
        var smith = new Building(5, BuildingIds.Weaponsmith, 0, 10, 10, 0, BuildingState.Complete, 0, Cycle: 1, Choice: 2);
        Assert.True(Construction.IsConsistent(smith));
        Assert.False(Construction.IsConsistent(smith with { Choice = 3 }));             // no fourth weapon
        Assert.False(Construction.IsConsistent(smith with { Cycle = 0 }));              // idle with a choice
        var mill = new Building(6, BuildingIds.Sawmill, 0, 10, 10, 0, BuildingState.Complete, 0, Cycle: 1);
        Assert.False(Construction.IsConsistent(mill with { Choice = 1 }));              // single output
        Assert.False(Construction.IsConsistent(mill with { State = BuildingState.ConstructionSite, Cycle = 0, Choice = 1 }));

        // Quotas: per player, per quota good in good index order, weight(1) credit(4).
        var sim = Simulation.Create(TwoPlayers());
        var w = new CanonicalWriter(512);
        sim.Quotas.WriteTo(w);
        var good = w.ToArray();
        ProductionQuotas.ReadFrom(new CanonicalReader(good), sim.Players.Count);
        var cases = new System.Action<byte[]>[]
        {
            b => b[0] = ProductionQuotas.MaxWeight + 1,                           // weight too high
            b => { b[0] = 0; b[1] = 1; },                                        // credit at weight 0
            b => System.BitConverter.GetBytes(100000).CopyTo(b, 1),              // credit out of range
            b => b[1] = 1,                                                       // credits of the smith's outputs do not sum to 0
        };
        foreach (var corrupt in cases)
        {
            var bad = (byte[])good.Clone();
            corrupt(bad);
            Assert.Throws<InvalidDataException>(() => ProductionQuotas.ReadFrom(new CanonicalReader(bad), sim.Players.Count));
        }
        // Balanced non-zero credits (as after some picks) load.
        var q = new ProductionQuotas(sim.Players.Count);
        for (int i = 0; i < 5; i++) q.Pick(1, BuildingCatalog.All[BuildingIds.Toolsmith].Production!);
        var picked = new CanonicalWriter(512);
        q.WriteTo(picked);
        var loaded = ProductionQuotas.ReadFrom(new CanonicalReader(picked.ToArray()), sim.Players.Count);
        Assert.Equal(q.CreditOf(1, GoodIds.Axe), loaded.CreditOf(1, GoodIds.Axe));
        Assert.NotEqual(0, loaded.CreditOf(1, GoodIds.Axe));
    }
}
