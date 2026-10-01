using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Engine;

/// <summary>
/// What the main (render) thread may read about the sim. Filled on the sim thread, then handed over whole via
/// a triple buffer, so the renderer never reads arrays the sim is writing (§30.2 "main thread reads a snapshot").
/// </summary>
public sealed class SimSnapshot
{
    public int Version;
    public int Tick;
    public DateOnly Date;
    public int HourOfDay;
    public int WeekdayIndex;

    public double LastTickMs, AvgTickMs, P95TickMs, MaxTickMs;
    public int TicksInWindow;
    public float AverageHappiness;
    public int WalksLastTick, LateLastTick;
    public long DroppedTicks;

    /// <summary>Walks that started in the latest tick: agent, route endpoints, and a colour category
    /// (students: year-1 → 0..3, faculty: 4).</summary>
    public int WalkCount;
    public readonly int[] WalkAgent;
    public readonly short[] WalkFrom;
    public readonly short[] WalkTo;
    public readonly byte[] WalkCategory;

    /// <summary>Copy of the foot-traffic grid, refreshed only when requested (it's 4 bytes × tiles). A snapshot
    /// may carry an older copy than one already seen: only use it when TrafficVersion increased.</summary>
    public readonly int[] Traffic;
    /// <summary>Desire-path wear per tile (0..1), copied together with Traffic.</summary>
    public readonly float[] Wear;
    public int TrafficVersion;

    /// <summary>Land layer (Phase 1 1d), copied when it changes: compare LandVersion. Empty for worlds without land.</summary>
    public readonly LandState[] LandStates;
    public readonly Ownership[] Owners;
    public readonly byte[] Clearing;
    public readonly TileType[] Surfaces;
    public int LandVersion = -1;
    public long CashCents;
    public int ClearingTiles;

    /// <summary>Construction sites, entrances and Heritage Projects taken (Phase 1 1e). Immutable; replaced when it changes.</summary>
    public PlacementView? Placement;

    public SimSnapshot(int agentCount, int tileCount)
    {
        WalkAgent = new int[agentCount];
        WalkFrom = new short[agentCount];
        WalkTo = new short[agentCount];
        WalkCategory = new byte[agentCount];
        Traffic = new int[tileCount];
        Wear = new float[tileCount];
        LandStates = new LandState[tileCount];
        Owners = new Ownership[tileCount];
        Clearing = new byte[tileCount];
        Surfaces = new TileType[tileCount];
    }
}

/// <summary>
/// Runs a <see cref="Simulation"/> on its own thread at the game speed (§6.1): at speed s the sim advances
/// s × ticksPerDay / realSecondsPerGameDay ticks per real second. The main thread calls
/// <see cref="AdvanceRealTime"/> each frame and <see cref="AcquireLatest"/> to read the newest snapshot.
/// If the sim can't keep up, backlog beyond a quarter second is dropped (game time slows) and counted.
/// </summary>
public sealed class SimRunner : IDisposable
{
    public const byte FacultyCategory = 4;

    private readonly Simulation _sim;
    private readonly TileGrid _grid;
    private readonly float[] _speeds;
    private readonly double _ticksPerRealSecondAt1x;
    private readonly Thread _thread;
    private readonly AutoResetEvent _signal = new(false);
    private volatile bool _stop;

    // Main-thread state.
    private double _tickDebt;
    private int _speedIndex;

    // Shared counters.
    private int _pendingTicks;
    private long _droppedTicks;
    private int _trafficRequested;
    private int _commandsPending;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

    /// <summary>Main thread: the next message for the player (land orders and their results), if any. Not part of the
    /// snapshot, so none are lost when the renderer skips snapshots.</summary>
    public bool TryTakeMessage(out string message) => _messages.TryDequeue(out message!);

    // Triple buffer: _state holds the index of the "middle" snapshot plus a fresh bit.
    private const int FreshBit = 4;
    private readonly SimSnapshot[] _buffers;
    private int _state;
    private int _backIndex = 1;   // sim thread only
    private int _frontIndex = 2;  // main thread only

