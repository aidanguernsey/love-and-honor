using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.View;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

public class TimelineTests
{
    private static readonly FileSystemDataSource Source = new(RepoPaths.Data);
    private static readonly RealMap Map = RealMapLoader.Load(Source);
    private static readonly TimelineData Timeline = TimelineData.Load(Source);
    private static readonly LandHistoryConfig HistoryCfg = LandHistoryConfig.Load(Source);
    private static readonly IReadOnlyList<FeatureBuilding> Features = FeatureBuilding.Load(Source, Map.Meta.FeaturesFile);
    private static readonly List<HistoricBuilding> Buildings = HistoricBuilding.Build(Features, Timeline, HistoryCfg.PresentYear, out Unplaced);
    private static readonly List<TimelineEntry> Unplaced;
    private static readonly LandHistory History = new(Map.Grid, HistoryCfg,
        Buildings.Where(b => b.Entry?.BuiltYear is not null).Select(b => { var c = b.Footprint.Centroid(); return (c.X, c.Y, b.BuiltYear); }));

    [Fact]
    public void PresentYear_ReproducesTodaysMapExactly()
    {
        int present = HistoryCfg.PresentYear;
        for (int i = 0; i < Map.Grid.LandState.Length; i++)
        {
            Assert.Equal(Map.Grid.LandState[i], History.StateAt(i, present));
            if (Map.Grid.PathType[i] != PathType.None) Assert.True(History.PathVisible(i, present));
        }
    }

    [Fact]
    public void In1809_TheMapIsMostlyForest_AndHasNoRoads()
    {
        int forest = 0, roads = 0;
        for (int i = 0; i < Map.Grid.LandState.Length; i++)
        {
            var s = History.StateAt(i, 1809);
            if (s == LandState.Forest) forest++;
            Assert.NotEqual(LandState.Town, s);
            Assert.NotEqual(LandState.University, s);
            if (History.PathVisible(i, 1809)) roads++;
        }
        Assert.True(forest > Map.Grid.LandState.Length * 0.9, $"only {forest} forest tiles in 1809");
        Assert.Equal(0, roads);
    }

    [Fact]
    public void Town_GrowsOutwardFromUptown()
    {
        int uptown = Map.Grid.Index(125, 180);
        Assert.Equal(LandState.Town, Map.Grid.LandState[uptown]);
        Assert.Equal(LandState.Town, History.StateAt(uptown, 1815));
        // A town tile far from Uptown urbanises later than one near it.
        int far = Enumerable.Range(0, Map.Grid.LandState.Length)
            .Where(i => Map.Grid.LandState[i] == LandState.Town)
            .OrderByDescending(i => Math.Abs(i % 400 - 125) + Math.Abs(i / 400 - 180)).First();
        Assert.True(History.UrbanYear(far) > History.UrbanYear(uptown));
    }

    [Fact]
    public void Buildings_AppearAndDisappearByYear()
    {
        HistoricBuilding Get(string id) => Buildings.Single(b => b.Entry?.Id == id);
        var elliott = Get("elliott_hall");
        Assert.False(elliott.StandsIn(1820, false));
        Assert.True(elliott.StandsIn(1830, false));
        Assert.True(elliott.StandsIn(2026, false));

        var oldMain = Get("old_main"); // drawn on Harrison Hall's footprint
        Assert.True(oldMain.ApproximateSite);
        Assert.True(oldMain.StandsIn(1900, false));
        Assert.False(oldMain.StandsIn(1958, false));
        var harrison = Get("harrison_hall");
        Assert.Same(oldMain.Footprint, harrison.Footprint);
        Assert.False(harrison.StandsIn(1958, false));
        Assert.True(harrison.StandsIn(1960, false));

        var wells = Get("wells_hall"); // demolished 2026, but OSM still has it
        Assert.True(wells.StandsIn(2025, false));
        Assert.False(wells.StandsIn(2026, false));
    }

    [Fact]
    public void UndatedBuildings_ShowOnlyInThePresent_UnlessToggled()
    {
        var undated = Buildings.First(b => b.Entry is null);
        Assert.False(undated.StandsIn(1950, false));
        Assert.True(undated.StandsIn(1950, true));
        Assert.True(undated.StandsIn(2026, false));
        // Present day: every OSM footprint stands except those the timeline says are gone.
        int standing = Buildings.Count(b => !b.ApproximateSite && b.StandsIn(2026, false));
        int gone = Timeline.Entries.Count(e => e.OsmId is not null && e.DemolishedYear is <= 2026);
        Assert.Equal(Features.Count - gone, standing);
    }

