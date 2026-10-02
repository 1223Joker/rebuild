using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Rebuild.Sim.Core;
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
          sample-log <out.rblog> [--seed N] [--turns N]
                                           write the scripted M0 sample log
          hashes <golden-dir>              print probe hash + final hash of every *.rblog in <golden-dir>/replays
                                           (CI compares this output across operating systems)
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
        var log = SampleLogs.MetaScript(ULongOption(args, "--seed", 1), (int)ULongOption(args, "--turns", 3000));
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
        return 0;
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
