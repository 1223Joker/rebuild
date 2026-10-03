using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Core;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Sim.World;
using Rebuild.Tools;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>Building data, start castles, PlaceBuilding / CancelConstruction (docs/06-economy.md §1, §5).</summary>
public class BuildingTests
{
    private static MatchSetup TwoPlayers(ulong seed = 1) => new(
        new MapSpec(seed, MapSize.Small, 2), seed,
        new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Ai, 1, "rivermen") });

    private static PlacementResult Check(Simulation sim, byte slot, int type, int x, int y) =>
        BuildingPlacement.Check(sim.Map, sim.Territory, sim.Buildings, slot, type, x, y);

    /// <summary>First valid spot in row-major order, searched around the slot's castle.</summary>
    private static (int X, int Y) FindSpot(Simulation sim, byte slot, int type)
    {
        var s = sim.StartOf(slot)!.Value;
        for (int y = s.Y - 16; y <= s.Y + 16; y++)
            for (int x = s.X - 16; x <= s.X + 16; x++)
                if (Check(sim, slot, type, x, y) == PlacementResult.Ok) return (x, y);
        throw new Xunit.Sdk.XunitException($"No spot for {BuildingCatalog.All[type].Id} near slot {slot}");
    }

    private static void Run(Simulation sim, params Command[] commands) => sim.ExecuteTurn(new TurnBundle(sim.Turn, commands));

    [Fact]
    public void Catalog_is_compiled_from_data()
    {
        Assert.Equal(25, BuildingCatalog.All.Count);
        Assert.Equal(BuildingIds.Castle, BuildingCatalog.IndexOf("castle"));
        Assert.Equal(BuildingIds.GuardTowerLarge, BuildingCatalog.IndexOf("guard_tower_large"));
        Assert.Equal(-1, BuildingCatalog.IndexOf("wonder"));
        var castle = BuildingCatalog.All[BuildingIds.Castle];
        Assert.False(castle.PlayerPlaceable);
        Assert.Equal(16, castle.TerritoryRadius);
        Assert.Equal(4, castle.Side);
        Assert.Equal(8, BuildingCatalog.All[BuildingIds.GuardTowerSmall].TerritoryRadius);
        Assert.Equal(12, BuildingCatalog.All[BuildingIds.GuardTowerLarge].TerritoryRadius);
        Assert.Equal(BuildingTerrain.Mountain, BuildingCatalog.All[BuildingIds.IronMine].Terrain);
        Assert.Equal(2, BuildingCatalog.All[BuildingIds.Woodcutter].Side);
        Assert.Equal(3, BuildingCatalog.All[BuildingIds.Sawmill].Side);
        Assert.All(BuildingCatalog.All.Where(b => b.Index != BuildingIds.Castle), b => Assert.True(b.PlayerPlaceable));
        Assert.Equal(BuildingCatalog.All.Count, BuildingCatalog.All.Select(b => b.Id).Distinct().Count());
    }

    [Fact]
    public void Building_data_hash_is_part_of_the_version()
    {
        Assert.NotEqual(0UL, BuildingCatalog.DataHash);
        Assert.Equal(GameVersion.CombinedDataHash, GameVersion.Current.DataHash);
        Assert.NotEqual(Rebuild.Sim.Cultures.CultureCatalog.DataHash, GameVersion.Current.DataHash);
    }

    [Fact]
    public void Every_playing_slot_starts_with_a_complete_castle_centred_on_its_start()
    {
        var sim = Simulation.Create(SampleLogs.MetaScript(1, 1).Setup); // Human, Human, Ai, Monster, Open
        Assert.Equal(3, sim.Buildings.All.Count);
        foreach (var b in sim.Buildings.All)
        {
            var start = sim.StartOf(b.Owner)!.Value;
            Assert.Equal(BuildingIds.Castle, b.Type);
            Assert.Equal(BuildingState.Complete, b.State);
            Assert.Equal((start.X, start.Y), (b.CenterX, b.CenterY));
            var claim = sim.Territory.Claims.Single(c => c.Id == b.ClaimId);
            Assert.Equal((b.Owner, start.X, start.Y, 16), (claim.Owner, claim.X, claim.Y, claim.Radius));
            Assert.Equal(b.Id, sim.Buildings.AtTile(sim.Map.Index(b.X + 3, b.Y + 3)));
        }
        Assert.Equal(new[] { 0, 1, 2 }, sim.Buildings.All.Select(b => (int)b.Owner));
    }

    [Theory]
    [InlineData("s3-none")]
    [InlineData("m4-teams-mirror")]
    [InlineData("l2-mountains")]
    public void Every_start_can_place_land_buildings_and_mines(string name)
    {
        var c = MapSpecs.LoadGolden(MapSpecs.GoldenPath(TestPaths.Golden)).Single(g => g.Name == name);
        var slots = Enumerable.Range(0, c.Spec.PlayerCount)
            .Select(k => new SlotInfo(SlotKind.Human, (byte)c.Spec.TeamOf(k), "rivermen")).ToArray();
        var sim = Simulation.Create(new MatchSetup(c.Spec, 1, slots));
        for (byte slot = 0; slot < slots.Length; slot++)
        {
            FindSpot(sim, slot, BuildingIds.Storehouse);
            FindSpot(sim, slot, BuildingIds.Woodcutter);
            FindSpot(sim, slot, BuildingIds.CoalMine);
        }
    }

    [Fact]
    public void PlaceBuilding_creates_a_construction_site()
    {
        var sim = Simulation.Create(TwoPlayers());
        var (x, y) = FindSpot(sim, 0, BuildingIds.Sawmill);
        Run(sim, BuildingCommands.Place(0, 0, BuildingIds.Sawmill, x, y, rotation: 2));
        Assert.Equal(0, sim.RejectedCommands);
        var site = sim.Buildings.All[^1];
        // Turn 0 ran ticks 0 and 1: the first plank arrived at tick 0 and two build ticks are done.
        Assert.Equal(new Building(3, BuildingIds.Sawmill, 0, x, y, 2, BuildingState.ConstructionSite, 0, 1, 0, 2), site);
        for (int dy = 0; dy < 3; dy++)
            for (int dx = 0; dx < 3; dx++)
                Assert.Equal(site.Id, sim.Buildings.AtTile(sim.Map.Index(x + dx, y + dy)));
        Assert.Equal(0, sim.Buildings.AtTile(sim.Map.Index(x + 3, y)));
        // The same spot (or one touching its margin) is now taken.
        Assert.False(sim.Buildings.IsFootprintFree(x + 3, y, 2));
        Assert.False(sim.Buildings.IsFootprintFree(x - 2, y - 2, 2));
        Assert.True(sim.Buildings.IsFootprintFree(x + 4, y, 2));
        Assert.True(sim.Buildings.IsFootprintFree(x - 3, y - 3, 2));
    }

    [Fact]
    public void Invalid_placements_are_rejected()
    {
        var sim = Simulation.Create(TwoPlayers());
        var (x, y) = FindSpot(sim, 0, BuildingIds.Woodcutter);
        var (ex, ey) = FindSpot(sim, 1, BuildingIds.Woodcutter);
        var castle = sim.Buildings.All[0];
        var mine = FindSpot(sim, 0, BuildingIds.IronMine);

        Assert.Equal(PlacementResult.UnknownType, Check(sim, 0, BuildingCatalog.All.Count, x, y));
        Assert.Equal(PlacementResult.NotPlaceable, Check(sim, 0, BuildingIds.Castle, x, y));
        Assert.Equal(PlacementResult.OutOfMap, Check(sim, 0, BuildingIds.Woodcutter, sim.Map.Edge - 1, y));
        Assert.Equal(PlacementResult.NotOwnTerritory, Check(sim, 0, BuildingIds.Woodcutter, ex, ey)); // enemy land
        Assert.Equal(PlacementResult.NotOwnTerritory, Check(sim, 0, BuildingIds.Woodcutter, 0, 0));   // no man's land
        Assert.Equal(PlacementResult.Occupied, Check(sim, 0, BuildingIds.Woodcutter, castle.X + 1, castle.Y + 1));
        Assert.Equal(PlacementResult.WrongTerrain, Check(sim, 0, BuildingIds.Woodcutter, mine.X, mine.Y));
        Assert.Equal(PlacementResult.WrongTerrain, Check(sim, 0, BuildingIds.IronMine, x, y));

        int before = sim.Buildings.All.Count;
        var bad = new List<Command>
        {
            BuildingCommands.Place(0, 0, BuildingIds.Woodcutter, ex, ey),
            BuildingCommands.Place(0, 1, BuildingIds.Castle, x, y),
            BuildingCommands.Place(0, 2, BuildingIds.Woodcutter, x, y, rotation: 4),
            new Command(CommandType.PlaceBuilding, 0, 0, 3, new byte[] { 3, 0, 1, 0, 1, 0 }), // payload too short
            BuildingCommands.Place(Command.SystemSlot, 4, BuildingIds.Woodcutter, x, y),       // gameplay from system slot
            BuildingCommands.Place(5, 5, BuildingIds.Woodcutter, x, y),                         // no such slot
        };
        Run(sim, bad.ToArray());
        Assert.Equal(bad.Count, sim.RejectedCommands);
        Assert.Equal(before, sim.Buildings.All.Count);
    }

    [Fact]
    public void Later_commands_in_a_turn_see_earlier_placements()
    {
        var sim = Simulation.Create(TwoPlayers());
        var (x, y) = FindSpot(sim, 0, BuildingIds.Woodcutter);
        Run(sim, BuildingCommands.Place(0, 0, BuildingIds.Woodcutter, x, y), BuildingCommands.Place(0, 1, BuildingIds.Forester, x, y));
        Assert.Equal(1, sim.RejectedCommands);
        Assert.Equal(BuildingIds.Woodcutter, sim.Buildings.All[^1].Type);
    }

    [Fact]
    public void Players_who_left_cannot_build()
    {
        var sim = Simulation.Create(TwoPlayers());
        var (x, y) = FindSpot(sim, 1, BuildingIds.Woodcutter);
        Run(sim, MetaCommands.ForSlot(CommandType.PlayerLeft, 1, 0));
        Run(sim, BuildingCommands.Place(1, 0, BuildingIds.Woodcutter, x, y));
        Assert.Equal(1, sim.RejectedCommands);
    }

    [Fact]
    public void CancelConstruction_removes_only_own_construction_sites()
    {
        var sim = Simulation.Create(TwoPlayers());
        var (x, y) = FindSpot(sim, 0, BuildingIds.Farm);
        Run(sim, BuildingCommands.Place(0, 0, BuildingIds.Farm, x, y));
        int site = sim.Buildings.All[^1].Id;
        int enemyCastle = sim.Buildings.All[1].Id;
        int ownCastle = sim.Buildings.All[0].Id;

        Run(sim,
            BuildingCommands.Cancel(1, 0, site),        // not the owner
            BuildingCommands.Cancel(0, 1, ownCastle),   // complete building
            BuildingCommands.Cancel(0, 2, enemyCastle), // neither
            BuildingCommands.Cancel(0, 3, 999),         // unknown id
            new Command(CommandType.CancelConstruction, 0, 0, 4, new byte[] { 1 })); // bad payload
        Assert.Equal(5, sim.RejectedCommands);
        Assert.True(sim.Buildings.TryGet(site, out _));

        Run(sim, BuildingCommands.Cancel(0, 5, site));
        Assert.Equal(5, sim.RejectedCommands);
        Assert.False(sim.Buildings.TryGet(site, out _));
        Assert.Equal(PlacementResult.Ok, Check(sim, 0, BuildingIds.Farm, x, y)); // footprint free again
        Run(sim, BuildingCommands.Cancel(0, 6, site)); // already gone
        Assert.Equal(6, sim.RejectedCommands);
    }

    [Fact]
    public void Buildings_survive_save_and_load()
    {
        var sim = Simulation.Create(TwoPlayers());
        var a = FindSpot(sim, 0, BuildingIds.Woodcutter);
        Run(sim, BuildingCommands.Place(0, 0, BuildingIds.Woodcutter, a.X, a.Y, 1));
        var b = FindSpot(sim, 1, BuildingIds.CoalMine);
        Run(sim, BuildingCommands.Place(1, 0, BuildingIds.CoalMine, b.X, b.Y));
        var c = FindSpot(sim, 0, BuildingIds.Residence);
        Run(sim, BuildingCommands.Place(0, 1, BuildingIds.Residence, c.X, c.Y), BuildingCommands.Cancel(0, 2, 3));

        var loaded = Simulation.Load(sim.Save());
        Assert.Equal(sim.ComputeHash(), loaded.ComputeHash());
        Assert.Equal(sim.Buildings.All, loaded.Buildings.All);
        Assert.Equal(sim.Buildings.NextId, loaded.Buildings.NextId);
        for (int i = 0; i < sim.Map.TileCount; i++) Assert.Equal(sim.Buildings.AtTile(i), loaded.Buildings.AtTile(i));
    }

    [Fact]
    public void Corrupt_building_lists_are_rejected()
    {
        var sim = Simulation.Create(TwoPlayers());
        byte[] Encode(BuildingRegistry reg)
        {
            var w = new CanonicalWriter(256);
            reg.WriteTo(w);
            return w.ToArray();
        }
        BuildingRegistry Read(byte[] bytes) =>
            BuildingRegistry.ReadFrom(new CanonicalReader(bytes), sim.Map.Edge, sim.Players.Count, sim.Territory);

        var good = Encode(sim.Buildings);
        Assert.Equal(sim.Buildings.All, Read(good).All);

        // Layout: nextId(4) count(4) then per building id(4) type(2) owner(1) x(2) y(2) rot(1) state(1) claim(4)
        // planks(1) stone(1) work(2) stock flag(1) [stock 4 × goods]; a castle takes 22 + 4 × goods bytes.
        const int first = 8;
        int second = first + 22 + 4 * Rebuild.Sim.Goods.GoodCatalog.All.Count;
        var cases = new (int Offset, byte Value)[]
        {
            (0, 0),                 // next id 0
            (first + 0, 9),         // id ≥ next id
            (first + 4, 200),       // unknown type
            (first + 6, 7),         // unknown owner
            (first + 11, 9),        // rotation
            (first + 12, 5),        // state
            (first + 13, 99),       // claim that does not exist
            (second + 13, 1),       // second castle points at the first castle's claim (other owner)
        };
        foreach (var (offset, value) in cases)
        {
            var bytes = (byte[])good.Clone();
            bytes[offset] = value;
            Assert.Throws<InvalidDataException>(() => Read(bytes));
        }
        // Second castle moved onto the first one.
        var overlap = (byte[])good.Clone();
        System.Array.Copy(good, first + 7, overlap, second + 7, 4);
        Assert.Throws<InvalidDataException>(() => Read(overlap));
    }

    [Fact]
    public void Build_script_exercises_accepted_and_rejected_commands()
    {
        var log = SampleLogs.BuildScript(1, 600);
        var sim = Simulation.Create(log.Setup);
        foreach (var bundle in log.Bundles) sim.ExecuteTurn(bundle);
        int placed = sim.Buildings.NextId - 1 - 2; // minus the two start castles
        int live = sim.Buildings.All.Count - 2;
        Assert.True(placed >= 50, $"placed {placed}");
        Assert.True(placed - live >= 5, $"cancelled {placed - live}");
        Assert.True(sim.RejectedCommands >= 200, $"rejected {sim.RejectedCommands}");
        Assert.Contains(sim.Buildings.All, b => b.Definition.Terrain == BuildingTerrain.Mountain);
        Assert.Equal(Replay.Run(log)[^1], sim.ComputeHash());
        Assert.Equal(sim.ComputeHash(), Simulation.Load(sim.Save()).ComputeHash());
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void Occupancy_matches_the_building_list(ulong seed)
    {
        const int edge = 40;
        var rng = new Pcg32(seed, 11);
        var reg = new BuildingRegistry(edge);
        for (int step = 0; step < 400; step++)
        {
            if (reg.All.Count > 0 && rng.NextInt(3) == 0)
            {
                Assert.True(reg.Remove(reg.All[rng.NextInt(reg.All.Count)].Id));
            }
            else
            {
                ushort type = (ushort)rng.NextInt(BuildingCatalog.All.Count);
                int x = rng.NextInt(edge);
                int y = rng.NextInt(edge);
                if (reg.IsFootprintFree(x, y, BuildingCatalog.All[type].Side))
                    reg.Add(type, 0, x, y, 0, BuildingState.ConstructionSite, 0);
                else
                    Assert.Throws<System.InvalidOperationException>(() => reg.Add(type, 0, x, y, 0, BuildingState.ConstructionSite, 0));
            }
            var expected = new int[edge * edge];
            foreach (var b in reg.All)
            {
                int side = b.Definition.Side;
                Assert.True(b.X + side <= edge && b.Y + side <= edge);
                for (int ty = b.Y; ty < b.Y + side; ty++)
                    for (int tx = b.X; tx < b.X + side; tx++)
                        expected[ty * edge + tx] = b.Id;
                foreach (var o in reg.All) // footprints keep the margin to each other
                    if (o.Id != b.Id)
                        Assert.True(o.X >= b.X + side + BuildingRegistry.Margin || b.X >= o.X + o.Definition.Side + BuildingRegistry.Margin
                            || o.Y >= b.Y + side + BuildingRegistry.Margin || b.Y >= o.Y + o.Definition.Side + BuildingRegistry.Margin);
            }
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], reg.AtTile(i));
        }
    }
}
