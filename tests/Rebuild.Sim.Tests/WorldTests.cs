using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Core;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Sim.World;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>Territory and start assignment (docs/06-economy.md §5, docs/04-game-modes.md §1).</summary>
public class WorldTests
{
    /// <summary>Brute-force reference: every tile goes to the lowest-id live claim covering it.</summary>
    private static byte[] Reference(int edge, IReadOnlyList<TerritoryClaim> claims)
    {
        var owner = new byte[edge * edge];
        for (int y = 0; y < edge; y++)
            for (int x = 0; x < edge; x++)
            {
                owner[y * edge + x] = Territory.NoOwner;
                foreach (var c in claims)
                {
                    long dx = x - c.X, dy = y - c.Y;
                    if (dx * dx + dy * dy > (long)c.Radius * c.Radius) continue;
                    owner[y * edge + x] = c.Owner;
                    break;
                }
            }
        return owner;
    }

    private static byte[] Grid(Territory t)
    {
        var g = new byte[t.Edge * t.Edge];
        for (int i = 0; i < g.Length; i++) g[i] = t.OwnerAt(i);
        return g;
    }

    [Fact]
    public void Older_claim_keeps_overlapping_tiles()
    {
        var t = new Territory(64);
        int a = t.AddClaim(0, 20, 20, 10);
        t.AddClaim(1, 28, 20, 10);
        Assert.Equal(0, t.OwnerAt(24, 20)); // overlap → older claim
        Assert.Equal(1, t.OwnerAt(32, 20));
        Assert.Equal(Territory.NoOwner, t.OwnerAt(50, 50));
        Assert.Equal(0, t.OwnerAt(30, 20)); // dx = 10 is still inside r = 10
        Assert.Equal(1, t.OwnerAt(31, 20));

        Assert.True(t.RemoveClaim(a));
        Assert.Equal(1, t.OwnerAt(24, 20)); // inherited by the remaining claim
        Assert.Equal(Territory.NoOwner, t.OwnerAt(14, 20));
        Assert.False(t.RemoveClaim(a));
    }

