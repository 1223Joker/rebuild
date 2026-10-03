using System.Collections.Generic;
using Rebuild.Sim.MapGen;

namespace Rebuild.Sim.Buildings;

/// <summary>Footprint class; the footprint is a square of <see cref="BuildingDefinition.Side"/> tiles.</summary>
public enum BuildingSize : byte
{
    Small,
    Medium,
    Large,
}

/// <summary>Which tiles a building may stand on (docs/06-economy.md §1).</summary>
public enum BuildingTerrain : byte
{
    /// <summary>Walkable plains/fertile tiles without objects; uneven sites are levelled by a digger first.</summary>
    Land,
    /// <summary>Walkable mountain tiles without objects (mines).</summary>
    Mountain,
}

/// <summary>One building type as compiled from data/buildings.json.</summary>
public sealed class BuildingDefinition
{
    public BuildingDefinition(int index, string id, string name, BuildingSize size, BuildingTerrain terrain,
        int territoryRadius, int costPlanks, int costStone, bool playerPlaceable, int storageCapacity, int beds,
        ProductionDefinition? production = null)
    {
        Index = index;
        Id = id;
        Name = name;
        Size = size;
        Terrain = terrain;
        TerritoryRadius = territoryRadius;
        CostPlanks = costPlanks;
        CostStone = costStone;
        PlayerPlaceable = playerPlaceable;
        StorageCapacity = storageCapacity;
        Beds = beds;
        Production = production;
    }

    /// <summary>Type index = position in data/buildings.json (used in commands and saves).</summary>
    public int Index { get; }
    public string Id { get; }
    public string Name { get; }
    public BuildingSize Size { get; }
    public BuildingTerrain Terrain { get; }
    /// <summary>Territory claim radius once complete; 0 = civilian building.</summary>
    public int TerritoryRadius { get; }
    public int CostPlanks { get; }
    public int CostStone { get; }
    /// <summary>False for buildings only the sim creates (the start castle).</summary>
    public bool PlayerPlaceable { get; }
    /// <summary>
    /// Units (all goods together) a complete storage accepts as output overflow (data <c>"storage"</c>); 0 = no storage.
    /// Returned units (refunds, tools of freed workers, units whose destination vanished) may exceed it (ASSUMPTION).
    /// </summary>
    public int StorageCapacity { get; }
    /// <summary>Holds a goods stock once complete (castle, storehouse).</summary>
    public bool IsStorage => StorageCapacity > 0;
    /// <summary>
    /// Beds of the complete building (data <c>"beds"</c>; castle 30, residence 10): every settler homed there (carriers and
    /// workers) takes one; one carrier spawns every <see cref="World.Settlers.SpawnIntervalTicks"/> while a bed is free; the
    /// start castle starts full (docs/12-needs-seasons-weather.md §1.1).
    /// </summary>
    public int Beds { get; }
    /// <summary>Work cycle of the complete building, or null if it produces nothing (docs/06-economy.md §4).</summary>
    public ProductionDefinition? Production { get; }
    /// <summary>Plank + stone units a construction site needs.</summary>
    public int CostTotal => CostPlanks + CostStone;

    /// <summary>Footprint edge in tiles: S 2, M 3, L 4 (ASSUMPTION, docs/06-economy.md §1).</summary>
    public int Side => Size switch
    {
        BuildingSize.Small => 2,
        BuildingSize.Medium => 3,
        _ => 4,
    };
}

