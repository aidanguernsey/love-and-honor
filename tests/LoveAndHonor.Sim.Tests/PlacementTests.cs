using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.Pathing;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Phase 1 (1e): footprints, the era-gated catalogue, placement checks, construction, Heritage Projects.</summary>
public class PlacementTests
{
    private static readonly FileSystemDataSource Source = new(RepoPaths.Data);
    private static readonly SimData Data = SimData.Load(Source);

    private static SimWorld Chapter1(int threads = 2, double? cash = null) =>
        SimWorld.CreateScenario(Data, Source, "chapter1_the_hill", threads: threads, chunkSize: 256, startingCash: cash);

    /// <summary>
    /// The first pose (scanning outward from Old Main) where the item can be built on the live map. A spot whose only
    /// problem is the missing path at the door gets one: a path tile is laid at the entrance (as a player would).
    /// </summary>
    private static Pose FindSpot(SimWorld w, CatalogItem item, int rotation = 0)
    {
        var g = w.Campus.Grid;
        var p = w.Placement!;
        var oldMain = w.Campus.Buildings[0];
        var map = p.LiveMap();
        for (int r = 0; r < 40; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    var pose = FootprintMath.Snap(oldMain.X + dx + 0.5f, oldMain.Y + dy + 0.5f, item.W, item.H, rotation);
                    var q = PlacementSystem.Check(p.Catalog, item, pose, map, w.Simulation.Time.Date);
                    if (q.Ok) return pose;
                    if (q.Problem.StartsWith("The entrance needs a path"))
                    {
                        LayPath(w, q.Entrance, q.Entrance);
                        Assert.True(PlacementSystem.Check(p.Catalog, item, pose, p.LiveMap(), w.Simulation.Time.Date).Ok);
                        return pose;
                    }
                }
        throw new InvalidOperationException($"no spot for {item.Id} near Old Main");
    }

    private static void LayPath(SimWorld w, int from, int to)
    {
        int gw = w.Campus.Grid.Width;
        w.Land!.Enqueue(new LandCommand(LandAction.Path, from % gw, from / gw, to % gw, to / gw));
        w.Simulation.ApplyPendingCommands();
        w.Land.TakeMessages();
    }

    private static void RunUntil(SimWorld w, DateOnly date)
    {
        while (w.Simulation.Time.Date < date) w.Simulation.Tick();
    }

    // ---------------- geometry ----------------

    [Theory]
    [InlineData(0, 3, 4)]
    [InlineData(90, 4, 3)]
    [InlineData(180, 3, 4)]
    [InlineData(270, 4, 3)]
    public void SquareRotations_CoverExactlyTheFootprint_WithTheEntranceOutsideTheFrontSide(int rot, int spanX, int spanY)
    {
        var pose = FootprintMath.Snap(20.3f, 30.7f, 3, 4, rot);
        var tiles = FootprintMath.Tiles(pose, 3, 4, 100, 100);
        Assert.Equal(12, tiles.Count);
        Assert.Equal(spanX, tiles.Max(t => t % 100) - tiles.Min(t => t % 100) + 1);
        Assert.Equal(spanY, tiles.Max(t => t / 100) - tiles.Min(t => t / 100) + 1);

        int e = FootprintMath.Entrance(pose, 3, 4, 100, 100, tiles.ToHashSet());
        Assert.DoesNotContain(e, tiles);
        Assert.Contains(tiles, t => Math.Abs(t % 100 - e % 100) <= 1 && Math.Abs(t / 100 - e / 100) <= 1); // touches the building
        // The front faces south at 0°, west at 90° (clockwise), north at 180°, east at 270°.
        int ex = e % 100, ey = e / 100;
        switch (rot)
        {
            case 0: Assert.True(ey > tiles.Max(t => t / 100)); break;
            case 90: Assert.True(ex < tiles.Min(t => t % 100)); break;
            case 180: Assert.True(ey < tiles.Min(t => t / 100)); break;
            case 270: Assert.True(ex > tiles.Max(t => t % 100)); break;
        }
    }

    [Fact]
    public void AngledFootprints_CoverAboutTheirArea()
    {
        for (int rot = 15; rot < 360; rot += 15)
        {
            if (rot % 90 == 0) continue;
            var pose = FootprintMath.Snap(50, 50, 4, 6, rot);
            var tiles = FootprintMath.Tiles(pose, 4, 6, 100, 100);
            Assert.InRange(tiles.Count, 18, 30); // 24 tiles of area; edge tiles count when their centre is inside
            Assert.DoesNotContain(FootprintMath.Entrance(pose, 4, 6, 100, 100, tiles.ToHashSet()), tiles);
        }
    }

