using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Core;
using Rebuild.Sim.Cultures;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim;

/// <summary>
/// The deterministic simulation: <c>State(n+1) = Step(State(n), Commands(n))</c> (docs/01-architecture.md §1).
/// Single-threaded, integer-only. M0 holds match/player state and RNG streams; game systems are added
/// from M2 on and must write their state in <see cref="WriteState"/>.
/// </summary>
public sealed class Simulation
{
    /// <summary>Simulation ticks per second at 1× speed (ADR 0006).</summary>
    public const int TicksPerSecond = 10;
    /// <summary>Ticks per lockstep turn (ADR 0006).</summary>
    public const int TicksPerTurn = 2;

    private const uint SaveMagic = 0x56415342; // "BSAV" little-endian
    private const ushort SaveFormatVersion = 1;

    private readonly PlayerState[] _players;
    private readonly PlayerCultureTable?[] _cultureTables;

    public MatchSetup Setup { get; }
    public int Tick { get; private set; }
    public uint Turn => (uint)(Tick / TicksPerTurn);

    public Pcg32 EconomyRng { get; }
    public Pcg32 CombatRng { get; }
    public Pcg32 MonsterRng { get; }

    /// <summary>Commands rejected by validation so far (no-ops on every peer).</summary>
    public int RejectedCommands { get; private set; }

    public IReadOnlyList<PlayerState> Players => _players;

    private Simulation(MatchSetup setup, int tick, PlayerState[] players, Pcg32 economy, Pcg32 combat, Pcg32 monsters, int rejected)
    {
        Setup = setup;
        Tick = tick;
        _players = players;
        EconomyRng = economy;
        CombatRng = combat;
        MonsterRng = monsters;
        RejectedCommands = rejected;
        _cultureTables = new PlayerCultureTable?[players.Length];
        for (int i = 0; i < players.Length; i++)
            if (players[i].CultureIndex >= 0)
                _cultureTables[i] = new PlayerCultureTable(CultureCatalog.All[players[i].CultureIndex]);
    }

    /// <summary>Starts a new match. Random cultures are resolved with the Setup RNG stream.</summary>
    public static Simulation Create(MatchSetup setup)
    {
        var setupRng = Pcg32.ForStream(setup.MatchSeed, RngStream.Setup);
        var players = new PlayerState[setup.Slots.Count];
        for (int i = 0; i < players.Length; i++)
        {
            var s = setup.Slots[i];
            int culture = -1;
            if (s.Kind == SlotKind.Human || s.Kind == SlotKind.Ai)
            {
                if (s.CultureId == SlotInfo.RandomCulture)
                {
                    culture = setupRng.NextInt(CultureCatalog.All.Count);
                }
                else
                {
                    culture = CultureCatalog.IndexOf(s.CultureId);
                    if (culture < 0) throw new System.ArgumentException($"Unknown culture '{s.CultureId}' in slot {i}");
                }
            }
            var controller = s.Kind switch
            {
                SlotKind.Human => Controller.Human,
                SlotKind.Ai => Controller.Ai,
                SlotKind.Monster => Controller.Monster,
                _ => Controller.None,
            };
            players[i] = new PlayerState((byte)i, s.Team, culture, controller, s.Difficulty, PlayerStatus.Active);
        }
        return new Simulation(setup, 0, players,
            Pcg32.ForStream(setup.MatchSeed, RngStream.Economy),
            Pcg32.ForStream(setup.MatchSeed, RngStream.Combat),
            Pcg32.ForStream(setup.MatchSeed, RngStream.Monsters),
            rejected: 0);
    }

    /// <summary>Culture table of a slot, or null for open/monster slots.</summary>
    public PlayerCultureTable? CultureOf(int slot) => _cultureTables[slot];

