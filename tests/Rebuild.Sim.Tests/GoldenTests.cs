using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Rebuild.Sim.Core;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Serialization;
using Rebuild.Tools;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>
/// Golden values in tests/golden/hashes.txt (same format as `rebuild-tools hashes`). They may only change
/// together with a GameVersion bump (docs/08-testing.md §3).
/// </summary>
public class GoldenTests
{
    private static Dictionary<string, string[]> ReadGolden() =>
        File.ReadAllLines(Path.Combine(TestPaths.Golden, "hashes.txt"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split(' '))
            .ToDictionary(p => p[0] == "replay" || p[0] == "map" ? p[0] + " " + p[1] : p[0], p => p);

    [Fact]
    public void Version_matches_golden()
    {
        Assert.Equal(GameVersion.Current.ToString(), ReadGolden()["version"][1]);
    }

    [Fact]
    public void Probe_matches_golden()
    {
        string actual = DeterminismProbe.Run(1, DeterminismProbe.DefaultSteps).ToString("x16", CultureInfo.InvariantCulture);
        Assert.Equal(ReadGolden()["probe"][1], actual);
    }

    [Fact]
    public void Replays_match_golden()
    {
        var golden = ReadGolden();
        var files = Directory.GetFiles(Path.Combine(TestPaths.Golden, "replays"), "*.rblog");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var entry = golden["replay " + Path.GetFileName(file)];
            var hashes = Replay.Run(CommandLog.FromBytes(File.ReadAllBytes(file)));
            Assert.Equal(int.Parse(entry[2], CultureInfo.InvariantCulture), hashes.Length);
            Assert.Equal(entry[3], hashes[^1].ToString("x16", CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void Generator_version_matches_golden()
    {
        Assert.Equal(MapGenerator.Version.ToString(CultureInfo.InvariantCulture), ReadGolden()["generator"][1]);
    }

    [Fact]
    public void Maps_match_golden()
    {
        var golden = ReadGolden();
        var cases = MapSpecs.LoadGolden(MapSpecs.GoldenPath(TestPaths.Golden));
        Assert.Equal(24, cases.Count);
        foreach (var c in cases)
            Assert.Equal(string.Join(' ', golden["map " + c.Name]), MapSpecs.MapLine(c));
    }
}
