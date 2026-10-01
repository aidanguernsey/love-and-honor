using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Sim.World;

/// <summary>data/real_campus.json: how the simulation's campus is derived from the real map and the timeline.</summary>
public sealed class RealCampusConfig
{
    /// <summary>timeline.json kind → sim building kind (academic, residence, dining, library, recreation).
    /// Kinds not listed aren't destinations (they still block walking).</summary>
    public Dictionary<string, string> KindMap { get; init; } = [];
    /// <summary>Unpaved tiles in these land states are lawn; other unpaved land is rough ground (fields, woods).</summary>
    public string[] LawnLandStates { get; init; } = [];
    public int EntranceSearchRadiusTiles { get; init; }
    public int PreferPathWithinTiles { get; init; }
    public HousingSection Housing { get; init; } = new();

    public sealed class HousingSection
    {
        public int ZoneSizeTiles { get; init; }
        public float MaxDistanceM { get; init; }
        public int MinBuildingTiles { get; init; }
    }

    public const string File = "real_campus.json";

    public static RealCampusConfig Load(IDataSource source) => SimJson.Parse<RealCampusConfig>(source.ReadText(File), File);
}

/// <summary>What <see cref="RealCampusBuilder"/> did, for logs and the benchmark.</summary>
public sealed record RealCampusReport(
    int Year,
    int CampusBuildings,
    int HousingZones,
    int HousingBuildingTiles,
    IReadOnlyDictionary<BuildingKind, int> ByKind,
    IReadOnlyList<string> SkippedNoFootprint,
    IReadOnlyList<string> SkippedUnreachable);

