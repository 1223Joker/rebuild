namespace Rebuild.Sim.Commands;

/// <summary>
/// Command catalogue (docs/01-architecture.md §4). Values are part of the wire/replay format:
/// never renumber, only append.
/// </summary>
public enum CommandType : ushort
{
    None = 0,

    // Building
    PlaceBuilding = 100,
    CancelConstruction = 101,
    Demolish = 102,
    SetBuildingPaused = 103,

    // Economy
    SetTransportPriority = 200,
    SetToolProductionQuota = 201,
    SetFoodDistribution = 202,
    SetStorePolicy = 203,

    // Military (payloads: docs/11-military.md §2)
    Move = 300,
    AttackMove = 301,
    Attack = 302,
    Stop = 303,
    Hold = 304,
    SetStance = 305,
    Garrison = 306,
    Ungarrison = 307,
    Train = 308,
    BuildWall = 309,
    BuildGate = 310,
    SetGateLocked = 311,

    // Meta, issued by the host with Slot = Command.SystemSlot
    PlayerJoined = 1000,
    PlayerLeft = 1001,
    AiTakeover = 1002,
    HumanResume = 1003,
    Pause = 1004,
    Resume = 1005,
    SetSpeed = 1006,
    Surrender = 1007,
}
