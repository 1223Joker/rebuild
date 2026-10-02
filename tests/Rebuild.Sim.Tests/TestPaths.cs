using System;
using System.IO;

namespace Rebuild.Sim.Tests;

internal static class TestPaths
{
    /// <summary>Repository root (the directory containing global.json).</summary>
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
        }
    }

    public static string Golden => Path.Combine(RepoRoot, "tests", "golden");
}
