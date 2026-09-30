using System.Diagnostics;
using System.Globalization;
using LoveAndHonor.Sim;
using LoveAndHonor.Sim.Benchmarks;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;

// Spike A headless benchmark (§30.2): full sim tick for the configured population, no renderer.
//
//   dotnet run -c Release --project tests/LoveAndHonor.Sim.Benchmarks [-- options]
//
// Options:
//   --ticks N         measured ticks (default: balance.json performance.benchmark_ticks)
//   --warmup N        warm-up ticks, not measured (default: performance.benchmark_warmup_ticks)
//   --threads N       sim worker threads for the gated run (default: performance.sim_worker_threads)
//   --students N / --faculty N   override population size
//   --quick           only the gated run (skip the comparison runs)
//   --data DIR        data folder (default: repo /data)
//
// Pass/fail: the gated run pins the process to N physical cores. On hybrid CPUs it uses the slower E-cores as a
// conservative stand-in for a mid-range 4-core desktop CPU. Exit code 0 = within budget, 1 = over budget.

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var opts = Args.Parse(args);
var data = SimData.Load(new FileSystemDataSource(opts.DataDir ?? FindRepoData()));
var perf = data.Balance.Performance;
int ticks = opts.Ticks ?? perf.BenchmarkTicks;
int warmup = opts.Warmup ?? perf.BenchmarkWarmupTicks;
int threads = opts.Threads ?? perf.SimWorkerThreads;
int students = opts.Students ?? data.Balance.Population.TargetStudents;
int faculty = opts.Faculty ?? data.Balance.Population.TargetFaculty;

#if DEBUG
Console.WriteLine("WARNING: Debug build. Timings are not representative; run with -c Release.");
#endif

Console.WriteLine("=== Love & Honor — Spike A population benchmark ===");
Console.WriteLine($"{SimInfo.Describe()} | {Environment.OSVersion} | GC server={System.Runtime.GCSettings.IsServerGC}");
Console.WriteLine($"CPU: {CpuName()} | logical processors: {Environment.ProcessorCount}");
Console.WriteLine($"Population: {students:N0} students + {faculty:N0} faculty = {students + faculty:N0} agents");
Console.WriteLine($"Budget: {perf.TickBudgetStatistic} tick time <= {perf.TickBudgetMs} ms | {warmup} warm-up + {ticks} measured ticks");
Console.WriteLine();

var topology = CpuTopology.Read();
var process = Process.GetCurrentProcess();
long allCpus = GetAffinity(process);

var runs = new List<(string name, long? mask, string cores, int threads, bool gated)>();
var slow = CpuTopology.PickCores(topology, threads, fast: false);
var fast = CpuTopology.PickCores(topology, threads, fast: true);
bool hybrid = topology.Select(c => c.EfficiencyClass).Distinct().Count() > 1;

if (hybrid && slow is { } s)
    runs.Add(($"{threads} E-cores (gated)", s.mask, s.description, threads, true));
if (fast is { } f)
    runs.Add(($"{threads} P-cores{(hybrid ? "" : " (gated)")}", f.mask, f.description, threads, !hybrid));
if (runs.Count == 0)
    runs.Add(($"{threads} threads, unpinned (gated)", null, "no pinning (topology unavailable)", threads, true));
if (!opts.Quick)
{
    if (fast is { } f1 && CpuTopology.PickCores(topology, 1, fast: true) is { } one)
        runs.Add(("1 P-core, 1 thread (reference)", one.mask, one.description, 1, false));
    runs.Add(($"all cores, {Environment.ProcessorCount} threads (reference)", null, "all logical CPUs", Environment.ProcessorCount, false));
}
else
{
    runs.RemoveAll(r => !r.gated);
}

bool passed = true;
var summaries = new List<string>();
foreach (var run in runs)
{
    SetAffinity(process, run.mask ?? allCpus);
    var result = Bench(run.name, run.cores, run.threads);
    if (run.gated) passed = result.pass;
    summaries.Add(result.summary);
}
SetAffinity(process, allCpus);

Console.WriteLine("=== Summary ===");
foreach (var line in summaries) Console.WriteLine(line);
Console.WriteLine(passed ? "RESULT: PASS" : "RESULT: FAIL — tick budget missed");
return passed ? 0 : 1;

