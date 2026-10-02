using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Sim.World;

/// <summary>A game start (data/scenarios/*.json): date, map year, money, population, starting university land.</summary>
public sealed class ScenarioConfig
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string StartDate { get; init; } = "";
    public int MapYear { get; init; }
    public bool Verified { get; init; }
    public ulong Seed { get; init; }
    public double StartingCash { get; init; }
    public int Students { get; init; }
    public int Faculty { get; init; }
    /// <summary>Students arrive, graduate and leave each year (enrollment.json, 1h); false = a fixed population.</summary>
    public bool Enrollment { get; init; }
    public int? PopulationCapacity { get; init; }
    public CampusSection? Campus { get; init; }

    public sealed class CampusSection
    {
        public string CenterTimelineId { get; init; } = "";
        public float HalfSizeM { get; init; }
    }

    public DateOnly Start => DateOnly.Parse(StartDate, System.Globalization.CultureInfo.InvariantCulture);

    public static ScenarioConfig Load(IDataSource source, string id) =>
        SimJson.Parse<ScenarioConfig>(source.ReadText($"scenarios/{id}.json"), $"scenarios/{id}.json");
}

/// <summary>
/// Sets the real map up as it stood in a past year (Phase 1 1d): land states, roads and the town from the land-history
/// rules, the buildings standing that year from the timeline, the university's starting land, and the walking
/// surfaces that follow from all that. Returns the land layer's road flags.
/// </summary>
public static class HistoricMap
{
    public static bool[] Apply(RealMap map, IReadOnlyList<HistoricBuilding> buildings, LandHistory history, ScenarioConfig s)
    {
        var g = map.Grid;
        int year = s.MapYear, tiles = g.Width * g.Height;
        int waterCode = map.Meta.Codes.Landcover.GetValueOrDefault("water", -1);
        int wetlandCode = map.Meta.Codes.Landcover.GetValueOrDefault("wetland", -1);
        var pathOn = new bool[tiles];

        // Land state, owner, roads.
        for (int t = 0; t < tiles; t++)
        {
            var state = history.StateAt(t, year);
            g.LandState[t] = state;
            g.Ownership[t] = state == LandState.Town ? Ownership.Town : Ownership.Private;
            pathOn[t] = history.PathVisible(t, year);
            g.BuildingAt[t] = -1;
            bool water = map.LandCover[t] == waterCode || map.LandCover[t] == wetlandCode;
            g.Types[t] = water && !pathOn[t] ? TileType.Water : TileType.Grass; // surfaces fixed below
        }

        // The university's starting land: a square around a timeline building's site.
        if (s.Campus is { } c)
        {
            var anchor = buildings.FirstOrDefault(b => b.Entry?.Id == c.CenterTimelineId)
                ?? throw new InvalidDataException($"scenario {s.Id}: no footprint for timeline entry '{c.CenterTimelineId}'");
            var (cx, cy) = anchor.Footprint.Centroid();
            float half = c.HalfSizeM / g.TileSizeM;
            for (int y = Math.Max(0, (int)(cy - half)); y <= Math.Min(g.Height - 1, (int)(cy + half)); y++)
                for (int x = Math.Max(0, (int)(cx - half)); x <= Math.Min(g.Width - 1, (int)(cx + half)); x++)
                    g.Ownership[g.Index(x, y)] = Ownership.University;
        }

        // Buildings standing that year (timeline buildings only: undated town houses aren't placed in the past).
        foreach (var b in buildings)
        {
            if (b.Entry is null || !b.StandsIn(year, showUndatedAlways: false)) continue;
            foreach (int t in RealCampusBuilder.FootprintTiles(g, b.Footprint)) g.Types[t] = TileType.Building;
        }

        for (int t = 0; t < tiles; t++)
            if (g.Types[t] is not (TileType.Water or TileType.Building)) g.Types[t] = LandSystem.SurfaceFor(g, pathOn, t);
        g.MarkChanged();
        return pathOn;
    }
}