    // Tick-time window (sim thread only).
    private readonly double[] _window;
    private readonly double[] _sortScratch;
    private int _windowCount, _windowNext, _version, _trafficVersion;

    public SimRunner(Simulation sim, TileGrid grid, float[] speeds, double realSecondsPerGameDay, int ticksPerGameDay,
        int statsWindowTicks = 120, int startSpeedIndex = 1)
    {
        _sim = sim;
        _grid = grid;
        _speeds = speeds;
        _ticksPerRealSecondAt1x = ticksPerGameDay / realSecondsPerGameDay;
        _speedIndex = Math.Clamp(startSpeedIndex, 0, speeds.Length - 1);
        int agents = sim.Population.Count, tiles = grid.Width * grid.Height;
        _buffers = [new SimSnapshot(agents, tiles), new SimSnapshot(agents, tiles), new SimSnapshot(agents, tiles)];
        _state = 0;
        _window = new double[statsWindowTicks];
        _sortScratch = new double[statsWindowTicks];
        _thread = new Thread(Run) { IsBackground = true, Name = "SimThread" };
    }

    public IReadOnlyList<float> Speeds => _speeds;

    /// <summary>Main thread: how far game time has got toward the next tick (0..1), for smooth clocks and sun.</summary>
    public double TickFraction => _tickDebt;
    public float CurrentSpeed => _speeds[_speedIndex];

    public int SpeedIndex
    {
        get => _speedIndex;
        set => _speedIndex = Math.Clamp(value, 0, _speeds.Length - 1);
    }

    public void Start()
    {
        Publish(); // an initial snapshot so the renderer has something before the first tick
        _thread.Start();
    }

    /// <summary>Main thread: advance game time by <paramref name="realSeconds"/> of wall-clock time at the current speed.</summary>
    public void AdvanceRealTime(double realSeconds)
    {
        double tps = CurrentSpeed * _ticksPerRealSecondAt1x;
        if (tps <= 0) return;
        _tickDebt += realSeconds * tps;
        int due = (int)_tickDebt;
        if (due == 0) return;
        _tickDebt -= due;

        int pending = Interlocked.Add(ref _pendingTicks, due);
        int maxBacklog = Math.Max(2, (int)(tps * 0.25));
        if (pending > maxBacklog)
        {
            int drop = pending - maxBacklog;
            Interlocked.Add(ref _pendingTicks, -drop);
            Interlocked.Add(ref _droppedTicks, drop);
        }
        _signal.Set();
    }

    /// <summary>Main thread: ask for the foot-traffic grid to be included in the next snapshot.</summary>
    public void RequestTraffic() => Interlocked.Exchange(ref _trafficRequested, 1);

    /// <summary>Main thread: a land order (clear / buy). Applied on the sim thread at the next tick boundary, or right
    /// away while paused, so the player can plan with the game stopped.</summary>
    public void Submit(LandCommand command)
    {
        if (_sim.Land is null) return;
        _sim.Land.Enqueue(command);
        Interlocked.Exchange(ref _commandsPending, 1);
        _signal.Set();
    }

    /// <summary>Main thread: a build or cancel order. Applied like land orders (next tick boundary, or at once when paused).</summary>
    public void Submit(PlacementCommand command)
    {
        if (_sim.Placement is null) return;
        _sim.Placement.Enqueue(command);
        Interlocked.Exchange(ref _commandsPending, 1);
        _signal.Set();
    }

    /// <summary>Main thread: the newest published snapshot. Valid until the next call.</summary>
    public SimSnapshot AcquireLatest()
    {
        if ((Volatile.Read(ref _state) & FreshBit) != 0)
            _frontIndex = Interlocked.Exchange(ref _state, _frontIndex) & 3;
        return _buffers[_frontIndex];
    }

    /// <summary>Runs ticks synchronously on the caller's thread (tests / headless tools). Don't mix with Start().</summary>
    public void RunTicksNow(int count)
    {
        for (int i = 0; i < count; i++) TickOnce();
        Publish();
    }

