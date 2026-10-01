using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Pathing;

/// <summary>
/// The published, read-only result of a flow-field build: distances and routes between building entrances.
/// The sim swaps a whole new instance in when the map changes, so readers (sim thread, renderer) never see a
/// half-updated set.
/// </summary>
public sealed class FlowFieldData
{
    public required int BuildingCount { get; init; }
    public required int[] Entrances { get; init; }
    public required bool[] HasField { get; init; }
    /// <summary>Walking distance in metres between entrances, [from * B + to]. +∞ if unreachable or unsupported.</summary>
    public required float[] DistanceM { get; init; }
    /// <summary>Tile indices from the entrance of from to the entrance of to, inclusive; [from * B + to].</summary>
    public required int[][] Routes { get; init; }
    public required FlowFieldSet.RouteBox[] RouteBounds { get; init; }
    public required int GridVersion { get; init; }
    public required double BuildMs { get; init; }
    /// <summary>Fields computed from scratch, updated incrementally (and changed), and left untouched.</summary>
    public required int FieldsBuilt { get; init; }
    public required int FieldsUpdated { get; init; }
    public required int FieldsUnchanged { get; init; }
    /// <summary>Tiles whose cost changed, summed over fields (incremental updates only).</summary>
    public required long TilesRecomputed { get; init; }
    /// <summary>The tile types this set was built for (the baseline for the next incremental update).</summary>
    internal TileType[] TileTypes { get; init; } = [];
}

/// <summary>
/// Flow fields (§30.2): one per destination building, shared by every agent heading there. For every tile a field
/// stores the walking cost to the building's entrance and the direction of the next step (8-connected, no
/// corner-cutting past buildings or water, grass costs more than paths).
///
/// Phase 1 (1a):
///  - Costs are integers (<see cref="CostScale"/> per tile of path). A full build is Dijkstra with a bucket queue
///    (Dial's algorithm). Directions are then chosen by a fixed rule from the costs (the first of E, NE, N, … that
///    lies on a shortest path). Shortest-path costs are unique, so fields are identical whichever way they were
///    computed — which lets map edits be applied incrementally without ever diverging from a full rebuild.
///  - Map edits update fields incrementally: tiles whose shortest path ran through a changed tile are invalidated,
///    then Dijkstra re-settles them and spreads any improvement outward. Usually a few hundred tiles, not 160,000.
///  - Rebuilds run on background threads (<see cref="BeginRebuild"/>) into a spare buffer; the caller swaps them
///    in with <see cref="CompleteRebuild"/> (the Simulation does it at a fixed tick, so results are deterministic).
///  - Distances and routes are symmetric, so buildings without a field (off-campus housing zones) get routes by
///    tracing the other building's field and reversing it.
/// Memory: 3 bytes per tile per field, twice (current + spare): about 0.9 MB per field on the 400×400 map.
/// </summary>
public sealed class FlowFieldSet
{
    public const byte NoDirection = 255;
    /// <summary>Cost units per tile of straight path walking. Costs are stored as ushort, so the costliest route must
    /// stay below 65,535 units (~1,600 grass tiles); tiles beyond that count as unreachable.</summary>
    public const int CostScale = 30;
    public const ushort Unreached = ushort.MaxValue;

    // E, NE, N, NW, W, SW, S, SE. Opposite direction = (d + 4) & 7. "N" is -y (row 0 is the top).
    public static readonly int[] Dx = [1, 1, 0, -1, -1, -1, 0, 1];
    public static readonly int[] Dy = [0, -1, -1, -1, 0, 1, 1, 1];
    private const float Diagonal = 1.41421356f;
    private const int BucketCount = 256; // ring size for the bucket queue; must exceed the largest edge cost

    private const int Types = 5; // TileType values

    private readonly Campus _campus;
    private readonly WalkCosts _costs;
    private readonly int _threads;
    private readonly int _backgroundThreads;
    private readonly int[] _edgeCost; // [(typeA * Types + typeB) * 2 + diagonal]

    private sealed class Buffer
    {
        public ushort[]?[] Cost = [];
        public byte[]?[] Dir = [];
    }

    private Buffer _currentBuf = new(), _spareBuf = new();
    // Per-thread working memory, reused across builds so background rebuilds allocate almost nothing.
    private readonly System.Collections.Concurrent.ConcurrentBag<Scratch> _scratchPool = new();
    private volatile FlowFieldData? _current;
    private Rebuild? _pending;

    public readonly record struct RouteBox(short MinX, short MinY, short MaxX, short MaxY);

