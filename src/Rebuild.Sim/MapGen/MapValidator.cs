using System.Collections.Generic;
using System.Globalization;
using Rebuild.Sim.Core;
using Rebuild.Sim.Match;

namespace Rebuild.Sim.MapGen;

public sealed record MetricResult(string Id, bool Passed, string Detail);

public sealed class ValidationReport
{
    public ValidationReport(IReadOnlyList<MetricResult> metrics) => Metrics = metrics;

    public IReadOnlyList<MetricResult> Metrics { get; }

    public bool Passed
    {
        get
        {
            for (int i = 0; i < Metrics.Count; i++)
                if (!Metrics[i].Passed) return false;
            return true;
        }
    }

    public IEnumerable<MetricResult> Failed
    {
        get
        {
            for (int i = 0; i < Metrics.Count; i++)
                if (!Metrics[i].Passed) yield return Metrics[i];
        }
    }
}

/// <summary>Fairness model F1–F11 (docs/03-mapgen.md §4). Thresholds marked ASSUMPTION are tuned in spike S4.</summary>
public static class MapValidator
{
    public const int MinBuildable = 600;
    public const int MinTrees = 80;
    public const int MinStone = 60;
    /// <summary>ASSUMPTION: coal and iron units each within Rf + 8.</summary>
    public const int MinOre = 100;
    /// <summary>ASSUMPTION: fish units + 10 per game animal + fertile tiles within Rf.</summary>
    public const int MinFood = 100;
    public const int GameFoodValue = 10;
    /// <summary>F11 tolerance in percentage points.</summary>
    public const int MixTolerance = 2;

