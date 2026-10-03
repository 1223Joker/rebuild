using System.Collections.Generic;
using Rebuild.Sim.Core;
using Rebuild.Sim.Match;

namespace Rebuild.Sim.MapGen;

/// <summary>Outcome of <see cref="MapGenerator.Generate"/>: the first attempt that passed validation, or none.</summary>
public sealed record MapGenResult(MapData? Map, ValidationReport Report, int Attempts)
{
    public bool Succeeded => Map != null;
}

/// <summary>
/// Seed-based map generator (docs/03-mapgen.md §3, ADR 0003): integer math only, own RNG streams, one thread.
/// Same <see cref="MapSpec"/> ⇒ bit-identical <see cref="MapData"/> on every platform.
/// </summary>
public static class MapGenerator
{
    /// <summary>Bump on any change of generator output; regenerate tests/golden in the same commit.</summary>
    public const ushort Version = 1;
    public const int MaxAttempts = 16;

    /// <summary>Flat buildable plateau around each start.</summary>
    public const int StartPlateauRadius = 20;
    /// <summary>Equalized start-zone template radius.</summary>
    public const int StartZoneRadius = 28;
    /// <summary>Fairness radius Rf (F2–F6).</summary>
    public const int FairnessRadius = 24;
    /// <summary>Lmin: neutral zones and lairs are at least this far (path tiles) from every start.</summary>
    public const int LairMinDistance = 40;

