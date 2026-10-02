using System;
using System.IO;
using Rebuild.Sim.Core;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>Map generation (docs/03-mapgen.md §8): noise reference values, symmetry, fairness, share codes.</summary>
public class MapGenTests
{
    [Fact]
    public void Squirrel3_matches_reference_values()
    {
        Assert.Equal(0x1A0A96C2u, IntNoise.Squirrel3(0, 0));
        Assert.Equal(0x2B427F45u, IntNoise.Squirrel3(1, 0));
        Assert.Equal(0x1C512502u, IntNoise.Squirrel3(-1, 0));
        Assert.Equal(0x7404189Au, IntNoise.Squirrel3(12345, 0xDEADBEEF));
    }

    [Fact]
    public void Fbm_matches_reference_values()
    {
        Assert.Equal(40874, IntNoise.Fbm(0, 0, 6, 5, 1));
        Assert.Equal(39640, IntNoise.Fbm(100, 37, 6, 5, 1));
        Assert.Equal(36448, IntNoise.Fbm(-50, 511, 6, 5, 42));
        Assert.Equal(32091, IntNoise.Value(3, 5, 2, 7));
    }

    [Fact]
    public void Noise_stays_in_16_bit_range_and_smoothstep_is_monotone()
    {
        for (int i = 0; i < 20000; i++)
        {
            int v = IntNoise.Fbm(i * 7 - 3000, i * 13 % 997, 6, 5, (uint)i);
            Assert.InRange(v, 0, 65535);
        }
        int previous = -1;
        for (int t = 0; t < IntNoise.One; t += 97)
        {
            int s = IntNoise.SmoothStep(t);
            Assert.True(s >= previous);
            previous = s;
        }
        Assert.Equal(0, IntNoise.SmoothStep(0));
    }

    [Fact]
    public void IntTrig_is_close_to_sine()
    {
        Assert.Equal(0, IntTrig.Sin(0));
        Assert.Equal(65536, IntTrig.Sin(16384));
        Assert.Equal(-65536, IntTrig.Sin(49152));
        for (int a = 0; a < IntTrig.FullTurn; a += 61)
        {
            double expected = Math.Sin(a * 2 * Math.PI / IntTrig.FullTurn) * 65536;
            Assert.True(Math.Abs(IntTrig.Sin(a) - expected) <= 0.002 * 65536, $"angle {a}");
        }
    }

    [Fact]
    public void Same_spec_gives_same_map_and_different_seeds_differ()
    {
        var spec = new MapSpec(99, MapSize.Small, 3);
        ulong a = MapGenerator.GenerateAttempt(spec, 0).ComputeHash();
        ulong b = MapGenerator.GenerateAttempt(spec, 0).ComputeHash();
        ulong c = MapGenerator.GenerateAttempt(spec with { Seed = 100 }, 0).ComputeHash();
        ulong d = MapGenerator.GenerateAttempt(spec, 1).ComputeHash();
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
    }

