using System.Linq;
using FsCheck.Xunit;
using Rebuild.Sim.Core;
using Rebuild.Sim.Serialization;
using Xunit;

namespace Rebuild.Sim.Tests;

public class RngTests
{
    [Fact]
    public void Pcg32_matches_reference_implementation()
    {
        // pcg32-demo from the reference C library: pcg32_srandom_r(&rng, 42u, 54u).
        var rng = new Pcg32(42, 54);
        uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
        foreach (uint e in expected) Assert.Equal(e, rng.NextUInt());
    }

    [Fact]
    public void SplitMix64_matches_reference_implementation()
    {
        // Vigna's splitmix64.c with state 0.
        ulong state = 0;
        Assert.Equal(0xe220a8397b1dcdafUL, SplitMix64.Next(ref state));
        Assert.Equal(0x6e789e6aa1b965f4UL, SplitMix64.Next(ref state));
    }

    [Property(MaxTest = 1000)]
    public bool NextInt_stays_in_bounds(ulong seed, int bound)
    {
        if (bound <= 0) bound = bound == int.MinValue ? int.MaxValue : -bound + 1;
        var rng = new Pcg32(seed, 3);
        for (int i = 0; i < 20; i++)
        {
            int v = rng.NextInt(bound);
            if (v < 0 || v >= bound) return false;
        }
        return true;
    }

    [Fact]
    public void NextInt_is_roughly_uniform()
    {
        var rng = new Pcg32(7, 1);
        var counts = new int[10];
        for (int i = 0; i < 100_000; i++) counts[rng.NextInt(10)]++;
        Assert.All(counts, c => Assert.InRange(c, 9_500, 10_500));
    }

    [Fact]
    public void Chance_extremes()
    {
        var rng = new Pcg32(1, 1);
        for (int i = 0; i < 1000; i++)
        {
            Assert.False(rng.Chance(0));
            Assert.True(rng.Chance(65_536));
        }
    }

    [Fact]
    public void Streams_are_independent_and_reproducible()
    {
        var a1 = Pcg32.ForStream(99, RngStream.Economy);
        var a2 = Pcg32.ForStream(99, RngStream.Economy);
        var b = Pcg32.ForStream(99, RngStream.Combat);
        var seqA1 = Enumerable.Range(0, 8).Select(_ => a1.NextUInt()).ToArray();
        var seqA2 = Enumerable.Range(0, 8).Select(_ => a2.NextUInt()).ToArray();
        var seqB = Enumerable.Range(0, 8).Select(_ => b.NextUInt()).ToArray();
        Assert.Equal(seqA1, seqA2);
        Assert.NotEqual(seqA1, seqB);
    }

    [Fact]
    public void State_round_trips_through_serialization()
    {
        var rng = new Pcg32(5, 6);
        for (int i = 0; i < 17; i++) rng.NextUInt();
        var w = new CanonicalWriter();
        rng.WriteTo(w);
        var copy = Pcg32.ReadFrom(new CanonicalReader(w.ToArray()));
        for (int i = 0; i < 100; i++) Assert.Equal(rng.NextUInt(), copy.NextUInt());
    }

    [Fact]
    public void Probe_hash_is_stable_between_runs()
    {
        Assert.Equal(DeterminismProbe.Run(3, 10_000), DeterminismProbe.Run(3, 10_000));
        Assert.NotEqual(DeterminismProbe.Run(3, 10_000), DeterminismProbe.Run(4, 10_000));
    }
}