    /// <summary>Generates and validates attempts 0..15 and returns the first that passes (docs/03-mapgen.md §5).</summary>
    public static MapGenResult Generate(MapSpec spec)
    {
        ValidationReport? last = null;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var map = GenerateAttempt(spec, attempt);
            last = MapValidator.Validate(map);
            if (last.Passed) return new MapGenResult(map, last, attempt + 1);
        }
        return new MapGenResult(null, last!, MaxAttempts);
    }

    /// <summary>One unvalidated attempt; attempt n uses the derived seed of step 1.</summary>
    public static MapData GenerateAttempt(MapSpec spec, int attempt)
    {
        string? error = spec.Check();
        if (error != null) throw new System.ArgumentException("Invalid MapSpec: " + error, nameof(spec));
        if (spec.GeneratorVersion != Version)
            throw new System.ArgumentException($"Generator version {spec.GeneratorVersion} is not supported (this is {Version})", nameof(spec));
        if (attempt < 0 || attempt >= MaxAttempts) throw new System.ArgumentOutOfRangeException(nameof(attempt));
        return new Builder(spec, attempt).Run();
    }

    /// <summary>
    /// Walkable/buildable flags of one tile: walkable = land with a slope ≤ 2 to its land neighbours; buildable =
    /// also slope ≤ 1, not mountain and no object. Systems that remove objects refresh the tile with this.
    /// </summary>
    public static byte TileFlagsAt(MapData map, int x, int y)
    {
        int s = map.Edge, i = y * s + x;
        if (map.Terrain[i] == (byte)Terrain.Water) return 0;
        int slope = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= s || ny >= s) continue;
                int j = ny * s + nx;
                if (map.Terrain[j] == (byte)Terrain.Water) continue;
                int d = map.Height[i] - map.Height[j];
                if (d < 0) d = -d;
                if (d > slope) slope = d;
            }
        }
        byte flags = 0;
        if (slope <= 2) flags |= (byte)TileFlags.Walkable;
        if (slope <= 1 && map.Terrain[i] != (byte)Terrain.Mountain && map.Object[i] == (byte)MapObject.None)
            flags |= (byte)TileFlags.Buildable;
        return flags;
    }

    /// <summary>Step 10: walkable/buildable flags and region ids from height, terrain and objects.</summary>
    public static void DeriveLayers(MapData map)
    {
        int s = map.Edge;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                map.Flags[y * s + x] = TileFlagsAt(map, x, y);

        var region = map.Region;
        System.Array.Fill(region, -1);
        var stack = new int[map.TileCount];
        int next = 0;
        for (int start = 0; start < region.Length; start++)
        {
            if (region[start] >= 0 || !map.IsWalkable(start)) continue;
            int top = 0;
            stack[top++] = start;
            region[start] = next;
            while (top > 0)
            {
                int i = stack[--top];
                int x = i % s, y = i / s;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= s || ny >= s) continue;
                        int j = ny * s + nx;
                        if (region[j] >= 0 || !map.IsWalkable(j)) continue;
                        region[j] = next;
                        stack[top++] = j;
                    }
                }
            }
            next++;
        }
    }

    /// <summary>
    /// Octile path distance in tenths of a tile (orthogonal 10, diagonal 14) over walkable tiles from all
    /// <paramref name="sources"/>; <see cref="int.MaxValue"/> = unreachable. Dial's bucket queue, O(tiles).
    /// </summary>
    public static int[] DistanceField(MapData map, System.ReadOnlySpan<int> sources)
    {
        const int Buckets = 15;
        int s = map.Edge;
        var dist = new int[map.TileCount];
        System.Array.Fill(dist, int.MaxValue);
        var buckets = new List<int>[Buckets];
        for (int b = 0; b < Buckets; b++) buckets[b] = new List<int>();
        int pending = 0;
        for (int k = 0; k < sources.Length; k++)
        {
            int src = sources[k];
            if (!map.IsWalkable(src) || dist[src] == 0) continue;
            dist[src] = 0;
            buckets[0].Add(src);
            pending++;
        }
        for (int d = 0; pending > 0; d++)
        {
            var bucket = buckets[d % Buckets];
            for (int n = 0; n < bucket.Count; n++)
            {
                int i = bucket[n];
                pending--;
                if (dist[i] != d) continue;
                int x = i % s, y = i / s;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= s || ny >= s) continue;
                        int j = ny * s + nx;
                        if (!map.IsWalkable(j)) continue;
                        int nd = d + (dx != 0 && dy != 0 ? 14 : 10);
                        if (nd >= dist[j]) continue;
                        dist[j] = nd;
                        buckets[nd % Buckets].Add(j);
                        pending++;
                    }
                }
            }
            bucket.Clear();
        }
        return dist;
    }

    /// <summary>Exact symmetry images of tile (x, y) other than itself (docs/03-mapgen.md §3 step 6).</summary>
    internal static int Images(MapSymmetry symmetry, int edge, int x, int y, System.Span<int> output)
    {
        int last = edge - 1;
        switch (symmetry)
        {
            case MapSymmetry.Mirror:
                output[0] = (last - y) * edge + (last - x);
                return 1;
            case MapSymmetry.Rotational:
                // (x, y) → (last − y, x), applied 1, 2, 3 times.
                output[0] = x * edge + (last - y);
                output[1] = (last - y) * edge + (last - x);
                output[2] = (last - x) * edge + y;
                return 3;
            default:
                return 0;
        }
    }

    /// <summary>Whether (x, y) lies in the fundamental domain that the other domains are copied from.</summary>
    internal static bool InDomain(MapSymmetry symmetry, int edge, int x, int y) => symmetry switch
    {
        MapSymmetry.Mirror => y < edge / 2,
        MapSymmetry.Rotational => x < edge / 2 && y < edge / 2,
        _ => true,
    };

    private sealed class Builder
    {
        // RNG stream ids of the map generator (step 1); one per step so tuning one step leaves the others alone.
        private const ulong StreamStarts = 0x4D47_0001;
        private const ulong StreamNoise = 0x4D47_0002;
        private const ulong StreamResources = 0x4D47_0003;
        private const ulong StreamLairs = 0x4D47_0004;

        // Height bands (height byte): water below LandLow, land LandLow..LandHigh, mountains above.
        private const int WaterHeight = 4;
        private const int LandLow = 10;
        private const int LandHigh = 22;
        private const int MountainTop = LandHigh + 24;
        private const int ClearingRadius = 5;
        private const int CorridorCore = 3;
        private const int CorridorEdge = 9;
        /// <summary>Share of all tiles that become fertile plains (ASSUMPTION, tune in S4).</summary>
        private const int FertilePercent = 6;

        private readonly MapSpec _spec;
        private readonly MapData _map;
        private readonly int _s;
        private readonly int _n;
        private readonly Pcg32 _startsRng;
        private readonly Pcg32 _noiseRng;
        private readonly Pcg32 _resourceRng;
        private readonly Pcg32 _lairRng;
        private readonly int[] _nearest;   // index of the nearest start (or -1 beyond the zone radius)
        private readonly int[] _nearestD2; // squared distance to that start
        private readonly int[] _rotation;  // template rotation per start (quarter turns)
        private int[] _ringOrder = System.Array.Empty<int>(); // start indices in angular order

        public Builder(MapSpec spec, int attempt)
        {
            _spec = spec;
            _map = new MapData(spec, attempt);
            _s = _map.Edge;
            _n = _map.TileCount;
            ulong seed = SplitMix64.Mix(spec.Seed ^ ((ulong)attempt * 0x9E3779B97F4A7C15UL));
            _startsRng = Pcg32.ForStream(seed, StreamStarts);
            _noiseRng = Pcg32.ForStream(seed, StreamNoise);
            _resourceRng = Pcg32.ForStream(seed, StreamResources);
            _lairRng = Pcg32.ForStream(seed, StreamLairs);
            _nearest = new int[_n];
            _nearestD2 = new int[_n];
            _rotation = new int[spec.PlayerCount];
        }

        public MapData Run()
        {
            uint elevationSeed = _noiseRng.NextUInt();
            uint moistureSeed = _noiseRng.NextUInt();
            uint templateElevationSeed = _noiseRng.NextUInt();
            uint templateMoistureSeed = _noiseRng.NextUInt();

            PlaceStarts();
            ComputeZones();
            int[] elevation = Elevation(elevationSeed, templateElevationSeed);
            Classify(elevation);
            Vegetation(moistureSeed, templateMoistureSeed);
            ClearStarts();
            for (int k = 0; k < _spec.PlayerCount; k++) StampStartResources(k);
            ScatterResources();
            Symmetrize();
            DeriveLayers(_map);
            if (_spec.Monsters != MonsterDensity.None)
            {
                PlaceLairs();
                DeriveLayers(_map);
            }
            return _map;
        }

        // ---- step 2: starts --------------------------------------------------------------------------

        private void PlaceStarts()
        {
            int count = _spec.PlayerCount;
            // Teammates get adjacent angle sectors: order starts by (team, index).
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = 1; i < count; i++)
            {
                int v = order[i], j = i - 1;
                while (j >= 0 && _spec.TeamOf(order[j]) > _spec.TeamOf(v)) { order[j + 1] = order[j]; j--; }
                order[j + 1] = v;
            }

            int radius = _s * 35 / 100;
            // Small jitter: larger values make neighbour distances (F8) unequal by construction.
            int angleJitter = IntTrig.FullTurn / count / 32;
            int radialJitter = _s / 80;
            int baseAngle = _startsRng.NextInt(IntTrig.FullTurn);
            var xs = new int[count];
            var ys = new int[count];

            void Ring(int k, int angle)
            {
                int a = angle + _startsRng.NextInt(-angleJitter, angleJitter + 1);
                int r = radius + _startsRng.NextInt(-radialJitter, radialJitter + 1);
                int min = StartZoneRadius, max = _s - 1 - StartZoneRadius;
                xs[k] = System.Math.Clamp(_s / 2 + (int)(((long)r * IntTrig.Cos(a)) >> 16), min, max);
                ys[k] = System.Math.Clamp(_s / 2 + (int)(((long)r * IntTrig.Sin(a)) >> 16), min, max);
            }

            int last = _s - 1;
            switch (_spec.Symmetry)
            {
                case MapSymmetry.Mirror:
                    int half = count / 2;
                    for (int k = 0; k < half; k++)
                    {
                        Ring(k, baseAngle + k * IntTrig.FullTurn / count);
                        xs[k + half] = last - xs[k];
                        ys[k + half] = last - ys[k];
                    }
                    break;
                case MapSymmetry.Rotational:
                    Ring(0, baseAngle);
                    for (int k = 1; k < 4; k++)
                    {
                        xs[k] = last - ys[k - 1];
                        ys[k] = xs[k - 1];
                    }
                    break;
                default:
                    for (int k = 0; k < count; k++) Ring(k, baseAngle + k * IntTrig.FullTurn / count);
                    break;
            }

            var starts = new StartPosition[count];
            for (int k = 0; k < count; k++)
            {
                int slot = order[k];
                starts[slot] = new StartPosition(xs[k], ys[k], (byte)_spec.TeamOf(slot));
            }
            _map.Starts = starts;
            _ringOrder = order;

            // Template orientation: local +x points (mostly) toward the map centre. Doubled coordinates around the
            // exact centre ((edge − 1) / 2) make the choice commute with the Mirror/Rotational symmetries.
            for (int i = 0; i < count; i++)
            {
                int dx = (_s - 1) - 2 * starts[i].X, dy = (_s - 1) - 2 * starts[i].Y;
                _rotation[i] = System.Math.Abs(dx) >= System.Math.Abs(dy) ? (dx >= 0 ? 0 : 2) : (dy >= 0 ? 1 : 3);
            }
        }

        private static (int X, int Y) Rotate(int quarterTurns, int x, int y) => quarterTurns switch
        {
            0 => (x, y),
            1 => (-y, x),
            2 => (-x, -y),
            _ => (y, -x),
        };

        private void ComputeZones()
        {
            var starts = _map.Starts;
            int zone2 = StartZoneRadius * StartZoneRadius;
            for (int y = 0; y < _s; y++)
            {
                for (int x = 0; x < _s; x++)
                {
                    int best = -1, bestD2 = int.MaxValue;
                    for (int k = 0; k < starts.Length; k++)
                    {
                        int dx = x - starts[k].X, dy = y - starts[k].Y;
                        int d2 = dx * dx + dy * dy;
                        if (d2 < bestD2) { bestD2 = d2; best = k; }
                    }
                    int i = y * _s + x;
                    _nearest[i] = bestD2 < zone2 ? best : -1;
                    _nearestD2[i] = bestD2;
                }
            }
        }

        /// <summary>Template coordinates of tile i inside the zone of its nearest start (rotated back).</summary>
        private (int X, int Y) TemplateCoords(int i)
        {
            var start = _map.Starts[_nearest[i]];
            int dx = i % _s - start.X, dy = i / _s - start.Y;
            var local = Rotate((4 - _rotation[_nearest[i]]) & 3, dx, dy);
            return (local.X + 4096, local.Y + 4096);
        }

        /// <summary>Blend weight 0..65536 for d² in [from², to²].</summary>
        private static int Weight(int d2, int from, int to)
        {
            int a = from * from, b = to * to;
            if (d2 <= a) return 0;
            if (d2 >= b) return IntNoise.One;
            return (int)((long)(d2 - a) * IntNoise.One / (b - a));
        }

        private static int Lerp(int a, int b, int t) => a + (int)(((long)(b - a) * t) >> 16);

        // ---- step 3: elevation -------------------------------------------------------------------------

        private int[] Elevation(uint seed, uint templateSeed)
        {
            var e = new int[_n];
            for (int y = 0; y < _s; y++)
                for (int x = 0; x < _s; x++)
                    e[y * _s + x] = IntNoise.Fbm(x, y, 6, 5, seed);
            SymmetrizeField(e);

            // Plateau level: the middle of the land band of the unmodified field.
            var mix = _spec.Mix;
            int plateau = Quantile(e, null, (long)_n * (mix.Water + (100 - mix.Water - mix.Mountain) / 2) / 100);
            // The start-zone template is used by every mode except None: symmetry alone makes only images equal,
            // the template also makes teammates' zones equal.
            bool equalized = _spec.Symmetry != MapSymmetry.None;
            // Keep the template inside the land band (with margin) so start zones hold no natural water or
            // mountains that could block the way to the neighbours (F7/F8); stamps add the guaranteed ones.
            int landLow = Quantile(e, null, (long)_n * mix.Water / 100);
            int landHigh = Quantile(e, null, (long)_n * (100 - mix.Mountain) / 100);
            int margin = (landHigh - landLow) / 6;
            for (int i = 0; i < _n; i++)
            {
                if (_nearest[i] < 0) continue;
                int d2 = _nearestD2[i];
                if (equalized)
                {
                    var t = TemplateCoords(i);
                    int template = System.Math.Clamp(IntNoise.Fbm(t.X, t.Y, 6, 5, templateSeed), landLow + margin, landHigh - margin);
                    // Plateau → template over the whole ring (gentle slopes), then → global terrain beyond Rf;
                    // within Rf the value depends only on the template, so all start zones are equal.
                    int zone = Lerp(plateau, template, Weight(d2, StartPlateauRadius, StartZoneRadius));
                    e[i] = Lerp(zone, e[i], Weight(d2, FairnessRadius, StartZoneRadius));
                }
                else
                {
                    e[i] = Lerp(plateau, e[i], Weight(d2, StartPlateauRadius, StartZoneRadius));
                }
            }
            Corridors(e, plateau);
            return e;
        }

        /// <summary>
        /// Land corridors between ring-adjacent starts: elevation is pulled to the plateau level within
        /// <see cref="CorridorCore"/> tiles of the segment, fading out at <see cref="CorridorEdge"/>, so neighbours are
        /// connected (F7) by near-straight paths (F8). Faded in only outside Rf, so start zones stay equal.
        /// </summary>
        private void Corridors(int[] e, int plateau)
        {
            int count = _ringOrder.Length;
            int pairs = count == 2 ? 1 : count;
            var strength = new int[_n];
            for (int p = 0; p < pairs; p++)
            {
                var a = _map.Starts[_ringOrder[p]];
                var b = _map.Starts[_ringOrder[(p + 1) % count]];
                long vx = b.X - a.X, vy = b.Y - a.Y, len2 = vx * vx + vy * vy;
                int x0 = System.Math.Max(0, System.Math.Min(a.X, b.X) - CorridorEdge);
                int x1 = System.Math.Min(_s - 1, System.Math.Max(a.X, b.X) + CorridorEdge);
                int y0 = System.Math.Max(0, System.Math.Min(a.Y, b.Y) - CorridorEdge);
                int y1 = System.Math.Min(_s - 1, System.Math.Max(a.Y, b.Y) + CorridorEdge);
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        long wx = x - a.X, wy = y - a.Y;
                        long t = System.Math.Clamp(wx * vx + wy * vy, 0, len2); // projection × len2
                        // |w·len2 − v·t|² / len2² = squared distance to the segment.
                        long ex = wx * len2 - vx * t, ey = wy * len2 - vy * t;
                        long d2 = (long)((((System.Int128)ex * ex) + ((System.Int128)ey * ey)) / ((System.Int128)len2 * len2));
                        int w = IntNoise.One - Weight((int)System.Math.Min(d2, int.MaxValue), CorridorCore, CorridorEdge);
                        int i = y * _s + x;
                        if (w > strength[i]) strength[i] = w;
                    }
                }
            }
            for (int i = 0; i < _n; i++)
            {
                if (strength[i] == 0) continue;
                int fade = _nearest[i] < 0 ? IntNoise.One : Weight(_nearestD2[i], FairnessRadius, StartZoneRadius);
                int w = (int)(((long)strength[i] * fade) >> 16);
                e[i] = Lerp(e[i], plateau, w);
            }
        }

        /// <summary>Averages each symmetry orbit, so continuous fields become exactly symmetric without seams.</summary>
        private void SymmetrizeField(int[] f)
        {
            if (_spec.Symmetry != MapSymmetry.Mirror && _spec.Symmetry != MapSymmetry.Rotational) return;
            System.Span<int> images = stackalloc int[3];
            for (int y = 0; y < _s; y++)
            {
                for (int x = 0; x < _s; x++)
                {
                    if (!InDomain(_spec.Symmetry, _s, x, y)) continue;
                    int i = y * _s + x;
                    int count = Images(_spec.Symmetry, _s, x, y, images);
                    long sum = f[i];
                    for (int k = 0; k < count; k++) sum += f[images[k]];
                    int avg = (int)(sum / (count + 1));
                    f[i] = avg;
                    for (int k = 0; k < count; k++) f[images[k]] = avg;
                }
            }
        }

        /// <summary>Smallest value v such that at least <paramref name="below"/> selected tiles are &lt; v.</summary>
        private static int Quantile(int[] values, bool[]? selected, long below)
        {
            var histogram = new int[IntNoise.One + 1];
            for (int i = 0; i < values.Length; i++)
                if (selected == null || selected[i]) histogram[values[i]]++;
            long cumulative = 0;
            for (int v = 0; v <= IntNoise.One; v++)
            {
                if (cumulative >= below) return v;
                cumulative += histogram[v];
            }
            return IntNoise.One + 1;
        }

        // ---- step 4: classification by quantiles --------------------------------------------------------

        private void Classify(int[] e)
        {
            var mix = _spec.Mix;
            int starts = _spec.PlayerCount;
            // Start stamps add water and mountain tiles; take them off the quantile targets so F11 holds.
            long waterTarget = System.Math.Max(0L, (long)_n * mix.Water / 100 - starts * PondArea);
            long mountainTarget = System.Math.Max(0L, (long)_n * mix.Mountain / 100 - starts * MountainArea);
            int waterLevel = Quantile(e, null, waterTarget);
            int mountainLevel = Quantile(e, null, _n - mountainTarget);
            if (mountainLevel <= waterLevel) mountainLevel = waterLevel + 1;
            int peak = 0;
            for (int i = 0; i < _n; i++) if (e[i] > peak) peak = e[i];

            for (int i = 0; i < _n; i++)
            {
                int v = e[i];
                if (v < waterLevel)
                {
                    _map.Terrain[i] = (byte)Terrain.Water;
                    _map.Height[i] = WaterHeight;
                }
                else if (v >= mountainLevel)
                {
                    _map.Terrain[i] = (byte)Terrain.Mountain;
                    long span = System.Math.Max(1, peak - mountainLevel);
                    _map.Height[i] = (byte)(LandHigh + 1 + (v - mountainLevel) * (long)(MountainTop - LandHigh) / span);
                }
                else
                {
                    _map.Terrain[i] = (byte)Terrain.Plains;
                    _map.Height[i] = (byte)(LandLow + (v - waterLevel) * (long)(LandHigh - LandLow) / (mountainLevel - waterLevel));
                }
            }
        }

        // ---- step 5: moisture → forest and fertile plains ----------------------------------------------

        private void Vegetation(uint seed, uint templateSeed)
        {
            var m = new int[_n];
            for (int y = 0; y < _s; y++)
                for (int x = 0; x < _s; x++)
                    m[y * _s + x] = IntNoise.Fbm(x, y, 5, 4, seed);
            SymmetrizeField(m);
            if (_spec.Symmetry != MapSymmetry.None)
            {
                for (int i = 0; i < _n; i++)
                {
                    if (_nearest[i] < 0) continue;
                    var t = TemplateCoords(i);
                    int template = IntNoise.Fbm(t.X, t.Y, 5, 4, templateSeed);
                    m[i] = Lerp(template, m[i], Weight(_nearestD2[i], FairnessRadius, StartZoneRadius));
                }
            }
            // Keep the start plateau open for building: no natural forest inside it (wood comes from step 7).
            for (int i = 0; i < _n; i++)
                if (_nearest[i] >= 0)
                    m[i] = _spec.Symmetry == MapSymmetry.None && _nearestD2[i] <= FairnessRadius * FairnessRadius
                        ? 0
                        : Lerp(0, m[i], Weight(_nearestD2[i], StartPlateauRadius - 4, StartPlateauRadius + 2));

            var candidate = new bool[_n];
            long candidates = 0;
            for (int i = 0; i < _n; i++)
            {
                candidate[i] = _map.Terrain[i] == (byte)Terrain.Plains;
                if (candidate[i]) candidates++;
            }
            long forestTarget = System.Math.Max(0L, (long)_n * _spec.Mix.Forest / 100 - _spec.PlayerCount * ForestArea);
            int forestLevel = Quantile(m, candidate, System.Math.Max(0, candidates - forestTarget));
            long fertileTarget = (long)_n * FertilePercent / 100;
            int fertileLevel = Quantile(m, candidate, System.Math.Max(0, candidates - forestTarget - fertileTarget));
            for (int i = 0; i < _n; i++)
            {
                if (!candidate[i]) continue;
                if (m[i] >= forestLevel) _map.Object[i] = (byte)MapObject.Tree;
                else if (m[i] >= fertileLevel) _map.Terrain[i] = (byte)Terrain.Fertile;
            }
        }

        private void ClearStarts()
        {
            int r2 = ClearingRadius * ClearingRadius;
            for (int i = 0; i < _n; i++)
            {
                if (_nearest[i] < 0 || _nearestD2[i] > r2) continue;
                _map.Object[i] = (byte)MapObject.None;
                _map.Terrain[i] = (byte)Terrain.Plains;
            }
        }

        // ---- step 7: guaranteed start resources (template in local coordinates, +x toward the centre) -----

        private const int MountainRadius = 5, PondRadius = 3, ForestRadius = 5;
        // Disc areas (tiles with dx² + dy² ≤ r²) used to correct the quantile targets.
        private const int MountainArea = 81, PondArea = 29, ForestArea = 81;
        private static readonly int[] GameOffsets = { 18, -12, 6, -12, 12, -18, 12, -6, 16, -17, 7, -7 };

        private void StampStartResources(int k)
        {
            var start = _map.Starts[k];
            int rot = _rotation[k];
            bool gold = _spec.Size >= MapSize.Large;

            // Small mountain with coal (one half) and iron (other half), gold core on L/XL.
            Disc(start, rot, 2, 16, MountainRadius, (i, dx, dy, d2) =>
            {
                // Rises gently (2 per tile) from the surrounding plateau so miners can walk up.
                int baseHeight = _map.Terrain[i] == (byte)Terrain.Water ? LandLow : _map.Height[i];
                int rise = (MountainRadius - (int)IntMath.Isqrt(d2)) * 2;
                _map.Terrain[i] = (byte)Terrain.Mountain;
                _map.Height[i] = (byte)System.Math.Min(255, baseHeight + rise);
                _map.Object[i] = (byte)MapObject.None;
                if (gold && d2 <= 1) { _map.Resource[i] = (byte)Resource.Gold; _map.Amount[i] = 6; }
                else if (dx < 0) { _map.Resource[i] = (byte)Resource.Coal; _map.Amount[i] = 8; }
                else { _map.Resource[i] = (byte)Resource.Iron; _map.Amount[i] = 8; }
            });
            // Forest patch.
            Disc(start, rot, 12, -12, ForestRadius, (i, dx, dy, d2) => SetLand(i, MapObject.Tree, 0));
            // Game animals around the forest.
            for (int g = 0; g < GameOffsets.Length; g += 2)
                Disc(start, rot, GameOffsets[g], GameOffsets[g + 1], 0, (i, dx, dy, d2) => SetLand(i, MapObject.Game, 0));
            // Stone outcrop.
            Disc(start, rot, -10, -10, 2, (i, dx, dy, d2) => SetLand(i, MapObject.Stone, 6));
            // Fertile plains.
            Disc(start, rot, 10, 10, 4, (i, dx, dy, d2) =>
            {
                SetLand(i, MapObject.None, 0);
                _map.Terrain[i] = (byte)Terrain.Fertile;
            });
            // Fish pond.
            Disc(start, rot, -14, 8, PondRadius, (i, dx, dy, d2) =>
            {
                _map.Terrain[i] = (byte)Terrain.Water;
                _map.Height[i] = WaterHeight;
                _map.Object[i] = (byte)MapObject.None;
                _map.Resource[i] = (byte)Resource.Fish;
                _map.Amount[i] = 8;
            });
        }

        private void SetLand(int i, MapObject obj, byte amount)
        {
            if (_map.Terrain[i] == (byte)Terrain.Water || _map.Terrain[i] == (byte)Terrain.Mountain)
            {
                _map.Terrain[i] = (byte)Terrain.Plains;
                _map.Height[i] = (byte)System.Math.Clamp((int)_map.Height[i], LandLow, LandHigh);
            }
            _map.Object[i] = (byte)obj;
            _map.Resource[i] = (byte)Resource.None;
            _map.Amount[i] = amount;
        }

        /// <summary>Calls <paramref name="action"/>(tile, local dx, local dy, d²) for every tile of a template disc.</summary>
        private void Disc(StartPosition start, int rot, int cx, int cy, int radius, System.Action<int, int, int, int> action)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int d2 = dx * dx + dy * dy;
                    if (d2 > radius * radius) continue;
                    var w = Rotate(rot, cx + dx, cy + dy);
                    int x = start.X + w.X, y = start.Y + w.Y;
                    if (x < 0 || y < 0 || x >= _s || y >= _s) continue;
                    action(y * _s + x, dx, dy, d2);
                }
            }
        }

        // ---- step 8: global resources (Poisson disc) ---------------------------------------------------

        private void ScatterResources()
        {
            int spacing = _spec.Resources switch
            {
                ResourceDensity.Low => 30,
                ResourceDensity.High => 16,
                _ => 22,
            };
            // Outside the F5 ore radius (Rf + 8) plus the largest deposit radius, so start zones stay equal.
            int keepOut = (FairnessRadius + 8 + 4) * (FairnessRadius + 8 + 4);
            var points = PoissonDisc.Sample(_s, spacing, _resourceRng);
            for (int p = 0; p < points.Count; p++)
            {
                int i = points[p];
                if (_nearestD2[i] < keepOut) continue;
                int x = i % _s, y = i / _s;
                var terrain = (Terrain)_map.Terrain[i];
                if (terrain == Terrain.Mountain)
                {
                    int roll = _resourceRng.NextInt(100);
                    var type = roll < 45 ? Resource.Coal : roll < 80 ? Resource.Iron : Resource.Gold;
                    int radius = 2 + _resourceRng.NextInt(2);
                    int amount = 4 + _resourceRng.NextInt(9);
                    Around(x, y, radius, j =>
                    {
                        if (_map.Terrain[j] != (byte)Terrain.Mountain || _map.Resource[j] != 0) return;
                        _map.Resource[j] = (byte)type;
                        _map.Amount[j] = (byte)amount;
                    });
                }
                else if (terrain == Terrain.Water)
                {
                    if (!Near(x, y, 3, Terrain.Plains)) continue;
                    int amount = 4 + _resourceRng.NextInt(5);
                    Around(x, y, 3, j =>
                    {
                        if (_map.Terrain[j] != (byte)Terrain.Water) return;
                        _map.Resource[j] = (byte)Resource.Fish;
                        _map.Amount[j] = (byte)amount;
                    });
                }
                else if (Near(x, y, 3, Terrain.Mountain))
                {
                    int radius = 1 + _resourceRng.NextInt(2);
                    int amount = 4 + _resourceRng.NextInt(5);
                    Around(x, y, radius, j =>
                    {
                        if (_map.Terrain[j] == (byte)Terrain.Water || _map.Terrain[j] == (byte)Terrain.Mountain) return;
                        _map.Object[j] = (byte)MapObject.Stone;
                        _map.Amount[j] = (byte)amount;
                    });
                }
                else if (_resourceRng.NextInt(3) == 0)
                {
                    _map.Object[i] = (byte)MapObject.Game;
                }
            }
        }

        private void Around(int x, int y, int radius, System.Action<int> action)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (dx * dx + dy * dy > radius * radius || nx < 0 || ny < 0 || nx >= _s || ny >= _s) continue;
                    action(ny * _s + nx);
                }
            }
        }

        private bool Near(int x, int y, int radius, Terrain terrain)
        {
            bool found = false;
            Around(x, y, radius, j => found |= _map.Terrain[j] == (byte)terrain);
            return found;
        }

        // ---- step 6 (discrete part): exact copy from the fundamental domain -----------------------------

        private void Symmetrize()
        {
            if (_spec.Symmetry != MapSymmetry.Mirror && _spec.Symmetry != MapSymmetry.Rotational) return;
            System.Span<int> images = stackalloc int[3];
            for (int y = 0; y < _s; y++)
            {
                for (int x = 0; x < _s; x++)
                {
                    if (!InDomain(_spec.Symmetry, _s, x, y)) continue;
                    int i = y * _s + x;
                    int count = Images(_spec.Symmetry, _s, x, y, images);
                    for (int k = 0; k < count; k++)
                    {
                        int j = images[k];
                        _map.Height[j] = _map.Height[i];
                        _map.Terrain[j] = _map.Terrain[i];
                        _map.Object[j] = _map.Object[i];
                        _map.Resource[j] = _map.Resource[i];
                        _map.Amount[j] = _map.Amount[i];
                    }
                }
            }
        }

        // ---- step 9: neutral zones and monster lairs --------------------------------------------------

        private void PlaceLairs()
        {
            var starts = _map.Starts;
            int count = starts.Length;
            var fields = new int[count][];
            for (int k = 0; k < count; k++)
                fields[k] = DistanceField(_map, new[] { _map.Index(starts[k].X, starts[k].Y) });

            int perArea = _spec.Monsters switch
            {
                MonsterDensity.Low => 1,
                MonsterDensity.High => 3,
                _ => 2,
            };
            int wanted = System.Math.Max(count, perArea * _n / (128 * 128));
            int startRegion = _map.Region[_map.Index(starts[0].X, starts[0].Y)];
            int minDistance = LairMinDistance * 10;
            int target = (LairMinDistance + 10) * 10;
            var nearest = new int[_n];
            var farthest = new int[_n];
            var owner = new int[_n];
            for (int i = 0; i < _n; i++)
            {
                int min = int.MaxValue, max = 0, who = -1;
                for (int k = 0; k < count; k++)
                {
                    int d = fields[k][i];
                    if (d < min) { min = d; who = k; }
                    if (d > max) max = d;
                }
                nearest[i] = min;
                farthest[i] = max;
                owner[i] = who;
            }
            bool Candidate(int i) =>
                nearest[i] != int.MaxValue && nearest[i] >= minDistance && _map.Region[i] == startRegion &&
                _map.Terrain[i] != (byte)Terrain.Mountain && _map.Object[i] != (byte)MapObject.Lair;

            var chosen = new List<int>();
            System.Span<int> images = stackalloc int[3];
            const int MinLairSpacing = 32;
            bool Spaced(int i)
            {
                int x = i % _s, y = i / _s;
                for (int q = 0; q < chosen.Count; q++)
                {
                    int dx = chosen[q] % _s - x, dy = chosen[q] / _s - y;
                    if (dx * dx + dy * dy < MinLairSpacing * MinLairSpacing) return false;
                }
                return true;
            }
            void Choose(int i, System.Span<int> buffer)
            {
                chosen.Add(i);
                int c = Images(_spec.Symmetry, _s, i % _s, i / _s, buffer);
                for (int k = 0; k < c; k++) chosen.Add(buffer[k]);
            }

            // 1) One lair per start at path distance ≈ target, nearer to that start than to any other (F9 balance).
            //    On symmetric maps the images of a lair serve the images of its start.
            var covered = new bool[count];
            for (int k = 0; k < count; k++)
            {
                if (covered[k]) continue;
                int best = -1, bestError = int.MaxValue;
                for (int i = 0; i < _n; i++)
                {
                    if (owner[i] != k || !Candidate(i) || !Spaced(i)) continue;
                    int error = System.Math.Abs(fields[k][i] - target);
                    if (error < bestError) { bestError = error; best = i; }
                }
                covered[k] = true;
                if (best < 0) continue;
                Choose(best, images);
                int c = Images(_spec.Symmetry, _s, starts[k].X, starts[k].Y, images);
                for (int q = 0; q < c; q++)
                    for (int o = 0; o < count; o++)
                        if (_map.Index(starts[o].X, starts[o].Y) == images[q]) covered[o] = true;
            }

            // 2) The rest deep in the neutral zone: far from every start and as equidistant as possible.
            var points = PoissonDisc.Sample(_s, 12, _lairRng);
            var scored = new List<(long Score, int Tile)>();
            for (int p = 0; p < points.Count; p++)
            {
                int i = points[p];
                if (!InDomain(_spec.Symmetry, _s, i % _s, i / _s) || !Candidate(i) || nearest[i] < target + 100) continue;
                scored.Add(((long)nearest[i] * 2 - (farthest[i] - nearest[i]), i));
            }
            scored.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Tile.CompareTo(b.Tile));
            for (int c = 0; c < scored.Count && chosen.Count < wanted; c++)
                if (Spaced(scored[c].Tile)) Choose(scored[c].Tile, images);

            for (int c = 0; c < chosen.Count; c++)
            {
                int i = chosen[c];
                Around(i % _s, i / _s, 1, j =>
                {
                    if (_map.Object[j] != (byte)MapObject.Lair) _map.Object[j] = (byte)MapObject.None;
                });
                _map.Object[i] = (byte)MapObject.Lair;
            }
        }
    }
}