    [Theory]
    [InlineData(MapSymmetry.Mirror, 2, MapSpec.FreeForAll)]
    [InlineData(MapSymmetry.Mirror, 4, 0x1100u)]
    [InlineData(MapSymmetry.Rotational, 4, MapSpec.FreeForAll)]
    public void Symmetric_maps_are_exactly_symmetric(MapSymmetry symmetry, byte players, uint teams)
    {
        var spec = new MapSpec(5, MapSize.Small, players) with { Symmetry = symmetry, Teams = teams, Monsters = MonsterDensity.Medium };
        var map = MapGenerator.GenerateAttempt(spec, 0);
        int s = map.Edge;
        Span<int> images = stackalloc int[3];
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                int i = y * s + x;
                int count = MapGenerator.Images(symmetry, s, x, y, images);
                for (int k = 0; k < count; k++)
                {
                    int j = images[k];
                    Assert.Equal(map.Height[i], map.Height[j]);
                    Assert.Equal(map.Terrain[i], map.Terrain[j]);
                    Assert.Equal(map.Object[i], map.Object[j]);
                    Assert.Equal(map.Resource[i], map.Resource[j]);
                    Assert.Equal(map.Amount[i], map.Amount[j]);
                }
            }
        }
        // Every start's symmetry image is a start too.
        foreach (var st in map.Starts)
        {
            int count = MapGenerator.Images(symmetry, s, st.X, st.Y, images);
            for (int k = 0; k < count; k++)
            {
                int image = images[k];
                Assert.Contains(map.Starts, o => map.Index(o.X, o.Y) == image);
            }
        }
    }

    [Theory]
    [InlineData(MapSymmetry.Equalized, 5)]
    [InlineData(MapSymmetry.Rotational, 4)]
    [InlineData(MapSymmetry.Mirror, 2)]
    public void Start_zones_are_identical_up_to_rotation(MapSymmetry symmetry, byte players)
    {
        var map = MapGenerator.GenerateAttempt(new MapSpec(17, MapSize.Medium, players) with { Symmetry = symmetry }, 0);
        int r = MapGenerator.FairnessRadius;
        var reference = map.Starts[0];
        for (int k = 1; k < map.Starts.Length; k++)
        {
            var st = map.Starts[k];
            bool anyRotation = false;
            for (int rot = 0; rot < 4 && !anyRotation; rot++)
            {
                bool same = true;
                for (int dy = -r; dy <= r && same; dy++)
                {
                    for (int dx = -r; dx <= r && same; dx++)
                    {
                        if (dx * dx + dy * dy > r * r) continue;
                        var (rx, ry) = rot switch { 0 => (dx, dy), 1 => (-dy, dx), 2 => (-dx, -dy), _ => (dy, -dx) };
                        int a = map.Index(reference.X + dx, reference.Y + dy);
                        int b = map.Index(st.X + rx, st.Y + ry);
                        same = map.Terrain[a] == map.Terrain[b] && map.Object[a] == map.Object[b] &&
                               map.Resource[a] == map.Resource[b] && map.Amount[a] == map.Amount[b];
                    }
                }
                anyRotation = same;
            }
            Assert.True(anyRotation, $"start {k} zone differs from start 0");
        }
    }

    [Fact]
    public void Generated_maps_pass_validation_with_starts_on_buildable_land()
    {
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var spec = new MapSpec(seed, MapSize.Small, (byte)(2 + seed % 5)) with { Monsters = (MonsterDensity)(seed % 4) };
            var result = MapGenerator.Generate(spec);
            Assert.True(result.Succeeded, string.Join("; ", result.Report.Failed));
            var map = result.Map!;
            Assert.True(result.Report.Passed);
            foreach (var st in map.Starts) Assert.True(map.IsBuildable(map.Index(st.X, st.Y)));
            Assert.Equal(spec.Monsters != MonsterDensity.None, map.Lairs.Count > 0);
        }
    }

    [Fact]
    public void Validation_reports_every_metric()
    {
        var report = MapValidator.Validate(MapGenerator.GenerateAttempt(new MapSpec(3, MapSize.Small, 2), 0));
        Assert.Equal(new[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11" },
            Array.ConvertAll(System.Linq.Enumerable.ToArray(report.Metrics), m => m.Id));
    }

    [Fact]
    public void Map_serialization_roundtrips_with_identical_hash_and_derived_layers()
    {
        var map = MapGenerator.GenerateAttempt(new MapSpec(8, MapSize.Small, 4) with { Monsters = MonsterDensity.Low }, 2);
        var w = new CanonicalWriter();
        map.WriteTo(w);
        var copy = MapData.ReadFrom(new CanonicalReader(w.ToArray()));
        Assert.Equal(map.ComputeHash(), copy.ComputeHash());
        Assert.Equal(map.Attempt, copy.Attempt);
        Assert.Equal(map.Flags, copy.Flags);
        Assert.Equal(map.Region, copy.Region);
    }

    [Fact]
    public void Distance_field_uses_octile_costs()
    {
        var map = MapGenerator.GenerateAttempt(new MapSpec(1, MapSize.Small, 2), 0);
        var st = map.Starts[0];
        var dist = MapGenerator.DistanceField(map, new[] { map.Index(st.X, st.Y) });
        Assert.Equal(0, dist[map.Index(st.X, st.Y)]);
        Assert.Equal(10, dist[map.Index(st.X + 1, st.Y)]);
        Assert.Equal(14, dist[map.Index(st.X + 1, st.Y + 1)]);
        Assert.Equal(30, dist[map.Index(st.X, st.Y - 3)]);
    }

    [Fact]
    public void Poisson_disc_points_keep_their_spacing()
    {
        var points = PoissonDisc.Sample(128, 9, new Pcg32(4, 4));
        Assert.True(points.Count > 100);
        for (int a = 0; a < points.Count; a++)
        {
            for (int b = a + 1; b < points.Count; b++)
            {
                int dx = points[a] % 128 - points[b] % 128, dy = points[a] / 128 - points[b] / 128;
                Assert.True(dx * dx + dy * dy >= 81);
            }
        }
    }

    [Fact]
    public void Share_code_roundtrips_and_tolerates_case_and_confusables()
    {
        var spec = new MapSpec(ulong.MaxValue - 12345, MapSize.Large, 4) with
        {
            Teams = MapSpec.PackTeams(new[] { 0, 0, 1, 1 }),
            Mix = new TerrainMix(20, 10, 30),
            Resources = ResourceDensity.High,
            Symmetry = MapSymmetry.Mirror,
            Monsters = MonsterDensity.Medium,
        };
        string code = ShareCode.Encode(spec);
        Assert.StartsWith("RB-", code);
        Assert.Equal(spec, ShareCode.Decode(code));
        Assert.Equal(spec, ShareCode.Decode(code.ToLowerInvariant().Replace('1', 'l').Replace('0', 'o')));
        Assert.Equal(spec, ShareCode.Decode(" " + code.Replace("-", "") + " "));
    }

    [Fact]
    public void Share_code_detects_typos()
    {
        string code = ShareCode.Encode(new MapSpec(42, MapSize.Medium, 2));
        int pos = code.Length - 7;
        char replacement = code[pos] == 'Z' ? 'Y' : 'Z';
        string typo = code.Substring(0, pos) + replacement + code.Substring(pos + 1);
        Assert.Throws<InvalidDataException>(() => ShareCode.Decode(typo));
        Assert.Throws<InvalidDataException>(() => ShareCode.Decode("RB-U"));
    }

    [Theory]
    [InlineData(MapSymmetry.Mirror, 3, MapSpec.FreeForAll)]
    [InlineData(MapSymmetry.Mirror, 4, 0x1000u)]
    [InlineData(MapSymmetry.Rotational, 5, MapSpec.FreeForAll)]
    [InlineData(MapSymmetry.Rotational, 4, 0x1000u)]
    public void Invalid_symmetry_layouts_are_rejected(MapSymmetry symmetry, byte players, uint teams)
    {
        var spec = new MapSpec(1, MapSize.Medium, players) with { Symmetry = symmetry, Teams = teams };
        Assert.NotNull(spec.Check());
        Assert.Throws<ArgumentException>(() => MapGenerator.GenerateAttempt(spec, 0));
    }

    [Fact]
    public void Spec_limits_are_checked()
    {
        Assert.NotNull(new MapSpec(1, MapSize.Small, 7).Check());
        Assert.Null(new MapSpec(1, MapSize.Medium, 8).Check());
        Assert.NotNull(new MapSpec(1, MapSize.Medium, 1).Check());
        Assert.NotNull((new MapSpec(1, MapSize.Medium, 2) with { Mix = new TerrainMix(40, 30, 30) }).Check());
        Assert.Throws<ArgumentException>(() =>
            MapGenerator.GenerateAttempt(new MapSpec(1, MapSize.Medium, 2) with { GeneratorVersion = 999 }, 0));
    }
}