    /// <summary>
    /// Walking costs (§12.4, balance.json "walking"): lawn and rough ground (fields, woods) cost more per metre than a
    /// path, and every step between a path and an unpaved tile adds half of <paramref name="PathExitPenaltyM"/>, so
    /// leaving a path and rejoining it costs the whole penalty: small corner cuts aren't worth it, long diagonals are.
    /// The same both ways, so routes and distances stay symmetric.
    /// </summary>
    public readonly record struct WalkCosts(float Grass, float Rough, float PathExitPenaltyM);

    public FlowFieldSet(Campus campus, float grassCostMultiplier, int threads, int backgroundThreads = 1)
        : this(campus, new WalkCosts(grassCostMultiplier, grassCostMultiplier, 0), threads, backgroundThreads) { }

    public FlowFieldSet(Campus campus, WalkCosts costs, int threads, int backgroundThreads = 1)
    {
        _campus = campus;
        _costs = costs;
        _threads = Math.Max(1, threads);
        _backgroundThreads = Math.Max(1, backgroundThreads);
        int halfPenalty = (int)MathF.Round(0.5f * CostScale * costs.PathExitPenaltyM / campus.Grid.TileSizeM);
        _edgeCost = new int[Types * Types * 2];
        for (int a = 0; a < Types; a++)
            for (int b = 0; b < Types; b++)
                for (int diag = 0; diag < 2; diag++)
                {
                    float avg = 0.5f * (TileCost((TileType)a) + TileCost((TileType)b));
                    int cost = (int)MathF.Round(CostScale * avg * (diag == 1 ? Diagonal : 1f));
                    bool aPath = (TileType)a == TileType.Path, bPath = (TileType)b == TileType.Path;
                    if (aPath != bPath && IsSurface((TileType)a) && IsSurface((TileType)b)) cost += halfPenalty;
                    if (cost >= BucketCount) throw new ArgumentOutOfRangeException(nameof(costs), "Walking costs too high for the bucket queue.");
                    _edgeCost[(a * Types + b) * 2 + diag] = cost;
                }
    }

    private static bool IsSurface(TileType t) => t is TileType.Grass or TileType.Rough or TileType.Path;

    /// <summary>The fields in use. Replaced as a whole on rebuild; safe to read from any thread.</summary>
    public FlowFieldData Current => _current ?? throw new InvalidOperationException("Flow fields not built yet.");

    public int BuildingCount => Current.BuildingCount;
    public float[] DistanceM => Current.DistanceM;
    public RouteBox[] RouteBounds => Current.RouteBounds;
    public int BuiltForVersion => _current?.GridVersion ?? -1;
    public double LastBuildMs => _current?.BuildMs ?? 0;
    public bool IsStale => BuiltForVersion != _campus.Grid.Version;
    public bool RebuildPending => _pending is not null;

    public bool HasField(int building) => Current.HasField[building];
    public float Distance(int from, int to) => Current.DistanceM[from * Current.BuildingCount + to];
    /// <summary>Tile indices from the entrance of <paramref name="from"/> to the entrance of <paramref name="to"/>, inclusive.</summary>
    public int[] Route(int from, int to) => Current.Routes[from * Current.BuildingCount + to];

    /// <summary>Directions toward a building's entrance, from the fields in use. Sim thread only (the buffer is
    /// recycled two rebuilds later).</summary>
    public byte[] DirectionsTo(int building) => _currentBuf.Dir[building] ?? throw new InvalidOperationException($"Building {building} has no flow field.");
    /// <summary>Costs to a building's entrance (<see cref="Unreached"/> if unreachable). Sim thread only.</summary>
    public ushort[] CostsTo(int building) => _currentBuf.Cost[building] ?? throw new InvalidOperationException($"Building {building} has no flow field.");

    /// <summary>Synchronous full build on the calling thread(s). Cancels any pending rebuild.</summary>
    public void Build()
    {
        CancelPending();
        var result = Compute(Snapshot(), null, _spareBuf, null, _threads, CancellationToken.None);
        (_currentBuf, _spareBuf) = (_spareBuf, _currentBuf);
        _current = result;
    }

    /// <summary>Rebuilds synchronously if the map changed since the last build. Returns true if it rebuilt.</summary>
    public bool EnsureCurrent()
    {
        if (!IsStale && _current is not null) return false;
        Build();
        return true;
    }

