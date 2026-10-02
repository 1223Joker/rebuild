namespace Rebuild.Sim.MapGen;

/// <summary>
/// Integer hash-based value noise (ADR 0003): Squirrel3 lattice hash (Eiserloh, GDC 2017,
/// https://www.gdcvault.com/play/1024365/Math-for-Game-Programmers-Noise), integer smoothstep interpolation,
/// fBm octaves summed in <c>int</c> (https://www.redblobgames.com/maps/terrain-from-noise/).
/// All values are 16-bit: 0..65535.
/// </summary>
public static class IntNoise
{
    public const int One = 1 << 16;

    /// <summary>Squirrel3 position hash.</summary>
    public static uint Squirrel3(int position, uint seed)
    {
        const uint B1 = 0xB5297A4D, B2 = 0x68E31DA4, B3 = 0x1B56C4E9; // BIT_NOISE1..3 of the reference
        uint m = (uint)position;
        m *= B1;
        m += seed;
        m ^= m >> 8;
        m += B2;
        m ^= m << 8;
        m *= B3;
        m ^= m >> 8;
        return m;
    }

    public static uint Hash2(int x, int y, uint seed) => Squirrel3(x + 198491317 * y, seed);

    /// <summary>3t² − 2t³ for t in [0, 65536), result in [0, 65536).</summary>
    public static int SmoothStep(int t)
    {
        long t2 = ((long)t * t) >> 16;
        return (int)((t2 * (3L * One - 2L * t)) >> 16);
    }

    private static int Lerp(int a, int b, int t) => a + (int)(((long)(b - a) * t) >> 16);

    /// <summary>Value noise with lattice spacing 2^<paramref name="cellShift"/> tiles, 0..65535.</summary>
    public static int Value(int x, int y, int cellShift, uint seed)
    {
        int cx = x >> cellShift, cy = y >> cellShift;
        int mask = (1 << cellShift) - 1;
        int sx = SmoothStep(((x & mask) << 16) >> cellShift);
        int sy = SmoothStep(((y & mask) << 16) >> cellShift);
        int v00 = (int)(Hash2(cx, cy, seed) >> 16);
        int v10 = (int)(Hash2(cx + 1, cy, seed) >> 16);
        int v01 = (int)(Hash2(cx, cy + 1, seed) >> 16);
        int v11 = (int)(Hash2(cx + 1, cy + 1, seed) >> 16);
        return Lerp(Lerp(v00, v10, sx), Lerp(v01, v11, sx), sy);
    }

    /// <summary>
    /// fBm: <paramref name="octaves"/> octaves from spacing 2^<paramref name="baseShift"/> down, each half the
    /// spacing and half the weight of the previous one; normalised to 0..65535.
    /// </summary>
    public static int Fbm(int x, int y, int baseShift, int octaves, uint seed)
    {
        long sum = 0, weights = 0;
        for (int o = 0; o < octaves; o++)
        {
            long weight = 1L << (octaves - 1 - o);
            sum += Value(x, y, baseShift - o, seed + (uint)o * 0x9E3779B9u) * weight;
            weights += weight;
        }
        return (int)(sum / weights);
    }
}

/// <summary>Integer sine/cosine (Bhaskara I approximation, max error ≈ 0.0016); angles in 1/65536 turns, results Q16.</summary>
public static class IntTrig
{
    public const int FullTurn = 1 << 16;

    public static int Sin(int angle)
    {
        const long half = FullTurn / 2;
        long a = angle & (FullTurn - 1);
        bool negative = a >= half;
        if (negative) a -= half;
        long p = a * (half - a);
        long s = (16 * p << 16) / (5 * half * half - 4 * p);
        return (int)(negative ? -s : s);
    }

    public static int Cos(int angle) => Sin(angle + FullTurn / 4);
}
