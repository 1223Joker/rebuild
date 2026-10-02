using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Core;
using Rebuild.Sim.Cultures;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Sim.World;

namespace Rebuild.Sim;

/// <summary>
/// The deterministic simulation: <c>State(n+1) = Step(State(n), Commands(n))</c> (docs/01-architecture.md §1).
/// Single-threaded, integer-only. Holds match/player state, RNG streams, the generated map, territory and
/// buildings; game systems are added from M2 on and must write their state in <see cref="WriteState"/>. The map is
/// regenerated from <see cref="MatchSetup.Map"/> on create and load (only its hash is saved); systems that
/// mutate map layers must serialize those layers themselves.
/// </summary>
public sealed class Simulation
{
    /// <summary>Simulation ticks per second at 1× speed (ADR 0006).</summary>
    public const int TicksPerSecond = 10;
    /// <summary>Ticks per lockstep turn (ADR 0006).</summary>
    public const int TicksPerTurn = 2;

    private const uint SaveMagic = 0x56415342; // "BSAV" little-endian
    private const ushort SaveFormatVersion = 3;

    private readonly PlayerState[] _players;
    private readonly PlayerCultureTable?[] _cultureTables;
    private readonly int[] _startOfSlot;

    public MatchSetup Setup { get; }
    public int Tick { get; private set; }
    public uint Turn => (uint)(Tick / TicksPerTurn);

    public Pcg32 EconomyRng { get; }
    public Pcg32 CombatRng { get; }
    public Pcg32 MonsterRng { get; }

    /// <summary>Commands rejected by validation so far (no-ops on every peer).</summary>
    public int RejectedCommands { get; private set; }

    public IReadOnlyList<PlayerState> Players => _players;

    /// <summary>The generated map (terrain, resources, starts); immutable until systems that change tiles exist.</summary>
    public MapData Map { get; }
    /// <summary>Hash of <see cref="Map"/>; part of the state hash so peers with different maps desync at turn 0.</summary>
    public ulong MapHash { get; }
    public Territory Territory { get; }
    public BuildingRegistry Buildings { get; }

    private Simulation(MatchSetup setup, MapData map, int tick, PlayerState[] players, Pcg32 economy, Pcg32 combat, Pcg32 monsters,
        int rejected, Territory territory, BuildingRegistry buildings)
    {
        Setup = setup;
        Map = map;
        MapHash = map.ComputeHash();
        Territory = territory;
        Buildings = buildings;
        _startOfSlot = StartAssignment.Assign(setup);
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

    /// <summary>
    /// Starts a new match: generates the map, resolves random cultures with the Setup RNG stream and places the
    /// complete start castle (centred on the start, with its territory claim) of every Human/AI slot in slot order. Throws <see cref="System.ArgumentException"/>
    /// if the setup does not fit its map spec or no valid map exists for the spec.
    /// </summary>
    public static Simulation Create(MatchSetup setup)
    {
        var map = GenerateMap(setup);
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
        var territory = new Territory(map.Edge);
        var buildings = new BuildingRegistry(map.Edge);
        var castle = BuildingCatalog.All[BuildingIds.Castle];
        var starts = StartAssignment.Assign(setup);
        for (int i = 0; i < starts.Length; i++)
        {
            if (starts[i] < 0) continue;
            var start = map.Starts[starts[i]];
            // The start castle ignores terrain (the start plateau is flat by construction, docs/03-mapgen.md §3).
            int x = start.X - castle.Side / 2, y = start.Y - castle.Side / 2;
            if (!buildings.IsFootprintFree(x, y, castle.Side))
                throw new System.ArgumentException($"Start castle of slot {i} does not fit at ({start.X}, {start.Y})", nameof(setup));
            int claim = territory.AddClaim((byte)i, start.X, start.Y, castle.TerritoryRadius);
            buildings.Add(BuildingIds.Castle, (byte)i, x, y, 0, BuildingState.Complete, claim);
        }
        return new Simulation(setup, map, 0, players,
            Pcg32.ForStream(setup.MatchSeed, RngStream.Economy),
            Pcg32.ForStream(setup.MatchSeed, RngStream.Combat),
            Pcg32.ForStream(setup.MatchSeed, RngStream.Monsters),
            rejected: 0, territory, buildings);
    }

    private static MapData GenerateMap(MatchSetup setup)
    {
        string? error = setup.Map.Check() ?? StartAssignment.Check(setup);
        if (error != null) throw new System.ArgumentException("Invalid match setup: " + error, nameof(setup));
        var result = MapGenerator.Generate(setup.Map);
        return result.Map ?? throw new System.ArgumentException(
            $"No valid map for this spec after {result.Attempts} attempts", nameof(setup));
    }

    /// <summary>Start position of a slot, or null for open and monster slots.</summary>
    public StartPosition? StartOf(int slot) => _startOfSlot[slot] < 0 ? null : Map.Starts[_startOfSlot[slot]];

    /// <summary>Whether two slots are allies (same team; a slot is its own ally). Monster slots have no allies.</summary>
    public bool AreAllies(int a, int b) =>
        a == b || (Setup.Slots[a].Kind != SlotKind.Monster && Setup.Slots[b].Kind != SlotKind.Monster
                   && _players[a].Team == _players[b].Team);

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
            case CommandType.PlaceBuilding:
                BuildingCommands.TryReadPlace(c, out ushort type, out int x, out int y, out byte rotation);
                Buildings.Add(type, c.Slot, x, y, rotation, BuildingState.ConstructionSite, claimId: 0);
                break;
            case CommandType.CancelConstruction:
                BuildingCommands.TryReadCancel(c, out int id);
                Buildings.Remove(id);
                break;
            // PlayerJoined, Pause, Resume and SetSpeed are handled by the lockstep scheduler; the sim only
            // sees them in the log. Other gameplay commands get their handlers with their systems (M2+).
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
        w.WriteUInt64(MapHash);
        Territory.WriteTo(w);
        Buildings.WriteTo(w);
    }

    public ulong ComputeHash()
    {
        var w = new CanonicalWriter(Map.TileCount + 1024);
        WriteState(w);
        return StateHash.Of(w);
    }

    /// <summary>Savegame = {magic, format, GameVersion, MatchSetup, SimState} (docs/01-architecture.md §8).</summary>
    public byte[] Save()
    {
        var w = new CanonicalWriter(Map.TileCount + 2048);
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
        ulong mapHash = r.ReadUInt64();
        MapData map;
        try { map = GenerateMap(setup); }
        catch (System.ArgumentException e) { throw new InvalidDataException(e.Message, e); }
        if (map.ComputeHash() != mapHash) throw new InvalidDataException("Regenerated map does not match the saved map hash");
        var territory = Territory.ReadFrom(r, map.Edge);
        foreach (var c in territory.Claims)
            if (c.Owner >= count) throw new InvalidDataException("Territory claim of an unknown slot");
        var buildings = BuildingRegistry.ReadFrom(r, map.Edge, count, territory);
        if (!r.AtEnd) throw new InvalidDataException("Trailing data in save");
        return new Simulation(setup, map, tick, players, economy, combat, monsters, rejected, territory, buildings);
    }
}
