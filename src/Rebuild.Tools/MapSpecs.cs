using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Match;

namespace Rebuild.Tools;

/// <summary>MapSpec parsing for the CLI and the golden map cases (tests/golden/mapgen.json).</summary>
public static class MapSpecs
{
    public sealed record GoldenCase(string Name, MapSpec Spec);

    public static MapSize ParseSize(string s) => s.ToUpperInvariant() switch
    {
        "S" or "SMALL" => MapSize.Small,
        "M" or "MEDIUM" => MapSize.Medium,
        "L" or "LARGE" => MapSize.Large,
        "XL" or "EXTRALARGE" => MapSize.ExtraLarge,
        _ => throw new ArgumentException($"unknown size '{s}' (S, M, L, XL)"),
    };

    public static string SizeName(MapSize size) => size switch
    {
        MapSize.Small => "S",
        MapSize.Medium => "M",
        MapSize.Large => "L",
        _ => "XL",
    };

    private static T ParseEnum<T>(string s) where T : struct, Enum =>
        Enum.TryParse<T>(s, ignoreCase: true, out var v) && Enum.IsDefined(v)
            ? v
            : throw new ArgumentException($"unknown {typeof(T).Name} '{s}' ({string.Join(", ", Enum.GetNames<T>())})");

    /// <summary>Builds a spec from named values; missing values take the defaults of docs/03-mapgen.md §2.</summary>
    public static MapSpec Build(Func<string, string?> get)
    {
        ulong seed = ulong.Parse(get("seed") ?? "1", CultureInfo.InvariantCulture);
        var size = ParseSize(get("size") ?? "M");
        byte players = byte.Parse(get("players") ?? "2", CultureInfo.InvariantCulture);
        var spec = new MapSpec(seed, size, players);
        if (get("teams") is string teams)
        {
            var list = Array.ConvertAll(teams.Split(','), t => int.Parse(t, CultureInfo.InvariantCulture));
            spec = spec with { Teams = MapSpec.PackTeams(list) };
        }
        if (get("mix") is string mix)
        {
            var p = Array.ConvertAll(mix.Split('/'), t => byte.Parse(t, CultureInfo.InvariantCulture));
            if (p.Length != 3) throw new ArgumentException("--mix needs water/mountain/forest, e.g. 15/15/25");
            spec = spec with { Mix = new TerrainMix(p[0], p[1], p[2]) };
        }
        if (get("resources") is string r) spec = spec with { Resources = ParseEnum<ResourceDensity>(r) };
        if (get("symmetry") is string sym) spec = spec with { Symmetry = ParseEnum<MapSymmetry>(sym) };
        if (get("monsters") is string m) spec = spec with { Monsters = ParseEnum<MonsterDensity>(m) };
        string? error = spec.Check();
        if (error != null) throw new ArgumentException("invalid map spec: " + error);
        return spec;
    }

    public static IReadOnlyList<GoldenCase> LoadGolden(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var cases = new List<GoldenCase>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string? Get(string key)
            {
                if (!e.TryGetProperty(key, out var v)) return null;
                return v.ValueKind switch
                {
                    JsonValueKind.Number => v.GetRawText(),
                    JsonValueKind.Array => string.Join(key == "mix" ? "/" : ",", EnumerateRaw(v)),
                    _ => v.GetString(),
                };
            }
            cases.Add(new GoldenCase(Get("name") ?? throw new InvalidDataException("golden case without name"), Build(Get)));
        }
        return cases;
    }

    private static IEnumerable<string> EnumerateRaw(JsonElement array)
    {
        foreach (var item in array.EnumerateArray()) yield return item.GetRawText();
    }

    public static string Describe(MapSpec s) =>
        $"seed {s.Seed}, {SizeName(s.Size)} {s.Edge}², {s.PlayerCount} starts, teams {Teams(s)}, " +
        $"mix {s.Mix.Water}/{s.Mix.Mountain}/{s.Mix.Forest}, resources {s.Resources}, {s.Symmetry}, monsters {s.Monsters}, " +
        $"generator v{s.GeneratorVersion}";

    private static string Teams(MapSpec s)
    {
        var parts = new string[s.PlayerCount];
        for (int i = 0; i < parts.Length; i++) parts[i] = s.TeamOf(i).ToString(CultureInfo.InvariantCulture);
        return string.Join(",", parts);
    }

    // Used by the tests and the hashes command.
    public static string GoldenPath(string goldenDir) => Path.Combine(goldenDir, "mapgen.json");

    public static string MapLine(GoldenCase c)
    {
        var result = MapGenerator.Generate(c.Spec);
        string hash = result.Map == null ? "failed" : result.Map.ComputeHash().ToString("x16", CultureInfo.InvariantCulture);
        return $"map {c.Name} {result.Attempts} {hash}";
    }
}
