using System.Runtime.InteropServices;

namespace LoveAndHonor.Sim.Benchmarks;

/// <summary>
/// Reads logical-processor → physical core / efficiency class from Windows, so the benchmark can pin itself
/// to N distinct physical cores of a chosen type (Intel hybrid CPUs mix fast P-cores and slower E-cores).
/// </summary>
internal static class CpuTopology
{
    public readonly record struct LogicalCpu(int Index, int Core, int EfficiencyClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemCpuSetInformation(IntPtr information, uint bufferLength, out uint returnedLength, IntPtr process, uint flags);

    public static List<LogicalCpu> Read()
    {
        var result = new List<LogicalCpu>();
        if (!OperatingSystem.IsWindows()) return result;

        GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint needed, IntPtr.Zero, 0);
        if (needed == 0) return result;
        IntPtr buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!GetSystemCpuSetInformation(buffer, needed, out uint returned, IntPtr.Zero, 0)) return result;
            int offset = 0;
            while (offset < returned)
            {
                // SYSTEM_CPU_SET_INFORMATION: Size(4) Type(4) Id(4) Group(2) LogicalProcessorIndex(1) CoreIndex(1)
                // LastLevelCacheIndex(1) NumaNodeIndex(1) EfficiencyClass(1) ...
                int size = Marshal.ReadInt32(buffer, offset);
                int type = Marshal.ReadInt32(buffer, offset + 4);
                if (type == 0) // CpuSetInformation
                {
                    int group = Marshal.ReadInt16(buffer, offset + 12);
                    int logical = Marshal.ReadByte(buffer, offset + 14);
                    int core = Marshal.ReadByte(buffer, offset + 15);
                    int efficiency = Marshal.ReadByte(buffer, offset + 18);
                    if (group == 0) result.Add(new LogicalCpu(logical, core, efficiency));
                }
                if (size <= 0) break;
                offset += size;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    /// <summary>
    /// Affinity mask covering <paramref name="count"/> distinct physical cores (one logical CPU each) from the
    /// requested class: "fast" = highest efficiency class (P-cores), "slow" = lowest (E-cores). Null if unavailable.
    /// </summary>
    public static (long mask, string description)? PickCores(List<LogicalCpu> cpus, int count, bool fast)
    {
        if (cpus.Count == 0) return null;
        int cls = fast ? cpus.Max(c => c.EfficiencyClass) : cpus.Min(c => c.EfficiencyClass);
        var picked = cpus.Where(c => c.EfficiencyClass == cls)
            .GroupBy(c => c.Core).Select(g => g.OrderBy(c => c.Index).First())
            .OrderBy(c => c.Index).Take(count).ToList();
        if (picked.Count < count || picked.Any(c => c.Index >= 63)) return null;
        long mask = picked.Aggregate(0L, (m, c) => m | (1L << c.Index));
        return (mask, $"logical CPUs {string.Join(",", picked.Select(c => c.Index))} (efficiency class {cls})");
    }
}
