using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.Pathing;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.View;

/// <summary>Axis-aligned ground rectangle in world metres (x = tile x, z = tile y).</summary>
public readonly record struct GroundRect(float MinX, float MinZ, float MaxX, float MaxZ)
{
    public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
    public GroundRect Expand(float m) => new(MinX - m, MinZ - m, MaxX + m, MaxZ + m);
}

/// <summary>
/// The rendered subset of the population (§30.2 "visual" movement level). Engine-agnostic: the Godot bridge feeds
/// it the camera's ground rectangle and the latest sim snapshot, and copies <see cref="WriteInstances"/> into a
/// MultiMesh buffer.
///
/// Walkers are sampled from walks that really started in the latest tick, preferring routes that cross the
/// camera view, and move along the actual flow-field route at a cosmetic speed (the game clock is far too fast
/// to show real walking speed: at 1× one in-game hour lasts 83 ms). Busy routes therefore show more walkers.
/// </summary>
public sealed class VisualCrowd
{
    /// <summary>Floats per MultiMesh instance: 3×4 transform + RGBA colour.</summary>
    public const int FloatsPerInstance = 16;

    private readonly FlowFieldSet _fields;
    private readonly float _tileM;
    private readonly int _gridWidth;
    private readonly float _speedMps;
    private readonly float _lateralSpread;
    private readonly int _attemptsPerSlot;
    private readonly DeterministicRng _rng;

    // Slot arrays; active walkers are kept packed in [0, ActiveCount).
    private readonly int[][] _route;
    private readonly int[] _segment;
    private readonly float[] _t;
    private readonly float[] _lateral;
    private readonly byte[] _category;
    private readonly float[] _x, _z;

    public int Capacity { get; }
    private bool[] _used = [];
    private int _usedTick = -1;
    public int ActiveCount { get; private set; }

    public VisualCrowd(FlowFieldSet fields, TileGrid grid, int capacity, float speedMps, float lateralSpreadM,
        int attemptsPerSlot, ulong seed)
    {
        _fields = fields;
        _tileM = grid.TileSizeM;
        _gridWidth = grid.Width;
        _speedMps = speedMps;
        _lateralSpread = lateralSpreadM;
        _attemptsPerSlot = attemptsPerSlot;
        _rng = new DeterministicRng(seed);
        Capacity = capacity;
        _route = new int[capacity][];
        _segment = new int[capacity];
        _t = new float[capacity];
        _lateral = new float[capacity];
        _category = new byte[capacity];
        _x = new float[capacity];
        _z = new float[capacity];
    }

    /// <param name="dtSeconds">Real seconds since the last update.</param>
    /// <param name="speedMultiplier">Current game speed (0 = paused: nobody moves, nobody spawns).</param>
    /// <param name="view">Ground area visible to the camera.</param>
    /// <param name="viewMarginM">Walkers this far outside the view are recycled.</param>
    public void Update(float dtSeconds, float speedMultiplier, GroundRect view, float viewMarginM, SimSnapshot snapshot)
    {
        if (speedMultiplier <= 0) { RecomputePositions(); return; }

        float step = _speedMps * speedMultiplier * dtSeconds;
        var keep = view.Expand(viewMarginM);
        int i = 0;
        while (i < ActiveCount)
        {
            bool alive = Advance(i, step);
            if (alive)
            {
                Position(i, out _x[i], out _z[i]);
                alive = keep.Contains(_x[i], _z[i]);
            }
            if (alive) i++;
            else RemoveAt(i);
        }

        Spawn(view, snapshot);
    }

    /// <summary>Writes active walkers as MultiMesh instances (row-major 3×4 transform + colour). Returns the count written.</summary>
    /// <param name="ground">Terrain height under a point (render space), or null for flat ground at y = 0.</param>
    public int WriteInstances(Span<float> buffer, float scale, float halfHeightM, ReadOnlySpan<float> categoryRgba,
        Heightmap? ground = null)
    {
        int n = Math.Min(ActiveCount, buffer.Length / FloatsPerInstance);
        float y = halfHeightM * scale;
        for (int i = 0; i < n; i++)
        {
            var b = buffer.Slice(i * FloatsPerInstance, FloatsPerInstance);
            float baseY = ground is null ? 0f : ground.HeightAt(_x[i], _z[i]);
            b[0] = scale; b[1] = 0; b[2] = 0; b[3] = _x[i];
            b[4] = 0; b[5] = scale; b[6] = 0; b[7] = baseY + y;
            b[8] = 0; b[9] = 0; b[10] = scale; b[11] = _z[i];
            int c = _category[i] * 4;
            b[12] = categoryRgba[c]; b[13] = categoryRgba[c + 1]; b[14] = categoryRgba[c + 2]; b[15] = categoryRgba[c + 3];
        }
        return n;
    }

