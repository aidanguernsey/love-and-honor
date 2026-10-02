using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Sim.World;

/// <summary>data/paths.json: path surfaces by era, routing, and the Slant Walk rule (§11.8, §12.4).</summary>
public sealed class PathConfig
{
    public Surface[] Surfaces { get; init; } = [];
    public RouteSection Route { get; init; } = new();
    public SlantWalkSection SlantWalk { get; init; } = new();

    public sealed class Surface
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string UnlockEra { get; init; } = "";
        public double CostPerTile { get; init; }
        public string Color { get; init; } = "";
        public float WidthM { get; init; }
    }

    public sealed class RouteSection
    {
        public float ExistingPathCost { get; init; } = 0.3f;
        public int SearchMarginTiles { get; init; } = 15;
        public int MaxTiles { get; init; } = 300;
    }

    public sealed class SlantWalkSection
    {
        public float MinLengthM { get; init; }
        public float MaxAngleFromDiagonalDeg { get; init; }
        public double HeritageBonus { get; init; }
    }

    public const string File = "paths.json";

    public static PathConfig Load(IDataSource source) => SimJson.Parse<PathConfig>(source.ReadText(File), File);

    /// <summary>Index into <see cref="Surfaces"/> of the surface new paths get in <paramref name="year"/>: the last one in
    /// the list (the order of preference) that is unlocked.</summary>
    public int SurfaceFor(EraTable eras, int year)
    {
        int era = eras.IndexAt(year), best = 0;
        for (int i = 0; i < Surfaces.Length; i++)
        {
            int unlock = eras.IndexOf(Surfaces[i].UnlockEra);
            if (unlock >= 0 && unlock <= era) best = i;
        }
        return best;
    }
}

/// <summary>The map layers path planning reads: the sim's live arrays or the UI's snapshot copies.</summary>
public sealed record PathMap(int Width, int Height, float TileSizeM, TileType[] Types, LandState[] States, Ownership[] Owners,
    bool[] Protected, byte[] Clearing, float[] Wear, PathType[] PathTypes);

/// <summary>
/// Path planning (Phase 1 1g), pure and deterministic, shared by the sim and the UI preview:
///  - <see cref="Route"/>: the cheapest route between two tiles over tiles a path may be laid on (university land, not
///    forest, water, protected land, buildings or land being cleared), joining existing paths cheaply;
///  - <see cref="DesireRegion"/>: the connected desire path (fully worn lawn, §12.4) under a tile, for paving;
///  - <see cref="SlantWalk"/>: whether a paved desire path is the long diagonal that becomes the Slant Walk.
/// </summary>
public static class PathPlanner
{
    private static readonly int[] Dx = Pathing.FlowFieldSet.Dx, Dy = Pathing.FlowFieldSet.Dy;

    /// <summary>A new path may be laid here.</summary>
    public static bool CanLay(PathMap m, int t) =>
        m.Owners[t] == Ownership.University && m.Types[t] is TileType.Grass or TileType.Rough
        && m.States[t] is not (LandState.Forest or LandState.Water or LandState.Protected) && !m.Protected[t] && m.Clearing[t] == 0;

    /// <summary>Why a path can't be laid on a tile (for messages).</summary>
    public static string WhyNot(PathMap m, int t) =>
        m.Types[t] == TileType.Building ? "a building is in the way"
        : m.Types[t] == TileType.Water || m.States[t] == LandState.Water ? "water is in the way"
        : m.Owners[t] != Ownership.University ? "that isn't university land (buy it first, L)"
        : m.States[t] == LandState.Forest ? "that's forest (clear it first, C)"
        : m.Clearing[t] != 0 ? "that land is still being cleared"
        : "protected land";

