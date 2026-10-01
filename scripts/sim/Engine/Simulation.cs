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
    float AverageHappiness,
    bool FieldsSwapped,
    double FieldSwapWaitMs);

/// <summary>A change to one tile's walking surface (paths laid or removed). Buildings come with placement (1e).</summary>
public readonly record struct TileEdit(int Tile, TileType Type);

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
    private readonly int _classChangeWindow;
    private readonly int _chunkSize;
    private readonly int _chunkCount;
    private readonly int _threads;
    private readonly ParallelOptions _parallel;
    private readonly Action<int> _processChunk;

    // Per-chunk scratch, reused every tick (no per-tick allocation in the agent loop).
    private readonly ChunkStats[] _chunkStats;
    // Walkers per (from, to) building pair this tick, and which pairs were touched (foot traffic). Two sets: the sim
    // fills one while the traffic work item traces the other.
    private int[] _pairTotals = [];
    private int[] _touchedPairs = [];
    private readonly TrafficWork _traffic;
    // Flow fields in use for the current tick (swapped only between ticks).
    private FlowFieldData _paths = null!;
    // Map edits waiting for their rebuilt flow fields.
    private readonly HashSet<int> _changedTiles = new();
    private readonly int _rebuildLatency;
    private int _rebuildApplyTick = -1;
    // Agents that started a walk this tick, per chunk (read by the renderer via CopyLastTickWalkers).
    private readonly int[][] _chunkWalkers;
    private readonly int[] _chunkWalkerCount;

    // Values for the tick in progress, read by ProcessChunk.
    private int _tickStartMinute, _day, _weekday, _hour;
    private bool _classesHeld;
    private readonly AcademicCalendar _calendar;

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

    /// <summary>The land layer and money (Phase 1 1d), or null for worlds without land actions (benchmarks, Spike A).</summary>
    public LandSystem? Land { get; }

    public Simulation(SimData data, Campus campus, FlowFieldSet fields, PopulationStore population, RngStreams rng,
        int? threads = null, int? chunkSize = null, DateOnly? startDate = null, LandSystem? land = null)
    {
        Land = land;
        _pop = population;
        _fields = fields;
        _grid = campus.Grid;
        _schedule = new ScheduleModel(data.Schedules, rng);
        _calendar = data.Calendar;
        _needs = new NeedsModel(data.Balance.Needs);
        _walkSpeed = data.Balance.Walking.SpeedMPerMin;
        _commutePenalty = data.Balance.Walking.CommutePenaltyPerMinute;
        _classChangeWindow = (int)MathF.Round(data.Balance.Time.ClassChangeWindowMinutes);
        _threads = Math.Max(1, threads ?? data.Balance.Performance.SimWorkerThreads);
        _chunkSize = chunkSize ?? data.Balance.Performance.AgentChunkSize;
        _chunkCount = (population.Count + _chunkSize - 1) / _chunkSize;
        _parallel = new ParallelOptions { MaxDegreeOfParallelism = _threads };
        _processChunk = ProcessChunk;
        Time = new SimTime(startDate ?? DateOnly.Parse(data.Spike.StartDate, System.Globalization.CultureInfo.InvariantCulture));

        _rebuildLatency = Math.Max(1, data.Balance.Performance.PathingRebuildLatencyTicks);
        var wear = data.Balance.Walking.DesirePaths;
        _traffic = new TrafficWork(campus.Grid, wear.MinWalkersPerDay, 1f / wear.DaysToWear, 1f / wear.DaysToRegrow);
        _fields.EnsureCurrent();
        UsePaths(_fields.Current);
        _chunkStats = new ChunkStats[_chunkCount];
        _chunkWalkers = new int[_chunkCount][];
        for (int c = 0; c < _chunkCount; c++) _chunkWalkers[c] = new int[_chunkSize];
        _chunkWalkerCount = new int[_chunkCount];

        for (int a = 0; a < _pop.Count; a++) _pop.Happiness[a] = _needs.Happiness(_pop.Needs, a);
    }

    /// <summary>The tick at which pending map edits get their new flow fields, or -1.</summary>
    public int RebuildApplyTick => _rebuildApplyTick;

    /// <summary>
    /// Changes walking surfaces (Grass ↔ Path) on the sim thread, between ticks. Walkers keep using the current flow
    /// fields; the rebuilt ones (computed in the background) are swapped in exactly pathing_rebuild_latency_ticks
    /// later, waiting if needed, so the outcome never depends on timing.
    /// </summary>
    public void ApplyTileEdits(ReadOnlySpan<TileEdit> edits)
    {
        if (edits.IsEmpty) return;
        foreach (var e in edits)
        {
            if (e.Type is not (TileType.Grass or TileType.Rough or TileType.Path))
                throw new ArgumentException("Only walking-surface edits (Grass/Rough/Path) are supported (buildings come with placement).");
            if (_grid.Types[e.Tile] is not (TileType.Grass or TileType.Rough or TileType.Path) || _grid.Types[e.Tile] == e.Type) continue;
            _grid.Types[e.Tile] = e.Type;
            _grid.MarkChanged();
            _changedTiles.Add(e.Tile);
        }
        if (_changedTiles.Count == 0) return;
        _fields.BeginRebuild(_changedTiles);
        _rebuildApplyTick = Time.Tick + _rebuildLatency;
    }

    private void UsePaths(FlowFieldData data)
    {
        _paths = data;
        int pairs = data.BuildingCount * data.BuildingCount;
        if (_pairTotals.Length != pairs)
        {
            SyncFootTraffic();
            _pairTotals = new int[pairs];
            _touchedPairs = new int[pairs];
            _traffic.Resize(pairs);
        }
    }

    /// <summary>
    /// Waits until foot traffic from earlier ticks has been added to the grid. Call before reading
    /// <see cref="TileGrid.FootTraffic"/> (snapshots, state hashes, tests). Sim thread only.
    /// </summary>
    public void SyncFootTraffic() => _traffic.Wait();

    /// <summary>Milliseconds the last foot-traffic trace took on its worker (it overlaps the idle time between ticks).</summary>
    public double LastTrafficTraceMs => _traffic.LastMs;

    /// <summary>
    /// Applies queued land orders now (sim thread, between ticks; also used while paused). Walking-surface changes go
    /// to the flow fields like any other map edit.
    /// </summary>
    public void ApplyLandCommands()
    {
        if (Land is null || !Land.HasPendingCommands) return;
        var edits = Land.ApplyCommands(Time.Date);
        if (edits.Count > 0) ApplyTileEdits(edits.ToArray());
    }

    public void Tick()
    {
        long t0 = Stopwatch.GetTimestamp();
        if (Land is not null)
        {
            ApplyLandCommands();
            if (Time.HourOfDay == 0)
            {
                var edits = Land.DailyUpdate(Time.Date, Time.Day);
                if (edits.Count > 0) ApplyTileEdits(edits.ToArray());
            }
        }
        bool swapped = false;
        double swapWait = 0;
        if (_rebuildApplyTick >= 0 && Time.Tick >= _rebuildApplyTick)
        {
            swapWait = _fields.CompleteRebuild();
            UsePaths(_fields.Current);
            _changedTiles.Clear();
            _rebuildApplyTick = -1;
            swapped = true;
        }

        _tickStartMinute = Time.TickStartMinute;
        _day = Time.Day;
        _weekday = Time.WeekdayIndex;
        _hour = Time.HourOfDay;
        _classesHeld = _calendar.ClassesHeld(Time.Date);

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
            (float)(happiness / _pop.Count), swapped, swapWait);
        Time.Advance();
    }

    private void ProcessChunk(int c)
    {
        var p = _pop;
        int start = c * _chunkSize;
        int end = Math.Min(start + _chunkSize, p.Count);
        int[] walkers = _chunkWalkers[c];
        int walkerCount = 0;
        int b = _paths.BuildingCount;
        float[] distance = _paths.DistanceM;
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
                short target = _schedule.Resolve(p, a, _day, _weekday, _hour, out activity, _classesHeld);
                if (target != current)
                {
                    int minutes = (int)MathF.Ceiling(distance[current * b + target] / _walkSpeed);
                    if (minutes < 1) minutes = 1;
                    // Classes start on the hour (§12.4 v0.4): people leave early enough to be there on time. The only
                    // limit is a class the hour before, which ends one class-change window before the hour, so only
                    // back-to-back classes with a walk longer than the window make anyone late. Other activities
                    // start when the hour starts.
                    int depart = _tickStartMinute;
                    bool scheduled = activity is Activity.Class or Activity.Teach;
                    if (scheduled)
                    {
                        Activity previous = p.CurrentActivity[a];
                        int earliest = previous is Activity.Class or Activity.Teach ? _tickStartMinute - _classChangeWindow : int.MinValue;
                        depart = Math.Max(Math.Max(_tickStartMinute - minutes, earliest), p.WalkArriveMinute[a]);
                    }
                    p.WalkFrom[a] = current;
                    p.WalkTo[a] = target;
                    p.WalkDepartMinute[a] = depart;
                    p.WalkArriveMinute[a] = depart + minutes;
                    p.CurrentBuilding[a] = target;
                    walkers[walkerCount++] = a;
                    p.Needs[a * PopulationStore.NeedCount + commute] -= minutes * _commutePenalty;
                    stats.Walks++;
                    stats.WalkMinutes += minutes;
                    if (scheduled && depart + minutes > _tickStartMinute)
                        stats.Late++;
                }
            }

            p.CurrentActivity[a] = activity;
            float h = _needs.Update(p.Needs, a, activity);
            p.Happiness[a] = h;
            stats.HappinessSum += h;
        }
        _chunkStats[c] = stats;
        _chunkWalkerCount[c] = walkerCount;
    }

    /// <summary>
    /// Copies the indices of agents that started a walk during the last tick into <paramref name="destination"/>
    /// (in agent order) and returns how many were written. Call from the thread that runs Tick().
    /// </summary>
    public int CopyLastTickWalkers(Span<int> destination)
    {
        int n = 0;
        for (int c = 0; c < _chunkCount && n < destination.Length; c++)
        {
            int count = Math.Min(_chunkWalkerCount[c], destination.Length - n);
            _chunkWalkers[c].AsSpan(0, count).CopyTo(destination[n..]);
            n += count;
        }
        return n;
    }

    /// <summary>
    /// Adds this tick's walkers to foot traffic. Walkers are grouped per (from, to) building pair (in chunk order, so
    /// deterministic), and each distinct route is traced once no matter how many agents took it (§12.4 desire paths).
    /// The tracing itself runs as a thread-pool work item while the sim moves on (usually in the idle time before the
    /// next tick): it only adds to the grid, so the result is the same in any order. Readers call SyncFootTraffic.
    /// </summary>
    private int AccumulateFootTraffic()
    {
        int b = _paths.BuildingCount;
        int[] totals = _pairTotals, touched = _touchedPairs;
        int touchedCount = 0;
        var p = _pop;
        for (int c = 0; c < _chunkCount; c++)
        {
            int[] walkers = _chunkWalkers[c];
            for (int i = 0; i < _chunkWalkerCount[c]; i++)
            {
                int a = walkers[i];
                int pair = p.WalkFrom[a] * b + p.WalkTo[a];
                if (totals[pair]++ == 0) touched[touchedCount++] = pair;
            }
        }
        bool endOfDay = _hour == 23;
        if (touchedCount == 0 && !endOfDay) return 0;
        // Hand this tick's set to the work item and take the one it finished with. At the end of the day the work
        // item also updates desire-path wear (§12.4).
        _traffic.Wait();
        (_pairTotals, _touchedPairs) = _traffic.Start(totals, touched, touchedCount, _paths.Routes, endOfDay);
        return touchedCount;
    }

    /// <summary>Traces grouped walks into the foot-traffic grids on a thread-pool thread, and at midnight updates
    /// desire-path wear. No allocation per tick.</summary>
    private sealed class TrafficWork(TileGrid grid, int minWalkers, float wearPerDay, float regrowPerDay) : IThreadPoolWorkItem
    {
        private bool _endOfDay;
        private readonly ManualResetEventSlim _idle = new(true);
        private int[] _totals = [], _pairs = [];
        private int _count;
        private int[][] _routes = [];
        public volatile float LastMsValue;
        public double LastMs => LastMsValue;

        public void Resize(int pairs)
        {
            _totals = new int[pairs];
            _pairs = new int[pairs];
        }

        public void Wait() => _idle.Wait();

        /// <summary>Starts tracing the given set; returns the (cleared) set it used last time, for the sim to refill.</summary>
        public (int[] totals, int[] pairs) Start(int[] totals, int[] pairs, int count, int[][] routes, bool endOfDay)
        {
            var spare = (_totals, _pairs);
            (_totals, _pairs, _count, _routes, _endOfDay) = (totals, pairs, count, routes, endOfDay);
            _idle.Reset();
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
            return spare;
        }

        public void Execute()
        {
            long t0 = Stopwatch.GetTimestamp();
            int[] totals = _totals, pairs = _pairs;
            int[][] routes = _routes;
            int[] traffic = grid.FootTraffic;
            for (int k = 0; k < _count; k++)
            {
                int pair = pairs[k];
                int total = totals[pair];
                totals[pair] = 0;
                foreach (int tile in routes[pair]) traffic[tile] += total;
            }
            if (_endOfDay) grid.UpdateWear(minWalkers, wearPerDay, regrowPerDay);
            LastMsValue = (float)Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            _idle.Set();
        }
    }
}
