using Rebuild.Sim.Buildings;
using Rebuild.Sim.MapGen;

namespace Rebuild.Sim.World;

public enum PlacementResult : byte
{
    Ok,
    UnknownType,
    /// <summary>Type exists but only the sim may create it (start castle).</summary>
    NotPlaceable,
    /// <summary>Footprint leaves the map.</summary>
    OutOfMap,
    /// <summary>A footprint tile is not owned by the placing player.</summary>
    NotOwnTerritory,
    /// <summary>A footprint tile does not suit the building (<see cref="BuildingTerrain"/>).</summary>
    WrongTerrain,
    /// <summary>The footprint or its margin touches another building.</summary>
    Occupied,
}

/// <summary>
/// Placement rules for <c>PlaceBuilding</c> (docs/06-economy.md §1, §5): every footprint tile lies in the
/// player's own territory and suits the building's terrain, and the footprint keeps
/// <see cref="BuildingRegistry.Margin"/> to other buildings. Shared by command validation, UI and AI.
/// </summary>
public static class BuildingPlacement
{
    public static PlacementResult Check(MapData map, Territory territory, BuildingRegistry buildings, byte slot, int type, int x, int y)
    {
        if (type < 0 || type >= BuildingCatalog.All.Count) return PlacementResult.UnknownType;
        var def = BuildingCatalog.All[type];
        if (!def.PlayerPlaceable) return PlacementResult.NotPlaceable;
        int side = def.Side;
        if (x < 0 || y < 0 || x > map.Edge - side || y > map.Edge - side) return PlacementResult.OutOfMap;
        for (int ty = y; ty < y + side; ty++)
            for (int tx = x; tx < x + side; tx++)
                if (territory.OwnerAt(tx, ty) != slot) return PlacementResult.NotOwnTerritory;
        for (int ty = y; ty < y + side; ty++)
            for (int tx = x; tx < x + side; tx++)
                if (!Suits(map, map.Index(tx, ty), def.Terrain)) return PlacementResult.WrongTerrain;
        return buildings.IsFootprintFree(x, y, side) ? PlacementResult.Ok : PlacementResult.Occupied;
    }

    /// <summary>Whether a single tile suits a building terrain class.</summary>
    public static bool Suits(MapData map, int tile, BuildingTerrain terrain) => terrain switch
    {
        BuildingTerrain.Land => map.IsBuildable(tile),
        _ => map.Terrain[tile] == (byte)Terrain.Mountain && map.IsWalkable(tile) && map.Object[tile] == (byte)MapObject.None,
    };
}
