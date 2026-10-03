using Rebuild.Sim.Match;
using Rebuild.Sim.World;

namespace Rebuild.Sim.Commands;

/// <summary>
/// Sim-side validation: invalid commands become no-ops identically on all peers (docs/01-architecture.md §4).
/// </summary>
public static class CommandValidator
{
    public static bool IsValid(Simulation sim, in Command c)
    {
        bool isMeta = (ushort)c.Type >= 1000;
        if (isMeta != (c.Slot == Command.SystemSlot)) return false;
        if (isMeta) return IsValidMeta(sim, c);
        if (c.Slot >= sim.Players.Count || !sim.Players[c.Slot].CanAct) return false;
        switch (c.Type)
        {
            case CommandType.PlaceBuilding:
                return BuildingCommands.TryReadPlace(c, out ushort type, out int x, out int y, out byte rotation)
                    && rotation <= BuildingRegistry.MaxRotation
                    && BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, c.Slot, type, x, y) == PlacementResult.Ok;
            case CommandType.CancelConstruction:
                return BuildingCommands.TryReadId(c, out int id) && sim.Buildings.TryGet(id, out var b)
                    && b.Owner == c.Slot && b.State == BuildingState.ConstructionSite;
            case CommandType.Demolish:
                // Complete own buildings only (sites are cancelled); the castle cannot be demolished.
                return BuildingCommands.TryReadId(c, out int target) && sim.Buildings.TryGet(target, out var d)
                    && d.Owner == c.Slot && d.State == BuildingState.Complete && d.Definition.PlayerPlaceable;
            default:
                // Other gameplay commands are validated by their systems once they exist; until then they are no-ops.
                return false;
        }
    }

    private static bool IsValidMeta(Simulation sim, in Command c)
    {
        switch (c.Type)
        {
            case CommandType.Pause:
            case CommandType.Resume:
                return c.Payload.Length == 0;
            case CommandType.SetSpeed:
                return c.Payload.Length == 1 && c.Payload[0] >= 1 && c.Payload[0] <= 3;
            case CommandType.PlayerJoined:
            case CommandType.PlayerLeft:
            case CommandType.Surrender:
            case CommandType.HumanResume:
                return c.Payload.Length == 1 && IsPlayableSlot(sim, c.Payload[0]);
            case CommandType.AiTakeover:
                return c.Payload.Length == 2 && IsPlayableSlot(sim, c.Payload[0]) && c.Payload[1] <= (byte)AiDifficulty.Hard
                    && sim.Players[c.Payload[0]].Status == PlayerStatus.Active;
            default:
                return false;
        }
    }

    private static bool IsPlayableSlot(Simulation sim, byte slot) =>
        slot < sim.Players.Count && sim.Players[slot].CultureIndex >= 0;
}
