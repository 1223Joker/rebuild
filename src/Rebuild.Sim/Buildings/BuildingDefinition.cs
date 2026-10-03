using System.Collections.Generic;

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
        int territoryRadius, int costPlanks, int costStone, bool playerPlaceable, bool isStorage)
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