/// <summary>
/// Builds the sim's <see cref="Campus"/> on the real Oxford map for a given year (Phase 1, 1a):
///  - Miami buildings = timeline.json entries standing that year whose kind maps to a destination kind, placed on
///    their OSM footprint (or approximate site). Each gets its footprint tiles and an entrance: the nearest walkable
///    tile in the main walkable network, preferring a path tile close to the building.
///  - Off-campus housing = zones of a coarse grid over the town's other buildings (not university-owned, within
///    max_distance_m of campus), weighted by how many building tiles they hold. One zone stands for many houses.
/// Present-day only for now: town housing comes from today's OSM buildings (the 1824 start state comes in 1d).
/// </summary>
public static class RealCampusBuilder
{
    public static Campus Build(RealMap map, IReadOnlyList<FeatureBuilding> features, TimelineData timeline, RealCampusConfig cfg,
        int year, int presentYear, out RealCampusReport report)
    {
        var grid = map.Grid;
        int w = grid.Width, h = grid.Height, tiles = w * h;
        var byOsm = features.ToDictionary(f => f.OsmId);

        // Walking surface: lawn where the land is town/campus, rough ground (fields, pasture, woods) elsewhere.
        var lawn = cfg.LawnLandStates.Select(s => Enum.Parse<LandState>(s, ignoreCase: true)).ToHashSet();
        for (int t = 0; t < tiles; t++)
            if (grid.Types[t] == TileType.Grass && !lawn.Contains(grid.LandState[t])) grid.Types[t] = TileType.Rough;
        grid.MarkChanged();

        var component = MainComponent(grid);

        var buildings = new List<CampusBuilding>();
        var noFootprint = new List<string>();
        var unreachable = new List<string>();

        foreach (var e in timeline.Entries.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            if (!cfg.KindMap.TryGetValue(e.Kind, out var kindName) || !e.StandsIn(year, presentYear)) continue;
            var kind = Enum.Parse<BuildingKind>(kindName, ignoreCase: true);
            string? osm = e.OsmId ?? e.ApproxSiteOsmId;
            if (osm is null || !byOsm.TryGetValue(osm, out var footprint))
            {
                noFootprint.Add(e.Name);
                continue;
            }
            var cells = FootprintTiles(grid, footprint);
            int entrance = FindEntrance(grid, component, cells, cfg);
            if (entrance < 0)
            {
                unreachable.Add(e.Name);
                continue;
            }
            var index = (short)buildings.Count;
            foreach (int t in cells)
                if (grid.Types[t] == TileType.Building && grid.BuildingAt[t] < 0) grid.BuildingAt[t] = index;
            buildings.Add(Make(index, e.Id, kind, cells, w, entrance, 1f));
        }

        // Campus centre = mean of the Miami buildings' entrances (for the housing radius).
        double cx = buildings.Count > 0 ? buildings.Average(b => b.EntranceTile % w) : w / 2.0;
        double cy = buildings.Count > 0 ? buildings.Average(b => b.EntranceTile / w) : h / 2.0;

        // Off-campus housing zones.
        int zone = Math.Max(1, cfg.Housing.ZoneSizeTiles);
        int zonesX = (w + zone - 1) / zone, zonesY = (h + zone - 1) / zone;
        var count = new int[zonesX * zonesY];
        var sumX = new long[zonesX * zonesY];
        var sumY = new long[zonesX * zonesY];
        float maxTiles = cfg.Housing.MaxDistanceM / grid.TileSizeM;
        int housingTiles = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int t = y * w + x;
                if (grid.Types[t] != TileType.Building || grid.BuildingAt[t] >= 0) continue;
                if (grid.Ownership[t] == Ownership.University) continue;
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > maxTiles * maxTiles) continue;
                int z = (y / zone) * zonesX + x / zone;
                count[z]++; sumX[z] += x; sumY[z] += y;
            }
        int zones = 0;
        for (int z = 0; z < count.Length; z++)
        {
            if (count[z] < cfg.Housing.MinBuildingTiles) continue;
            int mx = (int)(sumX[z] / count[z]), my = (int)(sumY[z] / count[z]);
            int entrance = FindEntrance(grid, component, [my * w + mx], cfg);
            if (entrance < 0) continue;
            var index = (short)buildings.Count;
            buildings.Add(Make(index, $"housing_zone_{z % zonesX}_{z / zonesX}", BuildingKind.OffCampusHousing, [my * w + mx], w, entrance, count[z]));
            housingTiles += count[z];
            zones++;
        }

        report = new RealCampusReport(year, buildings.Count - zones, zones, housingTiles,
            buildings.GroupBy(b => b.Kind).ToDictionary(g => g.Key, g => g.Count()), noFootprint, unreachable);
        return new Campus(grid, buildings);
    }

    private static CampusBuilding Make(short index, string id, BuildingKind kind, IReadOnlyList<int> cells, int w, int entrance, float weight)
    {
        int minX = cells.Min(t => t % w), maxX = cells.Max(t => t % w), minY = cells.Min(t => t / w), maxY = cells.Max(t => t / w);
        return new CampusBuilding
        {
            Index = index, DefId = id, Kind = kind,
            X = minX, Y = minY, W = maxX - minX + 1, H = maxY - minY + 1,
            EntranceTile = entrance, Weight = weight,
        };
    }

    /// <summary>Tiles whose centre lies inside the outline; the centroid's tile if the building is smaller than a tile.</summary>
    internal static List<int> FootprintTiles(TileGrid grid, FeatureBuilding f)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < f.Outline.Length; i += 2)
        {
            minX = MathF.Min(minX, f.Outline[i]); maxX = MathF.Max(maxX, f.Outline[i]);
            minY = MathF.Min(minY, f.Outline[i + 1]); maxY = MathF.Max(maxY, f.Outline[i + 1]);
        }
        var cells = new List<int>();
        for (int y = Math.Max(0, (int)minY); y <= Math.Min(grid.Height - 1, (int)maxY); y++)
            for (int x = Math.Max(0, (int)minX); x <= Math.Min(grid.Width - 1, (int)maxX); x++)
                if (f.Contains(x + 0.5f, y + 0.5f)) cells.Add(grid.Index(x, y));
        if (cells.Count == 0)
        {
            var (px, py) = f.Centroid();
            int x = Math.Clamp((int)px, 0, grid.Width - 1), y = Math.Clamp((int)py, 0, grid.Height - 1);
            cells.Add(grid.Index(x, y));
        }
        return cells;
    }

    /// <summary>
    /// Breadth-first search outward from the footprint (8-neighbour rings): the first path tile within
    /// prefer_path_within_tiles wins, otherwise the first walkable tile; only tiles in the main walkable network count.
    /// </summary>
    internal static int FindEntrance(TileGrid grid, bool[] mainComponent, IReadOnlyList<int> footprint, RealCampusConfig cfg)
    {
        int w = grid.Width;
        var dist = new Dictionary<int, int>();
        var queue = new Queue<int>();
        foreach (int t in footprint) { dist[t] = 0; queue.Enqueue(t); }
        int firstWalkable = -1, firstWalkableDist = int.MaxValue;
        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            int d = dist[t];
            if (d > cfg.EntranceSearchRadiusTiles) break;
            if (d > 0 && mainComponent[t])
            {
                if (grid.Types[t] == TileType.Path && d <= cfg.PreferPathWithinTiles) return t;
                if (firstWalkable < 0) { firstWalkable = t; firstWalkableDist = d; }
            }
            if (firstWalkable >= 0 && d > Math.Max(firstWalkableDist, cfg.PreferPathWithinTiles)) break;
            int x = t % w, y = t / w;
            for (int k = 0; k < 8; k++)
            {
                int nx = x + Pathing.FlowFieldSet.Dx[k], ny = y + Pathing.FlowFieldSet.Dy[k];
                if (!grid.InBounds(nx, ny)) continue;
                int n = ny * w + nx;
                if (dist.ContainsKey(n)) continue;
                dist[n] = d + 1;
                queue.Enqueue(n);
            }
        }
        return firstWalkable;
    }

    /// <summary>The largest 4-connected set of walkable tiles. With no corner-cutting, 4-connected = reachable.</summary>
    internal static bool[] MainComponent(TileGrid grid)
    {
        int tiles = grid.Width * grid.Height, w = grid.Width;
        var label = new int[tiles];
        int best = 0, bestSize = 0, next = 0;
        var stack = new Stack<int>();
        for (int s = 0; s < tiles; s++)
        {
            if (label[s] != 0 || !grid.IsWalkable(s)) continue;
            int id = ++next, size = 0;
            label[s] = id;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int t = stack.Pop();
                size++;
                int x = t % w, y = t / w;
                if (x > 0) Visit(t - 1); if (x < w - 1) Visit(t + 1);
                if (y > 0) Visit(t - w); if (y < grid.Height - 1) Visit(t + w);
            }
            if (size > bestSize) { bestSize = size; best = id; }

            void Visit(int n)
            {
                if (label[n] == 0 && grid.IsWalkable(n)) { label[n] = id; stack.Push(n); }
            }
        }
        var main = new bool[tiles];
        for (int t = 0; t < tiles; t++) main[t] = label[t] == best && best != 0;
        return main;
    }
}
