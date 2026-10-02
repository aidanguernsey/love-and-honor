using System.Collections.Concurrent;
using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Sim.World;

/// <summary>data/placement.json: rotation, slope, path access, construction times and Heritage Project rules (§12.1, §5.1b).</summary>
public sealed class PlacementConfig
{
    public int RotationStepDeg { get; init; } = 15;
    public float MaxSlopeM { get; init; }
    public PathAccessSection PathAccess { get; init; } = new();
    public ConstructionSection Construction { get; init; } = new();
    public HeritageSection Heritage { get; init; } = new();

    public sealed class PathAccessSection
    {
        public bool Required { get; init; }
    }

    public sealed class ConstructionSection
    {
        public double DaysPerMonth { get; init; }
        public SizeClass[] SizeClasses { get; init; } = [];
        public int[] SummerMonths { get; init; } = [];
        public double SummerSpeed { get; init; }
        public int[] WinterMonths { get; init; } = [];
        public double WinterSpeed { get; init; }
        public double CancelRefundFraction { get; init; }
    }

    public sealed class SizeClass
    {
        public string Id { get; init; } = "";
        public int? MaxTiles { get; init; }
        public double Months { get; init; }
    }

    public sealed class HeritageSection
    {
        public int AvailableYearsBeforeReal { get; init; }
        public double OnSiteMinOverlap { get; init; }
        public float SnapDistanceM { get; init; }
        public double OnSiteBonus { get; init; }
    }

    /// <summary>Construction speed on a day in this month: summer faster, winter slower (§12.1).</summary>
    public double SpeedIn(int month) =>
        Construction.SummerMonths.Contains(month) ? Construction.SummerSpeed
        : Construction.WinterMonths.Contains(month) ? Construction.WinterSpeed : 1.0;

    public double MonthsForArea(int tiles) =>
        (Construction.SizeClasses.FirstOrDefault(c => c.MaxTiles is null || tiles <= c.MaxTiles) ?? Construction.SizeClasses[^1]).Months;

    public const string File = "placement.json";

    public static PlacementConfig Load(IDataSource source) => SimJson.Parse<PlacementConfig>(source.ReadText(File), File);
}

/// <summary>data/heritage_projects.json.</summary>
public sealed class HeritageProjectsConfig
{
    public Project[] Projects { get; init; } = [];

    public sealed class Project
    {
        public string Id { get; init; } = "";
        public string TimelineId { get; init; } = "";
        public string Building { get; init; } = "";
        public double CostUsd { get; init; }
        public double ConstructionMonths { get; init; }
        public bool Verified { get; init; }
    }

    public const string File = "heritage_projects.json";

    public static HeritageProjectsConfig Load(IDataSource source) => SimJson.Parse<HeritageProjectsConfig>(source.ReadText(File), File);
}

/// <summary>
/// Where a building stands: its centre in half-tile units (so centres fall on tile corners or tile centres) and its
/// rotation in degrees, clockwise as seen from above with north up. At 0° the width runs east–west, the depth
/// north–south, and the entrance is in the middle of the south side.
/// </summary>
public readonly record struct Pose(int CxHalf, int CyHalf, int RotationDeg)
{
    public float Cx => CxHalf * 0.5f;
    public float Cy => CyHalf * 0.5f;
}

/// <summary>Footprint geometry in tile units (x east, y south). Pure and deterministic.</summary>
public static class FootprintMath
{
    private const float Eps = 1e-4f;

    public static (float Cos, float Sin) Rotation(int degrees)
    {
        int d = ((degrees % 360) + 360) % 360;
        return d switch
        {
            0 => (1, 0), 90 => (0, 1), 180 => (-1, 0), 270 => (0, -1),
            _ => ((float)Math.Cos(d * Math.PI / 180), (float)Math.Sin(d * Math.PI / 180)),
        };
    }

    /// <summary>Local footprint coordinates → map tile coordinates.</summary>
    public static (float X, float Y) ToMap(Pose p, float lx, float ly)
    {
        var (c, s) = Rotation(p.RotationDeg);
        return (p.Cx + lx * c - ly * s, p.Cy + lx * s + ly * c);
    }

    public static (float X, float Y) ToLocal(Pose p, float x, float y)
    {
        var (c, s) = Rotation(p.RotationDeg);
        float dx = x - p.Cx, dy = y - p.Cy;
        return (dx * c + dy * s, -dx * s + dy * c);
    }