    /// <summary>
    /// Starts updating the fields in the background for the map as it is now. <paramref name="changedTiles"/> are
    /// the tiles whose type changed since <see cref="Current"/> was built (null = unknown, rebuild everything). A newer
    /// request supersedes an unfinished one, so pass the union of all changes since <see cref="Current"/>.
    /// </summary>
    public void BeginRebuild(IReadOnlyCollection<int>? changedTiles)
    {
        CancelPending();
        var input = Snapshot();
        var baseData = _current;
        var baseBuf = _currentBuf;
        var target = _spareBuf;
        var changed = changedTiles?.ToArray();
        var rebuild = new Rebuild();
        var worker = new Thread(() =>
        {
            try { rebuild.Result = Compute(input, baseData is null ? null : (baseData, baseBuf), target, changed, _backgroundThreads, rebuild.Cancel.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { rebuild.Error = ex; }
            finally { rebuild.Done.Set(); }
        })
        { IsBackground = true, Name = "FlowFieldRebuild", Priority = ThreadPriority.BelowNormal };
        _pending = rebuild;
        worker.Start();
    }

    /// <summary>
    /// Swaps in the pending rebuild, waiting for it if it isn't finished. Returns the milliseconds spent waiting
    /// (0 if it was ready), or -1 if nothing was pending.
    /// </summary>
    public double CompleteRebuild()
    {
        var p = _pending;
        if (p is null) return -1;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        p.Done.Wait();
        double waited = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        _pending = null;
        p.Done.Dispose();
        if (p.Error is not null) throw new InvalidOperationException("Flow-field rebuild failed.", p.Error);
        (_currentBuf, _spareBuf) = (_spareBuf, _currentBuf);
        _current = p.Result!;
        return waited;
    }

    private void CancelPending()
    {
        var p = _pending;
        if (p is null) return;
        p.Cancel.Cancel();
        p.Done.Wait();
        p.Done.Dispose();
        _pending = null;
    }

    private sealed class Rebuild
    {
        public readonly ManualResetEventSlim Done = new(false);
        public readonly CancellationTokenSource Cancel = new();
        public FlowFieldData? Result;
        public Exception? Error;
    }

    // ---------------------------------------------------------------- computation (any thread)

    private sealed record Input(TileType[] Types, int Width, int Height, float TileSizeM, int[] Entrances, bool[] WantsField, int Version);

    private Input Snapshot()
    {
        var grid = _campus.Grid;
        var buildings = _campus.Buildings;
        return new Input((TileType[])grid.Types.Clone(), grid.Width, grid.Height, grid.TileSizeM,
            buildings.Select(b => b.EntranceTile).ToArray(),
            buildings.Select(b => b.Kind != BuildingKind.OffCampusHousing).ToArray(),
            grid.Version);
    }

    private FlowFieldData Compute(Input input, (FlowFieldData Data, Buffer Buf)? baseline, Buffer target, int[]? changed,
        int threads, CancellationToken cancel)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int b = input.Entrances.Length, w = input.Width, tiles = w * input.Height;
        bool incremental = baseline is { } bl && changed is not null && bl.Data.BuildingCount == b
                           && bl.Data.Entrances.AsSpan().SequenceEqual(input.Entrances)
                           && bl.Data.HasField.AsSpan().SequenceEqual(input.WantsField);

        if (target.Cost.Length != b) { target.Cost = new ushort[]?[b]; target.Dir = new byte[]?[b]; }
        // 0 = no field, 1 = built from scratch, 2 = updated (changed), 3 = updated (no change)
        var outcome = new byte[b];
        var recomputed = new long[b];
        var fields = Enumerable.Range(0, b).Where(k => input.WantsField[k]).ToArray();

        var options = new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = cancel };
        Parallel.ForEach(fields, options,
            () => RentScratch(tiles),
            (k, _, scratch) =>
            {
                var cost = target.Cost[k] ??= new ushort[tiles];
                var dir = target.Dir[k] ??= new byte[tiles];
                if (incremental)
                {
                    Array.Copy(baseline!.Value.Buf.Cost[k]!, cost, tiles);
                    Array.Copy(baseline.Value.Buf.Dir[k]!, dir, tiles);
                    long n = UpdateField(input, baseline.Value.Data.TileTypes, input.Entrances[k], changed!, cost, dir, scratch, out bool dirsChanged);
                    recomputed[k] = n;
                    outcome[k] = (byte)(n > 0 || dirsChanged ? 2 : 3);
                }
                else
                {
                    BuildField(input, input.Entrances[k], cost, scratch);
                    CanonicalDirections(input, cost, dir, input.Entrances[k]);
                    outcome[k] = 1;
                }
                return scratch;
            },
            _scratchPool.Add);
        for (int k = 0; k < b; k++)
            if (!input.WantsField[k]) { target.Cost[k] = null; target.Dir[k] = null; }

        // Routes (re-traced only where the field changed; identical ones keep the old array) and distances from them.
        var routes = new int[b * b][];
        var distance = new float[b * b];
        var oldData = incremental ? baseline!.Value.Data : null;
        Parallel.For(0, b, options,
            () => RentScratch(tiles),
            (from, _, scratch) =>
            {
                for (int to = 0; to < b; to++)
                {
                    int pair = from * b + to;
                    int[]? old = oldData?.Routes[pair];
                    int[] route;
                    if (input.WantsField[to])
                        route = oldData is not null && outcome[to] == 3 ? old!
                            : TraceInto(input, target.Dir[to]!, input.Entrances[from], input.Entrances[to], scratch, old, reverse: false);
                    else if (input.WantsField[from])
                        route = oldData is not null && outcome[from] == 3 ? old!
                            : TraceInto(input, target.Dir[from]!, input.Entrances[to], input.Entrances[from], scratch, old, reverse: true);
                    else
                        route = from == to ? old ?? [input.Entrances[from]] : []; // nobody walks between two housing zones
                    routes[pair] = route;
                    distance[pair] = oldData is not null && ReferenceEquals(route, old) ? oldData.DistanceM[pair] : RouteLength(route, w, input.TileSizeM, from == to);
                }
                return scratch;
            },
            _scratchPool.Add);

        var bounds = new RouteBox[b * b];
        for (int pair = 0; pair < routes.Length; pair++)
        {
            int[] route = routes[pair];
            if (route.Length == 0) continue;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (int t in route)
            {
                int x = t % w, y = t / w;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            bounds[pair] = new RouteBox((short)minX, (short)minY, (short)maxX, (short)maxY);
        }

        return new FlowFieldData
        {
            BuildingCount = b, Entrances = input.Entrances, HasField = input.WantsField,
            DistanceM = distance, Routes = routes, RouteBounds = bounds, GridVersion = input.Version,
            BuildMs = sw.Elapsed.TotalMilliseconds,
            FieldsBuilt = outcome.Count(o => o == 1), FieldsUpdated = outcome.Count(o => o == 2), FieldsUnchanged = outcome.Count(o => o == 3),
            TilesRecomputed = recomputed.Sum(),
            TileTypes = input.Types,
        };
    }

    private sealed class Scratch(int tiles)
    {
        // Bucket queue: one growable int stack per cost value modulo BucketCount.
        public readonly int[][] Buckets = Enumerable.Range(0, BucketCount).Select(_ => new int[64]).ToArray();
        public readonly int[] BucketSize = new int[BucketCount];
        // Incremental updates.
        public readonly PriorityQueue<int, int> Heap = new(1024);
        public readonly int[] Mark = new int[tiles];
        public int Stamp;
        public readonly List<int> Invalid = new(1024);
        public readonly List<int> Touched = new(1024);
        public readonly Queue<int> Queue = new(1024);
        public readonly Dictionary<int, ushort> OldCost = new(1024);
        public int[] Route = new int[1024];

        public void Push(int bucket, int tile)
        {
            ref int[] items = ref Buckets[bucket];
            int n = BucketSize[bucket];
            if (n == items.Length) Array.Resize(ref items, n * 2);
            items[n] = tile;
            BucketSize[bucket] = n + 1;
        }

        public int NextStamp()
        {
            if (++Stamp == int.MaxValue) { Array.Clear(Mark); Stamp = 1; }
            return Stamp;
        }
    }

    private float TileCost(TileType t) => t switch { TileType.Grass => _costs.Grass, TileType.Rough => _costs.Rough, _ => 1f };

    private static bool Walkable(TileType t) => t is not (TileType.Building or TileType.Water);

    /// <summary>Edge cost of stepping from tile a to its neighbour in direction d, or -1 if that step isn't allowed.</summary>
    private int StepCost(TileType[] types, int w, int h, int a, int d)
    {
        int ay = a / w, ax = a - ay * w;
        int nx = ax + Dx[d], ny = ay + Dy[d];
        if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) return -1;
        int n = ny * w + nx;
        var nType = types[n];
        if (!Walkable(nType) || !Walkable(types[a])) return -1;
        int diag = d & 1;
        if (diag == 1 && (!Walkable(types[ay * w + nx]) || !Walkable(types[ny * w + ax]))) return -1;
        return _edgeCost[((int)types[a] * Types + (int)nType) * 2 + diag];
    }

