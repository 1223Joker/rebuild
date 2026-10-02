using System.Collections.Generic;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Core;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;

namespace Rebuild.Tools;

/// <summary>Scripted command logs for golden replays until real matches can be recorded.</summary>
public static class SampleLogs
{
    /// <summary>
    /// M0 meta-command script: random cultures, AI takeover/resume, speed changes, a player leaving,
    /// surrender, plus gameplay and malformed commands that the M0 sim must reject identically.
    /// </summary>
    public static CommandLog MetaScript(ulong seed, int turns)
    {
        var setup = new MatchSetup(
            new MapSpec(seed, MapSize.Medium, 4),
            seed * 31 + 7,
            new[]
            {
                new SlotInfo(SlotKind.Human, 0, "rivermen"),
                new SlotInfo(SlotKind.Human, 1, SlotInfo.RandomCulture),
                new SlotInfo(SlotKind.Ai, 1, SlotInfo.RandomCulture, AiDifficulty.Hard),
                new SlotInfo(SlotKind.Monster, 2, ""),
                new SlotInfo(SlotKind.Open, 3, ""),
            });
        var log = new CommandLog(GameVersion.Current, setup);
        var rng = new Pcg32(seed, 0xC0FFEE);
        var seq = new ushort[256];
        for (uint turn = 0; turn < (uint)turns; turn++)
        {
            var commands = new List<Command>();
            int count = rng.NextInt(4);
            for (int i = 0; i < count; i++)
            {
                byte slot = (byte)rng.NextInt(5);
                int kind = rng.NextInt(10);
                var payload = new byte[rng.NextInt(12)];
                for (int b = 0; b < payload.Length; b++) payload[b] = (byte)rng.NextUInt();
                // Gameplay commands: all rejected in M0 (no systems yet) but still logged and counted.
                var type = kind < 5 ? CommandType.PlaceBuilding : kind < 8 ? CommandType.Move : CommandType.Train;
                commands.Add(new Command(type, slot, turn, seq[slot]++, payload));
            }
            ushort sys = seq[Command.SystemSlot];
            switch (turn % 97)
            {
                case 10: commands.Add(MetaCommands.AiTakeover(1, AiDifficulty.Easy, sys)); break;
                case 20: commands.Add(MetaCommands.ForSlot(CommandType.HumanResume, 1, sys)); break;
                case 30: commands.Add(MetaCommands.SetSpeed((byte)(1 + turn % 3), sys)); break;
                case 40: commands.Add(MetaCommands.SetSpeed(9, sys)); break; // invalid speed → rejected
                case 50: commands.Add(MetaCommands.ForSlot(CommandType.Surrender, 3, sys)); break; // monster → rejected
                case 60: commands.Add(MetaCommands.Pause(sys)); break;
                case 61: commands.Add(MetaCommands.Resume(sys)); break;
            }
            if (turn == (uint)turns / 2) commands.Add(MetaCommands.ForSlot(CommandType.PlayerLeft, 1, (ushort)(sys + 1)));
            if (turn == (uint)turns * 3 / 4) commands.Add(MetaCommands.ForSlot(CommandType.Surrender, 2, (ushort)(sys + 2)));
            seq[Command.SystemSlot] = (ushort)(sys + 3);
            log.Append(new TurnBundle(turn, commands));
        }
        return log;
    }
}
