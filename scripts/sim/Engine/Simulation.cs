using Stopwatch = System.Diagnostics.Stopwatch;
using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Pathing;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Engine;

public readonly record struct TickStats(
    int Tick,
    double ElapsedMs,
    double AgentPhaseMs,
    double TrafficPhaseMs,
    int WalksStarted,
    int LateArrivals,
    long WalkMinutes,
    int RoutesTraced,
    float AverageHappiness);

/// <summary>
/// Runs the hourly tick (§30.2) over the whole population.
///
/// Every tick: location/schedule + needs/happiness for all agents (parallel over fixed-size chunks), then foot
/// traffic. Daily / semester / yearly systems (§30.2 table) are not part of Spike A.
///
/// Determinism: chunk boundaries depend only on the chunk size (not the thread count); per-agent work reads only
/// that agent's data plus read-only tables; chunk results are reduced in chunk order. So 1 thread and N threads
/// produce bit-identical state.
/// </summary>
public sealed class Simulation
{
    private readonly PopulationStore _pop;
    private readonly FlowFieldSet _fields;
    private readonly TileGrid _grid;
    private readonly ScheduleModel _schedule;
    private readonly NeedsModel _needs;
    private readonly float _walkSpeed;
    private readonly float _commutePenalty;
    private readonly float _lateAfterMinutes;
    private readonly int _chunkSize;
    private readonly int _chunkCount;
    private readonly int _threads;
    private readonly ParallelOptions _parallel;
    private readonly Action<int> _processChunk;

    // Per-chunk scratch, reused every tick (no per-tick allocation in the agent loop).
    private readonly int[][] _pairCounts;
    private readonly ChunkStats[] _chunkStats;

    // Values for the tick in progress, read by ProcessChunk.
    private int _tickStartMinute, _day, _weekday, _hour;

    private struct ChunkStats
    {
        public int Walks, Late;
        public long WalkMinutes;
        public double HappinessSum;
    }

    public SimTime Time { get; }
    public TickStats LastTick { get; private set; }
    public PopulationStore Population => _pop;
    public FlowFieldSet Fields => _fields;
    public int Threads => _threads;

    public Simulation(SimData data, Campus campus, FlowFieldSet fields, PopulationStore population, RngStreams rng,
        int? threads = null, int? chunkSize = null)
    {
        _pop = population;
        _fields = fields;
        _grid = campus.Grid;
        _schedule = new ScheduleModel(data.Schedules, rng);
        _needs = new NeedsModel(data.Balance.Needs);
        _walkSpeed = data.Balance.Walking.SpeedMPerMin;
        _commutePenalty = data.Balance.Walking.CommutePenaltyPerMinute;
        _lateAfterMinutes = data.Balance.Time.ClassChangeWindowMinutes;
        _threads = Math.Max(1, threads ?? data.Balance.Performance.SimWorkerThreads);
        _chunkSize = chunkSize ?? data.Balance.Performance.AgentChunkSize;
        _chunkCount = (population.Count + _chunkSize - 1) / _chunkSize;
        _parallel = new ParallelOptions { MaxDegreeOfParallelism = _threads };
        _processChunk = ProcessChunk;
        Time = new SimTime(DateOnly.Parse(data.Spike.StartDate, System.Globalization.CultureInfo.InvariantCulture));

        _fields.EnsureCurrent();
        int pairs = _fields.BuildingCount * _fields.BuildingCount;
        _pairCounts = new int[_chunkCount][];
        for (int c = 0; c < _chunkCount; c++) _pairCounts[c] = new int[pairs];
        _chunkStats = new ChunkStats[_chunkCount];

        for (int a = 0; a < _pop.Count; a++) _pop.Happiness[a] = _needs.Happiness(_pop.Needs, a);
    }

