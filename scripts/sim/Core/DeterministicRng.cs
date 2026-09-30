namespace LoveAndHonor.Sim.Core;

/// <summary>
/// Seeded, deterministic PRNG (xoshiro256**, seeded via SplitMix64).
/// We own the algorithm so sequences never change across .NET versions or platforms,
/// which keeps saves and bug reports reproducible (§30.2). Not thread-safe: give each
/// system (or each worker partition) its own instance via <see cref="RngStreams"/>.
/// </summary>
public sealed class DeterministicRng
{
    private ulong _s0, _s1, _s2, _s3;

    public DeterministicRng(ulong seed)
    {
        // SplitMix64 expands one 64-bit seed into the 256-bit state; it never yields all-zero state.
        ulong x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    public ulong NextUInt64()
    {
        ulong result = RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);
        return result;
    }

    public uint NextUInt32() => (uint)(NextUInt64() >> 32);

    /// <summary>Uniform integer in [0, maxExclusive). Unbiased (Lemire's method).</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        uint range = (uint)maxExclusive;
        ulong m = (ulong)NextUInt32() * range;
        uint low = (uint)m;
        if (low < range)
        {
            uint threshold = (uint)(-(int)range) % range;
            while (low < threshold)
            {
                m = (ulong)NextUInt32() * range;
                low = (uint)m;
            }
        }
        return (int)(m >> 32);
    }

    /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return minInclusive + NextInt(maxExclusive - minInclusive);
    }

    /// <summary>Uniform double in [0, 1) with 53 bits of precision.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform float in [0, 1) with 24 bits of precision.</summary>
    public float NextFloat() => (NextUInt64() >> 40) * (1.0f / (1U << 24));

    public bool NextBool(double probability) => NextDouble() < probability;

    private static ulong SplitMix64(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));
}
