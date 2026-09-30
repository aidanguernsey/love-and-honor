using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Pathing;

/// <summary>
/// Flow fields (§30.2): one per destination building, shared by every agent heading there. Each field stores,
/// for every tile, the direction of the next step toward that building's entrance (8-connected Dijkstra; grass
/// costs more than paths). Built once, rebuilt only when the grid's Version changes.
///
/// Derived caches, built at the same time:
///  - DistanceM[from, to]: walking distance in metres between building entrances (an O(1) lookup per walk).
///  - Routes[from, to]: the tile sequence for each building pair, used for foot traffic and (Spike A 2b) for
///    moving rendered agents. With B buildings this is B² short arrays; fine for ~40-200 buildings.
/// Memory per field is one byte per tile (160 KB for 400×400).
/// </summary>
public sealed class FlowFieldSet
{
    public const byte NoDirection = 255;

    // E, NE, N, NW, W, SW, S, SE. Opposite direction = (d + 4) & 7. "N" is -y (row 0 is the top).
    public static readonly int[] Dx = [1, 1, 0, -1, -1, -1, 0, 1];
    public static readonly int[] Dy = [0, -1, -1, -1, 0, 1, 1, 1];
    private const float Diagonal = 1.41421356f;

    private readonly Campus _campus;
    private readonly float _grassCost;
    private readonly int _threads;
    private byte[][] _directions = [];
    private int[][] _routes = [];

    public int BuildingCount { get; private set; }
    public float[] DistanceM { get; private set; } = [];
    public int BuiltForVersion { get; private set; } = -1;
    public double LastBuildMs { get; private set; }

    public FlowFieldSet(Campus campus, float grassCostMultiplier, int threads)
    {
        _campus = campus;
        _grassCost = grassCostMultiplier;
        _threads = Math.Max(1, threads);
    }

    public bool IsStale => BuiltForVersion != _campus.Grid.Version;

    public byte[] DirectionsTo(int building) => _directions[building];
    public float Distance(int from, int to) => DistanceM[from * BuildingCount + to];
    /// <summary>Tile indices from the entrance of <paramref name="from"/> to the entrance of <paramref name="to"/>, inclusive.</summary>
    public int[] Route(int from, int to) => _routes[from * BuildingCount + to];

    /// <summary>Rebuilds all fields if the map changed since the last build. Returns true if it rebuilt.</summary>
    public bool EnsureCurrent()
    {
        if (!IsStale) return false;
        Build();
        return true;
    }

    public void Build()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var grid = _campus.Grid;
        int b = _campus.Buildings.Count;
        int tiles = grid.Width * grid.Height;
        var directions = new byte[b][];
        var distance = new float[b * b];
        var options = new ParallelOptions { MaxDegreeOfParallelism = _threads };

        Parallel.For(0, b, options,
            () => new Scratch(tiles),
            (to, _, scratch) =>
            {
                var dir = new byte[tiles];
                BuildField(grid, _campus.Buildings[to].EntranceTile, dir, scratch);
                directions[to] = dir;
                float tileM = grid.TileSizeM;
                for (int from = 0; from < b; from++)
                    distance[from * b + to] = scratch.Length[_campus.Buildings[from].EntranceTile] * tileM;
                return scratch;
            },
            _ => { });

        var routes = new int[b * b][];
        Parallel.For(0, b * b, options, pair =>
        {
            int from = pair / b, to = pair % b;
            routes[pair] = Trace(grid, directions[to], _campus.Buildings[from].EntranceTile, _campus.Buildings[to].EntranceTile);
        });

        _directions = directions;
        _routes = routes;
        DistanceM = distance;
        BuildingCount = b;
        BuiltForVersion = grid.Version;
        LastBuildMs = sw.Elapsed.TotalMilliseconds;
    }

    private sealed class Scratch(int tiles)
    {
        public readonly float[] Cost = new float[tiles];
        public readonly float[] Length = new float[tiles];
        public readonly PriorityQueue<int, float> Queue = new(4096);
    }

    private float TileCost(TileType t) => t == TileType.Grass ? _grassCost : 1f;

    private void BuildField(TileGrid grid, int target, byte[] dir, Scratch s)
    {
        Array.Fill(s.Cost, float.PositiveInfinity);
        Array.Fill(s.Length, float.PositiveInfinity);
        Array.Fill(dir, NoDirection);
        s.Queue.Clear();
        s.Cost[target] = 0;
        s.Length[target] = 0;
        s.Queue.Enqueue(target, 0);
        int w = grid.Width;
        var types = grid.Types;

        while (s.Queue.TryDequeue(out int t, out float c))
        {
            if (c > s.Cost[t]) continue;
            int tx = t % w, ty = t / w;
            float tCost = TileCost(types[t]);
            for (int d = 0; d < 8; d++)
            {
                int nx = tx + Dx[d], ny = ty + Dy[d];
                if (!grid.InBounds(nx, ny)) continue;
                int n = ny * w + nx;
                if (types[n] == TileType.Building) continue;
                bool diagonal = (d & 1) == 1;
                // No corner-cutting past buildings.
                if (diagonal && (types[ty * w + nx] == TileType.Building || types[ny * w + tx] == TileType.Building)) continue;
                float step = diagonal ? Diagonal : 1f;
                float nc = c + step * 0.5f * (tCost + TileCost(types[n]));
                if (nc < s.Cost[n])
                {
                    s.Cost[n] = nc;
                    s.Length[n] = s.Length[t] + step;
                    dir[n] = (byte)((d + 4) & 7); // from n, step back toward t
                    s.Queue.Enqueue(n, nc);
                }
            }
        }
    }

    private static int[] Trace(TileGrid grid, byte[] dir, int start, int target)
    {
        if (start == target) return [start];
        var route = new List<int>(128) { start };
        int w = grid.Width, t = start, guard = grid.Width * grid.Height;
        while (t != target && guard-- > 0)
        {
            byte d = dir[t];
            if (d == NoDirection) return []; // unreachable
            t = (t / w + Dy[d]) * w + (t % w + Dx[d]);
            route.Add(t);
        }
        return t == target ? route.ToArray() : [];
    }
}
