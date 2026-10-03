using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.Commands;

/// <summary>
/// Builders and payload readers for economy commands (docs/01-architecture.md §4).
/// SetTransportPriority payload: u16 good, u8 rank (new position in the player's priority list, 0 = first; World.Logistics).
/// SetToolProductionQuota payload: u16 good (a tool or weapon some smith makes), u8 weight 0..10 (World.ProductionQuotas).
/// </summary>
public static class EconomyCommands
{
    public const int QuotaPayloadBytes = 3;

    public static Command Priority(byte slot, ushort seq, ushort good, byte rank) =>
        new(CommandType.SetTransportPriority, slot, 0, seq, Quota(slot, seq, good, rank).Payload);

    /// <summary>Reads a SetTransportPriority payload (same layout as a quota).</summary>
    public static bool TryReadPriority(in Command c, out ushort good, out byte rank) => TryReadQuota(c, out good, out rank);

    public static Command Quota(byte slot, ushort seq, ushort good, byte weight)
    {
        var w = new CanonicalWriter(QuotaPayloadBytes);
        w.WriteUInt16(good);
        w.WriteByte(weight);
        return new Command(CommandType.SetToolProductionQuota, slot, 0, seq, w.ToArray());
    }

    public static bool TryReadQuota(in Command c, out ushort good, out byte weight)
    {
        good = 0; weight = 0;
        if (c.Payload.Length != QuotaPayloadBytes) return false;
        var r = new CanonicalReader(c.Payload);
        good = r.ReadUInt16();
        weight = r.ReadByte();
        return true;
    }
}
