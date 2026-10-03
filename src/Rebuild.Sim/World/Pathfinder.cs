using System.Collections.Generic;

namespace Rebuild.Sim.World;

/// <summary>
/// A* on the square tile grid (docs/06-economy.md §6): 8 neighbours, octile integer costs 10/14, no corner
/// cutting (a diagonal step needs both orthogonal neighbours passable), octile heuristic, binary min-heap ordered
/// by <c>(f, h, tile)</c> so ties break identically everywhere. Search work is bounded by a node-expansion limit,
/// never by time. The start tile is exempt from passability (a settler may stand on a tile that just became
/// blocked and walk off it). Scratch arrays are reused between searches; they are not sim state.
/// </summary>
public sealed class Pathfinder
{
    public const int StraightCost = 10;
    public const int DiagonalCost = 14;

    private static readonly int[] Dx = { 1, 0, -1, 0, 1, -1, -1, 1 };
    private static readonly int[] Dy = { 0, 1, 0, -1, 1, 1, -1, -1 };

    private readonly int _edge;
    private readonly int[] _g;
    private readonly int[] _parent;
    /// <summary>Search number that last touched a tile; avoids clearing the arrays per search.</summary>
    private readonly int[] _seen;
    private readonly bool[] _closed;
    private readonly List<long> _heap = new();
    private int _search;

    public Pathfinder(int edge)
    {
        _edge = edge;
        int n = edge * edge;
        _g = new int[n];
        _parent = new int[n];
        _seen = new int[n];
        _closed = new bool[n];
    }

    /// <summary>Nodes expanded by the last <see cref="FindPath"/> call.</summary>
    public int Expansions { get; private set; }

    /// <summary>Octile distance in path-cost units.</summary>
    public int Heuristic(int from, int to)
    {
        int dx = System.Math.Abs(from % _edge - to % _edge), dy = System.Math.Abs(from / _edge - to / _edge);
        int min = System.Math.Min(dx, dy), max = System.Math.Max(dx, dy);
        return StraightCost * max + (DiagonalCost - StraightCost) * min;
    }

    /// <summary>
    /// Finds a cheapest path from <paramref name="start"/> to <paramref name="goal"/> over tiles for which
    /// <paramref name="passable"/> holds and writes it to <paramref name="path"/> (excluding the start, ending with
    /// the goal). Returns its cost, or -1 if the goal is not passable or not reached within
    /// <paramref name="maxExpansions"/> expanded nodes. A path to the start itself is empty with cost 0.
    /// </summary>
    public int FindPath(System.Func<int, bool> passable, int start, int goal, int maxExpansions, List<int> path)
    {
        path.Clear();
        Expansions = 0;
        if (start == goal) return 0;
        if (!passable(goal)) return -1;
        if (++_search == int.MaxValue)
        {
            System.Array.Clear(_seen);
            _search = 1;
        }
        _heap.Clear();
        Visit(start, 0, -1);
        Push(Heuristic(start, goal), Heuristic(start, goal), start);
        while (_heap.Count > 0)
        {
            long top = Pop();
            int tile = (int)(top & TileMask);
            if (_closed[tile]) continue; // stale entry (lazy decrease-key)
            if (tile == goal) return Reconstruct(goal, path);
            if (Expansions == maxExpansions) return -1;
            Expansions++;
            _closed[tile] = true;
            int x = tile % _edge, y = tile / _edge;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Dx[d], ny = y + Dy[d];
                if ((uint)nx >= (uint)_edge || (uint)ny >= (uint)_edge) continue;
                int next = ny * _edge + nx;
                if (_seen[next] == _search && _closed[next]) continue;
                if (!passable(next)) continue;
                if (d >= 4 && (!passable(y * _edge + nx) || !passable(ny * _edge + x))) continue;
                int g = _g[tile] + (d < 4 ? StraightCost : DiagonalCost);
                if (_seen[next] == _search && g >= _g[next]) continue;
                Visit(next, g, tile);
                int h = Heuristic(next, goal);
                Push(g + h, h, next);
            }
        }
        return -1;
    }

    private void Visit(int tile, int g, int parent)
    {
        _seen[tile] = _search;
        _closed[tile] = false;
        _g[tile] = g;
        _parent[tile] = parent;
    }

    private int Reconstruct(int goal, List<int> path)
    {
        for (int t = goal; _parent[t] >= 0; t = _parent[t]) path.Add(t);
        path.Reverse();
        return _g[goal];
    }

    // Heap keys pack (f, h, tile) into one long: f and h < 2^22, tile < 2^20 (maps are at most 512² tiles).
    private const int TileBits = 20;
    private const long TileMask = (1L << TileBits) - 1;
    private const int HBits = 22;

    private void Push(int f, int h, int tile)
    {
        long key = ((long)f << (HBits + TileBits)) | ((long)h << TileBits) | (uint)tile;
        _heap.Add(key);
        int i = _heap.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (_heap[parent] <= key) break;
            _heap[i] = _heap[parent];
            i = parent;
        }
        _heap[i] = key;
    }

    private long Pop()
    {
        long top = _heap[0];
        long last = _heap[_heap.Count - 1];
        _heap.RemoveAt(_heap.Count - 1);
        int n = _heap.Count;
        if (n == 0) return top;
        int i = 0;
        while (true)
        {
            int child = 2 * i + 1;
            if (child >= n) break;
            if (child + 1 < n && _heap[child + 1] < _heap[child]) child++;
            if (_heap[child] >= last) break;
            _heap[i] = _heap[child];
            i = child;
        }
        _heap[i] = last;
        return top;
    }
}