    /// <summary>Corners in map tile units, clockwise from the north-west corner (at 0°).</summary>
    public static (float X, float Y)[] Corners(Pose p, int w, int h) =>
        [ToMap(p, -w / 2f, -h / 2f), ToMap(p, w / 2f, -h / 2f), ToMap(p, w / 2f, h / 2f), ToMap(p, -w / 2f, h / 2f)];

    /// <summary>Tiles whose centre lies inside the footprint (clipped to the map), in row order.</summary>
    public static List<int> Tiles(Pose p, int w, int h, int gridW, int gridH)
    {
        var corners = Corners(p, w, h);
        int x0 = Math.Max(0, (int)MathF.Floor(corners.Min(c => c.X))), x1 = Math.Min(gridW - 1, (int)MathF.Ceiling(corners.Max(c => c.X)));
        int y0 = Math.Max(0, (int)MathF.Floor(corners.Min(c => c.Y))), y1 = Math.Min(gridH - 1, (int)MathF.Ceiling(corners.Max(c => c.Y)));
        var list = new List<int>();
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var (lx, ly) = ToLocal(p, x + 0.5f, y + 0.5f);
                if (MathF.Abs(lx) < w / 2f - Eps && MathF.Abs(ly) < h / 2f - Eps) list.Add(y * gridW + x);
            }
        return list;
    }

    /// <summary>The tile just outside the middle of the front (south at 0°) side, or -1 if off the map.</summary>
    public static int Entrance(Pose p, int w, int h, int gridW, int gridH, IReadOnlyCollection<int> footprint)
    {
        foreach (float out_ in new[] { 0.5f, 1.0f, 1.5f })
        {
            var (x, y) = ToMap(p, 0, h / 2f + out_);
            int tx = (int)MathF.Floor(x), ty = (int)MathF.Floor(y);
            if (tx < 0 || ty < 0 || tx >= gridW || ty >= gridH) return -1;
            int t = ty * gridW + tx;
            if (!footprint.Contains(t)) return t;
        }
        return -1;
    }

    /// <summary>
    /// Snaps a point (tile units) to a pose: square rotations put the footprint's edges on tile edges (centre on a tile
    /// corner or a tile centre, by size parity); other angles snap the centre to the half-tile grid.
    /// </summary>
    public static Pose Snap(float x, float y, int w, int h, int rotationDeg)
    {
        int d = ((rotationDeg % 360) + 360) % 360;
        if (d % 90 != 0) return new Pose((int)MathF.Round(x * 2), (int)MathF.Round(y * 2), d);
        bool swap = d % 180 != 0;
        int ex = swap ? h : w, ey = swap ? w : h;
        static int Axis(float v, int extent) => extent % 2 == 0 ? 2 * (int)MathF.Round(v) : 2 * (int)MathF.Floor(v) + 1;
        return new Pose(Axis(x, ex), Axis(y, ey), d);
    }

    /// <summary>
    /// Fits a rectangle to an outline (tile units): of the rotations in steps of <paramref name="stepDeg"/> below 180°,
    /// the one with the smallest bounding box; size rounded to whole tiles. Used for Heritage Project sites.
    /// </summary>
    public static (Pose Pose, int W, int H) FitRectangle(float[] outline, int stepDeg)
    {
        var (cx, cy, deg, fw, fh) = FitExtents(outline, stepDeg);
        // Whole tiles, rounding up from 0.35 of a tile (a 14 m wide hall is 2 tiles, not 1).
        int w = Math.Max(1, (int)MathF.Ceiling(fw - 0.35f)), h = Math.Max(1, (int)MathF.Ceiling(fh - 0.35f));
        return (Snap(cx, cy, w, h, deg), w, h);
    }

    /// <summary>The fitted rectangle's exact centre, rotation and size (tile units), before snapping to tiles.</summary>
    public static (float Cx, float Cy, int Deg, float W, float H) FitExtents(float[] outline, int stepDeg)
    {
        int n = outline.Length / 2;
        float cx = 0, cy = 0;
        for (int i = 0; i < n; i++) { cx += outline[2 * i]; cy += outline[2 * i + 1]; }
        cx /= n; cy /= n;
        (float area, int deg, float minX, float maxX, float minY, float maxY) best = (float.MaxValue, 0, 0, 0, 0, 0);
        for (int deg = 0; deg < 180; deg += stepDeg)
        {
            var probe = new Pose((int)MathF.Round(cx * 2), (int)MathF.Round(cy * 2), deg);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var (lx, ly) = ToLocal(probe, outline[2 * i], outline[2 * i + 1]);
                minX = MathF.Min(minX, lx); maxX = MathF.Max(maxX, lx);
                minY = MathF.Min(minY, ly); maxY = MathF.Max(maxY, ly);
            }
            float area = (maxX - minX) * (maxY - minY);
            if (area < best.area - 1e-3f) best = (area, deg, minX, maxX, minY, maxY);
        }
        var p0 = new Pose((int)MathF.Round(cx * 2), (int)MathF.Round(cy * 2), best.deg);
        var (mx, my) = ToMap(p0, (best.minX + best.maxX) / 2, (best.minY + best.maxY) / 2);
        float w = best.maxX - best.minX, h = best.maxY - best.minY;
        // Long side first: the entrance (the local +y side) goes on a long wall, as in most Georgian halls.
        return w >= h ? (mx, my, best.deg, w, h) : (mx, my, (best.deg + 90) % 360, h, w);
    }
}

