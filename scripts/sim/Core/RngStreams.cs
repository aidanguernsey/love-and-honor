namespace LoveAndHonor.Sim.Core;

/// <summary>
/// Derives an independent, reproducible RNG per simulation system from one master seed.
/// Adding a new system never shifts the sequence of an existing one, because each stream's
/// seed depends only on (masterSeed, systemName, index), not on creation order.
/// </summary>
public sealed class RngStreams
{
    public ulong MasterSeed { get; }

    public RngStreams(ulong masterSeed) => MasterSeed = masterSeed;

    /// <summary>RNG for a named system, e.g. "population", "admissions", "weather".</summary>
    public DeterministicRng For(string systemName) => For(systemName, 0);

    /// <summary>
    /// RNG for a named system and sub-stream index (e.g. one per worker partition), so
    /// parallel work stays deterministic regardless of thread scheduling.
    /// </summary>
    public DeterministicRng For(string systemName, int index)
    {
        ulong h = StableHash(systemName);
        ulong seed = MasterSeed ^ h ^ ((ulong)(uint)index * 0x9E3779B97F4A7C15UL);
        return new DeterministicRng(seed);
    }

    /// <summary>FNV-1a 64-bit. string.GetHashCode is randomized per process and must never be used for seeding.</summary>
    public static ulong StableHash(string s)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in s)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
