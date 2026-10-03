using System.Collections.Generic;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Goods;

namespace Rebuild.Sim.World;

public enum NeedState : byte
{
    /// <summary>The last due unit was paid.</summary>
    Supplied = 0,
    /// <summary>
    /// A due unit has been unpaid for <see cref="Households.ShortTicks"/>: the home spawns no new carriers, its settlers walk
    /// −15 % (<see cref="Settlers.ShortSpeed"/>) and its workers work −25 % (<see cref="Production"/>).
    /// </summary>
    Short = 1,
    /// <summary>
    /// Short for <see cref="Households.CrisisTicks"/> more: as Short, but its workers stop working, and one occupant leaves the map
    /// every <see cref="Households.LeaveIntervalTicks"/>.
    /// </summary>
    Crisis = 2,
}

/// <summary>
/// Food, water and winter heat of every home (docs/12-needs-seasons-weather.md §1.2–1.3, §2.2), run every tick after production and before
/// logistics. A home is a complete building with beds (<see cref="Buildings.BuildingDefinition.Beds"/>); its occupants
/// are the settlers it homes, carriers and workers (§1.1). A settler whose home is gone is homeless: every tick it takes a
/// free bed in the owner's home nearest to it (<see cref="Logistics.SectorDistance"/>, ties lower id) if there is one;
/// after <see cref="HomelessTicks"/> without a bed a worker leaves its workplace as a carrier bringing its tool back, and a
/// carrier without a job leaves the map (one with a job finishes it first). Homeless settlers eat nothing. Needs are counted per
/// home, not per settler: every tick each need's due counter grows by the occupant count, and once it reaches the need's
/// period (<see cref="FoodTicks"/>, <see cref="WaterTicks"/> per settler) one unit is eaten — from the home's own stock if it
/// is a storage (the castle: the food good it holds most of, ties data order), else from its pantry pile, which
/// <see cref="Logistics"/> refills to <see cref="PantryTarget"/> (a storage home's stock: to <see cref="StockTarget"/>, from other storages). An unpaid unit stays due (no backlog: the counter stops at
/// the period) and its unpaid ticks count up until a unit is eaten; they set the <see cref="NeedState"/>, the worse need wins.
/// Units eaten from a stock count as consumed (<see cref="ProductionStatistics"/>); pantry units counted at hand-over.
/// Heat is a third need: only in winter, and per occupied home rather than per settler, its due counter grows by one per
/// tick and one fuel unit (log or coal) burns per <see cref="Period"/>; outside winter its counters are cleared. Logistics
/// fills fuel only in autumn and winter (§2.2 stockpiling).
/// A heated workplace (<see cref="IsHeatedWorkplace"/>) has the same counters but only the heat need: in winter its heat is
/// due while its worker is inside (an empty one's counters stay as they are), paid from its fuel pile (<see cref="FuelPile"/>); its state slows or stops its
/// <see cref="Production"/> like the worker's home state (the worse counts), but nobody leaves over a cold workplace.
/// </summary>
public static class Households
{
    /// <summary>Ticks per food unit per settler (1 per 10 min, ASSUMPTION).</summary>
    public const int FoodTicks = 6000;
    /// <summary>Ticks per water unit per settler (1 per 8 min, ASSUMPTION).</summary>
    public const int WaterTicks = 4800;
    /// <summary>Units a pantry pile is refilled to (ponytail: fixed; docs say max(2, occupants / 4), equal for 10 beds).</summary>
    public const int PantryTarget = 2;
    /// <summary>
    /// Units of each need logistics keeps in a storage home's stock, fetched from other storages: max(<see cref="PantryTarget"/>,
    /// beds / 4) (docs §1.3 with beds for occupants; castle 7).
    /// </summary>
    public static int StockTarget(in Building b) => System.Math.Max(PantryTarget, b.Definition.Beds / 4);
    /// <summary>Unpaid ticks until Short, per need (food 60 s, water 30 s, heat 60 s).</summary>
    public static readonly int[] ShortTicks = { 600, 300, 600 };
    /// <summary>Further unpaid ticks until Crisis, per need (food 180 s, water 120 s, heat 180 s).</summary>
    public static readonly int[] CrisisTicks = { 1800, 1200, 1800 };
    /// <summary>Ticks between two occupants leaving a home in Crisis (60 s).</summary>
    public const int LeaveIntervalTicks = 600;
    /// <summary>Ticks a homeless settler looks for a free bed before it leaves (120 s).</summary>
    public const int HomelessTicks = 1200;

    /// <summary>Food goods (ponytail: fixed list; culture data when a second culture needs other food).</summary>
    public static readonly ushort[] FoodGoods = { (ushort)GoodIds.Fish, (ushort)GoodIds.Meat, (ushort)GoodIds.Bread };
    public static readonly ushort[] WaterGoods = { (ushort)GoodIds.Water };
    /// <summary>
    /// Fuel goods (§2.2). ponytail: every unit pays one heat point (docs: coal 2) — add heat values with the culture fuel
    /// data (M8–M10), which needs a pantry pile that remembers its goods.
    /// </summary>
    public static readonly ushort[] FuelGoods = { (ushort)GoodIds.Log, (ushort)GoodIds.Coal };
    /// <summary>Goods per need: 0 food, 1 water, 2 heat (pantry pile index).</summary>
    public static readonly ushort[][] NeedGoods = { FoodGoods, WaterGoods, FuelGoods };
    /// <summary>Needs per home: food, water, heat.</summary>
    public const int NeedCount = 3;
    /// <summary>Index of the heat need.</summary>
    public const int Heat = 2;

