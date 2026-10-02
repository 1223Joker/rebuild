using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Core;
using Rebuild.Sim.Match;

namespace Rebuild.Sim.Serialization;

/// <summary>
/// Command log (<c>.rblog</c>, docs/08-testing.md §3): header {magic "RBLG", format, GameVersion, MatchSetup}
/// followed by one <see cref="TurnBundle"/> per turn, contiguous from turn 0, until end of data.
/// The host writes it for every real match, so any played game can become a regression test.
/// </summary>
public sealed class CommandLog
{
    private static readonly byte[] Magic = { (byte)'R', (byte)'B', (byte)'L', (byte)'G' };
    public const ushort FormatVersion = 1;

    private readonly List<TurnBundle> _bundles = new();

    public CommandLog(GameVersion version, MatchSetup setup)
    {
        Version = version;
        Setup = setup;
    }

    public GameVersion Version { get; }
    public MatchSetup Setup { get; }
    public IReadOnlyList<TurnBundle> Bundles => _bundles;

    public void Append(TurnBundle bundle)
    {
        if (bundle.Turn != (uint)_bundles.Count)
            throw new System.ArgumentException($"Expected turn {_bundles.Count}, got {bundle.Turn}", nameof(bundle));
        _bundles.Add(bundle);
    }

    /// <summary>Header bytes; a live match writes these once, then appends <see cref="EncodeBundle"/> per turn.</summary>
    public static byte[] EncodeHeader(GameVersion version, MatchSetup setup)
    {
        var w = new CanonicalWriter(128);
        w.WriteRaw(Magic);
        w.WriteUInt16(FormatVersion);
        version.WriteTo(w);
        setup.WriteTo(w);
        return w.ToArray();
    }

    public static byte[] EncodeBundle(TurnBundle bundle)
    {
        var w = new CanonicalWriter(64);
        bundle.WriteTo(w);
        return w.ToArray();
    }

    public byte[] ToBytes()
    {
        var w = new CanonicalWriter(1024);
        w.WriteRaw(EncodeHeader(Version, Setup));
        foreach (var b in _bundles) b.WriteTo(w);
        return w.ToArray();
    }

    public static CommandLog FromBytes(byte[] data)
    {
        var r = new CanonicalReader(data);
        var magic = r.ReadRaw(4);
        for (int i = 0; i < 4; i++)
            if (magic[i] != Magic[i]) throw new InvalidDataException("Not a Rebuild command log");
        ushort format = r.ReadUInt16();
        if (format != FormatVersion) throw new InvalidDataException($"Unsupported command log format {format}");
        var log = new CommandLog(GameVersion.ReadFrom(r), MatchSetup.ReadFrom(r));
        while (!r.AtEnd) log.Append(TurnBundle.ReadFrom(r));
        return log;
    }
}
