namespace LoveAndHonor.Sim.Core;

/// <summary>
/// Counter-based randomness: a pure function of (stream seed, agent, day, hour). Used for per-agent decisions
/// inside the parallel tick, so results don't depend on thread count, chunking, or processing order, and no
/// RNG state has to be stored or saved per agent. Stream seeds come from <see cref="RngStreams"/>.
/// </summary>
public static class StatelessRandom
{
    public static ulong Hash(ulong streamSeed, int agent, int day, int salt)
    {
        ulong x = streamSeed ^ ((ulong)(uint)agent << 32 | (uint)day) * 0x9E3779B97F4A7C15UL;
        x ^= (ulong)(uint)salt * 0xC2B2AE3D27D4EB4FUL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    /// <summary>Uniform 16-bit value in [0, 65535] for threshold comparisons.</summary>
    public static int Unit16(ulong streamSeed, int agent, int day, int salt) =>
        (int)(Hash(streamSeed, agent, day, salt) >> 48);
}