/// <summary>
/// What a production cycle needs within its radius on own territory (data/buildings.json <c>"harvest"</c>,
/// docs/06-economy.md §4). Map objects and the fish resource lose one unit per cycle; terrain is only required.
/// </summary>
public enum HarvestSource : byte
{
    None,
    /// <summary>Tree object; felled by the cycle.</summary>
    Tree,
    /// <summary>Stone outcrop object; loses one unit of <see cref="MapData.Amount"/>.</summary>
    Stone,
    /// <summary>Game animal object; hunted (removed) by the cycle.</summary>
    Game,
    /// <summary>Fish resource on a water tile; loses one unit of <see cref="MapData.Amount"/>.</summary>
    Fish,
    /// <summary>Any water tile; not consumed (waterworks).</summary>
    Water,
    /// <summary>Any fertile tile; not consumed (farm fields, ASSUMPTION until fields are sown).</summary>
    Fertile,
    /// <summary>Coal deposit (resource on a mountain tile without object); loses one unit of <see cref="MapData.Amount"/>.</summary>
    Coal,
    /// <summary>Iron ore deposit; like <see cref="Coal"/>.</summary>
    IronOre,
    /// <summary>Gold ore deposit; like <see cref="Coal"/>.</summary>
    GoldOre,
}

/// <summary>Tile tests for <see cref="HarvestSource"/>.</summary>
public static class Harvest
{
    /// <summary>True if <paramref name="tile"/> currently offers <paramref name="source"/>.</summary>
    public static bool Matches(MapData map, int tile, HarvestSource source) => source switch
    {
        HarvestSource.Tree => map.Object[tile] == (byte)MapObject.Tree,
        HarvestSource.Stone => map.Object[tile] == (byte)MapObject.Stone,
        HarvestSource.Game => map.Object[tile] == (byte)MapObject.Game,
        HarvestSource.Fish => map.Resource[tile] == (byte)Resource.Fish && map.Terrain[tile] == (byte)Terrain.Water,
        HarvestSource.Water => map.Terrain[tile] == (byte)Terrain.Water,
        HarvestSource.Fertile => map.Terrain[tile] == (byte)Terrain.Fertile,
        HarvestSource.Coal => IsDeposit(map, tile, Resource.Coal),
        HarvestSource.IronOre => IsDeposit(map, tile, Resource.Iron),
        HarvestSource.GoldOre => IsDeposit(map, tile, Resource.Gold),
        _ => false,
    };

    /// <summary>True if a cycle takes one unit of the source from the map (objects and resources, not terrain).</summary>
    public static bool IsConsumed(HarvestSource source) =>
        source is >= HarvestSource.Tree and <= HarvestSource.Fish or >= HarvestSource.Coal and <= HarvestSource.GoldOre;

    /// <summary>True if the source is a map resource (fish, ore deposits) rather than an object or terrain.</summary>
    public static bool IsResource(HarvestSource source) =>
        source is HarvestSource.Fish or >= HarvestSource.Coal and <= HarvestSource.GoldOre;

    /// <summary>
    /// An ore deposit: the resource on a mountain tile without an object (objects keep their own amount in
    /// <see cref="MapData.Amount"/>; the generator never puts both on one tile).
    /// </summary>
    private static bool IsDeposit(MapData map, int tile, Resource ore) =>
        map.Resource[tile] == (byte)ore && map.Terrain[tile] == (byte)Terrain.Mountain && map.Object[tile] == (byte)MapObject.None;
}

/// <summary>
/// Work cycle of a production building (data/buildings.json <c>"production"</c>): at the start it takes
/// <see cref="InputAmounts"/>[k] units from input pile k, which holds any of the goods <see cref="Alternatives"/>[k]
/// (one good, or alternatives written <c>"a|b|c"</c>, kept for the planned home fuel pile, docs/12-needs-seasons-weather.md §2); after <see cref="CycleTicks"/> ticks
/// it takes one unit of the <see cref="Harvest"/> source within <see cref="Radius"/> tiles (if any and consumed) and puts
/// one <see cref="Output"/> unit into its output pile, or — for a planter such as the forester — puts a
/// <see cref="Plant"/> object on a free tile within <see cref="Radius"/> instead (World.Production). A smith has several
/// <see cref="Outputs"/> (data <c>"outputs"</c>) and one output pile per output; each cycle makes the one the owner's quota
/// picks (World.ProductionQuotas). A cycle only runs while the building's worker is inside; the worker is an idle carrier
/// that fetched the building's <see cref="Tool"/> from a storage, if it has one (World.Logistics, docs/06-economy.md §3).
/// Its work speed depends on the season (<see cref="SeasonSpeed"/>, data <c>"seasons"</c>, docs/12-needs-seasons-weather.md §2.1).
/// </summary>
public sealed class ProductionDefinition
{
    /// <summary><see cref="Output"/> of a planter, which produces no good (its output pile stays empty).</summary>
    public const ushort NoOutput = ushort.MaxValue;
    /// <summary><see cref="Tool"/> of a building whose worker needs none (miller, baker, smelter, …).</summary>
    public const ushort NoTool = ushort.MaxValue;