    /// <summary>Full Dijkstra (Dial's algorithm) from the target entrance over the whole walkable map.</summary>
    private void BuildField(Input input, int target, ushort[] cost, Scratch s)
    {
        int w = input.Width, h = input.Height;
        var types = input.Types;
        Array.Fill(cost, Unreached);
        Array.Clear(s.BucketSize);
        if (!Walkable(types[target])) return;

        cost[target] = 0;
        s.Push(0, target);
        int queued = 1, current = 0;
        int[] edge = _edgeCost;
        int[] bucketSize = s.BucketSize;
        ReadOnlySpan<int> dxs = Dx, dys = Dy;

        while (queued > 0)
        {
            int slot = current & (BucketCount - 1);
            int size = bucketSize[slot];
            if (size == 0) { current++; continue; }
            int t = s.Buckets[slot][size - 1];
            bucketSize[slot] = size - 1;
            queued--;
            if (cost[t] != current) continue; // stale entry (improved later)

            int ty = t / w, tx = t - ty * w;
            int tRow = (int)types[t] * Types;
            for (int d = 0; d < 8; d++)
            {
                int nx = tx + dxs[d], ny = ty + dys[d];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int n = ny * w + nx;
                var nType = types[n];
                if (!Walkable(nType)) continue;
                int diag = d & 1;
                // No corner-cutting past buildings or water.
                if (diag == 1 && (!Walkable(types[ty * w + nx]) || !Walkable(types[n - dxs[d]]))) continue;
                int nc = current + edge[(tRow + (int)nType) * 2 + diag];
                if (nc < cost[n])
                {
                    cost[n] = (ushort)nc; // nc < cost[n] <= 65535
                    s.Push(nc & (BucketCount - 1), n);
                    queued++;
                }
            }
        }
    }

