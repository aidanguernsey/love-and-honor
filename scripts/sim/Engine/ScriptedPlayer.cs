using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Engine;

/// <summary>
/// A scripted player for balance tests and headless runs (Chapter 1 balance pass). It plays through the same orders a
/// person gives (land and placement commands), once a day: it works down a build plan, finds a site near the first
/// academic building (or a Heritage Project's real site), buys, clears and lays a door path as needed, and orders the
/// building when cash minus the cost stays above its reserve. Deterministic: same world, same plan → same game.
/// Call <see cref="Act"/> between ticks (sim thread).
/// </summary>
public sealed class ScriptedPlayer
{
    /// <summary>One plan entry: build <paramref name="ItemId"/> once <paramref name="When"/> holds (checked daily).
    /// <paramref name="Repeat"/>: stays in the plan after building (built again whenever the condition holds).</summary>
    public sealed record Step(string Name, string ItemId, Func<SimWorld, bool> When, bool Repeat = false);

    private sealed class Job(Step step, CatalogItem item)
    {
        public readonly Step Step = step;
        public readonly CatalogItem Item = item;
        public Pose? Pose;
        public readonly HashSet<Pose> Rejected = [];
        public int Started = -1, Waits;
    }

    private readonly SimWorld _w;
    private readonly List<Step> _plan;
    private readonly double _reserveMonths;
    private readonly HashSet<Step> _done = [];
    private Job? _job;

    public string Name { get; }
    public List<string> Log { get; } = [];

    /// <param name="reserveMonths">Months of running costs (salaries, administration, upkeep) it keeps in the bank
    /// after paying for a building.</param>
    public ScriptedPlayer(string name, SimWorld world, IEnumerable<Step> plan, double reserveMonths)
    {
        Name = name;
        _w = world;
        _plan = [.. plan];
        _reserveMonths = reserveMonths;
    }

    /// <summary>The cash it keeps: <c>reserveMonths</c> of the last twelve months' running costs.</summary>
    private long ReserveCents()
    {
        var today = _w.Simulation.Time.Date;
        long running = _w.Land!.Treasury.Ledger.Where(e => e.Date > today.AddYears(-1) && e.Category is "salaries" or "administration" or "upkeep")
            .Sum(e => -e.Cents);
        return (long)(running / 12.0 * _reserveMonths);
    }

    // ---------------- strategies ----------------

    /// <summary>Does nothing (the chapter should not be winnable this way).</summary>
    public static ScriptedPlayer Idle(SimWorld w) => new("idle", w, [], reserveMonths: 0);

    /// <summary>A sensible player: dining and Elliott early, classrooms before they fill, more hall beds as students
    /// arrive, Stoddard when it's offered; keeps a cash reserve.</summary>
    public static ScriptedPlayer Sensible(SimWorld w) => new("sensible", w,
    [
        new("steward's hall", "steward_hall", _ => true),
        new("Elliott Hall", "heritage:elliott_hall", _ => true),
        new("classrooms", "brick_classroom_hall", x => x.Population.StudentCount > 0.6 * x.Enrollment!.Seats, Repeat: true),
        new("more dining", "steward_hall", x => x.Enrollment!.CapacityOfKind(BuildingKind.Dining) < x.Population.StudentCount * 0.8, Repeat: true),
        new("Stoddard Hall", "heritage:stoddard_hall", _ => true),
        new("more beds", "brick_residence_hall", x => x.Enrollment!.CampusBeds < x.Population.StudentCount, Repeat: true),
    ], reserveMonths: 6);

    /// <summary>Just the goal and the seats it needs: Elliott, then classrooms once they fill; nothing for the
    /// students' everyday life (dining, more halls). Reputation should make this fall short.</summary>
    public static ScriptedPlayer Minimal(SimWorld w) => new("minimal", w,
    [
        new("Elliott Hall", "heritage:elliott_hall", _ => true),
        new("classrooms", "brick_classroom_hall", x => x.Population.StudentCount > 0.85 * x.Enrollment!.Seats, Repeat: true),
    ], reserveMonths: 6);

    /// <summary>Builds everything it can afford as soon as it can, with no reserve (should go broke).</summary>
    public static ScriptedPlayer Spender(SimWorld w) => new("spender", w,
    [
        new("Elliott Hall", "heritage:elliott_hall", _ => true),
        new("president's house", "presidents_house", _ => true),
        new("classrooms", "brick_classroom_hall", _ => true, Repeat: true),
        new("halls", "brick_residence_hall", _ => true, Repeat: true),
    ], reserveMonths: 0);

    // ---------------- playing ----------------

