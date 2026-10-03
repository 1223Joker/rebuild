using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.World;

/// <summary>
/// Changes systems made to the map's height, object and resource layers since generation (felled trees, quarried stone, hunted
/// game, caught fish, mined ore, planted trees, levelled building sites; docs/06-economy.md §4). The generated map is regenerated on load, so the changed tiles are part of
/// the sim state: tile index, height, object, resource and amount in ascending tile order. A tile's flags are refreshed
/// when its object goes or comes or its height (or a neighbour's) changes.
/// </summary>
public sealed class MapChanges
{
    /// <summary>Changed tiles in ascending order.</summary>
    private readonly List<int> _tiles = new();

    public IReadOnlyList<int> Tiles => _tiles;

    /// <summary>
    /// Takes one unit of a consumed <paramref name="source"/> (<see cref="Harvest.IsConsumed"/>) from <paramref name="tile"/>,
    /// which must offer it: an object (a stone outcrop loses one unit of <see cref="MapData.Amount"/> and disappears with
    /// its last one, an amount of 0 counting as 1; a tree or game animal disappears) or a resource — fish or an ore
    /// deposit — (one unit of <see cref="MapData.Amount"/>; the resource goes with the last one).
    /// </summary>
    public void Take(MapData map, int tile, HarvestSource source)
    {
        if (!Harvest.IsConsumed(source) || !Harvest.Matches(map, tile, source))
            throw new System.ArgumentException("Tile does not offer a consumed source", nameof(source));
        if (!Harvest.IsResource(source))
        {
            if (map.Object[tile] == (byte)MapObject.Stone && map.Amount[tile] > 1)
            {
                map.Amount[tile]--;
            }
            else
            {
                map.Object[tile] = (byte)MapObject.None;
                map.Amount[tile] = 0;
                map.Flags[tile] = MapGenerator.TileFlagsAt(map, tile % map.Edge, tile / map.Edge);
            }
        }
        else
        {
            if (map.Amount[tile] > 1)
            {
                map.Amount[tile]--;
            }
            else
            {
                map.Resource[tile] = (byte)Resource.None;
                map.Amount[tile] = 0;
            }
        }
        Record(tile);
    }

    /// <summary>
    /// Plants <paramref name="obj"/> (only <see cref="MapObject.Tree"/>, amount 0) on <paramref name="tile"/>, which must be
    /// buildable land without a resource (callers pick it with <see cref="Production.FindPlantSite"/>).
    /// </summary>
    public void Plant(MapData map, int tile, MapObject obj)
    {
        if (obj != MapObject.Tree || !map.IsBuildable(tile) || map.Resource[tile] != (byte)Resource.None)
            throw new System.ArgumentException("Only a tree on free land can be planted", nameof(obj));
        map.Object[tile] = (byte)obj;
        map.Amount[tile] = 0;
        map.Flags[tile] = MapGenerator.TileFlagsAt(map, tile % map.Edge, tile / map.Edge);
        Record(tile);
    }

    /// <summary>
    /// Levels the footprint at (x, y) to <see cref="Construction.LevelOf"/> (a digger's work; callers checked the placement)
    /// and refreshes the flags of the footprint and the tiles around it.
    /// </summary>
    public void Level(MapData map, int x, int y, int side)
    {
        byte level = (byte)Construction.LevelOf(map, x, y, side);
        for (int ty = y; ty < y + side; ty++)
            for (int tx = x; tx < x + side; tx++)
            {
                int t = map.Index(tx, ty);
                if (map.Height[t] == level) continue;
                map.Height[t] = level;
                Record(t);
            }
        for (int ty = System.Math.Max(0, y - 1); ty <= System.Math.Min(map.Edge - 1, y + side); ty++)
            for (int tx = System.Math.Max(0, x - 1); tx <= System.Math.Min(map.Edge - 1, x + side); tx++)
                map.Flags[map.Index(tx, ty)] = MapGenerator.TileFlagsAt(map, tx, ty);
    }

    private void Record(int tile)
    {
        int at = _tiles.BinarySearch(tile);
        if (at < 0) _tiles.Insert(~at, tile);
    }