/// <summary>A Heritage Project's real site (§5.1b): the real outline and the rectangle fitted to it.</summary>
public sealed record HeritageSite(string TimelineId, string Name, int RealYear, int? DemolishedYear, Pose Pose, int W, int H,
    float[] Outline, int[] Tiles);

/// <summary>Something the player can build: a catalogue building or a Heritage Project.</summary>
public sealed class CatalogItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required BuildingDef Def { get; init; }
    public required int W { get; init; }
    public required int H { get; init; }
    /// <summary>Modern dollars; × the era's price multiplier when ordered.</summary>
    public required double CostUsd { get; init; }
    public required double Months { get; init; }
    /// <summary>Era indices: offered from <see cref="FromEra"/> up to (not including) <see cref="UntilEra"/>.</summary>
    public int FromEra { get; init; }
    public int UntilEra { get; init; } = int.MaxValue;
    /// <summary>Heritage Projects: the project id and the real site; null for catalogue buildings.</summary>
    public string? HeritageId { get; init; }
    public HeritageSite? Site { get; init; }
    public bool HeritageVerified { get; init; }

    public string Category => Def.Category;
    public bool IsHeritage => HeritageId is not null;
}

/// <summary>
/// The buildings the player can place (§11, §25.1): catalogue buildings offered by era (unlock_era … retire_era) and
/// Heritage Projects offered from a few years before their real build year until (if ever) their real demolition.
/// Immutable; shared by the sim and the UI.
/// </summary>
public sealed class BuildingCatalog
{
    public const string HeritagePrefix = "heritage:";

    public PlacementConfig Config { get; }
    public EraTable Eras { get; }
    public IReadOnlyList<CatalogItem> Items { get; }
    private readonly Dictionary<string, CatalogItem> _byId;

    public BuildingCatalog(PlacementConfig config, EraTable eras, IReadOnlyList<CatalogItem> items)
    {
        Config = config;
        Eras = eras;
        Items = items;
        _byId = items.ToDictionary(i => i.Id);
    }

    public CatalogItem? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>What can be ordered on <paramref name="date"/>; Heritage Projects already built or placed are left out.</summary>
    public List<CatalogItem> AvailableOn(DateOnly date, IReadOnlyCollection<string> heritageTaken)
    {
        int era = Eras.IndexAt(date.Year);
        int lead = Config.Heritage.AvailableYearsBeforeReal;
        return Items.Where(i => i.Site is { } s
                ? !heritageTaken.Contains(i.HeritageId!) && date.Year >= s.RealYear - lead && (s.DemolishedYear is null || date.Year < s.DemolishedYear)
                : era >= i.FromEra && era < i.UntilEra)
            .ToList();
    }

    public long CostCents(CatalogItem item, DateOnly date) =>
        (long)Math.Round(item.CostUsd * Eras.At(date.Year).PriceMultiplier * 100);