    /// <summary>Executes one lockstep turn: commands at its first tick, then <see cref="TicksPerTurn"/> ticks.</summary>
    public void ExecuteTurn(TurnBundle bundle)
    {
        if (bundle.Turn != Turn) throw new System.InvalidOperationException($"Expected bundle for turn {Turn}, got {bundle.Turn}");
        if (Tick % TicksPerTurn != 0) throw new System.InvalidOperationException("Not at a turn boundary");
        foreach (var c in bundle.Commands) Apply(c);
        for (int i = 0; i < TicksPerTurn; i++) StepTick();
    }

    private void StepTick()
    {
        // Systems run here in a fixed order (construction, production, logistics, combat, ...), from M2 on.
        Tick++;
    }

    private void Apply(in Command c)
    {
        if (!CommandValidator.IsValid(this, c))
        {
            RejectedCommands++;
            return;
        }
        switch (c.Type)
        {
            case CommandType.PlayerLeft:
                _players[c.Payload[0]].Status = PlayerStatus.Left;
                _players[c.Payload[0]].Controller = Controller.None;
                break;
            case CommandType.Surrender:
                _players[c.Payload[0]].Status = PlayerStatus.Surrendered;
                break;
            case CommandType.AiTakeover:
                _players[c.Payload[0]].Controller = Controller.Ai;
                _players[c.Payload[0]].Difficulty = (AiDifficulty)c.Payload[1];
                break;
            case CommandType.HumanResume:
                _players[c.Payload[0]].Controller = Controller.Human;
                break;
            // PlayerJoined, Pause, Resume and SetSpeed are handled by the lockstep scheduler; the sim only
            // sees them in the log. Gameplay commands get their handlers with their systems (M2+).
        }
    }

    /// <summary>Canonical sim state (hashed every turn, saved, sent as reconnect snapshot).</summary>
    public void WriteState(CanonicalWriter w)
    {
        w.WriteInt32(Tick);
        w.WriteInt32(RejectedCommands);
        EconomyRng.WriteTo(w);
        CombatRng.WriteTo(w);
        MonsterRng.WriteTo(w);
        w.WriteByte((byte)_players.Length);
        foreach (var p in _players) p.WriteTo(w);
    }

    public ulong ComputeHash()
    {
        var w = new CanonicalWriter(256);
        WriteState(w);
        return StateHash.Of(w);
    }

    /// <summary>Savegame = {magic, format, GameVersion, MatchSetup, SimState} (docs/01-architecture.md §8).</summary>
    public byte[] Save()
    {
        var w = new CanonicalWriter(1024);
        w.WriteUInt32(SaveMagic);
        w.WriteUInt16(SaveFormatVersion);
        GameVersion.Current.WriteTo(w);
        Setup.WriteTo(w);
        WriteState(w);
        return w.ToArray();
    }

    public static Simulation Load(byte[] save)
    {
        var r = new CanonicalReader(save);
        if (r.ReadUInt32() != SaveMagic) throw new InvalidDataException("Not a Rebuild save");
        ushort format = r.ReadUInt16();
        if (format != SaveFormatVersion) throw new InvalidDataException($"Unsupported save format {format}");
        var version = GameVersion.ReadFrom(r);
        if (version != GameVersion.Current) throw new InvalidDataException($"Save is from version {version}, this is {GameVersion.Current}");
        var setup = MatchSetup.ReadFrom(r);
        int tick = r.ReadInt32();
        int rejected = r.ReadInt32();
        var economy = Pcg32.ReadFrom(r);
        var combat = Pcg32.ReadFrom(r);
        var monsters = Pcg32.ReadFrom(r);
        int count = r.ReadByte();
        if (count != setup.Slots.Count) throw new InvalidDataException("Player count does not match setup");
        var players = new PlayerState[count];
        for (int i = 0; i < count; i++) players[i] = PlayerState.ReadFrom(r);
        if (!r.AtEnd) throw new InvalidDataException("Trailing data in save");
        return new Simulation(setup, tick, players, economy, combat, monsters, rejected);
    }
}