    public ProductionDefinition(ushort[][] inputs, int[] inputAmounts, ushort output, int cycleTicks, HarvestSource harvest, int radius,
        MapObject plant = MapObject.None, ushort[]? outputs = null, ushort tool = NoTool, int[]? seasonSpeed = null)
    {
        if (seasonSpeed != null && seasonSpeed.Length != World.Calendar.SeasonsPerYear)
            throw new System.ArgumentException("One speed per season", nameof(seasonSpeed));
        if (seasonSpeed != null)
            foreach (int speed in seasonSpeed)
                if (speed != 0 && (speed < MinSeasonSpeed || speed > MaxSeasonSpeed))
                    throw new System.ArgumentException($"Season speeds are 0 or {MinSeasonSpeed}..{MaxSeasonSpeed} %", nameof(seasonSpeed));
        if (outputs != null && (outputs.Length < 2 || outputs[0] != output || plant != MapObject.None))
            throw new System.ArgumentException("Output choices are at least two goods, the first being the output, and no planter", nameof(outputs));
        if (outputs != null)
            for (int k = 0; k < outputs.Length; k++)
                if (outputs[k] == NoOutput || System.Array.IndexOf(outputs, outputs[k]) != k)
                    throw new System.ArgumentException("Output choices are distinct goods", nameof(outputs));
        if (plant != MapObject.None && (output != NoOutput || harvest != HarvestSource.None || radius < 1))
            throw new System.ArgumentException("A planter has a radius, no output and no harvest", nameof(plant));
        if (plant == MapObject.None && output == NoOutput)
            throw new System.ArgumentException("Only a planter has no output", nameof(output));
        if (inputs.Length != inputAmounts.Length) throw new System.ArgumentException("One amount per input", nameof(inputAmounts));
        foreach (var goods in inputs)
            if (goods.Length == 0) throw new System.ArgumentException("Every input pile takes a good", nameof(inputs));
        Alternatives = inputs;
        var first = new ushort[inputs.Length];
        for (int k = 0; k < inputs.Length; k++) first[k] = inputs[k][0];
        Inputs = first;
        InputAmounts = inputAmounts;
        Output = output;
        Outputs = outputs ?? new[] { output };
        CycleTicks = cycleTicks;
        Harvest = harvest;
        Radius = radius;
        Plant = plant;
        Tool = tool;
        SeasonSpeed = seasonSpeed ?? new[] { 100, 100, 100, 100 };
    }

    /// <summary>Lowest non-zero <see cref="SeasonSpeed"/> (a cycle takes at most 4× <see cref="CycleTicks"/>).</summary>
    public const int MinSeasonSpeed = 25;
    /// <summary>Highest <see cref="SeasonSpeed"/>.</summary>
    public const int MaxSeasonSpeed = 400;

