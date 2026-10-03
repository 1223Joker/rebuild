using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.MapGen;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.World;

/// <summary>
/// Changes systems made to the map's object layer since generation (harvested trees and stone, docs/06-economy.md §4).
/// The generated map is regenerated on load, so the changed tiles are part of the sim state: tile index, object and
/// amount in ascending tile order. A tile's buildable flag is refreshed when its object goes.
/// </summary>
public sealed class MapChanges
{
    /// <summary>Changed tiles in ascending order.</summary>
    private readonly List<int> _tiles = new();

    public IReadOnlyList<int> Tiles => _tiles;

    /// <summary>
    /// Takes one unit of the object on <paramref name="tile"/>: a stone outcrop loses one unit of
    /// <see cref="MapData.Amount"/> and disappears with its last one (an amount of 0 counts as 1); a tree disappears.
    /// </summary>
    public void Take(MapData map, int tile)
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
        int at = _tiles.BinarySearch(tile);
        if (at < 0) _tiles.Insert(~at, tile);
    }

    /// <summary>Canonical state: every changed tile with its current object and amount.</summary>
    public void WriteTo(CanonicalWriter w, MapData map)
    {
        w.WriteInt32(_tiles.Count);
        foreach (int t in _tiles)
        {
            w.WriteInt32(t);
            w.WriteByte(map.Object[t]);
            w.WriteByte(map.Amount[t]);
        }
    }

    /// <summary>
    /// Reads the changes and applies them to the freshly generated <paramref name="map"/>. Each tile must be in ascending
    /// order and hold a tree or stone in the generated map; it must now be empty, or (stone) hold fewer units.
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
            byte obj = r.ReadByte(), amount = r.ReadByte();
            if (t <= last || t >= map.TileCount) throw new InvalidDataException("Invalid map change tile");
            byte was = map.Object[t];
            bool valid = (was == (byte)MapObject.Tree || was == (byte)MapObject.Stone)
                && (obj == (byte)MapObject.None ? amount == 0
                    : obj == (byte)MapObject.Stone && was == (byte)MapObject.Stone && amount >= 1 && amount < map.Amount[t]);
            if (!valid) throw new InvalidDataException("Invalid map change");
            map.Object[t] = obj;
            map.Amount[t] = amount;
            map.Flags[t] = MapGenerator.TileFlagsAt(map, t % map.Edge, t / map.Edge);
            changes._tiles.Add(t);
            last = t;
        }
        return changes;
    }
}