    /// <summary>Canonical state: every changed tile with its current height, object, resource and amount.</summary>
    public void WriteTo(CanonicalWriter w, MapData map)
    {
        w.WriteInt32(_tiles.Count);
        foreach (int t in _tiles)
        {
            w.WriteInt32(t);
            w.WriteByte(map.Height[t]);
            w.WriteByte(map.Object[t]);
            w.WriteByte(map.Resource[t]);
            w.WriteByte(map.Amount[t]);
        }
    }

    /// <summary>
    /// Reads the changes and applies them to the freshly generated <paramref name="map"/>. Tiles must be in ascending order
    /// and each must show exactly what <see cref="Take"/> can leave: either the generated tree, stone or game object is
    /// gone (amount 0) or a stone outcrop holds fewer units, with the resource unchanged; or, on a tile without an object,
    /// the generated fish resource or ore deposit (coal, iron, gold) is gone (amount 0) or holds fewer units; or a tree
    /// or nothing (amount 0) is on a tile without resource that is buildable once cleared and whose generated object was
    /// none, a tree, stone or game (<see cref="Plant"/> after felling, quarrying or hunting, and the planted tree possibly felled again);
    /// or nothing changed but the height of a plains or fertile tile (<see cref="Level"/>). Heights apply before the other checks.
    /// </summary>
    public static MapChanges ReadFrom(CanonicalReader r, MapData map)
    {
        var changes = new MapChanges();
        int count = r.ReadInt32();
        if (count < 0 || count > map.TileCount) throw new InvalidDataException("Invalid map change count");
        var records = new (int Tile, byte Height, byte Obj, byte Res, byte Amount)[count];
        int last = -1;
        for (int i = 0; i < count; i++)
        {
            var c = records[i] = (r.ReadInt32(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
            if (c.Tile <= last || c.Tile >= map.TileCount) throw new InvalidDataException("Invalid map change tile");
            if (c.Height != map.Height[c.Tile] && map.Terrain[c.Tile] is not ((byte)Terrain.Plains or (byte)Terrain.Fertile))
                throw new InvalidDataException("Invalid map change height");
            last = c.Tile;
        }
        foreach (var c in records) map.Height[c.Tile] = c.Height;
        MapGenerator.DeriveLayers(map);
        foreach (var (t, _, obj, res, amount) in records)
        {
            byte wasObj = map.Object[t], wasRes = map.Resource[t], wasAmount = map.Amount[t];
            bool objectTaken = (wasObj == (byte)MapObject.Tree || wasObj == (byte)MapObject.Stone || wasObj == (byte)MapObject.Game)
                && res == wasRes
                && (obj == (byte)MapObject.None ? amount == 0
                    : obj == (byte)MapObject.Stone && wasObj == (byte)MapObject.Stone && amount >= 1 && amount < wasAmount);
            bool resourceTaken = wasObj == (byte)MapObject.None && obj == (byte)MapObject.None && wasRes != (byte)Resource.None
                && (res == (byte)Resource.None ? amount == 0 : res == wasRes && amount >= 1 && amount < wasAmount);
            // A planted tree (felled again or not) on land that is buildable once cleared, as Plant requires.
            bool planted = (obj == (byte)MapObject.Tree || obj == (byte)MapObject.None) && amount == 0
                && res == wasRes && wasRes == (byte)Resource.None
                && wasObj is (byte)MapObject.None or (byte)MapObject.Tree or (byte)MapObject.Stone or (byte)MapObject.Game
                && IsBuildableCleared(map, t);
            bool levelled = obj == wasObj && res == wasRes && amount == wasAmount && map.Terrain[t] is (byte)Terrain.Plains or (byte)Terrain.Fertile;
            if (!objectTaken && !resourceTaken && !planted && !levelled) throw new InvalidDataException("Invalid map change");
            map.Object[t] = obj;
            map.Resource[t] = res;
            map.Amount[t] = amount;
            map.Flags[t] = MapGenerator.TileFlagsAt(map, t % map.Edge, t / map.Edge);
            changes._tiles.Add(t);
        }
        return changes;
    }

    /// <summary>Whether the tile would be buildable (plains or fertile, flat enough) without its object.</summary>
    private static bool IsBuildableCleared(MapData map, int tile)
    {
        byte obj = map.Object[tile];
        map.Object[tile] = (byte)MapObject.None;
        bool buildable = (MapGenerator.TileFlagsAt(map, tile % map.Edge, tile / map.Edge) & (byte)TileFlags.Buildable) != 0;
        map.Object[tile] = obj;
        return buildable;
    }
}
