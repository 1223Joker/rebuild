using Rebuild.Sim.Buildings;
using Rebuild.Sim.Goods;
using Rebuild.Sim.MapGen;

namespace Rebuild.Sim.World;

/// <summary>
/// Construction system (docs/06-economy.md §4), run once per tick before other systems. Carriers deliver a site's
/// plank/stone cost one unit at a time (<see cref="Logistics"/>); the site is built up <see cref="WorkTicksPerMaterial"/>
/// ticks per delivered unit; when all work is done it becomes complete, military buildings add their territory claim,
/// storage buildings get an empty stock and production buildings empty piles. Work only advances while the site's builder
/// (a worker with a <see cref="BuilderTool"/>, brought by <see cref="Logistics"/> once delivered material waits to be
/// worked in; it leaves again when that is done and nothing is on the way, so stalled sites do not hold every hammer) is inside, at the worker speed of its home
/// (Short −25 %, Crisis stops, as <see cref="Production"/>); on completion the builder comes out as a carrier and takes the
/// hammer to storage. A land site on uneven ground is levelled first: a digger (a worker with a <see cref="DiggerTool"/>,
/// requested at once) works <see cref="Building.DigLeft"/> ticks inside, then the footprint drops or rises to
/// <see cref="LevelOf"/> in one go (<see cref="MapChanges.Level"/>) and the digger takes the shovel back; material is
/// delivered meanwhile, the builder comes after.
/// </summary>
public static class Construction
{
    /// <summary>Build ticks per delivered plank/stone unit (2 s, ASSUMPTION).</summary>
    public const int WorkTicksPerMaterial = 20;
    /// <summary>Tool a site's builder takes.</summary>
    public const ushort BuilderTool = GoodIds.Hammer;

    /// <summary>Tool a site's digger takes.</summary>
    public const ushort DiggerTool = GoodIds.Shovel;
    /// <summary>Dig ticks per height level a footprint tile moves (1 s, ASSUMPTION).</summary>
    public const int DigTicksPerLevel = 10;

    /// <summary>Height a footprint is levelled to: the midpoint of its lowest and highest tile, rounded down.</summary>
    public static int LevelOf(MapData map, int x, int y, int side)
    {
        int min = 255, max = 0;
        for (int ty = y; ty < y + side; ty++)
            for (int tx = x; tx < x + side; tx++)
            {
                int h = map.Height[map.Index(tx, ty)];
                if (h < min) min = h;
                if (h > max) max = h;
            }
        return (min + max) / 2;
    }

    /// <summary>Digger ticks a site of <paramref name="type"/> at (x, y) needs: <see cref="DigTicksPerLevel"/> per level each footprint tile moves; 0 for mines.</summary>
    public static int DigWork(MapData map, int type, int x, int y)
    {
        var def = BuildingCatalog.All[type];
        if (def.Terrain != BuildingTerrain.Land) return 0;
        int level = LevelOf(map, x, y, def.Side), work = 0;
        for (int ty = y; ty < y + def.Side; ty++)
            for (int tx = x; tx < x + def.Side; tx++)
                work += System.Math.Abs(map.Height[map.Index(tx, ty)] - level) * DigTicksPerLevel;
        return work;
    }

    /// <summary>Total build ticks of a building type.</summary>
    public static int TotalWork(BuildingDefinition def) => def.CostTotal * WorkTicksPerMaterial;

    /// <summary>Runs one tick of construction.</summary>
    public static void Step(int tick, BuildingRegistry buildings, MapData map, MapChanges mapChanges, Territory territory, Settlers settlers, Logistics logistics)
    {
        var all = buildings.All;
        bool[]? working = null;
        NeedState[]? states = null;
        for (int i = 0; i < all.Count; i++)
        {
            var b = all[i];
            if (b.State != BuildingState.ConstructionSite) continue;
            var def = b.Definition;
            working ??= settlers.Working(buildings);
            states ??= settlers.WorkerHomeStates(buildings);
            if (!working[i] || states[i] == NeedState.Crisis || (states[i] == NeedState.Short && tick % Production.ShortSkipEvery == 0)) continue;
            if (b.DigLeft > 0)
            {
                if (b.DigLeft == 1)
                {
                    logistics.ReleaseWorker(buildings, settlers, b.Id); // still a dig site: the digger takes its shovel back
                    mapChanges.Level(map, b.X, b.Y, def.Side);
                }
                buildings.Update(i, b with { DigLeft = b.DigLeft - 1 });
                continue;
            }
            if (b.WorkDone < (b.DeliveredPlanks + b.DeliveredStone) * WorkTicksPerMaterial)
                b = b with { WorkDone = b.WorkDone + 1 };
            else if (!logistics.HasDeliveryTo(b.Id))
            {
                logistics.ReleaseWorker(buildings, settlers, b.Id); // stalled: the hammer goes back for other sites
                continue;
            }
            if (b.WorkDone == TotalWork(def))
            {
                logistics.ReleaseWorker(buildings, settlers, b.Id); // the builder takes its hammer back while this is a site
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

    /// <summary>Whether progress fields, work cycle (and its output choice, 0 when idle) and claim fit the building's state and type (save validation).</summary>
    public static bool IsConsistent(in Building b)
    {
        var def = b.Definition;
        if (b.State == BuildingState.Complete)
            return b.DigLeft == 0 && b.DeliveredPlanks == 0 && b.DeliveredStone == 0 && b.WorkDone == 0 && (b.ClaimId != 0) == (def.TerritoryRadius > 0)
                && b.Cycle >= 0 && b.Cycle < (def.Production?.MaxCycleTicks ?? 1)
                && b.Choice >= 0 && b.Choice < (b.Cycle > 0 ? def.Production!.Outputs.Count : 1);
        return b.ClaimId == 0 && b.Cycle == 0 && b.Choice == 0 && (b.DigLeft == 0 || b.WorkDone == 0) && b.DeliveredPlanks <= def.CostPlanks && b.DeliveredStone <= def.CostStone
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
