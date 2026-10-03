using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.World;

/// <summary>
/// Changes systems made to the map's object and resource layers since generation (felled trees, quarried stone, hunted
/// game, caught fish; docs/06-economy.md §4). The generated map is regenerated on load, so the changed tiles are part of
/// the sim state: tile index, object, resource and amount in ascending tile order. A tile's buildable flag is refreshed
/// when its object goes.
/// </summary>
public sealed class MapChanges
{
    /// <summary>Changed tiles in ascending order.</summary>
    private readonly List<int> _tiles = new();

    public IReadOnlyList<int> Tiles => _tiles;

    /// <summary>
    /// Takes one unit of a consumed <paramref name="source"/> (<see cref="Harvest.IsConsumed"/>) from <paramref name="tile"/>,
    /// which must offer it: an object (a stone outcrop loses one unit of <see cref="MapData.Amount"/> and disappears with
    /// its last one, an amount of 0 counting as 1; a tree or game animal disappears) or the fish resource (one unit of
    /// <see cref="MapData.Amount"/>; the resource goes with the last one).
    /// </summary>
    public void Take(MapData map, int tile, HarvestSource source)
    {
        if (!Harvest.IsConsumed(source) || !Harvest.Matches(map, tile, source))
            throw new System.ArgumentException("Tile does not offer a consumed source", nameof(source));
        if (source != HarvestSource.Fish)
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
        int at = _tiles.BinarySearch(tile);
        if (at < 0) _tiles.Insert(~at, tile);
    }

    /// <summary>Canonical state: every changed tile with its current object, resource and amount.</summary>
    public void WriteTo(CanonicalWriter w, MapData map)
    {
        w.WriteInt32(_tiles.Count);
        foreach (int t in _tiles)
        {
            w.WriteInt32(t);
            w.WriteByte(map.Object[t]);
            w.WriteByte(map.Resource[t]);
            w.WriteByte(map.Amount[t]);
        }
    }

    /// <summary>
    /// Reads the changes and applies them to the freshly generated <paramref name="map"/>. Tiles must be in ascending order
    /// and each must show exactly what <see cref="Take"/> can leave: either the generated tree, stone or game object is
    /// gone (amount 0) or a stone outcrop holds fewer units, with the resource unchanged; or, on a tile without an object,
    /// the generated fish resource is gone (amount 0) or holds fewer units.
    /// </summary>
    public static MapChanges ReadFrom(CanonicalReader r, MapData map)
    {
        var changes = new MapChanges();
        int count = r.ReadInt32();
        if (count < 0 || count > map.TileCount) throw new InvalidDataException("Invalid map change count");
        int last = -1;
        for (int i = 0; i < count; i++)
        {
            int t = r.ReadInt32();
            byte obj = r.ReadByte(), res = r.ReadByte(), amount = r.ReadByte();
            if (t <= last || t >= map.TileCount) throw new InvalidDataException("Invalid map change tile");
            byte wasObj = map.Object[t], wasRes = map.Resource[t], wasAmount = map.Amount[t];
            bool objectTaken = (wasObj == (byte)MapObject.Tree || wasObj == (byte)MapObject.Stone || wasObj == (byte)MapObject.Game)
                && res == wasRes
                && (obj == (byte)MapObject.None ? amount == 0
                    : obj == (byte)MapObject.Stone && wasObj == (byte)MapObject.Stone && amount >= 1 && amount < wasAmount);
            bool resourceTaken = wasObj == (byte)MapObject.None && obj == (byte)MapObject.None && wasRes == (byte)Resource.Fish
                && (res == (byte)Resource.None ? amount == 0 : res == wasRes && amount >= 1 && amount < wasAmount);
            if (!objectTaken && !resourceTaken) throw new InvalidDataException("Invalid map change");
            map.Object[t] = obj;
            map.Resource[t] = res;
            map.Amount[t] = amount;
            map.Flags[t] = MapGenerator.TileFlagsAt(map, t % map.Edge, t / map.Edge);
            changes._tiles.Add(t);
            last = t;
        }
        return changes;
    }
}