    public static BuildingCatalog Load(IDataSource source, SimData data, EraTable eras, TimelineData timeline,
        IReadOnlyList<HistoricBuilding> historic, int gridW, int gridH)
    {
        var cfg = PlacementConfig.Load(source);
        var projects = HeritageProjectsConfig.Load(source);
        var items = new List<CatalogItem>();
        foreach (var def in data.Buildings.Values.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            if (def.CostUsd is not { } cost || def.Category == "town") continue;
            int area = def.Footprint.W * def.Footprint.H;
            items.Add(new CatalogItem
            {
                Id = def.Id, Name = def.Name, Def = def, W = def.Footprint.W, H = def.Footprint.H, CostUsd = cost,
                Months = def.ConstructionMonths ?? cfg.MonthsForArea(area),
                FromEra = Math.Max(0, eras.IndexOf(def.UnlockEra)),
                UntilEra = def.RetireEra is { } r && eras.IndexOf(r) >= 0 ? eras.IndexOf(r) : int.MaxValue,
            });
        }
        foreach (var p in projects.Projects)
        {
            var entry = timeline.Entries.FirstOrDefault(e => e.Id == p.TimelineId);
            var place = historic.FirstOrDefault(b => b.Entry?.Id == p.TimelineId);
            if (entry?.BuiltYear is not { } year || place is null || !data.Buildings.TryGetValue(p.Building, out var def)) continue;
            var (pose, w, h) = FootprintMath.FitRectangle(place.Footprint.Outline, cfg.RotationStepDeg);
            var site = new HeritageSite(entry.Id, entry.Name, year, entry.DemolishedYear, pose, w, h, place.Footprint.Outline,
                [.. FootprintMath.Tiles(pose, w, h, gridW, gridH)]);
            items.Add(new CatalogItem
            {
                Id = HeritagePrefix + p.Id, Name = entry.Name, Def = def, W = w, H = h, CostUsd = p.CostUsd,
                Months = p.ConstructionMonths, HeritageId = p.Id, Site = site, HeritageVerified = p.Verified && entry.Verified,
            });
        }
        return new BuildingCatalog(cfg, eras, items);
    }
}

/// <summary>The map layers a placement check reads. The sim passes its live arrays; the UI passes snapshot copies.</summary>
public sealed record PlacementMap(int Width, int Height, float TileSizeM, TileType[] Types, LandState[] States,
    Ownership[] Owners, bool[] Protected, byte[] Clearing, Heightmap Heights, int[] SortedEntrances);

/// <summary>The outcome of checking a placement: footprint tiles, entrance, price, time, and what's wrong (if anything).</summary>
public sealed record PlacementQuote(int[] Tiles, int Entrance, long Cents, double WorkDays, DateOnly EstimatedFinish,
    float SlopeM, bool OnHeritageSite, string Problem, string Warning)
{
    public bool Ok => Problem.Length == 0;
}

public enum PlacementAction : byte { Build, Cancel }

/// <summary>A player order: build <see cref="ItemId"/> at <see cref="Pose"/>, or cancel construction site <see cref="SiteId"/>.</summary>
public readonly record struct PlacementCommand(PlacementAction Action, string ItemId, Pose Pose, int SiteId)
{
    public static PlacementCommand Build(string itemId, Pose pose) => new(PlacementAction.Build, itemId, pose, -1);
    public static PlacementCommand Cancel(int siteId) => new(PlacementAction.Cancel, "", default, siteId);
}

/// <summary>A building ordered by the player: under construction until <see cref="CampusIndex"/> is set.</summary>
public sealed class ConstructionSite
{
    public required int Id { get; init; }
    public required CatalogItem Item { get; init; }
    public required Pose Pose { get; init; }
    public required int[] Tiles { get; init; }
    public required int Entrance { get; init; }
    public required long CostCents { get; init; }
    public required double WorkDays { get; init; }
    public required DateOnly Ordered { get; init; }
    public required bool OnHeritageSite { get; init; }
    public double DoneDays { get; set; }
    public short CampusIndex { get; set; } = -1;
    public DateOnly? Finished { get; set; }

    public bool Complete => CampusIndex >= 0;
    public float Progress => (float)Math.Min(1.0, DoneDays / WorkDays);
}

/// <summary>Read-only view of one site for the UI (snapshot).</summary>
public sealed record SiteView(int Id, string ItemId, string Name, string Category, Pose Pose, int W, int H, int Entrance,
    float Progress, bool Complete, bool OnHeritageSite, DateOnly EstimatedFinish);

/// <summary>Everything the UI needs about placement, immutable (published in snapshots when it changes).</summary>
public sealed record PlacementView(int Version, SiteView[] Sites, int[] SortedEntrances, string[] HeritageTaken, double HeritageBonus);

