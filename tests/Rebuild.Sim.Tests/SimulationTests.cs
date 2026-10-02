using System.IO;
using System.Linq;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Core;
using Rebuild.Sim.Cultures;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Tools;
using Xunit;

namespace Rebuild.Sim.Tests;

public class SimulationTests
{
    private static MatchSetup TwoPlayers(ulong seed = 1, string culture = "rivermen") => new(
        new MapSpec(seed, MapSize.Small, 2), seed,
        new[] { new SlotInfo(SlotKind.Human, 0, culture), new SlotInfo(SlotKind.Ai, 1, SlotInfo.RandomCulture) });

    [Fact]
    public void Cultures_are_resolved_into_tables()
    {
        var sim = Simulation.Create(TwoPlayers());
        var table = sim.CultureOf(0)!;
        Assert.Equal("rivermen", table.Culture.Id);
        Assert.Equal(125, table.Percent(ModifierKey.FarmYield));
        Assert.Equal(80, table.Percent(ModifierKey.MineYield));
        Assert.Equal(100, table.Percent(ModifierKey.WallHp));
        Assert.True(table.Has(CultureHook.SpearWithoutIron));
        Assert.False(table.Has(CultureHook.MountedUnits));
        Assert.NotNull(sim.CultureOf(1)); // random culture resolved
    }

    [Fact]
    public void Unknown_culture_is_rejected()
    {
        Assert.Throws<System.ArgumentException>(() => Simulation.Create(TwoPlayers(culture: "atlanteans")));
    }

    [Fact]
    public void Meta_commands_change_player_state()
    {
        var sim = Simulation.Create(TwoPlayers());
        sim.ExecuteTurn(new TurnBundle(0, new[] { MetaCommands.AiTakeover(0, AiDifficulty.Hard, 0) }));
        Assert.Equal(Controller.Ai, sim.Players[0].Controller);
        Assert.Equal(AiDifficulty.Hard, sim.Players[0].Difficulty);
        sim.ExecuteTurn(new TurnBundle(1, new[] { MetaCommands.ForSlot(CommandType.HumanResume, 0, 1) }));
        Assert.Equal(Controller.Human, sim.Players[0].Controller);
        sim.ExecuteTurn(new TurnBundle(2, new[] { MetaCommands.ForSlot(CommandType.PlayerLeft, 1, 2) }));
        Assert.Equal(PlayerStatus.Left, sim.Players[1].Status);
        Assert.False(sim.Players[1].CanAct);
        Assert.Equal(6, sim.Tick);
        Assert.Equal(0, sim.RejectedCommands);
    }

    [Fact]
    public void Invalid_commands_are_counted_noops()
    {
        var sim = Simulation.Create(TwoPlayers());
        ulong before = sim.ComputeHash();
        sim.ExecuteTurn(new TurnBundle(0, new[]
        {
            new Command(CommandType.PlayerLeft, 0, 0, 0, new byte[] { 1 }), // meta from a player slot
            new Command(CommandType.Move, Command.SystemSlot, 0, 0),         // gameplay from system slot
            MetaCommands.SetSpeed(0, 1),                                      // out of range
            MetaCommands.ForSlot(CommandType.Surrender, 7, 2),                // no such slot
        }));
        Assert.Equal(4, sim.RejectedCommands);
        Assert.All(sim.Players, p => Assert.Equal(PlayerStatus.Active, p.Status));
        Assert.NotEqual(before, sim.ComputeHash());
    }

    [Fact]
    public void Bundle_for_wrong_turn_throws()
    {
        var sim = Simulation.Create(TwoPlayers());
        Assert.Throws<System.InvalidOperationException>(() => sim.ExecuteTurn(TurnBundle.Empty(1)));
    }

    [Fact]
    public void Replay_is_deterministic_in_process()
    {
        var log = SampleLogs.MetaScript(seed: 3, turns: 500);
        Assert.Equal(Replay.Run(log), Replay.Run(CommandLog.FromBytes(log.ToBytes())));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(137)]
    [InlineData(499)]
    public void Save_load_equivalence(int saveAfterTurns)
    {
        var log = SampleLogs.MetaScript(seed: 5, turns: 500);
        var expected = Replay.Run(log);

        var sim = Simulation.Create(log.Setup);
        for (int t = 0; t < saveAfterTurns; t++) sim.ExecuteTurn(log.Bundles[t]);
        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
        for (int t = saveAfterTurns; t < log.Bundles.Count; t++)
        {
            loaded.ExecuteTurn(log.Bundles[t]);
            Assert.Equal(expected[t], loaded.ComputeHash());
        }
    }

    [Fact]
    public void Corrupt_save_is_rejected()
    {
        var save = Simulation.Create(TwoPlayers()).Save();
        Assert.Throws<InvalidDataException>(() => Simulation.Load(save.Take(save.Length - 1).ToArray()));
        Assert.Throws<InvalidDataException>(() => Simulation.Load(save.Append((byte)0).ToArray()));
        save[0] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => Simulation.Load(save));
    }

    [Fact]
    public void Different_match_seeds_give_different_state()
    {
        Assert.NotEqual(Simulation.Create(TwoPlayers(1)).ComputeHash(), Simulation.Create(TwoPlayers(2)).ComputeHash());
    }

    [Fact]
    public void Culture_data_hash_is_part_of_the_version()
    {
        Assert.Equal(GameVersion.CombinedDataHash, GameVersion.Current.DataHash);
        Assert.NotEqual(0UL, CultureCatalog.DataHash);
        Assert.Contains(CultureCatalog.All, c => c.Id == "rivermen");
        Assert.True(CultureCatalog.All.Select(c => c.Id).SequenceEqual(
            CultureCatalog.All.Select(c => c.Id).OrderBy(id => id, System.StringComparer.Ordinal)));
    }
}
