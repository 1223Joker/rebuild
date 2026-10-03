using System.IO;
using Rebuild.Sim.Buildings;
using Rebuild.Sim.Goods;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.World;

/// <summary>
/// Per-player production quotas of the smiths (docs/06-economy.md §4): every good that is an output choice of some
/// building (<see cref="ProductionDefinition.HasChoice"/>: tools, weapons) has a weight 0..<see cref="MaxWeight"/>
/// (default <see cref="DefaultWeight"/>, ASSUMPTION), set by <c>SetToolProductionQuota</c>. At the start of a smith's cycle
/// <see cref="Pick"/> chooses among its outputs with weight &gt; 0 by smooth weighted round robin: every candidate's credit
/// grows by its weight, the largest credit wins (ties: data order) and loses the candidates' total weight. Over time each
/// good gets its weight's share of the cycles, evenly interleaved; weight 0 means "do not make". A changed weight resets
/// the player's credits, so a good with weight 0 always has credit 0, the credits of one building's outputs with weight
/// &gt; 0 always sum to 0 (every pick adds and takes the same total) and credits stay within ±(total weight). Smiths have
/// disjoint output sets (toolsmith tools, weaponsmith weapons), so one smith's picks never shift another's credits.
/// </summary>
public sealed class ProductionQuotas
{
    /// <summary>Highest quota weight (a slider 0..10, like the Settlers tool settings).</summary>
    public const int MaxWeight = 10;
    /// <summary>Weight of every quota good at match start (ASSUMPTION: equal shares).</summary>
    public const int DefaultWeight = 1;

    /// <summary>Quota goods in good index order (outputs of buildings with a choice).</summary>
    private static readonly ushort[] QuotaGoods = FindQuotaGoods();

    private readonly int[][] _weights;
    private readonly int[][] _credits;

    public ProductionQuotas(int playerCount)
    {
        _weights = new int[playerCount][];
        _credits = new int[playerCount][];
        for (int p = 0; p < playerCount; p++)
        {
            _weights[p] = new int[GoodCatalog.All.Count];
            _credits[p] = new int[GoodCatalog.All.Count];
            foreach (ushort g in QuotaGoods) _weights[p][g] = DefaultWeight;
        }
    }

    /// <summary>Whether <paramref name="good"/> is an output choice of some building, i.e. has a quota.</summary>
    public static bool IsQuotaGood(int good) => System.Array.IndexOf(QuotaGoods, (ushort)good) >= 0;

    /// <summary>Quota weight of a good for a player (0 for goods without quota).</summary>
    public int WeightOf(int player, int good) => _weights[player][good];

    /// <summary>Round-robin credit of a good for a player (state; exposed for tests and UI).</summary>
    public int CreditOf(int player, int good) => _credits[player][good];

    /// <summary>Sets a quota weight (callers validate: quota good, 0..<see cref="MaxWeight"/>) and resets the player's credits.</summary>
    public void Set(int player, int good, int weight)
    {
        if (!IsQuotaGood(good) || weight < 0 || weight > MaxWeight) throw new System.ArgumentOutOfRangeException(nameof(good));
        _weights[player][good] = weight;
        System.Array.Clear(_credits[player]);
    }

    /// <summary>
    /// Output index for the next cycle of a building of <paramref name="player"/> producing <paramref name="p"/>: 0 for a
    /// single output; for a choice the round-robin winner among outputs with weight &gt; 0 (updates the credits), or -1 if
    /// every weight is 0 (the building idles).
    /// </summary>
    public int Pick(int player, ProductionDefinition p)
    {
        if (!p.HasChoice) return 0;
        var weights = _weights[player];
        var credits = _credits[player];
        int total = 0, best = -1;
        for (int k = 0; k < p.Outputs.Count; k++)
        {
            int g = p.Outputs[k];
            if (weights[g] == 0) continue;
            total += weights[g];
            credits[g] += weights[g];
            if (best < 0 || credits[g] > credits[p.Outputs[best]]) best = k;
        }
        if (best >= 0) credits[p.Outputs[best]] -= total;
        return best;
    }

    /// <summary>Canonical state: per player, weight and credit of every quota good in good index order.</summary>
    public void WriteTo(CanonicalWriter w)
    {
        for (int p = 0; p < _weights.Length; p++)
            foreach (ushort g in QuotaGoods)
            {
                w.WriteByte((byte)_weights[p][g]);
                w.WriteInt32(_credits[p][g]);
            }
    }

    /// <summary>
    /// Reads and validates quotas (weights ≤ <see cref="MaxWeight"/>, credit 0 at weight 0, credits within ±(all weights),
    /// credits of each building's outputs with weight &gt; 0 summing to 0).
    /// </summary>
    public static ProductionQuotas ReadFrom(CanonicalReader r, int playerCount)
    {
        var q = new ProductionQuotas(playerCount);
        int bound = MaxWeight * QuotaGoods.Length;
        for (int p = 0; p < playerCount; p++)
            foreach (ushort g in QuotaGoods)
            {
                int weight = r.ReadByte(), credit = r.ReadInt32();
                if (weight > MaxWeight || (weight == 0 && credit != 0) || credit < -bound || credit > bound)
                    throw new InvalidDataException("Invalid production quota");
                q._weights[p][g] = weight;
                q._credits[p][g] = credit;
            }
        for (int p = 0; p < playerCount; p++)
            foreach (var b in BuildingCatalog.All)
            {
                if (b.Production is not { HasChoice: true } prod) continue;
                int sum = 0;
                foreach (ushort g in prod.Outputs) sum += q._credits[p][g];
                if (sum != 0) throw new InvalidDataException("Production quota credits do not balance");
            }
        return q;
    }

    private static ushort[] FindQuotaGoods()
    {
        var quota = new bool[GoodCatalog.All.Count];
        foreach (var b in BuildingCatalog.All)
            if (b.Production is { HasChoice: true } p)
                foreach (ushort g in p.Outputs) quota[g] = true;
        int n = 0;
        foreach (bool q in quota) if (q) n++;
        var goods = new ushort[n];
        n = 0;
        for (int g = 0; g < quota.Length; g++)
            if (quota[g]) goods[n++] = (ushort)g;
        return goods;
    }
}