    public void Tick()
    {
        long t0 = Stopwatch.GetTimestamp();
        if (_fields.EnsureCurrent())
            throw new InvalidOperationException("Map changed: pair-count buffers must be resized (not needed in Spike A).");

        _tickStartMinute = Time.TickStartMinute;
        _day = Time.Day;
        _weekday = Time.WeekdayIndex;
        _hour = Time.HourOfDay;

        if (_threads == 1)
            for (int c = 0; c < _chunkCount; c++) ProcessChunk(c);
        else
            Parallel.For(0, _chunkCount, _parallel, _processChunk);

        long t1 = Stopwatch.GetTimestamp();

        // Reduce in chunk order (deterministic).
        int walks = 0, late = 0;
        long walkMinutes = 0;
        double happiness = 0;
        for (int c = 0; c < _chunkCount; c++)
        {
            ref var s = ref _chunkStats[c];
            walks += s.Walks; late += s.Late; walkMinutes += s.WalkMinutes; happiness += s.HappinessSum;
        }

        int routesTraced = AccumulateFootTraffic();
        long t2 = Stopwatch.GetTimestamp();

        LastTick = new TickStats(
            Time.Tick,
            Stopwatch.GetElapsedTime(t0, t2).TotalMilliseconds,
            Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds,
            Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds,
            walks, late, walkMinutes, routesTraced,
            (float)(happiness / _pop.Count));
        Time.Advance();
    }

    private void ProcessChunk(int c)
    {
        var p = _pop;
        int start = c * _chunkSize;
        int end = Math.Min(start + _chunkSize, p.Count);
        int[] pairs = _pairCounts[c];
        int b = _fields.BuildingCount;
        float[] distance = _fields.DistanceM;
        int commute = _needs.CommuteIndex;
        var stats = new ChunkStats();

        for (int a = start; a < end; a++)
        {
            short current = p.CurrentBuilding[a];
            Activity activity;

            if (p.WalkArriveMinute[a] > _tickStartMinute)
            {
                // Still on a long walk that started last hour: keep going.
                activity = p.CurrentActivity[a];
            }
            else
            {
                short target = _schedule.Resolve(p, a, _day, _weekday, _hour, out activity);
                if (target != current)
                {
                    int minutes = (int)MathF.Ceiling(distance[current * b + target] / _walkSpeed);
                    if (minutes < 1) minutes = 1;
                    p.WalkFrom[a] = current;
                    p.WalkTo[a] = target;
                    p.WalkDepartMinute[a] = _tickStartMinute;
                    p.WalkArriveMinute[a] = _tickStartMinute + minutes;
                    p.CurrentBuilding[a] = target;
                    pairs[current * b + target]++;
                    p.Needs[a * PopulationStore.NeedCount + commute] -= minutes * _commutePenalty;
                    stats.Walks++;
                    stats.WalkMinutes += minutes;
                    if ((activity == Activity.Class || activity == Activity.Teach) && minutes > _lateAfterMinutes)
                        stats.Late++;
                }
            }

            p.CurrentActivity[a] = activity;
            float h = _needs.Update(p.Needs, a, activity);
            p.Happiness[a] = h;
            stats.HappinessSum += h;
        }
        _chunkStats[c] = stats;
    }

    /// <summary>
    /// Adds this tick's walkers to foot traffic. Walkers are counted per (from, to) building pair during the
    /// agent phase, so each distinct route is traced once no matter how many agents took it (§12.4 desire paths).
    /// </summary>
    private int AccumulateFootTraffic()
    {
        int b = _fields.BuildingCount;
        int[] traffic = _grid.FootTraffic;
        int traced = 0;
        for (int pair = 0; pair < b * b; pair++)
        {
            int total = 0;
            for (int c = 0; c < _chunkCount; c++)
            {
                total += _pairCounts[c][pair];
                _pairCounts[c][pair] = 0;
            }
            if (total == 0) continue;
            foreach (int tile in _fields.Route(pair / b, pair % b)) traffic[tile] += total;
            traced++;
        }
        return traced;
    }
}
