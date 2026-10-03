using System.Collections.Generic;
using Rebuild.Sim;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Core;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Sim.World;

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
            // 3 Human/AI slots → 3 starts in teams 0, 1, 1 (docs/04-game-modes.md §1); the Monster slot needs no start.
            new MapSpec(seed, MapSize.Medium, 3) with { Teams = MapSpec.PackTeams(new[] { 0, 1, 1 }), Monsters = MonsterDensity.Low },
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

    /// <summary>
    /// M2 building script: both players place random building types around their castle, half at random
    /// tiles (mostly enemy/no-man's land, water or other buildings, so rejected) and half at the first valid
    /// spot scanning from a random tile; they cancel random building ids, demolish own complete buildings or
    /// random ids and send malformed payloads; valid placements pause while a slot has 4 open sites, so the
    /// castle stock completes buildings (towers extend the territory) until it runs out;
    /// player 1 leaves at 3/4. A shadow simulation runs along to find valid spots and building ids.
    /// </summary>
    public static CommandLog BuildScript(ulong seed, int turns)
    {
        var setup = new MatchSetup(
            new MapSpec(seed, MapSize.Medium, 2) with { Monsters = MonsterDensity.Low }, seed * 17 + 3,
            new[]
            {
                new SlotInfo(SlotKind.Human, 0, "rivermen"),
                new SlotInfo(SlotKind.Ai, 1, SlotInfo.RandomCulture, AiDifficulty.Normal),
                new SlotInfo(SlotKind.Monster, 2, ""),
            });
        var log = new CommandLog(GameVersion.Current, setup);
        var sim = Simulation.Create(setup);
        var rng = new Pcg32(seed, 0xB0117);
        var seq = new ushort[256];
        for (uint turn = 0; turn < (uint)turns; turn++)
        {
            var commands = new List<Command>();
            for (byte slot = 0; slot < 2; slot++)
            {
                var start = sim.StartOf(slot)!.Value;
                int count = rng.NextInt(3);
                for (int i = 0; i < count; i++)
                {
                    int kind = rng.NextInt(20);
                    if (kind < 14)
                    {
                        ushort type = (ushort)rng.NextInt(BuildingCatalog.All.Count + 1); // + 1: unknown type
                        int x = start.X - 20 + rng.NextInt(41);
                        int y = start.Y - 20 + rng.NextInt(41);
                        // Half aim at a valid spot while the slot has few open sites, so the castle stock completes some;
                        // with too many open sites every placement gets the invalid rotation 4.
                        bool full = OpenSites(sim, slot) >= MaxOpenSites;
                        if (kind >= 7 && !full) FindValidSpot(sim, slot, type, ref x, ref y);
                        byte rotation = (byte)rng.NextInt(5); // 4 is invalid
                        if (full) rotation = 4;
                        commands.Add(BuildingCommands.Place(slot, seq[slot]++, type, x, y, rotation));
                    }
                    else if (kind < 16)
                    {
                        commands.Add(BuildingCommands.Cancel(slot, seq[slot]++, rng.NextInt(sim.Buildings.NextId + 1)));
                    }
                    else if (kind < 18)
                    {
                        // Half aim at an own complete building (valid unless it is the castle), half at a random id.
                        int id = rng.NextInt(sim.Buildings.NextId + 1);
                        if (kind == 16) id = PickOwnComplete(sim, slot, id);
                        commands.Add(BuildingCommands.Demolish(slot, seq[slot]++, id));
                    }
                    else
                    {
                        var payload = new byte[rng.NextInt(9)];
                        for (int b = 0; b < payload.Length; b++) payload[b] = (byte)rng.NextUInt();
                        var type = kind == 18 ? CommandType.PlaceBuilding : payload.Length % 2 == 0 ? CommandType.CancelConstruction : CommandType.Demolish;
                        commands.Add(new Command(type, slot, turn, seq[slot]++, payload));
                    }
                }
            }
            if (turn == (uint)turns * 3 / 4) commands.Add(MetaCommands.ForSlot(CommandType.PlayerLeft, 1, seq[Command.SystemSlot]++));
            var bundle = new TurnBundle(turn, commands);
            log.Append(bundle);
            sim.ExecuteTurn(bundle);
        }
        return log;
    }

    private const int MaxOpenSites = 4;

    private static int OpenSites(Simulation sim, byte slot)
    {
        int n = 0;
        foreach (var b in sim.Buildings.All)
            if (b.Owner == slot && b.State == BuildingState.ConstructionSite) n++;
        return n;
    }

    /// <summary>The <paramref name="pick"/>-th (mod count) complete building of the slot, or <paramref name="pick"/> if it has none.</summary>
    private static int PickOwnComplete(Simulation sim, byte slot, int pick)
    {
        var own = new List<int>();
        foreach (var b in sim.Buildings.All)
            if (b.Owner == slot && b.State == BuildingState.Complete) own.Add(b.Id);
        return own.Count == 0 ? pick : own[pick % own.Count];
    }

    /// <summary>Moves (x, y) to the first valid spot in row-major order from (x, y) within a 41² window around the slot's start.</summary>
    private static void FindValidSpot(Simulation sim, byte slot, ushort type, ref int x, ref int y)
    {
        var start = sim.StartOf(slot)!.Value;
        int x0 = start.X - 20, y0 = start.Y - 20;
        int offset = (y - y0) * 41 + (x - x0);
        for (int i = 0; i < 41 * 41; i++)
        {
            int k = (offset + i) % (41 * 41);
            int tx = x0 + k % 41, ty = y0 + k / 41;
            if (BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, slot, type, tx, ty) != PlacementResult.Ok) continue;
            x = tx;
            y = ty;
            return;
        }
    }
}
