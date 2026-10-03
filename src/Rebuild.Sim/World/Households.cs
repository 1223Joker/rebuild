using System.Collections.Generic;
using Rebuild.Sim.Goods;

namespace Rebuild.Sim.World;

public enum NeedState : byte
{
    /// <summary>The last due unit was paid.</summary>
    Supplied = 0,
    /// <summary>A due unit has been unpaid for <see cref="Households.ShortTicks"/>: the home spawns no new carriers.</summary>
    Short = 1,
    /// <summary>Short for <see cref="Households.CrisisTicks"/> more: one occupant leaves the map every <see cref="Households.LeaveIntervalTicks"/>.</summary>
    Crisis = 2,
}

/// <summary>
/// Food and water of every home (docs/12-needs-seasons-weather.md §1.2–1.3), run every tick after production and before
/// logistics. A home is a complete building with beds (<see cref="Buildings.BuildingDefinition.Beds"/>); its occupants
/// are the settlers it homes, carriers and workers (§1.1). A settler whose home is gone is homeless: every tick it takes a
/// free bed in the owner's home nearest to it (<see cref="Logistics.SectorDistance"/>, ties lower id) if there is one;
/// after <see cref="HomelessTicks"/> without a bed a worker leaves its workplace as a carrier bringing its tool back, and a
/// carrier without a job leaves the map (one with a job finishes it first). Homeless settlers eat nothing. Needs are counted per
/// home, not per settler: every tick each need's due counter grows by the occupant count, and once it reaches the need's
/// period (<see cref="FoodTicks"/>, <see cref="WaterTicks"/> per settler) one unit is eaten — from the home's own stock if it
/// is a storage (the castle: the food good it holds most of, ties data order), else from its pantry pile, which
/// <see cref="Logistics"/> refills to <see cref="PantryTarget"/>. An unpaid unit stays due (no backlog: the counter stops at
/// the period) and its unpaid ticks count up until a unit is eaten; they set the <see cref="NeedState"/>, the worse need wins.
/// Units eaten from a stock count as consumed (<see cref="ProductionStatistics"/>); pantry units counted at hand-over.
/// </summary>
public static class Households
{
    /// <summary>Ticks per food unit per settler (1 per 10 min, ASSUMPTION).</summary>
    public const int FoodTicks = 6000;
    /// <summary>Ticks per water unit per settler (1 per 8 min, ASSUMPTION).</summary>
    public const int WaterTicks = 4800;
    /// <summary>Units a pantry pile is refilled to (ponytail: fixed; docs say max(2, occupants / 4), equal for 10 beds).</summary>
    public const int PantryTarget = 2;
    /// <summary>Unpaid ticks until Short, per need (food 60 s, water 30 s).</summary>
    public static readonly int[] ShortTicks = { 600, 300 };
    /// <summary>Further unpaid ticks until Crisis, per need (food 180 s, water 120 s).</summary>
    public static readonly int[] CrisisTicks = { 1800, 1200 };
    /// <summary>Ticks between two occupants leaving a home in Crisis (60 s).</summary>
    public const int LeaveIntervalTicks = 600;
    /// <summary>Ticks a homeless settler looks for a free bed before it leaves (120 s).</summary>
    public const int HomelessTicks = 1200;

    /// <summary>Food goods (ponytail: fixed list; culture data when a second culture needs other food).</summary>
    public static readonly ushort[] FoodGoods = { (ushort)GoodIds.Fish, (ushort)GoodIds.Meat, (ushort)GoodIds.Bread };
    public static readonly ushort[] WaterGoods = { (ushort)GoodIds.Water };
    /// <summary>Goods per need: 0 food, 1 water (pantry pile index).</summary>
    public static readonly ushort[][] NeedGoods = { FoodGoods, WaterGoods };
    private static readonly int[] Periods = { FoodTicks, WaterTicks };

    /// <summary>Need counters of a home: due ticks for food and water, then unpaid ticks for food and water.</summary>
    public const int CounterCount = 4;

    public static int Period(int need) => Periods[need];

    /// <summary>State of one need from its unpaid ticks.</summary>
    public static NeedState StateOf(int need, int unpaid) =>
        unpaid >= ShortTicks[need] + CrisisTicks[need] ? NeedState.Crisis : unpaid >= ShortTicks[need] ? NeedState.Short : NeedState.Supplied;

