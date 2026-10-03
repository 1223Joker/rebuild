using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.Match;

public enum SlotKind : byte
{
    Open = 0,
    Human = 1,
    Ai = 2,
    Monster = 3,
}

public enum AiDifficulty : byte
{
    Easy = 0,
    Normal = 1,
    Hard = 2,
}

/// <summary>
/// Lobby option for the calendar (docs/12-needs-seasons-weather.md §2.1): length of one season, or <see cref="Off"/> for
/// eternal summer. Values are written to saves and logs.
/// </summary>
public enum SeasonLength : byte
{
    Off = 0,
    /// <summary>4 min per season.</summary>
    Short = 1,
    /// <summary>6 min per season (year = 24 min); the default.</summary>
    Normal = 2,
    /// <summary>9 min per season.</summary>
    Long = 3,
}

/// <summary>One lobby slot. <see cref="CultureId"/> may be <see cref="RandomCulture"/>.</summary>
public sealed record SlotInfo(SlotKind Kind, byte Team, string CultureId, AiDifficulty Difficulty = AiDifficulty.Normal)
{
    public const string RandomCulture = "random";
}

/// <summary>Everything a match is started from (identical on all peers): map, slots, match seed, season length.</summary>
public sealed class MatchSetup
{
    public const int MaxSlots = 8;

    public MapSpec Map { get; }
    public ulong MatchSeed { get; }
    public IReadOnlyList<SlotInfo> Slots => _slots;
    public SeasonLength Seasons { get; }

    private readonly SlotInfo[] _slots;

    public MatchSetup(MapSpec map, ulong matchSeed, IReadOnlyList<SlotInfo> slots, SeasonLength seasons = SeasonLength.Normal)
    {
        if (slots.Count < 1 || slots.Count > MaxSlots) throw new System.ArgumentException("1..8 slots required", nameof(slots));
        if (seasons > SeasonLength.Long) throw new System.ArgumentException("Unknown season length", nameof(seasons));
        Map = map;
        MatchSeed = matchSeed;
        Seasons = seasons;
        _slots = new SlotInfo[slots.Count];
        for (int i = 0; i < slots.Count; i++) _slots[i] = slots[i];
    }

    public void WriteTo(CanonicalWriter w)
    {
        Map.WriteTo(w);
        w.WriteUInt64(MatchSeed);
        w.WriteByte((byte)_slots.Length);
        foreach (var s in _slots)
        {
            w.WriteByte((byte)s.Kind);
            w.WriteByte(s.Team);
            w.WriteString(s.CultureId);
            w.WriteByte((byte)s.Difficulty);
        }
        w.WriteByte((byte)Seasons);
    }

    public static MatchSetup ReadFrom(CanonicalReader r)
    {
        var map = MapSpec.ReadFrom(r);
        ulong seed = r.ReadUInt64();
        int count = r.ReadByte();
        if (count < 1 || count > MaxSlots) throw new InvalidDataException("Invalid slot count");
        var slots = new SlotInfo[count];
        for (int i = 0; i < count; i++)
            slots[i] = new SlotInfo((SlotKind)r.ReadByte(), r.ReadByte(), r.ReadString(), (AiDifficulty)r.ReadByte());
        var seasons = (SeasonLength)r.ReadByte();
        if (seasons > SeasonLength.Long) throw new InvalidDataException("Invalid season length");
        return new MatchSetup(map, seed, slots, seasons);
    }
}
