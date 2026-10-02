using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.Commands;

/// <summary>
/// All commands that execute in one lockstep turn, sealed by the host (docs/02-networking.md §2).
/// Commands are sorted by (Slot, Seq); duplicates (same Slot and Seq) keep the first occurrence.
/// </summary>
public sealed class TurnBundle
{
    public uint Turn { get; }
    public IReadOnlyList<Command> Commands => _commands;

    private readonly Command[] _commands;

    public TurnBundle(uint turn, IEnumerable<Command> commands)
    {
        Turn = turn;
        // OrderBy is a stable sort, so "first occurrence wins" is well defined.
        var sorted = commands.Select(c => c.WithTargetTurn(turn)).OrderBy(c => c.Slot).ThenBy(c => c.Seq).ToList();
        var unique = new List<Command>(sorted.Count);
        foreach (var c in sorted)
            if (unique.Count == 0 || Command.CompareOrder(unique[^1], c) != 0)
                unique.Add(c);
        _commands = unique.ToArray();
    }

    public static TurnBundle Empty(uint turn) => new(turn, System.Array.Empty<Command>());

    public void WriteTo(CanonicalWriter w)
    {
        w.WriteUInt32(Turn);
        w.WriteUInt16((ushort)_commands.Length);
        foreach (var c in _commands) c.WriteTo(w);
    }

    public static TurnBundle ReadFrom(CanonicalReader r)
    {
        uint turn = r.ReadUInt32();
        int count = r.ReadUInt16();
        var commands = new Command[count];
        for (int i = 0; i < count; i++)
        {
            commands[i] = Command.ReadFrom(r);
            if (commands[i].TargetTurn != turn) throw new InvalidDataException("Command turn does not match bundle");
        }
        return new TurnBundle(turn, commands);
    }
}