    [Fact]
    public void FitRectangle_RecoversARotatedOutline()
    {
        var truth = new Pose(81, 61, 30); // centre (40.5, 30.5), 30°
        var corners = FootprintMath.Corners(truth, 3, 7);
        float[] outline = [.. corners.SelectMany(c => new[] { c.X, c.Y })];
        var (pose, w, h) = FootprintMath.FitRectangle(outline, 15);
        Assert.Equal(120, pose.RotationDeg); // the same rectangle turned so its long side is the front
        Assert.Equal((7, 3), (w, h));
        Assert.InRange(pose.Cx, 40, 41);
        Assert.InRange(pose.Cy, 30, 31);
    }

    // ---------------- catalogue ----------------

    [Fact]
    public void Catalogue_IsEraGated_AndHeritageProjectsFollowTheirRealDates()
    {
        var w = Chapter1();
        var catalog = w.Placement!.Catalog;
        string[] Ids(int year) => [.. catalog.AvailableOn(new DateOnly(year, 6, 1), w.Placement.HeritageTaken).Select(i => i.Id)];

        var y1824 = Ids(1824);
        Assert.Contains("frame_recitation_hall", y1824);
        Assert.Contains("boarding_house", y1824);
        Assert.DoesNotContain("small_classroom_hall", y1824);   // modern buildings aren't offered in the 1820s
        Assert.DoesNotContain("historic_hall", y1824);          // only as a Heritage Project
        Assert.DoesNotContain("off_campus_housing", y1824);     // town buildings never
        Assert.DoesNotContain("heritage:old_main", y1824);      // already standing in Chapter 1
        Assert.DoesNotContain("heritage:elliott_hall", y1824);  // real 1828: offered from 1825

        Assert.Contains("heritage:elliott_hall", Ids(1825));
        Assert.DoesNotContain("heritage:stoddard_hall", Ids(1825));
        Assert.Contains("heritage:stoddard_hall", Ids(1833));

        var y1950 = Ids(1950);
        Assert.Contains("small_classroom_hall", y1950);
        Assert.DoesNotContain("frame_recitation_hall", y1950);  // retired

        var elliott = catalog.Find("heritage:elliott_hall")!;
        Assert.NotNull(elliott.Site);
        Assert.True(elliott.Site!.Tiles.Length >= 4, $"the real site has a footprint ({elliott.W}x{elliott.H} at {elliott.Site.Pose})");
        Assert.Equal(1828, elliott.Site.RealYear);
        // 1820s prices: modern dollars x 0.02.
        Assert.Equal(600_00, catalog.CostCents(catalog.Find("frame_recitation_hall")!, new DateOnly(1824, 11, 1)));
    }

    // ---------------- checks ----------------

    [Fact]
    public void Check_RefusesForestTownLandRoadsAndExistingBuildings()
    {
        var w = Chapter1();
        var g = w.Campus.Grid;
        var p = w.Placement!;
        var item = p.Catalog.Find("frame_recitation_hall")!;
        var map = p.LiveMap();
        var date = w.Simulation.Time.Date;
        string ProblemAt(int x, int y) => PlacementSystem.Check(p.Catalog, item, FootprintMath.Snap(x + 0.5f, y + 0.5f, item.W, item.H, 0), map, date).Problem;

        int Find(Func<int, bool> pred) => Enumerable.Range(0, g.Width * g.Height).First(pred);
        bool UniversityForest(int x, int y) => g.InBounds(x, y) && g.Ownership[g.Index(x, y)] == Ownership.University && g.LandState[g.Index(x, y)] == LandState.Forest;
        int forest = Find(t => Enumerable.Range(-3, 7).All(dy => Enumerable.Range(-3, 7).All(dx => UniversityForest(t % g.Width + dx, t / g.Width + dy))));
        var forestProblem = ProblemAt(forest % g.Width, forest / g.Width);
        Assert.True(forestProblem.Contains("clear them first"), forestProblem);
        int town = Find(t => g.LandState[t] == LandState.Town && g.Types[t] != TileType.Path && g.Types[t] != TileType.Building);
        Assert.NotEqual("", ProblemAt(town % g.Width, town / g.Width));
        var oldMain = w.Campus.Buildings[0];
        Assert.Contains("already built", ProblemAt(oldMain.X + oldMain.W / 2, oldMain.Y + oldMain.H / 2));

        var ok = PlacementSystem.Check(p.Catalog, item, FindSpot(w, item), map, date);
        Assert.True(ok.Ok);
        Assert.Equal(6, ok.Tiles.Length);
        Assert.Equal(600_00, ok.Cents);
        Assert.InRange(ok.SlopeM, 0, p.Catalog.Config.MaxSlopeM);
    }