/// <summary>
/// Building placement and construction (§12.1, §5.1b), Phase 1 1e. Sim thread only (commands are queued from any thread).
///  - A build order is checked again on the sim thread, paid in full, and its footprint becomes a construction site
///    straight away (walkers detour around it, §12.1).
///  - Work advances once a day (summer faster, winter slower). When it's done the building joins the campus as a
///    destination with its own flow field; people start using new buildings with enrollment and schedules (1h).
///  - Heritage Projects built on their real site earn a small Heritage bonus (recorded until the Heritage score exists).
/// </summary>
public sealed class PlacementSystem
{
    private readonly TileGrid _grid;
    private readonly Heightmap _heights;
    private readonly LandSystem _land;
    private readonly Campus _campus;
    private readonly ConcurrentQueue<PlacementCommand> _commands = new();
    private readonly List<string> _messages = [];
    private readonly List<ConstructionSite> _sites = [];
    private readonly HashSet<string> _heritageTaken;
    private int _nextSiteId = 1;
    private DateOnly _today;
    private PlacementView? _view;

    public BuildingCatalog Catalog { get; }
    public IReadOnlyList<ConstructionSite> Sites => _sites;
    public IReadOnlyCollection<string> HeritageTaken => _heritageTaken;
    public double HeritageBonus { get; private set; }
    /// <summary>Bumped whenever sites change (orders, daily progress, completion, cancels).</summary>
    public int Version { get; private set; }
    public int ActiveSites => _sites.Count(s => !s.Complete);
    public bool HasPendingCommands => !_commands.IsEmpty;

    /// <param name="heritageStanding">Heritage Projects already standing at the start (Old Main in 1824).</param>
    public PlacementSystem(TileGrid grid, Heightmap heights, LandSystem land, Campus campus, BuildingCatalog catalog,
        IEnumerable<string> heritageStanding)
    {
        _grid = grid;
        _heights = heights;
        _land = land;
        _campus = campus;
        Catalog = catalog;
        _heritageTaken = [.. heritageStanding];
    }

    public void Enqueue(PlacementCommand command) => _commands.Enqueue(command);

    public List<string> TakeMessages()
    {
        var m = new List<string>(_messages);
        _messages.Clear();
        return m;
    }

    /// <summary>The live map as a placement check sees it (sim thread).</summary>
    public PlacementMap LiveMap() => new(_grid.Width, _grid.Height, _grid.TileSizeM, _grid.Types, _grid.LandState, _grid.Ownership,
        _grid.Protected, _land.Clearing, _heights, Entrances());

    private int[] Entrances()
    {
        var list = _campus.Buildings.Select(b => b.EntranceTile).Concat(_sites.Where(s => !s.Complete).Select(s => s.Entrance)).ToArray();
        Array.Sort(list);
        return list;
    }

    public PlacementView View()
    {
        if (_view is { } v && v.Version == Version) return v;
        var cfg = Catalog.Config;
        var sites = _sites.Select(s => new SiteView(s.Id, s.Item.Id, s.Item.Name, s.Item.Category, s.Pose, s.Item.W, s.Item.H, s.Entrance,
            s.Progress, s.Complete, s.OnHeritageSite, s.Finished ?? EstimateFinish(cfg, _today, s.WorkDays - s.DoneDays))).ToArray();
        return _view = new PlacementView(Version, sites, Entrances(), [.. _heritageTaken.Order(StringComparer.Ordinal)], HeritageBonus);
    }