    /// <summary>First good of each input pile (at most two piles); the pile's only good unless it has alternatives.</summary>
    public IReadOnlyList<ushort> Inputs { get; }
    /// <summary>Goods each input pile accepts, in data order (one, or alternatives written "a|b|c").</summary>
    public IReadOnlyList<IReadOnlyList<ushort>> Alternatives { get; }
    /// <summary>Units of each input one cycle consumes.</summary>
    public IReadOnlyList<int> InputAmounts { get; }
    /// <summary>Good a cycle piles (the first of <see cref="Outputs"/>), or <see cref="NoOutput"/> for a planter.</summary>
    public ushort Output { get; }
    /// <summary>
    /// Goods a cycle may pile, in data order: just <see cref="Output"/>, or a smith's choices (tools, weapons), one of which
    /// the owner's quota picks per cycle. Output pile k (pile index <see cref="Inputs"/>.Count + k) holds Outputs[k].
    /// </summary>
    public IReadOnlyList<ushort> Outputs { get; }
    /// <summary>True if the output is chosen per cycle by the owner's production quota (smiths).</summary>
    public bool HasChoice => Outputs.Count > 1;
    /// <summary>Ticks of one work cycle.</summary>
    public int CycleTicks { get; }
    /// <summary>What a cycle needs in reach (tree, stone, game, fish, water, fertile land), or <see cref="HarvestSource.None"/>.</summary>
    public HarvestSource Harvest { get; }
    /// <summary>Harvest or planting radius in tiles around the building centre (0 without either).</summary>
    public int Radius { get; }
    /// <summary>Object a cycle plants (<see cref="MapObject.Tree"/> for the forester), or <see cref="MapObject.None"/>.</summary>
    public MapObject Plant { get; }
    /// <summary>Tool good the worker takes from a storage on its way to the building (axe, saw, …), or <see cref="NoTool"/>.</summary>
    public ushort Tool { get; }

    /// <summary>
    /// Work speed in percent per season (index = <see cref="World.Season"/>; default 100): a cycle takes
    /// <see cref="CycleTicksIn"/> ticks; 0 = no cycle starts in that season (a farm in winter), a running one still ends
    /// after <see cref="CycleTicks"/> (ASSUMPTION).
    /// </summary>
    public IReadOnlyList<int> SeasonSpeed { get; }

    /// <summary>
    /// Ticks of a cycle in <paramref name="season"/>: <see cref="CycleTicks"/> × 100 / speed, rounded up; <see cref="CycleTicks"/>
    /// at speed 0. A cycle ends at the first tick its elapsed ticks reach the current season's value, so a season change
    /// shortens or lengthens the running cycle.
    /// </summary>
    public int CycleTicksIn(World.Season season)
    {
        int speed = SeasonSpeed[(int)season];
        return speed == 0 ? CycleTicks : (CycleTicks * 100 + speed - 1) / speed;
    }

    /// <summary>True if a cycle may start in <paramref name="season"/> (speed above 0).</summary>
    public bool WorksIn(World.Season season) => SeasonSpeed[(int)season] > 0;

    /// <summary>Longest <see cref="CycleTicksIn"/> over all seasons (upper bound of a running cycle's elapsed ticks).</summary>
    public int MaxCycleTicks
    {
        get
        {
            int max = CycleTicks;
            for (int s = 0; s < World.Calendar.SeasonsPerYear; s++) max = System.Math.Max(max, CycleTicksIn((World.Season)s));
            return max;
        }
    }

    /// <summary>Output index of <paramref name="good"/> (its pile is <see cref="Inputs"/>.Count + index), or -1 if the building does not make it.</summary>
    public int OutputIndexOf(int good)
    {
        if (good == NoOutput) return -1;
        for (int k = 0; k < Outputs.Count; k++)
            if (Outputs[k] == good) return k;
        return -1;
    }

    /// <summary>Input pile index of <paramref name="good"/>, or -1 if the building does not take it.</summary>
    public int InputIndexOf(int good)
    {
        for (int k = 0; k < Alternatives.Count; k++)
            foreach (ushort g in Alternatives[k])
                if (g == good) return k;
        return -1;
    }
}

/// <summary>All building types; the table body is generated from data (BuildingCatalog.g.cs).</summary>
public static partial class BuildingCatalog
{
    private static readonly BuildingDefinition[] AllBuildings = CreateAll();

    /// <summary>All building types in data order.</summary>
    public static IReadOnlyList<BuildingDefinition> All => AllBuildings;

    /// <summary>Index of building <paramref name="id"/>, or -1.</summary>
    public static int IndexOf(string id)
    {
        for (int i = 0; i < AllBuildings.Length; i++)
            if (string.Equals(AllBuildings[i].Id, id, System.StringComparison.Ordinal)) return i;
        return -1;
    }
}
