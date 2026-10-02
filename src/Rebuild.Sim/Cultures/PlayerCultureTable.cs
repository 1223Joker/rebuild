using Rebuild.Sim.Core;

namespace Rebuild.Sim.Cultures;

/// <summary>
/// A culture resolved into flat per-player tables at match start, so hot paths never look up by name
/// (ADR 0007). Unlisted modifiers are 100 %.
/// </summary>
public sealed class PlayerCultureTable
{
    private static readonly int ModifierCount = System.Enum.GetValues<ModifierKey>().Length;
    private static readonly int HookCount = System.Enum.GetValues<CultureHook>().Length;

    private readonly int[] _percent;
    private readonly bool[] _hooks;

    public CultureDefinition Culture { get; }

    public PlayerCultureTable(CultureDefinition culture)
    {
        Culture = culture;
        _percent = new int[ModifierCount];
        System.Array.Fill(_percent, 100);
        foreach (var m in culture.Modifiers) _percent[(int)m.Key] = m.Percent;
        _hooks = new bool[HookCount];
        foreach (var h in culture.Hooks) _hooks[(int)h] = true;
    }

    public int Percent(ModifierKey key) => _percent[(int)key];

    public bool Has(CultureHook hook) => _hooks[(int)hook];

    /// <summary><paramref name="baseValue"/> scaled by the modifier, rounded toward negative infinity.</summary>
    public long Apply(ModifierKey key, long baseValue) => Percentage.Apply(baseValue, _percent[(int)key]);
}

public static class Percentage
{
    /// <summary>value × percent / 100, rounded toward negative infinity (ADR 0007).</summary>
    public static long Apply(long value, int percent) => IntMath.FloorDiv(value * percent, 100);
}