    private void Spawn(GroundRect view, SimSnapshot s)
    {
        // Never more walkers than people (a small early college), and each of this hour's walks drawn at most once.
        int limit = Math.Min(Capacity, s.WalkAgent.Length);
        int free = limit - ActiveCount;
        if (free <= 0 || s.WalkCount == 0) return;
        if (s.Tick != _usedTick)
        {
            _usedTick = s.Tick;
            if (_used.Length < s.WalkCount) _used = new bool[s.WalkAgent.Length];
            else Array.Clear(_used, 0, s.WalkCount);
        }
        int b = _fields.BuildingCount;
        var bounds = _fields.RouteBounds;
        // View rectangle in tile coordinates, for the cheap route-bounds test.
        float inv = 1f / _tileM;
        int vx0 = (int)(view.MinX * inv), vz0 = (int)(view.MinZ * inv), vx1 = (int)(view.MaxX * inv), vz1 = (int)(view.MaxZ * inv);

        int attempts = free * _attemptsPerSlot;
        for (int k = 0; k < attempts && ActiveCount < limit; k++)
        {
            int e = _rng.NextInt(s.WalkCount);
            if (_used[e]) continue;
            int from = s.WalkFrom[e], to = s.WalkTo[e];
            if (from < 0 || to < 0 || from == to) continue;
            int pair = from * b + to;
            var box = bounds[pair];
            if (box.MaxX < vx0 || box.MinX > vx1 || box.MaxY < vz0 || box.MinY > vz1) continue;
            int[] route = _fields.Route(from, to);
            if (route.Length < 2) continue;

            // Start mid-walk at a random point that's on screen: this is a cosmetic sample of a crowd in motion.
            int seg = _rng.NextInt(route.Length - 1);
            int slot = ActiveCount;
            _route[slot] = route;
            _segment[slot] = seg;
            _t[slot] = _rng.NextFloat();
            _lateral[slot] = (_rng.NextFloat() * 2f - 1f) * _lateralSpread;
            _category[slot] = s.WalkCategory[e];
            Position(slot, out _x[slot], out _z[slot]);
            if (!view.Contains(_x[slot], _z[slot])) continue;
            _used[e] = true;
            ActiveCount++;
        }
    }

    private bool Advance(int i, float distance)
    {
        int[] route = _route[i];
        while (distance > 0)
        {
            int seg = _segment[i];
            if (seg >= route.Length - 1) return false;
            float len = StepLength(route[seg], route[seg + 1]);
            float remaining = (1f - _t[i]) * len;
            if (distance < remaining)
            {
                _t[i] += distance / len;
                return true;
            }
            distance -= remaining;
            _segment[i] = seg + 1;
            _t[i] = 0;
        }
        return _segment[i] < route.Length - 1;
    }

    private void Position(int i, out float x, out float z)
    {
        int[] route = _route[i];
        int seg = Math.Min(_segment[i], route.Length - 2);
        int a = route[seg], c = route[seg + 1];
        float ax = (a % _gridWidth + 0.5f) * _tileM, az = (a / _gridWidth + 0.5f) * _tileM;
        float cx = (c % _gridWidth + 0.5f) * _tileM, cz = (c / _gridWidth + 0.5f) * _tileM;
        float t = _t[i];
        float dx = cx - ax, dz = cz - az;
        float len = MathF.Sqrt(dx * dx + dz * dz);
        float px = len > 0 ? -dz / len : 0, pz = len > 0 ? dx / len : 0; // perpendicular: keeps walkers from sharing one line
        x = ax + dx * t + px * _lateral[i];
        z = az + dz * t + pz * _lateral[i];
    }

    private void RecomputePositions()
    {
        for (int i = 0; i < ActiveCount; i++) Position(i, out _x[i], out _z[i]);
    }

    private float StepLength(int a, int c) =>
        (a % _gridWidth != c % _gridWidth && a / _gridWidth != c / _gridWidth ? 1.41421356f : 1f) * _tileM;

    private void RemoveAt(int i)
    {
        int last = --ActiveCount;
        _route[i] = _route[last]; _segment[i] = _segment[last]; _t[i] = _t[last];
        _lateral[i] = _lateral[last]; _category[i] = _category[last]; _x[i] = _x[last]; _z[i] = _z[last];
        _route[last] = null!;
    }
}