    [Fact]
    public void EveryTimelineEntryIsDrawnOrReportedAsUnplaced()
    {
        int drawn = Buildings.Count(b => b.Entry is not null);
        Assert.Equal(Timeline.Entries.Length, drawn + Unplaced.Count);
        Assert.DoesNotContain(Unplaced, e => e.Id == "old_main");
    }

    [Fact]
    public void FootprintContains_PointInPolygon()
    {
        var square = new FeatureBuilding { OsmId = "way/1", Outline = [0, 0, 4, 0, 4, 4, 0, 4] };
        Assert.True(square.Contains(2, 2));
        Assert.False(square.Contains(5, 2));
        var ell = new FeatureBuilding { OsmId = "way/2", Outline = [0, 0, 4, 0, 4, 1, 1, 1, 1, 4, 0, 4] };
        Assert.True(ell.Contains(0.5f, 3));
        Assert.False(ell.Contains(3, 3)); // the notch of the L
    }

    [Theory]
    [InlineData(1809, "dirt")]
    [InlineData(1885, "gravel")]
    [InlineData(1925, "brick")]
    [InlineData(2026, "asphalt")]
    [InlineData(2040, "asphalt")]
    public void Eras_GiveRoadSurface(int year, string surface) =>
        Assert.Equal(surface, EraTable.Load(Source).At(year).RoadType);

    [Theory]
    [InlineData(172, 73.9)]  // June solstice: 90 − 39.51 + 23.44
    [InlineData(355, 27.0)]  // December solstice: 90 − 39.51 − 23.44
    public void Sun_NoonElevationAtOxford(int day, double expected)
    {
        double best = -90, bestHour = 0, bestAz = 0;
        for (double h = 10; h <= 15; h += 1 / 60.0)
        {
            var (el, az) = SolarPosition.Compute(39.5087, -84.7337, day, h, -5);
            if (el > best) (best, bestHour, bestAz) = (el, h, az);
        }
        Assert.InRange(best, expected - 0.6, expected + 0.6);
        Assert.InRange(bestAz, 178, 182);            // due south at solar noon
        Assert.InRange(bestHour, 12.3, 12.9);        // Oxford is west of the EST meridian: noon ≈ 12:35–12:45 EST
        var (_, morningAz) = SolarPosition.Compute(39.5087, -84.7337, day, 9, -5);
        Assert.InRange(morningAz, 60, 170);          // east-ish in the morning
        var (x, y, z) = SolarPosition.Direction(best, bestAz);
        Assert.True(z > 0 && y > 0);                 // noon sun is up and to the south (+Z)
    }

    [Fact]
    public void SeasonWeights_BlendAndWrapAroundTheYear()
    {
        int[] anchors = [15, 105, 196, 293]; // winter, spring, summer, fall
        Assert.Equal([1f, 0, 0, 0], TileColorizer.SeasonWeights(15, anchors));
        var mid = TileColorizer.SeasonWeights(150, anchors);
        Assert.Equal(1f, mid.Sum(), precision: 5);
        Assert.True(mid[1] > 0 && mid[2] > 0);
        var newYear = TileColorizer.SeasonWeights(5, anchors);  // between fall (Oct 20) and winter (Jan 15)
        Assert.True(newYear[0] > 0.8f && newYear[3] > 0);
        var december = TileColorizer.SeasonWeights(340, anchors);
        Assert.True(december[3] > 0 && december[0] > 0);
        Assert.Equal(1f, december.Sum(), precision: 5);
    }

    [Fact]
    public void Colorizer_PaintsEveryTile()
    {
        var render = RenderingConfig.Load(Source);
        var palette = new Palette(Source.ReadText("branding.json"));
        var colorizer = new TileColorizer(Map, History, EraTable.Load(Source), render.Terrain, palette);
        colorizer.Paint(1809, 196);
        var summerForest = palette.Resolve(render.Terrain.SeasonColors["summer"]["forest"]);
        int uptown = Map.Grid.Index(125, 180) * 4;
        Assert.Equal((byte)(summerForest.R * 255 + 0.5f), colorizer.Pixels[uptown]);
        Assert.All(Enumerable.Range(0, colorizer.Pixels.Length / 4), i => Assert.Equal(255, colorizer.Pixels[i * 4 + 3]));
    }
}