    [Fact]
    public void Claims_are_clipped_at_the_map_edge()
    {
        var t = new Territory(32);
        t.AddClaim(3, 0, 31, 5);
        Assert.Equal(3, t.OwnerAt(0, 31));
        Assert.Equal(3, t.OwnerAt(3, 27));
        Assert.Equal(Reference(32, t.Claims), Grid(t));
        Assert.Equal(Count(Reference(32, t.Claims), 3), t.TilesOwnedBy(3));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void Incremental_updates_equal_brute_force(ulong seed)
    {
        const int edge = 48;
        var rng = new Pcg32(seed, 77);
        var t = new Territory(edge);
        var live = new List<int>();
        for (int step = 0; step < 200; step++)
        {
            if (live.Count > 0 && rng.NextInt(3) == 0)
            {
                int index = rng.NextInt(live.Count);
                Assert.True(t.RemoveClaim(live[index]));
                live.RemoveAt(index);
            }
            else
            {
                byte owner = (byte)rng.NextInt(8);
                int x = rng.NextInt(edge);
                int y = rng.NextInt(edge);
                int radius = rng.NextInt(14);
                live.Add(t.AddClaim(owner, x, y, radius));
            }
            var expected = Reference(edge, t.Claims);
            Assert.Equal(expected, Grid(t));
            for (byte p = 0; p < 8; p++) Assert.Equal(Count(expected, p), t.TilesOwnedBy(p));
        }
    }

    [Fact]
    public void Territory_round_trips_and_rejects_a_mismatching_grid()
    {
        var t = new Territory(40);
        int a = t.AddClaim(0, 10, 10, 8);
        t.AddClaim(1, 16, 12, 8);
        t.RemoveClaim(a);
        t.AddClaim(2, 30, 30, 6);
        var w = new CanonicalWriter(4096);
        t.WriteTo(w);
        var bytes = w.ToArray();

        var copy = Territory.ReadFrom(new CanonicalReader(bytes), 40);
        Assert.Equal(Grid(t), Grid(copy));
        Assert.Equal(t.NextClaimId, copy.NextClaimId);
        Assert.Equal(t.Claims, copy.Claims);

        bytes[^1] ^= 0x01; // last grid byte no longer matches the claims
        Assert.Throws<InvalidDataException>(() => Territory.ReadFrom(new CanonicalReader(bytes), 40));
    }

    [Fact]
    public void Invalid_claims_and_ids_are_rejected()
    {
        var t = new Territory(16);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => t.AddClaim(0, 1, 1, Territory.MaxRadius + 1));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => t.AddClaim(Territory.NoOwner, 1, 1, 3));
        var w = new CanonicalWriter(512);
        t.WriteTo(w);
        var bytes = w.ToArray();
        bytes[0] = 0; // NextClaimId = 0 would collide with the "unclaimed" marker
        Assert.Throws<InvalidDataException>(() => Territory.ReadFrom(new CanonicalReader(bytes), 16));
    }

    [Fact]
    public void Starts_go_to_human_and_ai_slots_in_slot_order()
    {
        var setup = new MatchSetup(
            new MapSpec(11, MapSize.Small, 3) with { Teams = MapSpec.PackTeams(new[] { 0, 1, 1 }), Monsters = MonsterDensity.Low },
            5,
            new[]
            {
                new SlotInfo(SlotKind.Open, 7, ""),
                new SlotInfo(SlotKind.Human, 0, "rivermen"),
                new SlotInfo(SlotKind.Monster, 2, ""),
                new SlotInfo(SlotKind.Ai, 1, "rivermen"),
                new SlotInfo(SlotKind.Ai, 1, "rivermen"),
            });
        var sim = Simulation.Create(setup);
        Assert.Null(sim.StartOf(0));
        Assert.Null(sim.StartOf(2));
        Assert.Equal(sim.Map.Starts[0], sim.StartOf(1));
        Assert.Equal(sim.Map.Starts[1], sim.StartOf(3));
        Assert.Equal(sim.Map.Starts[2], sim.StartOf(4));

        // Every start castle owns its own tile and a full r=16 disc (starts are ≥ Dmin apart, plateau r=20 is land).
        Assert.Equal(3, sim.Territory.Claims.Count);
        foreach (int slot in new[] { 1, 3, 4 })
        {
            var s = sim.StartOf(slot)!.Value;
            Assert.Equal(slot, sim.Territory.OwnerAt(s.X, s.Y));
            Assert.True(sim.Territory.TilesOwnedBy((byte)slot) > 700, $"slot {slot} owns {sim.Territory.TilesOwnedBy((byte)slot)}");
        }
        Assert.Equal(0, sim.Territory.TilesOwnedBy(0));
        Assert.Equal(0, sim.Territory.TilesOwnedBy(2));

        Assert.True(sim.AreAllies(3, 4));
        Assert.False(sim.AreAllies(1, 3));
        Assert.False(sim.AreAllies(2, 1)); // monsters have no allies
        Assert.True(sim.AreAllies(2, 2));
    }

    [Theory]
    [InlineData(3, "0,1")]   // more Human/AI slots than starts
    [InlineData(1, "0,1")]   // fewer
    [InlineData(2, "1,0")]   // team mismatch
    public void Setup_must_fit_the_map(int humans, string teams)
    {
        var mapTeams = System.Array.ConvertAll(teams.Split(','), int.Parse);
        var slots = new List<SlotInfo>();
        for (int i = 0; i < humans; i++) slots.Add(new SlotInfo(SlotKind.Human, (byte)(i % 2), "rivermen"));
        var setup = new MatchSetup(new MapSpec(1, MapSize.Small, 2) with { Teams = MapSpec.PackTeams(mapTeams) }, 1, slots);
        Assert.NotNull(StartAssignment.Check(setup));
        Assert.Throws<System.ArgumentException>(() => Simulation.Create(setup));
    }

    [Fact]
    public void Map_hash_is_part_of_the_state()
    {
        static MatchSetup Setup(ulong mapSeed) => new(new MapSpec(mapSeed, MapSize.Small, 2), 1,
            new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Human, 1, "rivermen") });
        var a = Simulation.Create(Setup(1));
        var b = Simulation.Create(Setup(2));
        Assert.Equal(MapGenerator.Generate(Setup(1).Map).Map!.ComputeHash(), a.MapHash);
        Assert.NotEqual(a.MapHash, b.MapHash);
        Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void Load_rejects_a_save_whose_map_hash_differs()
    {
        var sim = Simulation.Create(new MatchSetup(new MapSpec(9, MapSize.Small, 2), 1,
            new[] { new SlotInfo(SlotKind.Human, 0, "rivermen"), new SlotInfo(SlotKind.Ai, 1, "rivermen") }));
        var save = sim.Save();
        // The MapHash follows the player table and precedes territory, buildings, settlers, jobs and quotas; flip one of its bytes.
        var w = new CanonicalWriter(65536);
        sim.Territory.WriteTo(w);
        sim.Buildings.WriteTo(w);
        sim.Settlers.WriteTo(w);
        sim.Logistics.WriteTo(w);
        sim.Quotas.WriteTo(w);
        int mapHashOffset = save.Length - w.Length - 8;
        save[mapHashOffset] ^= 0xFF;
        var e = Assert.Throws<InvalidDataException>(() => Simulation.Load(save));
        Assert.Contains("map hash", e.Message);
    }

    private static int Count(byte[] grid, byte owner)
    {
        int n = 0;
        foreach (byte b in grid) if (b == owner) n++;
        return n;
    }
}
