using Rebuild.Sim.Match;

namespace Rebuild.Sim.World;

/// <summary>Season of the calendar, in calendar order (docs/12-needs-seasons-weather.md §2.1).</summary>
public enum Season : byte
{
    Spring = 0,
    Summer = 1,
    Autumn = 2,
    Winter = 3,
}

/// <summary>
/// Match calendar (docs/12-needs-seasons-weather.md §2.1): a match starts on the first tick of spring, the four seasons
/// follow each other in <see cref="SeasonTicks"/>-long blocks and repeat every year. A pure function of the tick and the
/// lobby's <see cref="SeasonLength"/>, so it has no state of its own; with <see cref="SeasonLength.Off"/> it is summer forever.
/// </summary>
public static class Calendar
{
    /// <summary>Seasons per year.</summary>
    public const int SeasonsPerYear = 4;

    /// <summary>Ticks of one season (Short 4 min, Normal 6 min, Long 9 min), or 0 for <see cref="SeasonLength.Off"/>.</summary>
    public static int SeasonTicks(SeasonLength length) => length switch
    {
        SeasonLength.Short => 4 * 60 * Simulation.TicksPerSecond,
        SeasonLength.Normal => 6 * 60 * Simulation.TicksPerSecond,
        SeasonLength.Long => 9 * 60 * Simulation.TicksPerSecond,
        _ => 0,
    };

    /// <summary>Season of tick <paramref name="tick"/> (≥ 0).</summary>
    public static Season SeasonAt(int tick, SeasonLength length)
    {
        int n = SeasonTicks(length);
        return n == 0 ? Season.Summer : (Season)(tick / n % SeasonsPerYear);
    }

    /// <summary>Year of tick <paramref name="tick"/>, counted from 0 (always 0 without seasons).</summary>
    public static int YearAt(int tick, SeasonLength length)
    {
        int n = SeasonTicks(length);
        return n == 0 ? 0 : tick / (n * SeasonsPerYear);
    }

    /// <summary>Ticks since the current season started (the tick itself without seasons).</summary>
    public static int TicksIntoSeason(int tick, SeasonLength length)
    {
        int n = SeasonTicks(length);
        return n == 0 ? tick : tick % n;
    }
}
