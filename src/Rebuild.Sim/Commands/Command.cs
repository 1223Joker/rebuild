using System.IO;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.Commands;

/// <summary>
/// A player or host instruction; the only thing that crosses the network (docs/01-architecture.md §4).
/// Payload is type-specific, little-endian (written with <see cref="CanonicalWriter"/>).
/// Treat <see cref="Payload"/> as immutable.
/// </summary>
public readonly struct Command
{
    /// <summary>Slot of host/system (meta) commands.</summary>
    public const byte SystemSlot = 255;

    public const int MaxPayloadBytes = 4096;

    public readonly CommandType Type;
    public readonly byte Slot;
    public readonly uint TargetTurn;
    public readonly ushort Seq;
    public readonly byte[] Payload;

    public Command(CommandType type, byte slot, uint targetTurn, ushort seq, byte[]? payload = null)
    {
        Type = type;
        Slot = slot;
        TargetTurn = targetTurn;
        Seq = seq;
        Payload = payload ?? System.Array.Empty<byte>();
        if (Payload.Length > MaxPayloadBytes) throw new System.ArgumentException("Payload too large", nameof(payload));
    }

    /// <summary>Execution order inside a turn: (Slot, Seq).</summary>
    public static int CompareOrder(Command a, Command b)
    {
        int c = a.Slot.CompareTo(b.Slot);
        return c != 0 ? c : a.Seq.CompareTo(b.Seq);
    }

    public Command WithTargetTurn(uint turn) => new(Type, Slot, turn, Seq, Payload);

    public void WriteTo(CanonicalWriter w)
    {
        w.WriteUInt16((ushort)Type);
        w.WriteByte(Slot);
        w.WriteUInt32(TargetTurn);
        w.WriteUInt16(Seq);
        w.WriteUInt16((ushort)Payload.Length);
        w.WriteRaw(Payload);
    }

    public static Command ReadFrom(CanonicalReader r)
    {
        var type = (CommandType)r.ReadUInt16();
        byte slot = r.ReadByte();
        uint turn = r.ReadUInt32();
        ushort seq = r.ReadUInt16();
        int length = r.ReadUInt16();
        if (length > MaxPayloadBytes) throw new InvalidDataException("Payload too large");
        return new Command(type, slot, turn, seq, r.ReadRaw(length));
    }

    public override string ToString() => $"{Type}(slot {Slot}, turn {TargetTurn}, seq {Seq}, {Payload.Length} B)";
}
