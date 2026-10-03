using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Core;
using Rebuild.Sim.Cultures;
using Rebuild.Sim.Goods;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Sim.World;

namespace Rebuild.Sim;

/// <summary>
/// The deterministic simulation: <c>State(n+1) = Step(State(n), Commands(n))</c> (docs/01-architecture.md §1).
/// Single-threaded, integer-only. Holds match/player state, RNG streams, the generated map, territory,
/// buildings, settlers (carriers and workers), transport and worker jobs, smith quotas and production statistics; game systems are added from M2 on and must write their state in <see cref="WriteState"/>. The map is
/// regenerated from <see cref="MatchSetup.Map"/> on create and load (only its hash is saved); changes to its object and
/// resource layers (harvested trees, stone, game and fish) are saved as <see cref="MapChanges"/>.
/// </summary>
public sealed class Simulation
{
    /// <summary>Simulation ticks per second at 1× speed (ADR 0006).</summary>
    public const int TicksPerSecond = 10;
    /// <summary>Ticks per lockstep turn (ADR 0006).</summary>
    public const int TicksPerTurn = 2;

    private const uint SaveMagic = 0x56415342; // "BSAV" little-endian
    private const ushort SaveFormatVersion = 22;

    private readonly PlayerState[] _players;
    private readonly PlayerCultureTable?[] _cultureTables;
    private readonly int[] _startOfSlot;
    private readonly Pathfinder _pathfinder;

    public MatchSetup Setup { get; }
    public int Tick { get; private set; }
    public uint Turn => (uint)(Tick / TicksPerTurn);

    public Pcg32 EconomyRng { get; }
    public Pcg32 CombatRng { get; }
    public Pcg32 MonsterRng { get; }

    /// <summary>Commands rejected by validation so far (no-ops on every peer).</summary>
    public int RejectedCommands { get; private set; }

    public IReadOnlyList<PlayerState> Players => _players;

    /// <summary>Season of the next tick (<see cref="Calendar"/>, lobby option <see cref="MatchSetup.Seasons"/>).</summary>
    public Season Season => Calendar.SeasonAt(Tick, Setup.Seasons);

    /// <summary>The map (terrain, resources, starts); its object layer changes as production harvests (<see cref="MapChanges"/>).</summary>
    public MapData Map { get; }
    /// <summary>Hash of the generated map; part of the state hash so peers with different maps desync at turn 0.</summary>
    public ulong MapHash { get; }
    /// <summary>Object-layer changes since generation (harvested trees and stone).</summary>
    public MapChanges MapChanges { get; }
    public Territory Territory { get; }
    public BuildingRegistry Buildings { get; }
    public Settlers Settlers { get; }
    public Logistics Logistics { get; }
    /// <summary>Per-player smith quotas (which tool or weapon the next cycle makes).</summary>
    public ProductionQuotas Quotas { get; }
    /// <summary>Per-player, per-good units produced and consumed per minute (UI and AI).</summary>
    public ProductionStatistics Statistics { get; }