    /// <summary>Plays one decision, at hour 1 of each day (after the day's updates). Call between ticks.</summary>
    public void Act()
    {
        var sim = _w.Simulation;
        if (sim.Time.HourOfDay != 1 || _w.Placement is null || _w.Land is null) return;
        var date = sim.Time.Date;
        var catalog = _w.Placement.Catalog;
        if (_job is null)
        {
            var offered = catalog.AvailableOn(date, _w.Placement.HeritageTaken);
            foreach (var step in _plan)
            {
                if (_done.Contains(step)) continue;
                var item = catalog.Find(step.ItemId);
                if (item is null || !offered.Contains(item) || !step.When(_w)) continue;
                // One of each at a time: capacity under construction doesn't show in the conditions yet.
                if (_w.Placement.Sites.Any(s => !s.Complete && s.Item.Id == item.Id)) continue;
                if (_w.Land.Treasury.Cents - catalog.CostCents(item, date) < ReserveCents()) break; // saving up for it
                _job = new Job(step, item) { Started = sim.Time.Day };
                break;
            }
            if (_job is null) return;
        }
        Work(_job, date);
    }

    private void Finish(Job job, string outcome)
    {
        Log.Add($"{_w.Simulation.Time.Date:yyyy-MM-dd} {job.Step.Name}: {outcome}");
        if (!job.Step.Repeat || outcome.StartsWith("gave up")) _done.Add(job.Step);
        _job = null;
    }

    private void Work(Job job, DateOnly date)
    {
        var placement = _w.Placement!;
        var land = _w.Land!;
        var g = _w.Campus.Grid;
        if (_w.Simulation.Time.Day - job.Started > 400) { Finish(job, "gave up (took too long)"); return; }
        if (land.ActiveClearingTiles > 0 || land.HasPendingCommands || placement.HasPendingCommands) return; // wait for crews
        job.Pose ??= job.Item.Site?.Pose ?? FindSite(job);
        if (job.Pose is not { } pose) { Finish(job, "gave up (no site)"); return; }
        if (land.Treasury.Cents - placement.Catalog.CostCents(job.Item, date) < ReserveCents())
        {
            job.Started = _w.Simulation.Time.Day; // saving up doesn't count toward giving up
            return;
        }

        var q = PlacementSystem.Check(placement.Catalog, job.Item, pose, placement.LiveMap(), date);
        var area = q.Tiles.Append(q.Entrance).Where(t => t >= 0).ToArray();
        int x0 = area.Min(t => t % g.Width) - 1, x1 = area.Max(t => t % g.Width) + 1;
        int y0 = area.Min(t => t / g.Width) - 1, y1 = area.Max(t => t / g.Width) + 1;
        if (q.Ok)
        {
            placement.Enqueue(PlacementCommand.Build(job.Item.Id, pose));
            Finish(job, $"ordered ({placement.Catalog.CostCents(job.Item, date) / 100:N0} dollars)");
        }
        else if (q.Problem.StartsWith("The entrance needs a path"))
        {
            if (++job.Waits > 6) { Reject(job, "no path"); return; }
            if (DoorPath(q.Entrance) is { } path)
            {
                land.Enqueue(path);
                placement.Enqueue(PlacementCommand.Build(job.Item.Id, pose));
                Finish(job, $"ordered with a door path ({placement.Catalog.CostCents(job.Item, date) / 100:N0} dollars)");
            }
            else if (NearestPath(q.Entrance) is { } target)
            {
                // Clear a corridor to the nearest path, then try again.
                land.Enqueue(new LandCommand(LandAction.Clear, Math.Min(x0, target % g.Width), Math.Min(y0, target / g.Width),
                    Math.Max(x1, target % g.Width), Math.Max(y1, target / g.Width)));
            }
            else Reject(job, "no path nearby");
        }
        else if (q.Problem.Contains("aren't university land") && ++job.Waits <= 6)
            land.Enqueue(new LandCommand(LandAction.Buy, x0, y0, x1, y1));
        else if (q.Problem.Contains("of forest") && ++job.Waits <= 6)
            land.Enqueue(new LandCommand(LandAction.Clear, x0, y0, x1, y1));
        else if (q.Problem.StartsWith("Wait until the clearing")) { }
        else Reject(job, q.Problem);
    }

    private void Reject(Job job, string why)
    {
        if (job.Item.Site is not null) { Finish(job, $"gave up ({why})"); return; }
        job.Rejected.Add(job.Pose!.Value);
        job.Pose = null;
        job.Waits = 0;
    }