    /// <summary>
    /// Cheapest route from <paramref name="from"/> to <paramref name="to"/> (both included), or null with a reason.
    /// Steps cost 1 (√2 diagonally) over new tiles and existing_path_cost over existing paths. Search stays within the
    /// two ends' bounding box plus a margin. Ties break by tile index, so the result is deterministic.
    /// </summary>
    public static List<int>? Route(PathConfig cfg, PathMap m, int from, int to, out string problem)
    {
        problem = "";
        bool Open(int t) => m.Types[t] == TileType.Path || CanLay(m, t);
        if (!Open(from)) { problem = $"Can't start a path there: {WhyNot(m, from)}."; return null; }
        if (!Open(to)) { problem = $"Can't end a path there: {WhyNot(m, to)}."; return null; }
        int w = m.Width, margin = cfg.Route.SearchMarginTiles;
        int x0 = Math.Max(0, Math.Min(from % w, to % w) - margin), x1 = Math.Min(w - 1, Math.Max(from % w, to % w) + margin);
        int y0 = Math.Max(0, Math.Min(from / w, to / w) - margin), y1 = Math.Min(m.Height - 1, Math.Max(from / w, to / w) + margin);
        float pathCost = cfg.Route.ExistingPathCost;
        var g = new Dictionary<int, float> { [from] = 0 };
        var parent = new Dictionary<int, int>();
        var open = new PriorityQueue<int, (float F, int T)>();
        int tx = to % w, ty = to / w;
        float H(int t)
        {
            int dx = Math.Abs(t % w - tx), dy = Math.Abs(t / w - ty);
            return (Math.Max(dx, dy) + 0.41421356f * Math.Min(dx, dy)) * pathCost;
        }
        open.Enqueue(from, (H(from), from));
        var closed = new HashSet<int>();
        while (open.TryDequeue(out int t, out _))
        {
            if (!closed.Add(t)) continue;
            if (t == to) break;
            int x = t % w, y = t / w;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Dx[d], ny = y + Dy[d];
                if (nx < x0 || nx > x1 || ny < y0 || ny > y1) continue;
                int n = ny * w + nx;
                if (closed.Contains(n) || !Open(n)) continue;
                if ((d & 1) == 1 && (!Open(y * w + nx) || !Open(ny * w + x))) continue; // no corner cutting
                float step = (d & 1) == 1 ? 1.41421356f : 1f;
                float ng = g[t] + step * (m.Types[n] == TileType.Path ? pathCost : 1f);
                if (g.TryGetValue(n, out float old) && old <= ng) continue;
                g[n] = ng;
                parent[n] = t;
                open.Enqueue(n, (ng + H(n), n));
            }
        }
        if (!closed.Contains(to)) { problem = "No route: something blocks the way (buildings, water, forest or land the university doesn't own)."; return null; }
        var route = new List<int> { to };
        for (int t = to; t != from; t = parent[t]) route.Add(parent[t]);
        route.Reverse();
        if (route.Count(t => m.Types[t] != TileType.Path) > cfg.Route.MaxTiles) { problem = $"Too long: at most {cfg.Route.MaxTiles} new tiles at once."; return null; }
        return route;
    }

    /// <summary>The connected (8-neighbour) desire path under <paramref name="start"/>: unpaved tiles worn to dirt.</summary>
    public static List<int> DesireRegion(PathMap m, int start)
    {
        bool Worn(int t) => m.Wear[t] >= TileGrid.DesirePathWear && m.Types[t] is TileType.Grass or TileType.Rough;
        var region = new List<int>();
        if (!Worn(start)) return region;
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        int w = m.Width;
        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            region.Add(t);
            for (int d = 0; d < 8; d++)
            {
                int nx = t % w + Dx[d], ny = t / w + Dy[d];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)m.Height) continue;
                int n = ny * w + nx;
                if (Worn(n) && seen.Add(n)) queue.Enqueue(n);
            }
        }
        region.Sort();
        return region;
    }

    /// <summary>
    /// Length (metres, along its main direction) and angle (degrees from the nearest map diagonal) of a set of tiles,
    /// and whether that makes it the Slant Walk (§12.4).
    /// </summary>
    public static (bool Qualifies, float LengthM, float AngleFromDiagonalDeg) SlantWalk(PathConfig cfg, IReadOnlyList<int> tiles, int width, float tileM)
    {
        if (tiles.Count < 2) return (false, 0, 90);
        double mx = tiles.Average(t => (double)(t % width)), my = tiles.Average(t => (double)(t / width));
        double sxx = 0, syy = 0, sxy = 0;
        foreach (int t in tiles)
        {
            double dx = t % width - mx, dy = t / width - my;
            sxx += dx * dx; syy += dy * dy; sxy += dx * dy;
        }
        double angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy); // main axis
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (int t in tiles)
        {
            double p = (t % width - mx) * cos + (t / width - my) * sin;
            lo = Math.Min(lo, p); hi = Math.Max(hi, p);
        }
        float length = (float)(hi - lo + 1) * tileM;
        double deg = ((angle * 180 / Math.PI) % 180 + 180) % 180;
        float fromDiagonal = (float)Math.Min(Math.Abs(deg - 45), Math.Abs(deg - 135));
        return (length >= cfg.SlantWalk.MinLengthM && fromDiagonal <= cfg.SlantWalk.MaxAngleFromDiagonalDeg, length, fromDiagonal);
    }

    /// <summary>Path tiles in a rectangle the player may remove: footpaths on university land (not town roads).</summary>
    public static List<int> Removable(PathMap m, int ax, int ay, int bx, int by)
    {
        var list = new List<int>();
        for (int y = Math.Max(0, Math.Min(ay, by)); y <= Math.Min(m.Height - 1, Math.Max(ay, by)); y++)
            for (int x = Math.Max(0, Math.Min(ax, bx)); x <= Math.Min(m.Width - 1, Math.Max(ax, bx)); x++)
            {
                int t = y * m.Width + x;
                if (m.Types[t] == TileType.Path && m.Owners[t] == Ownership.University
                    && m.PathTypes[t] is PathType.None or PathType.Footway or PathType.Cycleway) list.Add(t);
            }
        return list;
    }
}