    /// <summary>Applies queued orders (sim thread, between ticks). Returns walking-surface edits for the flow fields.</summary>
    public List<Engine.TileEdit> ApplyCommands(DateOnly date)
    {
        _today = date;
        var edits = new List<Engine.TileEdit>();
        while (_commands.TryDequeue(out var c))
        {
            if (c.Action == PlacementAction.Cancel) { CancelSite(c.SiteId, date, edits); continue; }
            var item = Catalog.Find(c.ItemId);
            if (item is null || !Catalog.AvailableOn(date, _heritageTaken).Contains(item))
            {
                _messages.Add(item is null ? $"Unknown building '{c.ItemId}'." : $"{item.Name} can't be built now.");
                continue;
            }
            var q = Check(Catalog, item, c.Pose, LiveMap(), date);
            if (!q.Ok) { _messages.Add($"{item.Name}: {q.Problem}"); continue; }
            var treasury = _land.Treasury;
            if (!treasury.CanAfford(q.Cents))
            {
                _messages.Add($"Not enough money for the {item.Name}: it costs {LandSystem.Money(q.Cents)}, you have {LandSystem.Money(treasury.Cents)}.");
                continue;
            }
            treasury.Spend(date, q.Cents, $"Construction: {item.Name}");
            var site = new ConstructionSite
            {
                Id = _nextSiteId++, Item = item, Pose = c.Pose, Tiles = q.Tiles, Entrance = q.Entrance, CostCents = q.Cents,
                WorkDays = q.WorkDays, Ordered = date, OnHeritageSite = q.OnHeritageSite,
            };
            _sites.Add(site);
            if (item.HeritageId is { } h) _heritageTaken.Add(h);
            foreach (int t in q.Tiles) edits.Add(new Engine.TileEdit(t, TileType.Building));
            _messages.Add($"Construction started: {item.Name}, {LandSystem.Money(q.Cents)}, done about {q.EstimatedFinish:MMM d, yyyy}." +
                          (q.OnHeritageSite ? " On its real site." : "") + (q.Warning.Length > 0 ? " " + q.Warning : ""));
            Version++;
        }
        return edits;
    }

    private void CancelSite(int id, DateOnly date, List<Engine.TileEdit> edits)
    {
        var site = _sites.FirstOrDefault(s => s.Id == id);
        if (site is null || site.Complete) { _messages.Add("Only buildings still under construction can be cancelled."); return; }
        double left = 1.0 - site.Progress;
        double share = site.Ordered == date ? 1.0 : left * Catalog.Config.Construction.CancelRefundFraction;
        long refund = (long)Math.Round(site.CostCents * share);
        _land.Treasury.Receive(date, refund, $"Cancelled: {site.Item.Name}");
        _sites.Remove(site);
        if (site.Item.HeritageId is { } h) _heritageTaken.Remove(h);
        foreach (int t in site.Tiles)
        {
            var s = LandSystem.GroundSurface(_grid, _land.PathOn, t);
            if (s != _grid.Types[t]) edits.Add(new Engine.TileEdit(t, s));
        }
        _messages.Add($"Cancelled the {site.Item.Name}: refunded {LandSystem.Money(refund)}.");
        Version++;
    }

    /// <summary>Once a day: crews work. Returns sites finished today (the Simulation adds them to the campus).</summary>
    public List<ConstructionSite> DailyUpdate(DateOnly date)
    {
        _today = date;
        var done = new List<ConstructionSite>();
        double speed = Catalog.Config.SpeedIn(date.Month);
        foreach (var s in _sites)
        {
            if (s.Complete) continue;
            s.DoneDays += speed;
            if (s.DoneDays >= s.WorkDays) done.Add(s);
        }
        if (_sites.Any(s => !s.Complete)) Version++;
        return done;
    }

    /// <summary>Marks a site finished as campus building <paramref name="index"/> (called by the Simulation).</summary>
    public void Finish(ConstructionSite s, short index, DateOnly date)
    {
        s.CampusIndex = index;
        s.Finished = date;
        foreach (int t in s.Tiles) _grid.BuildingAt[t] = index;
        string bonus = "";
        if (s.OnHeritageSite)
        {
            HeritageBonus += Catalog.Config.Heritage.OnSiteBonus;
            bonus = $" Built on its real site: Heritage +{Catalog.Config.Heritage.OnSiteBonus:0}.";
        }
        _messages.Add($"Finished: {s.Item.Name}.{bonus}");
        Version++;
    }

    // ---------------- checks (pure; the UI calls these on snapshot copies) ----------------

    public static DateOnly EstimateFinish(PlacementConfig cfg, DateOnly from, double workDays)
    {
        var d = from;
        double done = 0;
        for (int guard = 0; done < workDays - 1e-9 && guard < 20000; guard++)
        {
            d = d.AddDays(1);
            done += cfg.SpeedIn(d.Month);
        }
        return d;
    }

