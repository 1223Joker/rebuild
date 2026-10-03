using System.Collections.Generic;

namespace Rebuild.Sim.Goods;

/// <summary>One good type as compiled from data/goods.json.</summary>
public sealed class GoodDefinition
{
    public GoodDefinition(int index, string id, string name, int startStock)
    {
        Index = index;
        Id = id;
        Name = name;
        StartStock = startStock;
    }

    /// <summary>Good index = position in data/goods.json (used in saves).</summary>
    public int Index { get; }
    public string Id { get; }
    public string Name { get; }
    /// <summary>Amount in every start castle (Normal start resources, docs/06-economy.md §3).</summary>
    public int StartStock { get; }
}

/// <summary>All good types; the table body is generated from data (GoodCatalog.g.cs).</summary>
public static partial class GoodCatalog
{
    private static readonly GoodDefinition[] AllGoods = CreateAll();

    /// <summary>All good types in data order.</summary>
    public static IReadOnlyList<GoodDefinition> All => AllGoods;

    /// <summary>Index of good <paramref name="id"/>, or -1.</summary>
    public static int IndexOf(string id)
    {
        for (int i = 0; i < AllGoods.Length; i++)
            if (string.Equals(AllGoods[i].Id, id, System.StringComparison.Ordinal)) return i;
        return -1;
    }
}