    /// <summary>Worst need state of the building at list index <paramref name="index"/> (Supplied for non-homes).</summary>
    public static NeedState StateAt(BuildingRegistry buildings, int index)
    {
        var n = buildings.NeedsAt(index);
        if (n == null) return NeedState.Supplied;
        var food = StateOf(0, n[2]);
        var water = StateOf(1, n[3]);
        return food > water ? food : water;
    }

    /// <summary>Runs one tick of re-housing, eating and drinking.</summary>
    public static void Step(int edge, BuildingRegistry buildings, Settlers settlers, Logistics logistics, ProductionStatistics statistics)
    {
        var all = buildings.All;
        var occupants = settlers.Occupants(buildings);
        for (int i = settlers.All.Count - 1; i >= 0; i--)
        {
            var s = settlers.All[i];
            int home = buildings.IndexOf(s.HomeId);
            if (home >= 0 && BuildingRegistry.IsHome(all[home])) continue;
            int bed = FreeBed(edge, buildings, occupants, s);
            if (bed >= 0)
            {
                occupants[bed]++;
                settlers.Replace(i, s with { HomeId = all[bed].Id, HomelessTicks = 0 });
                continue;
            }
            if (s.HomelessTicks < HomelessTicks)
                settlers.Replace(i, s with { HomelessTicks = s.HomelessTicks + 1 });
            else if (s.Kind == SettlerKind.Worker)
                logistics.ReleaseWorkerAt(buildings, settlers, i);
            else if (s.JobId == 0)
                settlers.RemoveAt(i);
        }
        for (int i = 0; i < all.Count; i++)
        {
            var n = buildings.NeedsAt(i);
            if (n == null) continue;
            bool leave = false;
            for (int need = 0; need < 2; need++)
            {
                n[need] += occupants[i];
                if (n[need] < Periods[need]) continue;
                if (TryEat(buildings, i, need, statistics))
                {
                    n[need] -= Periods[need];
                    n[2 + need] = 0;
                    continue;
                }
                n[need] = Periods[need];
                int crisis = ++n[2 + need] - ShortTicks[need] - CrisisTicks[need];
                if (crisis >= 0 && crisis % LeaveIntervalTicks == 0) leave = true;
            }
            if (leave) Leave(buildings, settlers, logistics, all[i].Id);
        }
    }

    /// <summary>List index of the owner's home with a free bed nearest to the settler (ties lower id), or -1.</summary>
    private static int FreeBed(int edge, BuildingRegistry buildings, int[] occupants, in Settler s)
    {
        int best = -1, bestDistance = int.MaxValue;
        for (int i = 0; i < occupants.Length; i++)
        {
            var b = buildings.All[i];
            if (b.Owner != s.Owner || !BuildingRegistry.IsHome(b) || occupants[i] >= b.Definition.Beds) continue;
            int d = Logistics.SectorDistance(edge, s.Tile, b.CenterY * edge + b.CenterX);
            if (d < bestDistance) (best, bestDistance) = (i, d);
        }
        return best;
    }

    /// <summary>Eats one unit of the need from the home's stock or pantry pile; false if there is none.</summary>
    private static bool TryEat(BuildingRegistry buildings, int index, int need, ProductionStatistics statistics)
    {
        var stock = buildings.StockAt(index);
        if (stock == null)
        {
            var piles = buildings.PilesAt(index)!;
            if (piles[need] == 0) return false;
            piles[need]--;
            return true;
        }
        int best = -1;
        foreach (ushort g in NeedGoods[need])
            if (stock[g] > 0 && (best < 0 || stock[g] > stock[best])) best = g;
        if (best < 0) return false;
        stock[best]--;
        statistics.Consume(buildings.All[index].Owner, (ushort)best);
        return true;
    }

    /// <summary>
    /// The highest-id carrier of the home without a transport job leaves the map; with none, the highest-id worker of the
    /// home leaves its workplace as a carrier bringing its tool back (it can leave the map next time).
    /// </summary>
    private static void Leave(BuildingRegistry buildings, Settlers settlers, Logistics logistics, int homeId)
    {
        int worker = -1;
        for (int i = settlers.All.Count - 1; i >= 0; i--)
        {
            var s = settlers.All[i];
            if (s.HomeId != homeId) continue;
            if (s.Kind == SettlerKind.Worker)
            {
                if (worker < 0) worker = i;
            }
            else if (s.JobId == 0)
            {
                settlers.RemoveAt(i);
                return;
            }
        }
        if (worker >= 0) logistics.ReleaseWorkerAt(buildings, settlers, worker);
    }
}
