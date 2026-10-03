using System.Collections.Generic;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.MapGen;

namespace Rebuild.Sim.World;

/// <summary>
/// Production system (docs/06-economy.md §4), run every tick after construction and before logistics matching. Every
/// complete building with a <see cref="ProductionDefinition"/> runs work cycles: a cycle starts when every input pile
/// holds its amount, a tile offering its <see cref="HarvestSource"/> lies within the radius on own territory, and the
/// output piles have room (output piles + units reserved from them by transport jobs + this cycle's unit ≤
/// <see cref="OutputCap"/>); the inputs are taken at the start. A smith (<see cref="ProductionDefinition.HasChoice"/>) also
/// needs an output its owner's quota allows: <see cref="ProductionQuotas.Pick"/> chooses it last, when every other
/// condition holds, and the cycle keeps it in <see cref="Building.Choice"/>. After <see cref="ProductionDefinition.CycleTicks"/> ticks
/// the nearest such tile (by squared distance from the building centre, ties: lower tile index) loses one unit if the
/// source is consumed (objects, fish; <see cref="MapChanges.Take"/>) and one output unit goes into the output pile; if
/// the source was taken meanwhile and none is left, the cycle yields nothing. The unit goes into the pile of the
/// chosen output. A planter (forester,
/// <see cref="ProductionDefinition.Plant"/>) instead needs a free tile in reach (<see cref="FindPlantSite"/>) to start and
/// plants its object on the nearest such tile at the end (<see cref="MapChanges.Plant"/>; nothing if none is left).
/// A cycle only starts while the building's worker is inside (<see cref="Settlers.Working"/>; brought by
/// <see cref="Logistics"/>, docs/06-economy.md §3).
/// Every piled output unit is counted in <see cref="ProductionStatistics"/>.
/// <see cref="Logistics"/> refills the input piles to <see cref="InputTarget"/> and carries output units away.
/// </summary>
public static class Production
{
    /// <summary>Units an input pile is refilled to (docs/06-economy.md §4).</summary>
    public const int InputTarget = 4;
    /// <summary>Output pile capacity; a full building pauses (docs/06-economy.md §4).</summary>
    public const int OutputCap = 8;

    /// <summary>Runs one tick of production.</summary>
    public static void Step(BuildingRegistry buildings, MapData map, Territory territory, MapChanges changes, Logistics logistics,
        Settlers settlers, ProductionQuotas quotas, ProductionStatistics statistics)
    {
        var all = buildings.All;
        int[]? reserved = null;
        bool[]? working = null;
        for (int i = 0; i < all.Count; i++)
        {
            var b = all[i];
            var p = b.Definition.Production;
            if (p == null || b.State != BuildingState.Complete) continue;
            var piles = buildings.PilesAt(i)!;
            if (b.Cycle == 0)
            {
                working ??= settlers.Working(buildings);
                if (!working[i]) continue;
                reserved ??= logistics.ReservedOutput(buildings);
                if (OutputUnits(piles, p) + reserved[i] + 1 > OutputCap) continue;
                bool ready = true;
                for (int k = 0; k < p.Inputs.Count; k++)
                    if (piles[k] < p.InputAmounts[k]) ready = false;
                if (!ready || (p.Harvest != HarvestSource.None && FindHarvest(map, territory, b, p) < 0)
                    || (p.Plant != MapObject.None && FindPlantSite(map, territory, buildings, b, p) < 0)) continue;
                int choice = quotas.Pick(b.Owner, p);
                if (choice < 0) continue;
                for (int k = 0; k < p.Inputs.Count; k++) piles[k] -= p.InputAmounts[k];
                b = b with { Choice = choice };
            }
            int cycle = b.Cycle + 1;
            if (cycle == p.CycleTicks)
            {
                cycle = 0;
                if (p.Plant != MapObject.None)
                {
                    int site = FindPlantSite(map, territory, buildings, b, p);
                    if (site >= 0) changes.Plant(map, site, p.Plant);
                    buildings.Update(i, b with { Cycle = cycle, Choice = 0 });
                    continue;
                }
                int tile = p.Harvest == HarvestSource.None ? int.MaxValue : FindHarvest(map, territory, b, p);
                if (tile >= 0)
                {
                    if (tile != int.MaxValue && Harvest.IsConsumed(p.Harvest)) changes.Take(map, tile, p.Harvest);
                    piles[p.Inputs.Count + b.Choice]++;
                    statistics.Produce(b.Owner, p.Outputs[b.Choice]);
                }
                b = b with { Choice = 0 };
            }
            buildings.Update(i, b with { Cycle = cycle });
        }
    }

    /// <summary>Units in the output piles of a production building's <paramref name="piles"/>.</summary>
    public static int OutputUnits(IReadOnlyList<int> piles, ProductionDefinition p)
    {
        int n = 0;
        for (int k = p.Inputs.Count; k < piles.Count; k++) n += piles[k];
        return n;
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

    /// <summary>
    /// Free tile for the planter's object nearest to the building centre within the radius (squared distance ≤ r², ties:
    /// lower tile index) on the owner's territory, or -1. A free tile is buildable land (plains or fertile, flat enough, no
    /// object) without a resource, is no footprint tile and does not touch one (keeps <see cref="BuildingRegistry.Margin"/>
    /// and doors clear), and has no tree among its 8 neighbours (planted trees never form solid forest; ASSUMPTION).
    /// </summary>
    public static int FindPlantSite(MapData map, Territory territory, BuildingRegistry buildings, in Building b, ProductionDefinition p)
    {
        int r = p.Radius, cx = b.CenterX, cy = b.CenterY, edge = map.Edge;
        int best = -1, bestD2 = int.MaxValue;
        for (int y = System.Math.Max(0, cy - r); y <= System.Math.Min(edge - 1, cy + r); y++)
            for (int x = System.Math.Max(0, cx - r); x <= System.Math.Min(edge - 1, cx + r); x++)
            {
                int d2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                int t = y * edge + x;
                if (d2 > r * r || d2 >= bestD2 || territory.OwnerAt(t) != b.Owner || !IsPlantable(map, buildings, x, y)) continue;
                best = t;
                bestD2 = d2;
            }
        return best;
    }

    private static bool IsPlantable(MapData map, BuildingRegistry buildings, int x, int y)
    {
        int edge = map.Edge, t = y * edge + x;
        if (!map.IsBuildable(t) || map.Resource[t] != (byte)Resource.None) return false;
        for (int ny = System.Math.Max(0, y - 1); ny <= System.Math.Min(edge - 1, y + 1); ny++)
            for (int nx = System.Math.Max(0, x - 1); nx <= System.Math.Min(edge - 1, x + 1); nx++)
            {
                int n = ny * edge + nx;
                if (buildings.AtTile(n) != 0 || map.Object[n] == (byte)MapObject.Tree) return false;
            }
        return true;
    }
}