(bool pass, string summary) Bench(string name, string cores, int runThreads)
{
    Console.WriteLine($"--- {name} ---");
    Console.WriteLine($"Pinned to: {cores}");

    var world = SpikeWorld.Create(data, runThreads, students, faculty);
    var sim = world.Simulation;
    Console.WriteLine($"Setup: campus {world.CampusMs:F0} ms, flow fields {world.FlowFieldsMs:F0} ms " +
                      $"({world.Fields.BuildingCount} buildings), population {world.PopulationMs:F0} ms, " +
                      $"population arrays ~{world.Population.ApproximateBytes() / 1024.0 / 1024.0:F1} MB");

    for (int i = 0; i < warmup; i++) sim.Tick();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var total = new double[ticks];
    var agentPhase = new double[ticks];
    var trafficPhase = new double[ticks];
    long walks = 0, late = 0, routes = 0, walkMinutes = 0;
    int maxWalks = 0, worstTickIndex = 0;
    int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
    long allocBefore = GC.GetTotalAllocatedBytes(precise: true);

    for (int i = 0; i < ticks; i++)
    {
        long t0 = Stopwatch.GetTimestamp();
        sim.Tick();
        total[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        var st = sim.LastTick;
        agentPhase[i] = st.AgentPhaseMs;
        trafficPhase[i] = st.TrafficPhaseMs;
        walks += st.WalksStarted;
        late += st.LateArrivals;
        routes += st.RoutesTraced;
        walkMinutes += st.WalkMinutes;
        if (st.WalksStarted > maxWalks) maxWalks = st.WalksStarted;
        if (total[i] > total[worstTickIndex]) worstTickIndex = i;
    }

    long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;
    int g0 = GC.CollectionCount(0) - gen0, g1 = GC.CollectionCount(1) - gen1, g2 = GC.CollectionCount(2) - gen2;

    var sorted = total.OrderBy(x => x).ToArray();
    double avg = total.Average(), p50 = Pct(sorted, 50), p95 = Pct(sorted, 95), p99 = Pct(sorted, 99), max = sorted[^1];
    double gatedValue = perf.TickBudgetStatistic switch { "avg" => avg, "p99" => p99, "max" => max, _ => p95 };
    bool pass = gatedValue <= perf.TickBudgetMs;

    Console.WriteLine($"Tick time (ms): avg {avg:F3} | p50 {p50:F3} | p95 {p95:F3} | p99 {p99:F3} | max {max:F3}");
    Console.WriteLine($"  agent phase avg {agentPhase.Average():F3} ms (max {agentPhase.Max():F3}) | foot-traffic phase avg {trafficPhase.Average():F3} ms (max {trafficPhase.Max():F3})");
    Console.WriteLine($"  worst tick: #{worstTickIndex + warmup} ({HourLabel(worstTickIndex + warmup)})");
    Console.WriteLine($"Work: {walks / (double)ticks:F0} walks/tick avg (peak {maxWalks:N0}), {routes / (double)ticks:F0} distinct routes traced/tick, " +
                      $"avg walk {walkMinutes / (double)Math.Max(1, walks):F1} min, {late:N0} late-to-class walks (walk > class-change window), " +
                      $"avg happiness {sim.LastTick.AverageHappiness:F1}");
    Console.WriteLine($"GC during measurement: {allocated / 1024.0:F0} KB allocated, collections gen0={g0} gen1={g1} gen2={g2}");
    Console.WriteLine($"Budget ({perf.TickBudgetStatistic} <= {perf.TickBudgetMs} ms): {(pass ? "PASS" : "FAIL")} — {perf.TickBudgetStatistic} = {gatedValue:F3} ms");
    Console.WriteLine();

    return (pass, $"{name,-40} avg {avg,7:F3}  p95 {p95,7:F3}  p99 {p99,7:F3}  max {max,7:F3} ms  {(pass ? "within" : "OVER")} budget");
}

// Affinity is only supported on Windows and Linux; elsewhere the benchmark runs unpinned.
static long GetAffinity(Process p) => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ? p.ProcessorAffinity : 0;

static void SetAffinity(Process p, long mask)
{
    if ((OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && mask != 0) p.ProcessorAffinity = (nint)mask;
}

static double Pct(double[] sorted, double pct)
{
    double rank = pct / 100.0 * (sorted.Length - 1);
    int lo = (int)Math.Floor(rank), hi = (int)Math.Ceiling(rank);
    return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
}

string HourLabel(int tick)
{
    var date = DateOnly.Parse(data.Spike.StartDate, CultureInfo.InvariantCulture).AddDays(tick / 24);
    return $"{date:ddd yyyy-MM-dd} {tick % 24:00}:00";
}

static string CpuName()
{
    if (OperatingSystem.IsWindows())
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        if (key?.GetValue("ProcessorNameString") is string name) return name.Trim();
    }
    return "unknown";
}

static string FindRepoData()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
            return Path.Combine(dir.FullName, "data");
    throw new DirectoryNotFoundException("project.godot not found; pass --data <dir>.");
}

internal sealed record Args(int? Ticks, int? Warmup, int? Threads, int? Students, int? Faculty, bool Quick, string? DataDir)
{
    public static Args Parse(string[] a)
    {
        int? Int(string name) => Array.IndexOf(a, name) is var i and >= 0 && i + 1 < a.Length ? int.Parse(a[i + 1], CultureInfo.InvariantCulture) : null;
        string? Str(string name) => Array.IndexOf(a, name) is var i and >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        return new Args(Int("--ticks"), Int("--warmup"), Int("--threads"), Int("--students"), Int("--faculty"), a.Contains("--quick"), Str("--data"));
    }
}