    /// <summary>Checks a placement: ownership, land state, existing buildings and roads, slope, the entrance and path
    /// access (§12.1), and for Heritage Projects whether it's on the real site. Price and time are always filled in.</summary>
    public static PlacementQuote Check(BuildingCatalog catalog, CatalogItem item, Pose pose, PlacementMap m, DateOnly date)
    {
        var cfg = catalog.Config;
        long cents = catalog.CostCents(item, date);
        double workDays = item.Months * cfg.Construction.DaysPerMonth;
        var finish = EstimateFinish(cfg, date, workDays);
        var tiles = FootprintMath.Tiles(pose, item.W, item.H, m.Width, m.Height);
        var set = tiles.ToHashSet();
        int entrance = FootprintMath.Entrance(pose, item.W, item.H, m.Width, m.Height, set);
        bool onSite = false;
        if (item.Site is { } site && site.Tiles.Length > 0)
            onSite = site.Tiles.Count(set.Contains) >= cfg.Heritage.OnSiteMinOverlap * site.Tiles.Length;

        PlacementQuote Fail(string problem, float slope = 0) =>
            new([.. tiles], entrance, cents, workDays, finish, slope, onSite, problem, "");

        foreach (var (x, y) in FootprintMath.Corners(pose, item.W, item.H))
            if (x < 0 || y < 0 || x > m.Width || y > m.Height) return Fail("Off the edge of the map.");
        if (tiles.Count == 0) return Fail("Too small to place.");

        int water = 0, built = 0, road = 0, notOurs = 0, prot = 0, forest = 0, clearing = 0, doors = 0;
        foreach (int t in tiles)
        {
            if (m.Types[t] == TileType.Water || m.States[t] == LandState.Water) water++;
            else if (m.Types[t] == TileType.Building) built++;
            else if (m.Types[t] == TileType.Path) road++;
            else if (m.Owners[t] != Ownership.University) notOurs++;
            else if (m.Protected[t] || m.States[t] == LandState.Protected) prot++;
            else if (m.States[t] == LandState.Forest) forest++;
            else if (m.Clearing[t] != 0) clearing++;
            if (Array.BinarySearch(m.SortedEntrances, t) >= 0) doors++;
        }
        string Tiles(int n) => n == 1 ? "1 tile" : $"{n} tiles";
        if (water > 0) return Fail($"{Tiles(water)} of water in the way.");
        if (built > 0) return Fail("Something is already built there.");
        if (doors > 0) return Fail("That would block another building's entrance.");
        if (road > 0) return Fail($"A road or path crosses it ({Tiles(road)}).");
        if (notOurs > 0) return Fail($"{Tiles(notOurs)} aren't university land: buy them first (L).");
        if (prot > 0) return Fail("Protected land can't be built on.");
        if (forest > 0) return Fail($"{Tiles(forest)} of forest: clear them first (C).");
        if (clearing > 0) return Fail("Wait until the clearing there is finished.");

        // Slope: ground height at the corners and every tile centre.
        float lo = float.MaxValue, hi = float.MinValue, tm = m.TileSizeM;
        foreach (var (x, y) in FootprintMath.Corners(pose, item.W, item.H))
        {
            float g = m.Heights.HeightAt(x * tm, y * tm);
            lo = MathF.Min(lo, g); hi = MathF.Max(hi, g);
        }
        foreach (int t in tiles)
        {
            float g = m.Heights.HeightAt((t % m.Width + 0.5f) * tm, (t / m.Width + 0.5f) * tm);
            lo = MathF.Min(lo, g); hi = MathF.Max(hi, g);
        }
        float slope = hi - lo;
        if (slope > cfg.MaxSlopeM) return Fail($"Too steep: the ground drops {slope:0.0} m under it (at most {cfg.MaxSlopeM:0.#} m).", slope);

        if (entrance < 0) return Fail("The entrance would be off the map.", slope);
        if (m.Types[entrance] is TileType.Building or TileType.Water) return Fail("The entrance is blocked: turn it (Z/X) or move it.", slope);

        bool pathAtDoor = false;
        int ex = entrance % m.Width, ey = entrance / m.Width;
        for (int dy = -1; dy <= 1 && !pathAtDoor; dy++)
            for (int dx = -1; dx <= 1 && !pathAtDoor; dx++)
            {
                int nx = ex + dx, ny = ey + dy;
                pathAtDoor = (uint)nx < (uint)m.Width && (uint)ny < (uint)m.Height && m.Types[ny * m.Width + nx] == TileType.Path;
            }
        string warning = "";
        if (!pathAtDoor)
        {
            if (cfg.PathAccess.Required) return Fail("The entrance needs a path next to it.", slope);
            warning = "No path at the door yet: people will walk across the grass to it.";
        }
        return new PlacementQuote([.. tiles], entrance, cents, workDays, finish, slope, onSite, "", warning);
    }
}