    [Fact]
    public void Construction_IsFasterInSummerThanInWinter()
    {
        var cfg = PlacementConfig.Load(Source);
        double work = 90 * cfg.Construction.DaysPerMonth / 30;
        var summer = PlacementSystem.EstimateFinish(cfg, new DateOnly(1825, 6, 1), work);
        var winter = PlacementSystem.EstimateFinish(cfg, new DateOnly(1825, 12, 1), work);
        Assert.True(summer.DayNumber - new DateOnly(1825, 6, 1).DayNumber < 70);
        Assert.True(winter.DayNumber - new DateOnly(1825, 12, 1).DayNumber > 110);
    }

    // ---------------- building ----------------

    [Fact]
    public void Building_PaysUpFront_BlocksWalking_ThenJoinsTheCampusWithItsOwnFlowField()
    {
        var w = Chapter1();
        var g = w.Campus.Grid;
        var p = w.Placement!;
        var sim = w.Simulation;
        var item = p.Catalog.Find("frame_recitation_hall")!;
        var pose = FindSpot(w, item);
        long cash = w.Land!.Treasury.Cents; // after the path at the door
        int buildingsBefore = w.Campus.Buildings.Count;

        p.Enqueue(PlacementCommand.Build(item.Id, pose));
        sim.ApplyPendingCommands();
        Assert.Contains(p.TakeMessages(), m => m.StartsWith("Construction started: Frame Recitation Hall"));
        Assert.Equal(cash - 600_00, w.Land!.Treasury.Cents);
        var site = Assert.Single(p.Sites);
        Assert.All(site.Tiles, t => Assert.Equal(TileType.Building, g.Types[t])); // a construction site blocks walking at once
        Assert.True(sim.RebuildApplyTick >= 0);

        // 4 months of work starting in November (winter is slower): done in spring 1825.
        RunUntil(w, site.Ordered.AddDays(200));
        Assert.True(site.Complete);
        Assert.InRange(site.Finished!.Value, new DateOnly(1825, 3, 1), new DateOnly(1825, 5, 31));
        Assert.Contains(p.TakeMessages(), m => m == "Finished: Frame Recitation Hall.");
        Assert.Equal(buildingsBefore + 1, w.Campus.Buildings.Count);
        var added = w.Campus.Buildings[^1];
        Assert.Equal("frame_recitation_hall", added.DefId);
        Assert.Equal(BuildingKind.Academic, added.Kind);
        Assert.Equal(site.Entrance, added.EntranceTile);
        Assert.All(site.Tiles, t => Assert.Equal(added.Index, g.BuildingAt[t]));

        // The new building's flow field is in use (swapped in after the rebuild latency) and Old Main can reach it.
        while (sim.RebuildApplyTick >= 0) sim.Tick();
        Assert.Equal(w.Campus.Buildings.Count, w.Fields.BuildingCount);
        Assert.True(float.IsFinite(w.Fields.Distance(0, added.Index)));
        Assert.Equal(w.Fields.Distance(0, added.Index), w.Fields.Distance(added.Index, 0));
    }

    [Fact]
    public void FlowFieldsGrownIncrementally_MatchAFullRebuild()
    {
        var w = Chapter1(cash: 100_000);
        var p = w.Placement!;
        var sim = w.Simulation;
        foreach (var id in new[] { "frame_recitation_hall", "boarding_house", "steward_hall" })
        {
            var item = p.Catalog.Find(id)!;
            p.Enqueue(PlacementCommand.Build(id, FindSpot(w, item, rotation: id == "boarding_house" ? 90 : 0)));
            sim.ApplyPendingCommands();
        }
        RunUntil(w, new DateOnly(1825, 8, 1));
        while (sim.RebuildApplyTick >= 0) sim.Tick();
        Assert.Equal(3, p.Sites.Count(s => s.Complete));

        var fresh = new FlowFieldSet(w.Campus, Data.Balance.Walking.Costs, 2);
        fresh.Build();
        var a = w.Fields.Current;
        var b = fresh.Current;
        Assert.Equal(b.BuildingCount, a.BuildingCount);
        Assert.Equal(b.DistanceM, a.DistanceM);
        for (int i = 0; i < a.Routes.Length; i++) Assert.Equal(b.Routes[i], a.Routes[i]);
    }

