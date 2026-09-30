using System.Text.Json;
using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Sim.World;

/// <summary>One entry of data/timeline.json: a real Miami building, current or past (§30.3 era system).</summary>
public sealed class TimelineEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string[] OtherNames { get; init; } = [];
    public string Kind { get; init; } = "other";
    public string Campus { get; init; } = "";
    public int? BuiltYear { get; init; }
    public string BuiltPrecision { get; init; } = "unknown";
    public int? DemolishedYear { get; init; }
    public string Status { get; init; } = "";
    public string Confidence { get; init; } = "";
    public bool Verified { get; init; }
    public string[] Sources { get; init; } = [];
    public string Notes { get; init; } = "";
    public string? OsmId { get; init; }
    public string? ApproxSiteOsmId { get; init; }
    public TimelineEvent[] Events { get; init; } = [];

    public sealed class TimelineEvent
    {
        public int Year { get; init; }
        public string Event { get; init; } = "";
    }

    /// <summary>
    /// Standing in <paramref name="year"/>? Buildings with no known build year are shown only from the present year
    /// (we don't know when they appeared, so we don't invent it).
    /// </summary>
    public bool StandsIn(int year, int presentYear) =>
        year >= (BuiltYear ?? presentYear) && (DemolishedYear is null || year < DemolishedYear);
}

public sealed class TimelineData
{
    public TimelineEntry[] Entries { get; init; } = [];

    public static TimelineData Load(IDataSource source) =>
        SimJson.Parse<TimelineData>(source.ReadText("timeline.json"), "timeline.json");
}

/// <summary>Era table from data/eras.json (§25.1): road surface per era etc.</summary>
public sealed class EraTable
{
    public Era[] Eras { get; init; } = [];

    public sealed class Era
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public int StartYear { get; init; }
        public int? EndYear { get; init; }
        public string RoadType { get; init; } = "";
    }

    public Era At(int year) =>
        Eras.FirstOrDefault(e => year >= e.StartYear && (e.EndYear is null || year <= e.EndYear))
        ?? (year < Eras[0].StartYear ? Eras[0] : Eras[^1]);

    public static EraTable Load(IDataSource source) => SimJson.Parse<EraTable>(source.ReadText("eras.json"), "eras.json");
}

/// <summary>Building outlines from the map pipeline (data/map/&lt;prefix&gt;_features.json), in tile coordinates.</summary>
public sealed class FeatureBuilding
{
    public required string OsmId { get; init; }
    public string? Name { get; init; }
    public string? BuildingTag { get; init; }
    public float? Levels { get; init; }
    public float? HeightM { get; init; }
    /// <summary>x0, y0, x1, y1, … in tile units (open ring).</summary>
    public required float[] Outline { get; init; }

    public (float X, float Y) Centroid()
    {
        float sx = 0, sy = 0;
        int n = Outline.Length / 2;
        for (int i = 0; i < n; i++) { sx += Outline[2 * i]; sy += Outline[2 * i + 1]; }
        return (sx / n, sy / n);
    }

    /// <summary>Even-odd point-in-polygon test in tile units.</summary>
    public bool Contains(float x, float y)
    {
        bool inside = false;
        int n = Outline.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = Outline[2 * i], yi = Outline[2 * i + 1], xj = Outline[2 * j], yj = Outline[2 * j + 1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    public static IReadOnlyList<FeatureBuilding> Load(IDataSource source, string featuresFile)
    {
        using var doc = JsonDocument.Parse(source.ReadText($"map/{featuresFile}"));
        var list = new List<FeatureBuilding>();
        foreach (var b in doc.RootElement.GetProperty("buildings").EnumerateArray())
        {
            var ring = b.GetProperty("outline");
            var outline = new float[ring.GetArrayLength() * 2];
            int k = 0;
            foreach (var p in ring.EnumerateArray()) { outline[k++] = p[0].GetSingle(); outline[k++] = p[1].GetSingle(); }
            list.Add(new FeatureBuilding
            {
                OsmId = b.GetProperty("osm_id").GetString()!,
                Name = Opt(b, "name"),
                BuildingTag = Opt(b, "building"),
                Levels = OptNum(b, "levels"),
                HeightM = OptNum(b, "height_m"),
                Outline = outline,
            });
        }
        return list;
    }

    private static string? Opt(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static float? OptNum(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : null;
}

/// <summary>
/// A footprint to draw, with the years it stands: every OSM building (dated through the timeline where linked, else
/// "present day only"), plus timeline buildings drawn on a borrowed, approximate footprint.
/// </summary>
public sealed class HistoricBuilding
{
    public required FeatureBuilding Footprint { get; init; }
    public TimelineEntry? Entry { get; init; }
    /// <summary>First year it stands (present year if undated).</summary>
    public required int BuiltYear { get; init; }
    /// <summary>First year it no longer stands, or null.</summary>
    public int? DemolishedYear { get; init; }
    public bool Undated => Entry?.BuiltYear is null;
    public bool ApproximateSite { get; init; }
    public string Name => Entry?.Name ?? Footprint.Name ?? "Unnamed building";

    public bool StandsIn(int year, bool showUndatedAlways) =>
        (year >= BuiltYear || (showUndatedAlways && Undated)) && (DemolishedYear is null || year < DemolishedYear);

    public static List<HistoricBuilding> Build(IReadOnlyList<FeatureBuilding> features, TimelineData timeline, int presentYear, out List<TimelineEntry> unplaced)
    {
        var byOsm = features.ToDictionary(f => f.OsmId);
        var linked = timeline.Entries.Where(e => e.OsmId is not null).ToDictionary(e => e.OsmId!);
        var result = new List<HistoricBuilding>(features.Count + 16);
        foreach (var f in features)
        {
            linked.TryGetValue(f.OsmId, out var entry);
            result.Add(new HistoricBuilding
            {
                Footprint = f, Entry = entry,
                BuiltYear = entry?.BuiltYear ?? presentYear,
                DemolishedYear = entry?.DemolishedYear,
            });
        }
        unplaced = [];
        foreach (var e in timeline.Entries)
        {
            if (e.OsmId is not null) continue;
            if (e.ApproxSiteOsmId is not null && byOsm.TryGetValue(e.ApproxSiteOsmId, out var site))
                result.Add(new HistoricBuilding
                {
                    Footprint = site, Entry = e, ApproximateSite = true,
                    BuiltYear = e.BuiltYear ?? presentYear, DemolishedYear = e.DemolishedYear,
                });
            else
                unplaced.Add(e);
        }
        return result;
    }
}