    private void Run()
    {
        while (!_stop)
        {
            _signal.WaitOne(100);
            int n = Interlocked.Exchange(ref _pendingTicks, 0);
            if (n == 0)
            {
                if (Interlocked.Exchange(ref _commandsPending, 0) == 1)
                {
                    _sim.ApplyPendingCommands();
                    Publish();
                }
                continue;
            }
            Interlocked.Exchange(ref _commandsPending, 0); // ticks apply queued commands themselves
            for (int i = 0; i < n && !_stop; i++) TickOnce();
            Publish();
        }
    }

    private void TickOnce()
    {
        _sim.Tick();
        _window[_windowNext] = _sim.LastTick.ElapsedMs;
        _windowNext = (_windowNext + 1) % _window.Length;
        if (_windowCount < _window.Length) _windowCount++;
    }

    private void Publish()
    {
        var s = _buffers[_backIndex];
        var p = _sim.Population;
        var time = _sim.Time;
        var last = _sim.LastTick;

        s.Version = ++_version;
        s.Tick = time.Tick;
        s.Date = time.Date;
        s.HourOfDay = time.HourOfDay;
        s.WeekdayIndex = time.WeekdayIndex;
        s.AverageHappiness = last.AverageHappiness;
        s.WalksLastTick = last.WalksStarted;
        s.LateLastTick = last.LateArrivals;
        s.DroppedTicks = Interlocked.Read(ref _droppedTicks);
        s.LastTickMs = last.ElapsedMs;

        if (_sim.Land is { } land)
        {
            s.CashCents = land.Treasury.Cents;
            s.ClearingTiles = land.ActiveClearingTiles;
            if (s.LandVersion != land.Version + _grid.Version)
            {
                Array.Copy(_grid.LandState, s.LandStates, s.LandStates.Length);
                Array.Copy(_grid.Ownership, s.Owners, s.Owners.Length);
                Array.Copy(land.Clearing, s.Clearing, s.Clearing.Length);
                Array.Copy(_grid.Types, s.Surfaces, s.Surfaces.Length);
                s.LandVersion = land.Version + _grid.Version;
            }
            foreach (var m in land.TakeMessages()) _messages.Enqueue(m);
        }
        if (_sim.Placement is { } placement)
        {
            s.Placement = placement.View();
            foreach (var m in placement.TakeMessages()) _messages.Enqueue(m);
        }

        s.TicksInWindow = _windowCount;
        if (_windowCount > 0)
        {
            Array.Copy(_window, _sortScratch, _windowCount);
            Array.Sort(_sortScratch, 0, _windowCount);
            double sum = 0;
            for (int i = 0; i < _windowCount; i++) sum += _sortScratch[i];
            s.AvgTickMs = sum / _windowCount;
            s.P95TickMs = _sortScratch[Math.Min(_windowCount - 1, (int)Math.Ceiling(0.95 * _windowCount) - 1)];
            s.MaxTickMs = _sortScratch[_windowCount - 1];
        }

        s.WalkCount = _sim.CopyLastTickWalkers(s.WalkAgent);
        for (int i = 0; i < s.WalkCount; i++)
        {
            int a = s.WalkAgent[i];
            s.WalkFrom[i] = p.WalkFrom[a];
            s.WalkTo[i] = p.WalkTo[a];
            s.WalkCategory[i] = p.Kind[a] == AgentKind.Faculty ? FacultyCategory : (byte)Math.Clamp(p.Year[a] - 1, 0, 3);
        }

        if (Interlocked.Exchange(ref _trafficRequested, 0) == 1)
        {
            _sim.SyncFootTraffic();
            Array.Copy(_grid.FootTraffic, s.Traffic, s.Traffic.Length);
            Array.Copy(_grid.Wear, s.Wear, s.Wear.Length);
            s.TrafficVersion = ++_trafficVersion;
        }
        // Otherwise the buffer keeps an older copy; readers compare TrafficVersion and keep the newest they've seen.

        _backIndex = Interlocked.Exchange(ref _state, _backIndex | FreshBit) & 3;
    }

    public void Dispose()
    {
        _stop = true;
        _signal.Set();
        if (_thread.IsAlive) _thread.Join(2000);
        _signal.Dispose();
    }
}