    /// <summary>Direction rule: the first d (E, NE, N, …) whose neighbour lies on a shortest path to the target.</summary>
    private byte CanonicalDirection(Input input, ushort[] cost, int t, int target)
    {
        if (t == target || cost[t] == Unreached) return NoDirection;
        int w = input.Width;
        for (int d = 0; d < 8; d++)
        {
            int c = StepCost(input.Types, w, input.Height, t, d);
            if (c < 0) continue;
            int n = t + Dy[d] * w + Dx[d];
            if (cost[n] != Unreached && cost[n] + c == cost[t]) return (byte)d;
        }
        return NoDirection; // unreachable in practice: a finite cost always has a predecessor
    }

    private void CanonicalDirections(Input input, ushort[] cost, byte[] dir, int target)
    {
        for (int t = 0; t < cost.Length; t++) dir[t] = CanonicalDirection(input, cost, t, target);
    }

    /// <summary>
    /// Incremental update after the tiles in <paramref name="changed"/> changed type. <paramref name="cost"/>/<paramref name="dir"/>
    /// hold the old field and are updated in place. Returns how many tiles' costs changed.
    /// </summary>
    private long UpdateField(Input input, TileType[] oldTypes, int target, int[] changed, ushort[] cost, byte[] dir, Scratch s, out bool dirsChanged)
    {
        int w = input.Width, h = input.Height;
        var types = input.Types;
        s.Invalid.Clear();
        s.Touched.Clear();
        s.Heap.Clear();
        s.Queue.Clear();

        // 1. Invalidate every tile whose shortest path ran through a changed tile, i.e. its subtree in the
        //    shortest-path tree. If walkability changed, the neighbours' subtrees too (corner-cutting rules).
        int inA = s.NextStamp();
        void Seed(int t)
        {
            if (s.Mark[t] == inA) return;
            s.Mark[t] = inA;
            s.Queue.Enqueue(t);
        }
        foreach (int c in changed)
        {
            Seed(c);
            if (Walkable(oldTypes[c]) == Walkable(types[c])) continue;
            int cy = c / w, cx = c - cy * w;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + Dx[d], ny = cy + Dy[d];
                if ((uint)nx < (uint)w && (uint)ny < (uint)h) Seed(ny * w + nx);
            }
        }
        while (s.Queue.Count > 0)
        {
            int u = s.Queue.Dequeue();
            s.Invalid.Add(u);
            int uy = u / w, ux = u - uy * w;
            for (int d = 0; d < 8; d++) // children: neighbours whose next step is u
            {
                int vx = ux + Dx[d], vy = uy + Dy[d];
                if ((uint)vx >= (uint)w || (uint)vy >= (uint)h) continue;
                int v = vy * w + vx;
                byte dv = dir[v];
                if (dv != NoDirection && dv == ((d + 4) & 7) && s.Mark[v] != inA)
                {
                    s.Mark[v] = inA;
                    s.Queue.Enqueue(v);
                }
            }
        }