    /// <summary>The nearest spot around the first building that is ready, needs only a path, or only clearing (fewest
    /// forest tiles), in ring order so ties go to the closest.</summary>
    private Pose? FindSite(Job job)
    {
        var placement = _w.Placement!;
        var map = placement.LiveMap();
        var date = _w.Simulation.Time.Date;
        var first = _w.Campus.Buildings[0];
        float cx = first.X + first.W / 2f, cy = first.Y + first.H / 2f;
        Pose? best = null;
        int bestScore = int.MaxValue;
        for (int r = 3; r <= 20; r++)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    foreach (int rot in (int[])[0, 90, 180, 270])
                    {
                        var pose = FootprintMath.Snap(cx + dx, cy + dy, job.Item.W, job.Item.H, rot);
                        if (job.Rejected.Contains(pose)) continue;
                        var q = PlacementSystem.Check(placement.Catalog, job.Item, pose, map, date);
                        int score = q.Ok ? 0
                            : q.Problem.StartsWith("The entrance needs a path") ? 1
                            : q.Problem.Contains("of forest") ? 2 + q.Tiles.Count(t => map.States[t] == LandState.Forest)
                            : int.MaxValue;
                        if (score < bestScore) { bestScore = score; best = pose; }
                    }
                }
            if (bestScore <= 1) break; // nothing closer will need less work
        }
        return best;
    }

    private int? NearestPath(int door)
    {
        var g = _w.Campus.Grid;
        int dx0 = door % g.Width, dy0 = door / g.Width;
        for (int r = 1; r < 30; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !g.InBounds(dx0 + dx, dy0 + dy)) continue;
                    int t = g.Index(dx0 + dx, dy0 + dy);
                    if (g.Types[t] == TileType.Path) return t;
                }
        return null;
    }

    /// <summary>A path order from the door to the nearest reachable path, or null.</summary>
    private LandCommand? DoorPath(int door)
    {
        var g = _w.Campus.Grid;
        var land = _w.Land!;
        if (land.Paths is not { } cfg) return null;
        var m = land.LivePathMap();
        int dx0 = door % g.Width, dy0 = door / g.Width;
        for (int r = 1; r < 25; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !g.InBounds(dx0 + dx, dy0 + dy)) continue;
                    int t = g.Index(dx0 + dx, dy0 + dy);
                    if (g.Types[t] == TileType.Path && PathPlanner.Route(cfg, m, door, t, out _) is not null)
                        return new LandCommand(LandAction.Path, dx0, dy0, t % g.Width, t / g.Width);
                }
        return null;
    }

    // ---------------- a whole chapter ----------------

    /// <summary>Year-by-year figures of a headless chapter run (taken on September 1, after move-in).</summary>
    public sealed record YearRow(int Year, int Students, int Faculty, int Applicants, int HallBeds, int Seats, double Cash,
        double Confidence, double Reputation, double Quality);

    public sealed record ChapterResult(string Player, CampaignOutcome Outcome, DateOnly End, int Students, double Cash,
        List<YearRow> Years, List<string> Log, string OutcomeText);

    /// <summary>Plays a campaign world to its end (won, lost or out of time) with this player.</summary>
    public ChapterResult PlayChapter()
    {
        var sim = _w.Simulation;
        var camp = _w.Campaign ?? throw new InvalidOperationException("Not a campaign world.");
        var years = new List<YearRow>();
        int lastYear = 0;
        while (camp.Outcome == CampaignOutcome.Playing)
        {
            Act();
            sim.Tick();
            var d = sim.Time.Date;
            if (sim.Time.HourOfDay == 1)
            {
                // Nobody reads the systems' player messages here: keep the ones that tell the story, drop the rest.
                foreach (var m in _w.Placement!.TakeMessages()) Log.Add($"{d:yyyy-MM-dd} {m}");
                var messages = _w.Land!.TakeMessages().Concat(_w.Enrollment!.TakeMessages()).Concat(_w.Budget!.TakeMessages())
                    .Concat(camp.TakeMessages());
                foreach (var m in messages)
                    if (m.StartsWith("Move-in") || m.StartsWith("Budget") || m.StartsWith("The Trustees") || m.Contains("Slant Walk"))
                        Log.Add($"{d:yyyy-MM-dd} {m}");
            }
            if (d.Month == 9 && d.Day == 1 && sim.Time.HourOfDay == 1 && d.Year != lastYear)
            {
                lastYear = d.Year;
                var e = _w.Enrollment!.View(d);
                years.Add(new YearRow(d.Year, _w.Population.StudentCount, _w.Population.FacultyCount, e.LastApplicants, e.CampusBeds,
                    e.Seats, _w.Land!.Treasury.Dollars, _w.Budget!.Confidence, _w.Reputation?.Reputation ?? 0, _w.Reputation?.Quality ?? 0));
            }
        }
        return new ChapterResult(Name, camp.Outcome, sim.Time.Date, _w.Population.StudentCount, _w.Land!.Treasury.Dollars, years, Log,
            camp.View().OutcomeText);
    }
}