    private Simulation(MatchSetup setup, MapData map, ulong mapHash, MapChanges mapChanges, int tick, PlayerState[] players, Pcg32 economy,
        Pcg32 combat, Pcg32 monsters, int rejected, Territory territory, BuildingRegistry buildings, Settlers settlers, Logistics logistics,
        ProductionQuotas quotas, ProductionStatistics statistics)
    {
        Setup = setup;
        Map = map;
        MapHash = mapHash;
        MapChanges = mapChanges;
        Territory = territory;
        Buildings = buildings;
        Settlers = settlers;
        Logistics = logistics;
        Quotas = quotas;
        Statistics = statistics;
        _pathfinder = new Pathfinder(map.Edge);
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
    /// complete start castle (centred on the start, with its territory claim, the start stock of every good and its full
    /// set of carriers at its door) of every Human/AI slot in slot order. Throws <see cref="System.ArgumentException"/>
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
        var settlers = new Settlers(map.Edge);
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
            int id = buildings.Add(BuildingIds.Castle, (byte)i, x, y, 0, BuildingState.Complete, claim);
            var stock = buildings.StockAt(buildings.IndexOf(id))!;
            foreach (var good in GoodCatalog.All) stock[good.Index] = good.StartStock;
        }
        foreach (var b in buildings.All)
        {
            int door = Settlers.DoorOf(b, map, buildings);
            if (door < 0) continue; // no free tile around the castle (not on a generated start plateau): no carriers
            for (int k = 0; k < b.Definition.Beds; k++) settlers.Spawn(b.Owner, b.Id, door);
        }
        return new Simulation(setup, map, map.ComputeHash(), new MapChanges(), 0, players,
            Pcg32.ForStream(setup.MatchSeed, RngStream.Economy),
            Pcg32.ForStream(setup.MatchSeed, RngStream.Combat),
            Pcg32.ForStream(setup.MatchSeed, RngStream.Monsters),
            rejected: 0, territory, buildings, settlers, new Logistics(map.Edge, players.Length), new ProductionQuotas(players.Length),
            new ProductionStatistics(players.Length, tick: 0));
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
        // Systems run here in a fixed order: construction, production, households, logistics matching, settlers (movement + jobs),
        // then (later milestones) combat, ...
        Construction.Step(Tick, Buildings, Map, MapChanges, Territory, Settlers, Logistics);
        Production.Step(Tick, Buildings, Map, Territory, MapChanges, Logistics, Settlers, Quotas, Statistics, Season, _pathfinder);
        Households.Step(Map.Edge, Buildings, Settlers, Logistics, Statistics, Season);
        Logistics.Match(Tick, Buildings, Settlers, Season);
        Settlers.Step(Tick, Map, Territory, Buildings, Logistics, Statistics, EconomyRng, _pathfinder, Season);
        Tick++;
        Statistics.Advance(Tick);
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
                Buildings.Add(type, c.Slot, x, y, rotation, BuildingState.ConstructionSite, claimId: 0, Construction.DigWork(Map, type, x, y));
                break;
            case CommandType.CancelConstruction:
                BuildingCommands.TryReadId(c, out int id);
                Logistics.ReleaseWorker(Buildings, Settlers, id);
                Construction.Cancel(Buildings, id);
                break;
            case CommandType.Demolish:
                BuildingCommands.TryReadId(c, out int demolished);
                Logistics.ReleaseWorker(Buildings, Settlers, demolished);
                Construction.Demolish(Buildings, Territory, demolished);
                break;
            case CommandType.SetTransportPriority:
                EconomyCommands.TryReadPriority(c, out ushort moved, out byte rank);
                Logistics.SetPriority(c.Slot, moved, rank);
                break;
            case CommandType.SetToolProductionQuota:
                EconomyCommands.TryReadQuota(c, out ushort good, out byte weight);
                Quotas.Set(c.Slot, good, weight);
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
        MapChanges.WriteTo(w, Map);
        Territory.WriteTo(w);
        Buildings.WriteTo(w);
        Settlers.WriteTo(w);
        Logistics.WriteTo(w);
        Quotas.WriteTo(w);
        Statistics.WriteTo(w);
    }

    public ulong ComputeHash()
    {
        var w = new CanonicalWriter(Map.TileCount + 4096);
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
        if (tick < 0) throw new InvalidDataException("Negative tick in save");
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
        var mapChanges = MapChanges.ReadFrom(r, map);
        var territory = Territory.ReadFrom(r, map.Edge);
        foreach (var c in territory.Claims)
            if (c.Owner >= count) throw new InvalidDataException("Territory claim of an unknown slot");
        var buildings = BuildingRegistry.ReadFrom(r, map.Edge, count, territory);
        var settlers = Settlers.ReadFrom(r, map.Edge, count, buildings.NextId);
        var logistics = Logistics.ReadFrom(r, map.Edge, count, buildings, settlers);
        var quotas = ProductionQuotas.ReadFrom(r, count);
        var statistics = ProductionStatistics.ReadFrom(r, count, tick);
        // Production ran last at tick - 1 and ended every cycle that reached that season's length.
        foreach (var b in buildings.All)
        {
            var p = b.Definition.Production;
            if (p != null && b.Cycle > 0 && (tick == 0 || b.Cycle >= p.CycleTicksIn(Calendar.SeasonAt(tick - 1, setup.Seasons))))
                throw new InvalidDataException("Work cycle longer than its season allows");
        }
        if (!r.AtEnd) throw new InvalidDataException("Trailing data in save");
        return new Simulation(setup, map, mapHash, mapChanges, tick, players, economy, combat, monsters, rejected, territory, buildings, settlers, logistics, quotas, statistics);
    }
}
