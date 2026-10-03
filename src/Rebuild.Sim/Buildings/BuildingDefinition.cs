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
    /// <summary>Buildable land tiles (flat, no water/mountain/object).</summary>
    Land,
    /// <summary>Walkable mountain tiles without objects (mines).</summary>
    Mountain,
}

/// <summary>One building type as compiled from data/buildings.json.</summary>
public sealed class BuildingDefinition
{
    public BuildingDefinition(int index, string id, string name, BuildingSize size, BuildingTerrain terrain,
        int territoryRadius, int costPlanks, int costStone, bool playerPlaceable, bool isStorage, int carriers,
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
        IsStorage = isStorage;
        Carriers = carriers;
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
    /// <summary>Holds a goods stock once complete (castle, storehouse).</summary>
    public bool IsStorage { get; }
    /// <summary>
    /// Carriers homed at the complete building (castle 30, residence 10): one more spawns every
    /// <see cref="World.Settlers.SpawnIntervalTicks"/> while fewer live; the start castle starts full (docs/06-economy.md §3).
    /// </summary>
    public int Carriers { get; }
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
/// Work cycle of a production building (data/buildings.json <c>"production"</c>): at the start it takes
/// <see cref="InputAmounts"/>[k] units of <see cref="Inputs"/>[k] from its input piles; after <see cref="CycleTicks"/> ticks
/// it takes one <see cref="Harvest"/> object within <see cref="Radius"/> tiles (if any) and puts one <see cref="Output"/>
/// unit into its output pile (World.Production).
/// </summary>
public sealed class ProductionDefinition
{
    public ProductionDefinition(ushort[] inputs, int[] inputAmounts, ushort output, int cycleTicks, MapObject harvest, int radius)
    {
        if (inputs.Length != inputAmounts.Length) throw new System.ArgumentException("One amount per input", nameof(inputAmounts));
        Inputs = inputs;
        InputAmounts = inputAmounts;
        Output = output;
        CycleTicks = cycleTicks;
        Harvest = harvest;
        Radius = radius;
    }

    /// <summary>Input goods (at most two); one input pile each.</summary>
    public IReadOnlyList<ushort> Inputs { get; }
    /// <summary>Units of each input one cycle consumes.</summary>
    public IReadOnlyList<int> InputAmounts { get; }
    public ushort Output { get; }
    /// <summary>Ticks of one work cycle.</summary>
    public int CycleTicks { get; }
    /// <summary>Map object a cycle takes (tree, stone), or <see cref="MapObject.None"/>.</summary>
    public MapObject Harvest { get; }
    /// <summary>Harvest radius in tiles around the building centre (0 without harvest).</summary>
    public int Radius { get; }

    /// <summary>Input pile index of <paramref name="good"/>, or -1 if the building does not take it.</summary>
    public int InputIndexOf(int good)
    {
        for (int k = 0; k < Inputs.Count; k++)
            if (Inputs[k] == good) return k;
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