/// <summary>
/// Bridson Poisson-disc sampling on the integer tile grid (Bridson 2007,
/// https://www.cs.ubc.ca/~rbridson/docs/bridson-siggraph07-poissondisk.pdf); squared integer distances only.
/// </summary>
public static class PoissonDisc
{
    private const int Candidates = 24;

    /// <summary>Tile indices (in generation order) with pairwise distance ≥ <paramref name="spacing"/>.</summary>
    public static List<int> Sample(int edge, int spacing, Pcg32 rng)
    {
        int cell = System.Math.Max(1, spacing * 7 / 10); // ≤ spacing/√2 → at most one point per cell
        int cells = (edge + cell - 1) / cell;
        var grid = new int[cells * cells];
        System.Array.Fill(grid, -1);
        var points = new List<int>();
        var active = new List<int>();
        int min2 = spacing * spacing, max2 = 4 * spacing * spacing;

        bool Fits(int x, int y)
        {
            int gx = x / cell, gy = y / cell;
            int reach = spacing / cell + 1;
            for (int cy = System.Math.Max(0, gy - reach); cy <= System.Math.Min(cells - 1, gy + reach); cy++)
            {
                for (int cx = System.Math.Max(0, gx - reach); cx <= System.Math.Min(cells - 1, gx + reach); cx++)
                {
                    int p = grid[cy * cells + cx];
                    if (p < 0) continue;
                    int dx = p % edge - x, dy = p / edge - y;
                    if (dx * dx + dy * dy < min2) return false;
                }
            }
            return true;
        }

        void Add(int x, int y)
        {
            int i = y * edge + x;
            grid[(y / cell) * cells + x / cell] = i;
            points.Add(i);
            active.Add(i);
        }

        int firstX = rng.NextInt(edge);
        int firstY = rng.NextInt(edge);
        Add(firstX, firstY);
        while (active.Count > 0)
        {
            int slot = rng.NextInt(active.Count);
            int origin = active[slot];
            int ox = origin % edge, oy = origin / edge;
            bool placed = false;
            for (int c = 0; c < Candidates && !placed; c++)
            {
                int dx = rng.NextInt(-2 * spacing, 2 * spacing + 1);
                int dy = rng.NextInt(-2 * spacing, 2 * spacing + 1);
                int d2 = dx * dx + dy * dy;
                int x = ox + dx, y = oy + dy;
                if (d2 < min2 || d2 > max2 || x < 0 || y < 0 || x >= edge || y >= edge || !Fits(x, y)) continue;
                Add(x, y);
                placed = true;
            }
            if (!placed)
            {
                active[slot] = active[active.Count - 1];
                active.RemoveAt(active.Count - 1);
            }
        }
        return points;
    }
}
