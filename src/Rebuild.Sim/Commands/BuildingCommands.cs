using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.Commands;

/// <summary>
/// Builders and payload readers for building commands (docs/01-architecture.md §4).
/// PlaceBuilding payload: u16 type, u16 x, u16 y, u8 rotation (footprint top-left tile).
/// CancelConstruction payload: i32 building id.
/// </summary>
public static class BuildingCommands
{
    public const int PlacePayloadBytes = 7;
    public const int CancelPayloadBytes = 4;

    public static Command Place(byte slot, ushort seq, ushort type, int x, int y, byte rotation = 0)
    {
        var w = new CanonicalWriter(PlacePayloadBytes);
        w.WriteUInt16(type);
        w.WriteUInt16((ushort)x);
        w.WriteUInt16((ushort)y);
        w.WriteByte(rotation);
        return new Command(CommandType.PlaceBuilding, slot, 0, seq, w.ToArray());
    }

    public static Command Cancel(byte slot, ushort seq, int buildingId)
    {
        var w = new CanonicalWriter(CancelPayloadBytes);
        w.WriteInt32(buildingId);
        return new Command(CommandType.CancelConstruction, slot, 0, seq, w.ToArray());
    }

    public static bool TryReadPlace(in Command c, out ushort type, out int x, out int y, out byte rotation)
    {
        type = 0; x = 0; y = 0; rotation = 0;
        if (c.Payload.Length != PlacePayloadBytes) return false;
        var r = new CanonicalReader(c.Payload);
        type = r.ReadUInt16();
        x = r.ReadUInt16();
        y = r.ReadUInt16();
        rotation = r.ReadByte();
        return true;
    }

    public static bool TryReadCancel(in Command c, out int buildingId)
    {
        buildingId = 0;
        if (c.Payload.Length != CancelPayloadBytes) return false;
        buildingId = new CanonicalReader(c.Payload).ReadInt32();
        return true;
    }
}
