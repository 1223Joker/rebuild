using System.IO;
using Rebuild.Sim.Goods;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.World;

/// <summary>
/// Production statistics (docs/06-economy.md §4): per player and good, units produced and consumed per game minute in a
/// ring buffer of the last <see cref="Minutes"/> minutes, plus match totals. A unit is <i>produced</i> when a production
/// cycle puts it into an output pile; it is <i>consumed</i> when a carrier hands it over to a consumer — an input pile of a
/// production building or a construction site (ASSUMPTION: counted at hand-over rather than at cycle start, because an
/// input pile with alternatives ("a|b|c" in data) does not remember which good it holds; refunds of a
/// cancelled site and units lost with a demolished building are not counted). Household food, water and fuel come with
/// needs (docs/12-needs-seasons-weather.md). Used by the UI and the AI (docs/05-ai.md).
/// Minute m covers ticks [m·<see cref="TicksPerMinute"/>, (m+1)·<see cref="TicksPerMinute"/>) and lives in slot
/// m mod <see cref="Minutes"/>; <see cref="Advance"/> clears the slot of a minute when it begins.
/// </summary>
public sealed class ProductionStatistics
{
    /// <summary>Ticks per game minute at 1× speed.</summary>
    public const int TicksPerMinute = 60 * Simulation.TicksPerSecond;
    /// <summary>Minutes kept in the ring buffer (ASSUMPTION: one hour, enough for the UI's 1 h graph and AI trends).</summary>
    public const int Minutes = 60;

    private static int Goods => GoodCatalog.All.Count;

    /// <summary>Per player: [slot · goods + good] for produced and consumed units.</summary>
    private readonly int[][] _produced;
    private readonly int[][] _consumed;
    /// <summary>Per player: [good] match totals.</summary>
    private readonly long[][] _totalProduced;
    private readonly long[][] _totalConsumed;
    private int _minute;

    public ProductionStatistics(int playerCount, int tick)
    {
        _produced = new int[playerCount][];
        _consumed = new int[playerCount][];
        _totalProduced = new long[playerCount][];
        _totalConsumed = new long[playerCount][];
        for (int p = 0; p < playerCount; p++)
        {
            _produced[p] = new int[Minutes * Goods];
            _consumed[p] = new int[Minutes * Goods];
            _totalProduced[p] = new long[Goods];
            _totalConsumed[p] = new long[Goods];
        }
        _minute = tick / TicksPerMinute;
    }

    /// <summary>The running game minute (the minute the next tick belongs to).</summary>
    public int CurrentMinute => _minute;

    /// <summary>Called after every tick with the new tick count: clears the slot of a minute that begins.</summary>
    public void Advance(int tick)
    {
        if (tick % TicksPerMinute != 0) return;
        _minute = tick / TicksPerMinute;
        int from = Slot(_minute) * Goods;
        for (int p = 0; p < _produced.Length; p++)
        {
            System.Array.Clear(_produced[p], from, Goods);
            System.Array.Clear(_consumed[p], from, Goods);
        }
    }

    /// <summary>Records one unit of <paramref name="good"/> produced by <paramref name="player"/> in the running minute.</summary>
    public void Produce(int player, int good)
    {
        _produced[player][Slot(_minute) * Goods + good]++;
        _totalProduced[player][good]++;
    }

    /// <summary>Records one unit of <paramref name="good"/> consumed by <paramref name="player"/> in the running minute.</summary>
    public void Consume(int player, int good)
    {
        _consumed[player][Slot(_minute) * Goods + good]++;
        _totalConsumed[player][good]++;
    }

    /// <summary>Units produced in the minute <paramref name="minutesAgo"/> before the running one (0 = running minute); 0 outside the buffer.</summary>
    public int Produced(int player, int good, int minutesAgo) => At(_produced, player, good, minutesAgo);

    /// <summary>Units consumed in the minute <paramref name="minutesAgo"/> before the running one (0 = running minute); 0 outside the buffer.</summary>
    public int Consumed(int player, int good, int minutesAgo) => At(_consumed, player, good, minutesAgo);

    /// <summary>Units produced since match start.</summary>
    public long TotalProduced(int player, int good) => _totalProduced[player][good];

    /// <summary>Units consumed since match start.</summary>
    public long TotalConsumed(int player, int good) => _totalConsumed[player][good];

    private int At(int[][] counts, int player, int good, int minutesAgo)
    {
        if (minutesAgo < 0 || minutesAgo >= Minutes || minutesAgo > _minute) return 0;
        return counts[player][Slot(_minute - minutesAgo) * Goods + good];
    }

    private static int Slot(int minute) => minute % Minutes;

    /// <summary>Slots in use: one per minute since match start, at most <see cref="Minutes"/> (slots 0..n-1).</summary>
    private static int UsedSlots(int minute) => System.Math.Min(minute + 1, Minutes);

    /// <summary>
    /// Canonical state: per player, the produced/consumed counts of the slots in use (slot order, goods in index order),
    /// then the match totals. The running minute follows from the tick.
    /// </summary>
    public void WriteTo(CanonicalWriter w)
    {
        int slots = UsedSlots(_minute);
        for (int p = 0; p < _produced.Length; p++)
        {
            for (int i = 0; i < slots * Goods; i++)
            {
                w.WriteInt32(_produced[p][i]);
                w.WriteInt32(_consumed[p][i]);
            }
            for (int g = 0; g < Goods; g++)
            {
                w.WriteInt64(_totalProduced[p][g]);
                w.WriteInt64(_totalConsumed[p][g]);
            }
        }
    }

    /// <summary>
    /// Reads and validates statistics at <paramref name="tick"/>: counts ≥ 0, and each match total at least the sum of its
    /// good's counts in the buffer.
    /// </summary>
    public static ProductionStatistics ReadFrom(CanonicalReader r, int playerCount, int tick)
    {
        var s = new ProductionStatistics(playerCount, tick);
        int slots = UsedSlots(s._minute);
        for (int p = 0; p < playerCount; p++)
        {
            for (int i = 0; i < slots * Goods; i++)
            {
                int produced = r.ReadInt32(), consumed = r.ReadInt32();
                if (produced < 0 || consumed < 0) throw new InvalidDataException("Negative production statistics");
                s._produced[p][i] = produced;
                s._consumed[p][i] = consumed;
            }
            for (int g = 0; g < Goods; g++)
            {
                long produced = r.ReadInt64(), consumed = r.ReadInt64();
                long sumProduced = 0, sumConsumed = 0;
                for (int k = 0; k < slots; k++)
                {
                    sumProduced += s._produced[p][k * Goods + g];
                    sumConsumed += s._consumed[p][k * Goods + g];
                }
                if (produced < sumProduced || consumed < sumConsumed)
                    throw new InvalidDataException("Production statistics totals below their per-minute counts");
                s._totalProduced[p][g] = produced;
                s._totalConsumed[p][g] = consumed;
            }
        }
        return s;
    }
}