    public static ValidationReport Validate(MapData map)
    {
        var results = new List<MetricResult>();
        var starts = map.Starts;
        int n = starts.Length;
        int s = map.Edge;

        // F1 start distance (octile, tenths of a tile) ≥ Dmin = size·0.6/√players.
        long dmin10 = (long)s * 6 * 1000 / IntMath.Isqrt((long)n * 1_000_000);
        long minPair = long.MaxValue;
        for (int a = 0; a < n; a++)
        {
            for (int b = a + 1; b < n; b++)
            {
                long dx = System.Math.Abs(starts[a].X - starts[b].X), dy = System.Math.Abs(starts[a].Y - starts[b].Y);
                long octile = 10 * System.Math.Max(dx, dy) + 4 * System.Math.Min(dx, dy);
                if (octile < minPair) minPair = octile;
            }
        }
        results.Add(new MetricResult("F1", minPair >= dmin10, $"min start distance {Tenths(minPair)} (need {Tenths(dmin10)})"));

        // F2–F6 per-start counts.
        var buildable = new long[n];
        var trees = new long[n];
        var stone = new long[n];
        var coal = new long[n];
        var iron = new long[n];
        var food = new long[n];
        int rf = MapGenerator.FairnessRadius, ro = rf + 8;
        for (int k = 0; k < n; k++)
        {
            for (int dy = -ro; dy <= ro; dy++)
            {
                for (int dx = -ro; dx <= ro; dx++)
                {
                    int x = starts[k].X + dx, y = starts[k].Y + dy;
                    int d2 = dx * dx + dy * dy;
                    if (x < 0 || y < 0 || x >= s || y >= s || d2 > ro * ro) continue;
                    int i = y * s + x;
                    var res = (Resource)map.Resource[i];
                    if (res == Resource.Coal) coal[k] += map.Amount[i];
                    if (res == Resource.Iron) iron[k] += map.Amount[i];
                    if (d2 > rf * rf) continue;
                    var obj = (MapObject)map.Object[i];
                    if (map.IsBuildable(i)) buildable[k]++;
                    if (obj == MapObject.Tree) trees[k]++;
                    if (obj == MapObject.Stone) stone[k] += map.Amount[i];
                    if (obj == MapObject.Game) food[k] += GameFoodValue;
                    if (res == Resource.Fish) food[k] += map.Amount[i];
                    if (map.Terrain[i] == (byte)Terrain.Fertile) food[k]++;
                }
            }
        }
        results.Add(Balanced("F2", "buildable", buildable, MinBuildable, 110));
        results.Add(Balanced("F3", "trees", trees, MinTrees, 110));
        results.Add(Balanced("F4", "stone", stone, MinStone, 110));
        var coalResult = Balanced("F5", "coal", coal, MinOre, 115);
        var ironResult = Balanced("F5", "iron", iron, MinOre, 115);
        results.Add(new MetricResult("F5", coalResult.Passed && ironResult.Passed, coalResult.Detail + "; " + ironResult.Detail));
        results.Add(Balanced("F6", "food", food, MinFood, 115));

        // F7 connectivity.
        int region = map.Region[map.Index(starts[0].X, starts[0].Y)];
        bool connected = region >= 0;
        for (int k = 1; k < n && connected; k++) connected = map.Region[map.Index(starts[k].X, starts[k].Y)] == region;
        results.Add(new MetricResult("F7", connected, connected ? "all starts in one land region" : "starts are in different regions"));

        // F8 path length to the nearest enemy start, per team (a team's front line): in FFA this is per start;
        // in team games the inner starts of a team sector are farther from enemies by design.
        var fields = new int[n][];
        for (int k = 0; k < n; k++) fields[k] = MapGenerator.DistanceField(map, new[] { map.Index(starts[k].X, starts[k].Y) });
        var teamFront = new List<long>();
        for (int team = 0; team < 8; team++)
        {
            long best = long.MaxValue;
            bool member = false;
            for (int k = 0; k < n; k++)
            {
                if (starts[k].Team != team) continue;
                member = true;
                for (int o = 0; o < n; o++)
                {
                    if (starts[o].Team == team) continue;
                    int d = fields[k][map.Index(starts[o].X, starts[o].Y)];
                    if (d < best) best = d;
                }
            }
            if (member) teamFront.Add(best);
        }
        if (teamFront.Count > 1)
            results.Add(Ratio("F8", "path to nearest enemy per team", teamFront.ToArray(), 120));
        else
            results.Add(new MetricResult("F8", true, "no enemy starts (cooperative)"));

        // F9/F10 lairs.
        var lairs = map.Lairs;
        if (map.Spec.Monsters == MonsterDensity.None)
        {
            bool none = lairs.Count == 0;
            results.Add(new MetricResult("F9", none, none ? "no lairs requested" : "lairs present although MonsterDensity = None"));
            results.Add(new MetricResult("F10", none, "-"));
        }
        else if (lairs.Count == 0)
        {
            results.Add(new MetricResult("F9", false, "no lair could be placed"));
            results.Add(new MetricResult("F10", false, "no lairs"));
        }
        else
        {
            var nearest = new long[n];
            long closest = long.MaxValue;
            for (int k = 0; k < n; k++)
            {
                long best = long.MaxValue;
                for (int l = 0; l < lairs.Count; l++)
                {
                    int d = fields[k][lairs[l]];
                    if (d != int.MaxValue && d < best) best = d;
                }
                nearest[k] = best;
                if (best < closest) closest = best;
            }
            var ratio = Ratio("F9", "nearest lair", nearest, 125);
            bool far = closest >= MapGenerator.LairMinDistance * 10L;
            results.Add(new MetricResult("F9", ratio.Passed && far, $"{ratio.Detail}; closest {Tenths(closest)} (need {MapGenerator.LairMinDistance})"));
            int unreachable = 0;
            for (int l = 0; l < lairs.Count; l++)
                if (map.Region[lairs[l]] != region) unreachable++;
            results.Add(new MetricResult("F10", unreachable == 0, $"{lairs.Count} lairs, {unreachable} unreachable"));
        }

        // F11 terrain mix.
        long water = 0, mountain = 0, forest = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (map.Terrain[i] == (byte)Terrain.Water) water++;
            else if (map.Terrain[i] == (byte)Terrain.Mountain) mountain++;
            if (map.Object[i] == (byte)MapObject.Tree) forest++;
        }
        var mix = map.Spec.Mix;
        long total = map.TileCount;
        bool Within(long count, int percent) => System.Math.Abs(count * 100 - total * percent) <= total * MixTolerance;
        bool mixOk = Within(water, mix.Water) && Within(mountain, mix.Mountain) && Within(forest, mix.Forest);
        results.Add(new MetricResult("F11", mixOk,
            $"water {Percent(water, total)} / mountain {Percent(mountain, total)} / forest {Percent(forest, total)} " +
            $"(requested {mix.Water}/{mix.Mountain}/{mix.Forest})"));

        return new ValidationReport(results);
    }

    private static MetricResult Balanced(string id, string name, long[] values, long minimum, int maxRatioPercent)
    {
        var ratio = Ratio(id, name, values, maxRatioPercent);
        long min = Min(values);
        return new MetricResult(id, ratio.Passed && min >= minimum, $"{ratio.Detail}, need ≥ {minimum}");
    }

    private static MetricResult Ratio(string id, string name, long[] values, int maxRatioPercent)
    {
        long min = Min(values), max = 0;
        for (int i = 0; i < values.Length; i++) if (values[i] > max) max = values[i];
        bool ok = min != long.MaxValue && min > 0 && max * 100 <= min * maxRatioPercent;
        string list = string.Join(",", System.Array.ConvertAll(values, v => v == long.MaxValue ? "∞" : v.ToString(CultureInfo.InvariantCulture)));
        return new MetricResult(id, ok, $"{name} [{list}] max/min ≤ {maxRatioPercent} %");
    }

    private static long Min(long[] values)
    {
        long min = long.MaxValue;
        for (int i = 0; i < values.Length; i++) if (values[i] < min) min = values[i];
        return min;
    }

    private static string Tenths(long v) =>
        v == long.MaxValue ? "∞" : string.Create(CultureInfo.InvariantCulture, $"{v / 10}.{v % 10}");

    private static string Percent(long count, long total)
    {
        long tenths = count * 1000 / total;
        return string.Create(CultureInfo.InvariantCulture, $"{tenths / 10}.{tenths % 10} %");
    }
}
