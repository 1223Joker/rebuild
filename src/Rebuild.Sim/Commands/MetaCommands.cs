using Rebuild.Sim.Match;

namespace Rebuild.Sim.Commands;

/// <summary>Builders and payload readers for host-issued meta commands.</summary>
public static class MetaCommands
{
    /// <summary>Command with a single slot byte as payload (PlayerJoined, PlayerLeft, HumanResume, Surrender).</summary>
    public static Command ForSlot(CommandType type, byte targetSlot, ushort seq) =>
        new(type, Command.SystemSlot, 0, seq, new[] { targetSlot });

    public static Command AiTakeover(byte targetSlot, AiDifficulty difficulty, ushort seq) =>
        new(CommandType.AiTakeover, Command.SystemSlot, 0, seq, new[] { targetSlot, (byte)difficulty });

    public static Command SetSpeed(byte speed, ushort seq) =>
        new(CommandType.SetSpeed, Command.SystemSlot, 0, seq, new[] { speed });

    public static Command Pause(ushort seq) => new(CommandType.Pause, Command.SystemSlot, 0, seq);

    public static Command Resume(ushort seq) => new(CommandType.Resume, Command.SystemSlot, 0, seq);

    internal static bool TryReadSlot(in Command c, out byte slot)
    {
        slot = 0;
        if (c.Payload.Length < 1) return false;
        slot = c.Payload[0];
        return true;
    }
}
