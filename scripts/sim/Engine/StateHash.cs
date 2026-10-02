using System.Runtime.InteropServices;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Engine;

/// <summary>
/// 64-bit fingerprint of the mutable sim state. Two runs with the same seed must produce the same hash
/// (determinism tests, and later: attach to bug reports to confirm a save replays identically).
/// </summary>
public static class StateHash
{
    /// <summary>Hash of a running simulation (waits for pending foot-traffic work first).</summary>
    public static ulong Compute(Simulation sim, TileGrid grid)
    {
        sim.SyncFootTraffic();
        return Compute(sim.Population, grid);
    }

    public static ulong Compute(PopulationStore p, TileGrid grid)
    {
        // Live agents only: slots past Count are spare capacity (enrollment, 1h).
        int n = p.Count;
        ulong h = 14695981039346656037UL;
        h = Mix(h, p.Needs.AsSpan(0, n * PopulationStore.NeedCount));
        h = Mix(h, p.Happiness.AsSpan(0, n));
        h = Mix(h, p.CurrentBuilding.AsSpan(0, n));
        h = Mix(h, p.CurrentActivity.AsSpan(0, n));
        h = Mix(h, p.WalkFrom.AsSpan(0, n));
        h = Mix(h, p.WalkTo.AsSpan(0, n));
        h = Mix(h, p.WalkDepartMinute.AsSpan(0, n));
        h = Mix(h, p.WalkArriveMinute.AsSpan(0, n));
        h = Mix(h, grid.FootTraffic.AsSpan());
        h = Mix(h, grid.TrafficAtMidnight.AsSpan());
        h = Mix(h, grid.Wear.AsSpan());
        return h;
    }

    private static ulong Mix<T>(ulong h, Span<T> values) where T : unmanaged
    {
        foreach (byte b in MemoryMarshal.AsBytes(values))
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        return h;
    }
}
