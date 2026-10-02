using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Rebuild.Sim.Core;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;

namespace Rebuild.Tools;

/// <summary>Headless developer/CI tools (docs/01-architecture.md §2): probe, replay, golden data.</summary>
public static class Program
{
    private const string Usage = """
        rebuild-tools <command> [options]

          version                          print GameVersion
          probe [--seed N] [--steps N]     cross-platform determinism hash (Pcg32 + Fix)
          replay <file.rblog> [--every N]  run a command log, print state hashes every N turns and at the end
          sample-log <out.rblog> [--script meta|build] [--seed N] [--turns N]
                                           write a scripted sample log (meta: M0 meta commands, build: M2 buildings)
          hashes <golden-dir>              print probe hash, final hash of every *.rblog in <golden-dir>/replays and
                                           the MapHash of every case in <golden-dir>/mapgen.json
                                           (CI compares this output across operating systems)
          mapgen [spec] [--png F] [--scale N] [--attempt N]
                                           generate + validate a map, print share code, hash and F1–F11 report
          mapgen [spec] --stats N [--min-first-pass P]
                                           N consecutive seeds from --seed: pass rates, failing metrics, timings;
                                           exit 3 if a map fails all attempts or first-attempt passes < P %
            spec: --code RB-… | --seed N --size S|M|L|XL --players N --teams 0,0,1,1 --mix 15/15/25
                  --resources Low|Normal|High --symmetry None|Mirror|Rotational|Equalized
                  --monsters None|Low|Medium|High
        """;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) return Fail(Usage);
            switch (args[0])
            {
                case "version":
                    Console.WriteLine(GameVersion.Current);
                    return 0;
                case "probe":
                    Console.WriteLine(Hex(DeterminismProbe.Run(
                        ULongOption(args, "--seed", 1), (int)ULongOption(args, "--steps", DeterminismProbe.DefaultSteps))));
                    return 0;
                case "replay":
                    return ReplayCommand(args);
                case "sample-log":
                    return SampleLogCommand(args);
                case "hashes":
                    return HashesCommand(args);
                case "mapgen":
                    return MapGenCommand(args);
                default:
                    return Fail(Usage);
            }
        }
        catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException || e is FormatException)
        {
            return Fail("error: " + e.Message);
        }
    }

    private static int ReplayCommand(string[] args)
    {
        if (args.Length < 2) return Fail(Usage);
        var hashes = Replay.Run(CommandLog.FromBytes(File.ReadAllBytes(args[1])));
        int every = (int)ULongOption(args, "--every", 100);
        for (int i = every - 1; i < hashes.Length; i += every)
            Console.WriteLine($"turn {i,6} {Hex(hashes[i])}");
        Console.WriteLine($"final  {hashes.Length - 1,6} {(hashes.Length > 0 ? Hex(hashes[^1]) : "-")}");
        return 0;
    }

    private static int SampleLogCommand(string[] args)
    {
        if (args.Length < 2) return Fail(Usage);
        string script = StringOption(args, "--script") ?? "meta";
        ulong seed = ULongOption(args, "--seed", 1);
        int turns = (int)ULongOption(args, "--turns", 3000);
        CommandLog log;
        switch (script)
        {
            case "meta": log = SampleLogs.MetaScript(seed, turns); break;
            case "build": log = SampleLogs.BuildScript(seed, turns); break;
            default: return Fail("unknown --script (meta, build)");
        }
        File.WriteAllBytes(args[1], log.ToBytes());
        Console.WriteLine($"wrote {args[1]}: {log.Bundles.Count} turns");
        return 0;
    }

    private static int HashesCommand(string[] args)
    {
        if (args.Length < 2) return Fail(Usage);
        Console.WriteLine($"version {GameVersion.Current}");
        Console.WriteLine($"probe {Hex(DeterminismProbe.Run(1, DeterminismProbe.DefaultSteps))}");
        var dir = Path.Combine(args[1], "replays");
        foreach (var file in Directory.GetFiles(dir, "*.rblog").OrderBy(f => f, StringComparer.Ordinal))
        {
            var hashes = Replay.Run(CommandLog.FromBytes(File.ReadAllBytes(file)));
            Console.WriteLine($"replay {Path.GetFileName(file)} {hashes.Length} {Hex(hashes[^1])}");
        }
        Console.WriteLine($"generator {MapGenerator.Version}");
        foreach (var c in MapSpecs.LoadGolden(MapSpecs.GoldenPath(args[1])))
            Console.WriteLine(MapSpecs.MapLine(c));
        return 0;
    }

    private static MapSpec SpecFromArgs(string[] args)
    {
        string? code = StringOption(args, "--code");
        if (code != null) return ShareCode.Decode(code);
        return MapSpecs.Build(key => StringOption(args, "--" + key));
    }

    private static int MapGenCommand(string[] args)
    {
        var spec = SpecFromArgs(args);
        if (StringOption(args, "--stats") != null)
            return MapGenStats(spec, (int)ULongOption(args, "--stats", 100), (int)ULongOption(args, "--min-first-pass", 0));

        var watch = Stopwatch.StartNew();
        MapData? map;
        ValidationReport report;
        string? attemptOption = StringOption(args, "--attempt");
        if (attemptOption != null)
        {
            map = MapGenerator.GenerateAttempt(spec, int.Parse(attemptOption, CultureInfo.InvariantCulture));
            report = MapValidator.Validate(map);
        }
        else
        {
            var result = MapGenerator.Generate(spec);
            map = result.Map;
            report = result.Report;
        }
        watch.Stop();

        Console.WriteLine($"code      {ShareCode.Encode(spec)}");
        Console.WriteLine($"spec      {MapSpecs.Describe(spec)}");
        Console.WriteLine(map == null
            ? $"result    FAILED after {MapGenerator.MaxAttempts} attempts ({watch.ElapsedMilliseconds} ms)"
            : $"result    attempt {map.Attempt} hash {Hex(map.ComputeHash())} ({watch.ElapsedMilliseconds} ms)");
        foreach (var m in report.Metrics)
            Console.WriteLine($"  {(m.Passed ? "ok  " : "FAIL")} {m.Id,-3} {m.Detail}");
        if (map != null) Console.WriteLine($"layers    {LayerStats(map)}");

        string? png = StringOption(args, "--png");
        if (png != null)
        {
            map ??= MapGenerator.GenerateAttempt(spec, MapGenerator.MaxAttempts - 1);
            File.WriteAllBytes(png, Png.RenderMap(map, (int)ULongOption(args, "--scale", 2)));
            Console.WriteLine($"wrote {png}");
        }
        return map == null ? 2 : 0;
    }

    private static string LayerStats(MapData map)
    {
        int n = map.TileCount, mountains = 0, walkableMountains = 0, walkable = 0, buildable = 0;
        var regions = new SortedSet<int>();
        for (int i = 0; i < n; i++)
        {
            bool w = map.IsWalkable(i);
            if (w) { walkable++; regions.Add(map.Region[i]); }
            if (map.IsBuildable(i)) buildable++;
            if (map.Terrain[i] == (byte)Terrain.Mountain) { mountains++; if (w) walkableMountains++; }
        }
        return $"walkable {100 * walkable / n} %, buildable {100 * buildable / n} %, walkable mountains " +
               $"{(mountains == 0 ? 0 : 100 * walkableMountains / mountains)} %, {regions.Count} regions, {map.Lairs.Count} lairs";
    }

    private static int MapGenStats(MapSpec spec, int count, int minFirstPassPercent)
    {
        int firstPass = 0, succeeded = 0;
        long totalAttempts = 0;
        var attemptMs = new List<double>();
        var totalMs = new List<double>();
        var failures = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int k = 0; k < count; k++)
        {
            var s = spec with { Seed = spec.Seed + (ulong)k };
            var watch = Stopwatch.StartNew();
            bool done = false;
            for (int attempt = 0; attempt < MapGenerator.MaxAttempts && !done; attempt++)
            {
                var one = Stopwatch.StartNew();
                var report = MapValidator.Validate(MapGenerator.GenerateAttempt(s, attempt));
                attemptMs.Add(one.Elapsed.TotalMilliseconds);
                totalAttempts++;
                if (report.Passed)
                {
                    done = true;
                    succeeded++;
                    if (attempt == 0) firstPass++;
                }
                else if (attempt == 0)
                {
                    foreach (var m in report.Failed) failures[m.Id] = failures.GetValueOrDefault(m.Id) + 1;
                }
            }
            totalMs.Add(watch.Elapsed.TotalMilliseconds);
        }
        attemptMs.Sort();
        totalMs.Sort();
        Console.WriteLine($"spec           {MapSpecs.Describe(spec)} (+{count} seeds)");
        Console.WriteLine($"first attempt  {firstPass}/{count} pass ({100.0 * firstPass / count:0.0} %)");
        Console.WriteLine($"within {MapGenerator.MaxAttempts}      {succeeded}/{count} ({100.0 * succeeded / count:0.0} %), {(double)totalAttempts / count:0.00} attempts/map");
        Console.WriteLine($"attempt ms     mean {Mean(attemptMs):0} p50 {Pct(attemptMs, 50):0} p99 {Pct(attemptMs, 99):0}");
        Console.WriteLine($"map ms         mean {Mean(totalMs):0} p99 {Pct(totalMs, 99):0} max {totalMs[^1]:0}");
        foreach (var kv in failures) Console.WriteLine($"first-attempt failures {kv.Key}: {kv.Value}");
        bool ok = succeeded == count && firstPass * 100 >= minFirstPassPercent * count;
        if (!ok) Console.WriteLine($"FAIL: need every map within {MapGenerator.MaxAttempts} attempts and >= {minFirstPassPercent} % first-attempt passes");
        return ok ? 0 : 3;
    }

    private static double Mean(List<double> v) => v.Count == 0 ? 0 : v.Average();

    private static double Pct(List<double> sorted, int p) =>
        sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (sorted.Count * p + 99) / 100 - 1)];

    private static string? StringOption(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        if (i < 0) return null;
        if (i + 1 >= args.Length) throw new ArgumentException($"{name} needs a value");
        return args[i + 1];
    }

    private static ulong ULongOption(string[] args, string name, ulong fallback)
    {
        int i = Array.IndexOf(args, name);
        if (i < 0) return fallback;
        if (i + 1 >= args.Length) throw new ArgumentException($"{name} needs a value");
        return ulong.Parse(args[i + 1], CultureInfo.InvariantCulture);
    }

    private static string Hex(ulong v) => v.ToString("x16", CultureInfo.InvariantCulture);

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
