using Rebuild.Sim.MapGen;
using Rebuild.Sim.Match;

namespace Rebuild.Sim.World;

/// <summary>
/// Maps lobby slots to generated start positions (docs/04-game-modes.md §1: one start per Human/AI slot).
/// Rule: the k-th Human/AI slot in slot order gets start k, and its team must equal
/// <see cref="MapSpec.TeamOf"/>(k) — the lobby derives <see cref="MapSpec.Teams"/> from the slot table,
/// so team-aware placement (teammates in adjacent sectors, Mirror symmetry) holds for the actual teams.
/// </summary>
public static class StartAssignment
{
    /// <summary>Null if the setup fits its map spec, else the reason.</summary>
    public static string? Check(MatchSetup setup)
    {
        int k = 0;
        for (int i = 0; i < setup.Slots.Count; i++)
        {
            var s = setup.Slots[i];
            if (!NeedsStart(s.Kind)) continue;
            if (k >= setup.Map.PlayerCount) return $"more Human/AI slots than map starts ({setup.Map.PlayerCount})";
            if (s.Team != setup.Map.TeamOf(k)) return $"slot {i} is in team {s.Team} but map start {k} is in team {setup.Map.TeamOf(k)}";
            k++;
        }
        return k == setup.Map.PlayerCount ? null : $"{k} Human/AI slots but the map has {setup.Map.PlayerCount} starts";
    }

    /// <summary>Start index per slot (−1 for open and monster slots). Throws if <see cref="Check"/> fails.</summary>
    public static int[] Assign(MatchSetup setup)
    {
        string? error = Check(setup);
        if (error != null) throw new System.ArgumentException(error, nameof(setup));
        var result = new int[setup.Slots.Count];
        int k = 0;
        for (int i = 0; i < result.Length; i++)
            result[i] = NeedsStart(setup.Slots[i].Kind) ? k++ : -1;
        return result;
    }

    public static bool NeedsStart(SlotKind kind) => kind == SlotKind.Human || kind == SlotKind.Ai;
}
