using Rebuild.Sim.Buildings;
using Rebuild.Sim.Goods;

namespace Rebuild.Sim.World;

/// <summary>
/// Construction system (docs/06-economy.md §4), run once per tick before other systems. Carriers deliver a site's
/// plank/stone cost one unit at a time (<see cref="Logistics"/>); the site is built up <see cref="WorkTicksPerMaterial"/>
/// ticks per delivered unit; when all work is done it becomes complete, military buildings add their territory claim
/// and storage buildings get an empty stock. Until builders exist, the work happens without one (ASSUMPTION).
/// </summary>
public static class Construction
{
    /// <summary>Build ticks per delivered plank/stone unit (2 s, ASSUMPTION).</summary>
    public const int WorkTicksPerMaterial = 20;

    /// <summary>Total build ticks of a building type.</summary>
    public static int TotalWork(BuildingDefinition def) => def.CostTotal * WorkTicksPerMaterial;

    /// <summary>Runs one tick of construction.</summary>
    public static void Step(BuildingRegistry buildings, Territory territory)
    {
        var all = buildings.All;
        for (int i = 0; i < all.Count; i++)
        {
            var b = all[i];
            if (b.State != BuildingState.ConstructionSite) continue;
            var def = b.Definition;
            if (b.WorkDone < (b.DeliveredPlanks + b.DeliveredStone) * WorkTicksPerMaterial)
                b = b with { WorkDone = b.WorkDone + 1 };
            if (b.WorkDone == TotalWork(def))
            {
                int claim = def.TerritoryRadius > 0 ? territory.AddClaim(b.Owner, b.CenterX, b.CenterY, def.TerritoryRadius) : 0;
                b = b with { State = BuildingState.Complete, ClaimId = claim, DeliveredPlanks = 0, DeliveredStone = 0, WorkDone = 0 };
            }
            if (b != all[i]) buildings.Update(i, b);
        }
    }

    /// <summary>
    /// Cancels a construction site: delivered materials go back to the owner's storage building with the lowest id
    /// (lost if there is none; ASSUMPTION until carriers can drop them as offers), then the site is removed.
    /// </summary>
    public static void Cancel(BuildingRegistry buildings, int id)
    {
        int index = buildings.IndexOf(id);
        var b = buildings.All[index];
        int storage = FirstStorage(buildings, b.Owner, good: -1);
        if (storage >= 0)
        {
            var stock = buildings.StockAt(storage)!;
            stock[GoodIds.Plank] += b.DeliveredPlanks;
            stock[GoodIds.Stone] += b.DeliveredStone;
        }
        buildings.Remove(id);
    }

    /// <summary>
    /// Demolishes a complete building: its territory claim is removed (tiles fall to older covering claims) and
    /// the building with its stock disappears; nothing is refunded (ASSUMPTION). Buildings left outside their
    /// owner's territory stay until capture rules exist (M4).
    /// </summary>
    public static void Demolish(BuildingRegistry buildings, Territory territory, int id)
    {
        buildings.TryGet(id, out var b);
        if (b.ClaimId != 0) territory.RemoveClaim(b.ClaimId);
        buildings.Remove(id);
    }

    /// <summary>Whether progress fields and claim fit the building's state and type (save validation).</summary>
    public static bool IsConsistent(in Building b)
    {
        var def = b.Definition;
        if (b.State == BuildingState.Complete)
            return b.DeliveredPlanks == 0 && b.DeliveredStone == 0 && b.WorkDone == 0 && (b.ClaimId != 0) == (def.TerritoryRadius > 0);
        return b.ClaimId == 0 && b.DeliveredPlanks <= def.CostPlanks && b.DeliveredStone <= def.CostStone
            && b.WorkDone <= (b.DeliveredPlanks + b.DeliveredStone) * WorkTicksPerMaterial && b.WorkDone < TotalWork(def);
    }

    /// <summary>List index of the owner's lowest-id stocked building holding <paramref name="good"/> (any stock if good &lt; 0), or -1.</summary>
    private static int FirstStorage(BuildingRegistry buildings, byte owner, int good)
    {
        var all = buildings.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Owner != owner) continue;
            var stock = buildings.StockAt(i);
            if (stock != null && (good < 0 || stock[good] > 0)) return i;
        }
        return -1;
    }
}
