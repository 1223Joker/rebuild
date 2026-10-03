using Rebuild.Sim.Buildings;
using Rebuild.Sim.MapGen;

namespace Rebuild.Sim.World;

/// <summary>
/// Production system (docs/06-economy.md §4), run every tick after construction and before logistics matching. Every
/// complete building with a <see cref="ProductionDefinition"/> runs work cycles: a cycle starts when every input pile
/// holds its amount, a tile offering its <see cref="HarvestSource"/> lies within the radius on own territory, and the
/// output pile has room (output pile + units reserved from it by transport jobs + this cycle's unit ≤
/// <see cref="OutputCap"/>); the inputs are taken at the start. After <see cref="ProductionDefinition.CycleTicks"/> ticks
/// the nearest such tile (by squared distance from the building centre, ties: lower tile index) loses one unit if the
/// source is consumed (objects, fish; <see cref="MapChanges.Take"/>) and one output unit goes into the output pile; if
/// the source was taken meanwhile and none is left, the cycle yields nothing.
/// Workers do not exist yet: a complete building works on its own (ASSUMPTION until specialists, docs/06-economy.md §3).
/// <see cref="Logistics"/> refills the input piles to <see cref="InputTarget"/> and carries output units away.
/// </summary>
public static class Production
{
    /// <summary>Units an input pile is refilled to (docs/06-economy.md §4).</summary>
    public const int InputTarget = 4;
    /// <summary>Output pile capacity; a full building pauses (docs/06-economy.md §4).</summary>
    public const int OutputCap = 8;

    /// <summary>Runs one tick of production.</summary>
    public static void Step(BuildingRegistry buildings, MapData map, Territory territory, MapChanges changes, Logistics logistics)
    {
        var all = buildings.All;
        int[]? reserved = null;
        for (int i = 0; i < all.Count; i++)
        {
            var b = all[i];
            var p = b.Definition.Production;
            if (p == null || b.State != BuildingState.Complete) continue;
            var piles = buildings.PilesAt(i)!;
            int output = piles.Length - 1;
            if (b.Cycle == 0)
            {
                reserved ??= logistics.ReservedOutput(buildings);
                if (piles[output] + reserved[i] + 1 > OutputCap) continue;
                bool ready = true;
                for (int k = 0; k < p.Inputs.Count; k++)
                    if (piles[k] < p.InputAmounts[k]) ready = false;
                if (!ready || (p.Harvest != HarvestSource.None && FindHarvest(map, territory, b, p) < 0)) continue;
                for (int k = 0; k < p.Inputs.Count; k++) piles[k] -= p.InputAmounts[k];
            }
            int cycle = b.Cycle + 1;
            if (cycle == p.CycleTicks)
            {
                cycle = 0;
                int tile = p.Harvest == HarvestSource.None ? int.MaxValue : FindHarvest(map, territory, b, p);
                if (tile >= 0)
                {
                    if (tile != int.MaxValue && Harvest.IsConsumed(p.Harvest)) changes.Take(map, tile, p.Harvest);
                    piles[output]++;
                }
            }
            if (cycle != b.Cycle) buildings.Update(i, b with { Cycle = cycle });
        }
    }

    /// <summary>
    /// Tile offering the building's harvest source nearest to the building centre within the radius (squared distance ≤ r²,
    /// ties: lower tile index) on the owner's territory, or -1.
    /// </summary>
    public static int FindHarvest(MapData map, Territory territory, in Building b, ProductionDefinition p)
    {
        int r = p.Radius, cx = b.CenterX, cy = b.CenterY, edge = map.Edge;
        int best = -1, bestD2 = int.MaxValue;
        for (int y = System.Math.Max(0, cy - r); y <= System.Math.Min(edge - 1, cy + r); y++)
            for (int x = System.Math.Max(0, cx - r); x <= System.Math.Min(edge - 1, cx + r); x++)
            {
                int d2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                int t = y * edge + x;
                if (d2 > r * r || d2 >= bestD2 || territory.OwnerAt(t) != b.Owner || !Harvest.Matches(map, t, p.Harvest)) continue;
                best = t;
                bestD2 = d2;
            }
        return best;
    }
}
