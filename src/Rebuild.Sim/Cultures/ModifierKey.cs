namespace Rebuild.Sim.Cultures;

/// <summary>
/// Named numeric parameters a culture may scale by an integer percent (ADR 0007).
/// Data key "build_cost.wood" maps to <see cref="BuildCostWood"/>. Dense values (array index): append only.
/// </summary>
public enum ModifierKey
{
    BuildCostWood,
    BuildCostStone,
    BuildingTime,
    BuildingHp,
    WallCost,
    WallHp,
    PalisadeCost,
    PalisadeHp,
    GuardTowerCost,
    CarrierSpeed,
    UnitSpeed,
    SiegeDamage,
    FarmYield,
    FisherYield,
    MillYield,
    MineYield,
    WoodcutterYield,
    StonecutterYield,
    ForesterPlanting,
    SettlerProduction,
}

/// <summary>Behaviour switches implemented once in the sim (docs/10-cultures.md §4). Append only.</summary>
public enum CultureHook
{
    ResourceConversion,
    AuraBonus,
    MaterialRequirementOverride,
    MountedUnits,
    Upkeep,
    SpearWithoutIron,
}
