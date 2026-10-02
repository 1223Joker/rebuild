using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.Match;

public enum PlayerStatus : byte
{
    Active = 0,
    Left = 1,
    Surrendered = 2,
    Defeated = 3,
}

public enum Controller : byte
{
    None = 0,
    Human = 1,
    Ai = 2,
    Monster = 3,
}

/// <summary>Per-slot sim state.</summary>
public sealed class PlayerState
{
    public PlayerState(byte slot, byte team, int cultureIndex, Controller controller, AiDifficulty difficulty, PlayerStatus status)
    {
        Slot = slot;
        Team = team;
        CultureIndex = cultureIndex;
        Controller = controller;
        Difficulty = difficulty;
        Status = status;
    }

    public byte Slot { get; }
    public byte Team { get; }
    /// <summary>Index into CultureCatalog.All; -1 for open and monster slots.</summary>
    public int CultureIndex { get; }
    public Controller Controller { get; internal set; }
    public AiDifficulty Difficulty { get; internal set; }
    public PlayerStatus Status { get; internal set; }

    /// <summary>Whether this slot may issue gameplay commands.</summary>
    public bool CanAct => Status == PlayerStatus.Active && (Controller == Controller.Human || Controller == Controller.Ai);

    internal void WriteTo(CanonicalWriter w)
    {
        w.WriteByte(Slot);
        w.WriteByte(Team);
        w.WriteInt32(CultureIndex);
        w.WriteByte((byte)Controller);
        w.WriteByte((byte)Difficulty);
        w.WriteByte((byte)Status);
    }

    internal static PlayerState ReadFrom(CanonicalReader r) =>
        new(r.ReadByte(), r.ReadByte(), r.ReadInt32(), (Controller)r.ReadByte(), (AiDifficulty)r.ReadByte(), (PlayerStatus)r.ReadByte());
}
