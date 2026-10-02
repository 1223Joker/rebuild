using System.Collections.Generic;

namespace Rebuild.Sim.Cultures;

public readonly record struct ModifierEntry(ModifierKey Key, int Percent);

/// <summary>One culture as compiled from data/cultures/&lt;id&gt;/culture.json.</summary>
public sealed class CultureDefinition
{
    public CultureDefinition(int index, string id, string name, ModifierEntry[] modifiers, CultureHook[] hooks)
    {
        Index = index;
        Id = id;
        Name = name;
        Modifiers = modifiers;
        Hooks = hooks;
    }

    /// <summary>Position in <see cref="CultureCatalog.All"/> (cultures sorted by id).</summary>
    public int Index { get; }
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<ModifierEntry> Modifiers { get; }
    public IReadOnlyList<CultureHook> Hooks { get; }
}

/// <summary>All cultures; the table body is generated from data (CultureCatalog.g.cs).</summary>
public static partial class CultureCatalog
{
    private static readonly CultureDefinition[] AllCultures = CreateAll();

    /// <summary>All cultures sorted by id (ordinal).</summary>
    public static IReadOnlyList<CultureDefinition> All => AllCultures;

    /// <summary>Index of culture <paramref name="id"/>, or -1.</summary>
    public static int IndexOf(string id)
    {
        for (int i = 0; i < AllCultures.Length; i++)
            if (string.Equals(AllCultures[i].Id, id, System.StringComparison.Ordinal)) return i;
        return -1;
    }
}