    [Fact]
    public void Cancelling_RefundsAndFreesTheGround()
    {
        var w = Chapter1();
        var g = w.Campus.Grid;
        var p = w.Placement!;
        var item = p.Catalog.Find("boarding_house")!;
        var pose = FindSpot(w, item);
        long cash = w.Land!.Treasury.Cents;
        var before = FootprintMath.Tiles(pose, item.W, item.H, g.Width, g.Height).Select(t => g.Types[t]).ToArray();

        p.Enqueue(PlacementCommand.Build(item.Id, pose));
        w.Simulation.ApplyPendingCommands();
        var site = p.Sites[0];
        p.Enqueue(PlacementCommand.Cancel(site.Id));
        w.Simulation.ApplyPendingCommands();     // same day: full refund
        Assert.Equal(cash, w.Land!.Treasury.Cents);
        Assert.Empty(p.Sites);
        Assert.Equal(before, site.Tiles.Select(t => g.Types[t]).ToArray());

        p.Enqueue(PlacementCommand.Build(item.Id, pose));
        w.Simulation.ApplyPendingCommands();
        RunUntil(w, w.Simulation.Time.Date.AddDays(10));
        var second = p.Sites[0];
        p.Enqueue(PlacementCommand.Cancel(second.Id));
        w.Simulation.ApplyPendingCommands();     // later: half of the unspent part
        // Salaries and upkeep come out monthly (1i), so check the construction entries of the ledger.
        long refund = (long)Math.Round(800_00 * (1 - second.Progress) * 0.5);
        var construction = w.Land.Treasury.Ledger.Where(e => e.Category == "construction").Select(e => e.Cents).ToList();
        Assert.Equal([-800_00, 800_00, -800_00, refund], construction);
    }

    [Fact]
    public void HeritageProject_OnItsRealSite_EarnsTheBonus_AndIsOfferedOnce()
    {
        var w = Chapter1(cash: 100_000);
        var g = w.Campus.Grid;
        var p = w.Placement!;
        var elliott = p.Catalog.Find("heritage:elliott_hall")!;
        var site = elliott.Site!;
        // Make the real site buildable (in play: buy and clear it first).
        foreach (int t in site.Tiles.Concat([FootprintMath.Entrance(site.Pose, site.W, site.H, g.Width, g.Height, site.Tiles.ToHashSet())]))
        {
            g.Ownership[t] = Ownership.University;
            if (g.LandState[t] == LandState.Forest) g.LandState[t] = LandState.Pasture;
        }
        RunUntil(w, new DateOnly(1825, 1, 2)); // offered from 1825
        int door = FootprintMath.Entrance(site.Pose, site.W, site.H, g.Width, g.Height, site.Tiles.ToHashSet());
        LayPath(w, door, door);

        var q = PlacementSystem.Check(p.Catalog, elliott, site.Pose, p.LiveMap(), w.Simulation.Time.Date);
        Assert.True(q.Ok, q.Problem);
        Assert.True(q.OnHeritageSite);
        Assert.Equal(250_000 * 0.02 * 100, q.Cents, precision: 0);

        p.Enqueue(PlacementCommand.Build(elliott.Id, site.Pose));
        w.Simulation.ApplyPendingCommands();
        Assert.Contains("elliott_hall", p.HeritageTaken);
        Assert.DoesNotContain(elliott, p.Catalog.AvailableOn(w.Simulation.Time.Date, p.HeritageTaken));
        p.Enqueue(PlacementCommand.Build(elliott.Id, site.Pose));
        w.Simulation.ApplyPendingCommands();
        Assert.Contains(p.TakeMessages(), m => m == "Elliott Hall can't be built now.");

        RunUntil(w, new DateOnly(1826, 3, 1));
        Assert.True(p.Sites[0].Complete);
        Assert.Equal(p.Catalog.Config.Heritage.OnSiteBonus, p.HeritageBonus);
        Assert.Equal(BuildingKind.Residence, w.Campus.Buildings[^1].Kind);
    }

    [Fact]
    public void Building_IsDeterministicAcrossThreadCounts()
    {
        ulong Run(int threads)
        {
            var w = Chapter1(threads, cash: 100_000);
            var item = w.Placement!.Catalog.Find("frame_recitation_hall")!;
            var pose = FindSpot(w, item);
            while (w.Simulation.Time.Date < new DateOnly(1825, 4, 15))
            {
                if (w.Simulation.Time.Tick == 7) w.Placement.Enqueue(PlacementCommand.Build(item.Id, pose));
                w.Simulation.Tick();
            }
            ulong h = StateHash.Compute(w.Simulation, w.Campus.Grid) ^ (ulong)w.Land!.Treasury.Cents;
            foreach (var t in w.Campus.Grid.Types) h = h * 31 + (ulong)t;
            return h ^ (ulong)w.Campus.Buildings.Count;
        }
        Assert.Equal(Run(1), Run(4));
    }
}
