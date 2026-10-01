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
        ulong h = 14695981039346656037UL;
        h = Mix(h, p.Needs);
        h = Mix(h, p.Happiness);
        h = Mix(h, p.CurrentBuilding);
        h = Mix(h, p.CurrentActivity);
        h = Mix(h, p.WalkFrom);
        h = Mix(h, p.WalkTo);
        h = Mix(h, p.WalkDepartMinute);
        h = Mix(h, p.WalkArriveMinute);
        h = Mix(h, grid.FootTraffic);
        return h;
    }

    private static ulong Mix<T>(ulong h, T[] array) where T : unmanaged
    {
        foreach (byte b in MemoryMarshal.AsBytes(array.AsSpan()))
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        return h;
    }
}
