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
    /// <summary>Levelling the footprint would leave a slope too steep to walk next to it.</summary>
    TooSteep,
}

/// <summary>
/// Placement rules for <c>PlaceBuilding</c> (docs/06-economy.md §1, §5): every footprint tile lies in the
/// player's own territory and suits the building's terrain, and the footprint keeps
/// <see cref="BuildingRegistry.Margin"/> to other buildings. Land may be gently sloped (walkable): a digger levels it to
/// <see cref="Construction.LevelOf"/> first, which must stay within the walking slope (2) of every land tile around it.
/// Shared by command validation, UI and AI.
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
        if (!buildings.IsFootprintFree(x, y, side)) return PlacementResult.Occupied;
        return def.Terrain != BuildingTerrain.Land || RingWalkable(map, x, y, side) ? PlacementResult.Ok : PlacementResult.TooSteep;
    }

    /// <summary>Whether a single tile suits a building terrain class (land: walkable plains or fertile land without an object).</summary>
    public static bool Suits(MapData map, int tile, BuildingTerrain terrain) => terrain switch
    {
        BuildingTerrain.Land => map.Terrain[tile] is (byte)Terrain.Plains or (byte)Terrain.Fertile && map.IsWalkable(tile)
            && map.Object[tile] == (byte)MapObject.None,
        _ => map.Terrain[tile] == (byte)Terrain.Mountain && map.IsWalkable(tile) && map.Object[tile] == (byte)MapObject.None,
    };

    /// <summary>Whether every land tile around the footprint stays within the walking slope of the levelled footprint.</summary>
    private static bool RingWalkable(MapData map, int x, int y, int side)
    {
        int level = Construction.LevelOf(map, x, y, side);
        for (int ty = y - 1; ty <= y + side; ty++)
            for (int tx = x - 1; tx <= x + side; tx++)
            {
                bool inside = tx >= x && tx < x + side && ty >= y && ty < y + side;
                if (inside || tx < 0 || ty < 0 || tx >= map.Edge || ty >= map.Edge) continue;
                int t = map.Index(tx, ty);
                if (map.Terrain[t] != (byte)Terrain.Water && System.Math.Abs(map.Height[t] - level) > 2) return false;
            }
        return true;
    }
}
