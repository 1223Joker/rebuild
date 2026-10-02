using System.IO;
using Rebuild.Sim.Core;

namespace Rebuild.Sim.Serialization;

/// <summary>Runs a command log on a fresh simulation and reports the state hash after every turn.</summary>
public static class Replay
{
    /// <summary>Returns hash[i] = state hash after executing turn i.</summary>
    public static ulong[] Run(CommandLog log)
    {
        if (log.Version != GameVersion.Current)
            throw new InvalidDataException($"Log is from version {log.Version}, this is {GameVersion.Current}");
        var sim = Simulation.Create(log.Setup);
        var hashes = new ulong[log.Bundles.Count];
        for (int i = 0; i < hashes.Length; i++)
        {
            sim.ExecuteTurn(log.Bundles[i]);
            hashes[i] = sim.ComputeHash();
        }
        return hashes;
    }
}