        var oldCost = s.OldCost;
        oldCost.Clear();
        foreach (int u in s.Invalid)
        {
            if (u == target) continue;
            oldCost[u] = cost[u];
            cost[u] = Unreached;
        }

        // 2. Re-seed invalidated tiles from their valid neighbours, then Dijkstra: settles them and spreads any
        //    improvement (a new path) to tiles outside the invalidated set.
        foreach (int u in s.Invalid)
        {
            if (u == target || !Walkable(types[u])) continue;
            int best = Unreached;
            for (int d = 0; d < 8; d++)
            {
                int c = StepCost(types, w, h, u, d);
                if (c < 0) continue;
                int n = u + Dy[d] * w + Dx[d];
                if (cost[n] != Unreached && cost[n] + c < best) best = cost[n] + c;
            }
            if (best < cost[u])
            {
                cost[u] = (ushort)best;
                s.Heap.Enqueue(u, best);
            }
        }
        while (s.Heap.TryDequeue(out int t, out int ct))
        {
            if (ct != cost[t]) continue;
            for (int d = 0; d < 8; d++)
            {
                int c = StepCost(types, w, h, t, d);
                if (c < 0) continue;
                int n = t + Dy[d] * w + Dx[d];
                int nc = ct + c;
                if (nc < cost[n])
                {
                    if (!oldCost.ContainsKey(n)) oldCost[n] = cost[n];
                    cost[n] = (ushort)nc;
                    s.Heap.Enqueue(n, nc);
                }
            }
        }

        // 3. Directions: recompute for every tile whose cost changed, the changed tiles, and their neighbours.
        long costChanges = 0;
        int inD = s.NextStamp();
        bool anyDirChanged = false;
        void Redo(int t)
        {
            if (s.Mark[t] == inD) return;
            s.Mark[t] = inD;
            byte nd = CanonicalDirection(input, cost, t, target);
            if (nd != dir[t]) { dir[t] = nd; anyDirChanged = true; }
        }
        void RedoAround(int t)
        {
            Redo(t);
            int ty = t / w, tx = t - ty * w;
            for (int d = 0; d < 8; d++)
            {
                int nx = tx + Dx[d], ny = ty + Dy[d];
                if ((uint)nx < (uint)w && (uint)ny < (uint)h) Redo(ny * w + nx);
            }
        }
        foreach (var (t, before) in oldCost)
        {
            if (cost[t] == before) continue;
            costChanges++;
            RedoAround(t);
        }
        foreach (int c in changed) RedoAround(c);
        dirsChanged = anyDirChanged;
        return costChanges;
    }

    private Scratch RentScratch(int tiles) =>
        _scratchPool.TryTake(out var s) && s.Mark.Length == tiles ? s : new Scratch(tiles);

    /// <summary>
    /// Follows the field from start to target into the scratch buffer (reversed if asked). Returns
    /// <paramref name="old"/> when the route is unchanged, so rebuilds allocate only for routes that really moved.
    /// </summary>
    private static int[] TraceInto(Input input, byte[] dir, int start, int target, Scratch s, int[]? old, bool reverse)
    {
        int w = input.Width, n = 0, t = start, guard = w * input.Height;
        void Add(int tile)
        {
            if (n == s.Route.Length) Array.Resize(ref s.Route, n * 2);
            s.Route[n++] = tile;
        }
        Add(start);
        while (t != target && guard-- > 0)
        {
            byte d = dir[t];
            if (d == NoDirection) return []; // unreachable
            t = (t / w + Dy[d]) * w + (t % w + Dx[d]);
            Add(t);
        }
        if (t != target) return [];
        var span = s.Route.AsSpan(0, n);
        if (reverse) span.Reverse();
        return old is not null && span.SequenceEqual(old) ? old : span.ToArray();
    }

    private static float RouteLength(int[] route, int w, float tileM, bool self)
    {
        if (route.Length == 0) return self ? 0 : float.PositiveInfinity;
        float len = 0;
        for (int i = 1; i < route.Length; i++)
            len += route[i] % w != route[i - 1] % w && route[i] / w != route[i - 1] / w ? Diagonal : 1f;
        return len * tileM;
    }
}