    /// <summary>
    /// Self-heating workplaces (§2.2: smiths and smelters; the sawmill burns its offcuts, ASSUMPTION — it also keeps log, a
    /// fuel good, out of every heated workplace's inputs, so a fuel unit always has one pile). ponytail: fixed list, a
    /// <c>"heat"</c> data field once a culture differs.
    /// </summary>
    public static readonly ushort[] SelfHeating =
        { BuildingIds.Sawmill, BuildingIds.IronSmelter, BuildingIds.GoldSmelter, BuildingIds.Toolsmith, BuildingIds.Weaponsmith };

    /// <summary>
    /// Whether the building is a complete production building that needs heating in winter: not self-heating and working in
    /// winter (the farm rests and burns nothing).
    /// </summary>
    public static bool IsHeatedWorkplace(in Building b) =>
        b.State == BuildingState.Complete && b.Definition.Production is { } p && p.WorksIn(Season.Winter)
        && System.Array.IndexOf(SelfHeating, b.Type) < 0;

    /// <summary>Index of a heated workplace's fuel pile: after its input and output piles.</summary>
    public static int FuelPile(ProductionDefinition p) => p.Inputs.Count + p.Outputs.Count;

    /// <summary>Need counters of a home: due ticks per need, then unpaid ticks per need.</summary>
    public const int CounterCount = 2 * NeedCount;

    /// <summary>
    /// Due ticks per unit of the need at home <paramref name="b"/>: food and water per settler, heat per building by size
    /// (S 1 fuel per 2 min, M per 90 s, L per 60 s; §2.2, ponytail: from the size instead of a data field until a culture
    /// differs).
    /// </summary>
    public static int Period(in Building b, int need) => need switch
    {
        0 => FoodTicks,
        1 => WaterTicks,
        _ => b.Definition.Size switch { BuildingSize.Small => 1200, BuildingSize.Medium => 900, _ => 600 },
    };

    /// <summary>Need of a good a home eats (0 food, 1 water, 2 heat), or -1.</summary>
    public static int NeedOf(int good)
    {
        for (int need = 0; need < NeedCount; need++)
            if (System.Array.IndexOf(NeedGoods[need], (ushort)good) >= 0) return need;
        return -1;
    }

    /// <summary>State of one need from its unpaid ticks.</summary>
    public static NeedState StateOf(int need, int unpaid) =>
        unpaid >= ShortTicks[need] + CrisisTicks[need] ? NeedState.Crisis : unpaid >= ShortTicks[need] ? NeedState.Short : NeedState.Supplied;

    /// <summary>Worst need state of the building at list index <paramref name="index"/> (Supplied for non-homes).</summary>
    public static NeedState StateAt(BuildingRegistry buildings, int index)
    {
        var n = buildings.NeedsAt(index);
        var worst = NeedState.Supplied;
        if (n == null) return worst;
        for (int need = 0; need < NeedCount; need++)
        {
            var state = StateOf(need, n[NeedCount + need]);
            if (state > worst) worst = state;
        }
        return worst;
    }

    /// <summary>Worst need state of home <paramref name="homeId"/> (Supplied if it is gone: homeless settlers eat nothing).</summary>
    public static NeedState HomeState(BuildingRegistry buildings, int homeId)
    {
        int index = buildings.IndexOf(homeId);
        return index < 0 ? NeedState.Supplied : StateAt(buildings, index);
    }

    /// <summary>Runs one tick of re-housing, eating, drinking and (in winter) heating.</summary>
    public static void Step(int edge, BuildingRegistry buildings, Settlers settlers, Logistics logistics, ProductionStatistics statistics,
        Season season)
    {
        var all = buildings.All;
        var occupants = settlers.Occupants(buildings);
        bool[]? working = null;
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
            bool leave = false, workplace = !BuildingRegistry.IsHome(all[i]);
            // An empty workplace burns nothing and its cold does not grow.
            if (workplace && !(working ??= settlers.Working(buildings))[i]) continue;
            for (int need = workplace ? Heat : 0; need < NeedCount; need++)
            {
                if (need == Heat && season != Season.Winter)
                {
                    // Outside winter nothing burns and the cold is over.
                    n[Heat] = n[NeedCount + Heat] = 0;
                    continue;
                }
                n[need] += need == Heat ? (workplace || occupants[i] > 0 ? 1 : 0) : occupants[i];
                int period = Period(all[i], need);
                if (n[need] < period) continue;
                if (TryEat(buildings, i, need, statistics))
                {
                    n[need] -= period;
                    n[NeedCount + need] = 0;
                    continue;
                }
                n[need] = period;
                int crisis = ++n[NeedCount + need] - ShortTicks[need] - CrisisTicks[need];
                if (crisis >= 0 && crisis % LeaveIntervalTicks == 0) leave = !workplace;
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

    /// <summary>Eats one unit of the need from the home's stock, pantry pile or (a workplace) fuel pile; false if there is none.</summary>
    private static bool TryEat(BuildingRegistry buildings, int index, int need, ProductionStatistics statistics)
    {
        var stock = buildings.StockAt(index);
        if (stock == null)
        {
            var piles = buildings.PilesAt(index)!;
            int pile = buildings.All[index].Definition.Production is { } p ? FuelPile(p) : need;
            if (piles[pile] == 0) return false;
            piles[pile]--;
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
