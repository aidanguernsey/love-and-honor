using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;
using Xunit.Abstractions;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Phase 1 (1h): a growing and shrinking population, enrollment, housing, breaks.</summary>
public class EnrollmentTests(ITestOutputHelper output)
{
    private static readonly FileSystemDataSource Source = new(RepoPaths.Data);
    private static readonly SimData Data = SimData.Load(Source);

    private static SimWorld Chapter1(int threads = 2, double? cash = null) =>
        SimWorld.CreateScenario(Data, Source, "chapter1_the_hill", threads: threads, chunkSize: 16, startingCash: cash);

    private static void RunUntil(SimWorld w, DateOnly date)
    {
        while (w.Simulation.Time.Date < date) w.Simulation.Tick();
    }

    [Fact]
    public void Store_AddsAtTheEnd_AndRemovesByMovingTheLastAgentIn()
    {
        var p = new PopulationStore(2, 1, capacity: 5);
        Assert.Equal((3, 2, 5), (p.Count, p.StudentCount, p.Capacity));
        p.Kind[2] = AgentKind.Faculty;
        p.Home[0] = 10; p.Home[1] = 11; p.Home[2] = 12;
        int a = p.Add(AgentKind.Student);
        Assert.Equal(3, a);
        Assert.Equal(PopulationStore.NoBuilding, p.Home[a]);
        p.Home[a] = 13;
        p.SectionHour[a * PopulationStore.MaxSections + 2] = 9;
        int id = p.Id[a];
        p.RemoveAt(1);
        Assert.Equal((3, 2), (p.Count, p.StudentCount));
        Assert.Equal(13, p.Home[1]);             // the last agent moved into the gap, with all its data
        Assert.Equal(9, p.SectionHour[1 * PopulationStore.MaxSections + 2]);
        Assert.Equal(id, p.Id[1]);
        p.Add(AgentKind.Faculty); p.Add(AgentKind.Faculty);
        Assert.Throws<InvalidOperationException>(() => p.Add(AgentKind.Student));
    }

    [Fact]
    public void Chapter1_GraduatesInMay_AdmitsInAugust_AndHiresFaculty()
    {
        var w = Chapter1();
        var p = w.Population;
        var e = w.Enrollment!;
        int seniors = Enumerable.Range(0, p.Count).Count(a => p.Kind[a] == AgentKind.Student && p.Year[a] == 4);
        int students = p.StudentCount;

        RunUntil(w, new DateOnly(1825, 5, 17));
        var commencement = e.TakeMessages();
        output.WriteLine(string.Join("\n", commencement));
        Assert.Contains(commencement, m => m.StartsWith("Commencement 1825"));
        Assert.Equal(seniors, e.Alumni);
        Assert.True(p.StudentCount <= students - seniors);
        Assert.DoesNotContain(Enumerable.Range(0, p.Count), a => p.Kind[a] == AgentKind.Student && p.Year[a] == 1); // everyone moved up
        Assert.True(e.Scholarship > 0);

        int before = p.StudentCount;
        RunUntil(w, new DateOnly(1825, 8, 20));
        var moveIn = e.TakeMessages();
        output.WriteLine(string.Join("\n", moveIn));
        var view = e.View(w.Simulation.Time.Date);
        output.WriteLine($"beds {view.CampusBeds} + town {view.TownBeds}, seats {view.Seats}, intake {view.LastIntake} of {view.LastApplicants}");
        Assert.Contains(moveIn, m => m.StartsWith("Move-in 1825"));
        Assert.True(view.LastIntake > 0);
        Assert.Equal(before + view.LastIntake, p.StudentCount);
        Assert.Equal(view.LastIntake, Enumerable.Range(0, p.Count).Count(a => p.Kind[a] == AgentKind.Student && p.Year[a] == 1));
        Assert.True(p.StudentCount <= view.CampusBeds + view.TownBeds);
        Assert.True(p.FacultyCount >= Math.Max(3, (int)Math.Ceiling(p.StudentCount / 12.0)));
        // Everyone has a home that exists and a timetable from the 1820s plan (3 recitations).
        for (int a = 0; a < p.Count; a++)
        {
            Assert.InRange(p.Home[a], 0, w.Fields.BuildingCount - 1);
            if (p.Kind[a] == AgentKind.Student) Assert.Equal(3, p.SectionCount[a]);
        }
    }

    [Fact]
    public void StudentsAreAwayOverTheSummer_FacultyStay()
    {
        var w = Chapter1();
        RunUntil(w, new DateOnly(1825, 7, 1));
        w.Simulation.Tick(); // an hour of summer
        var p = w.Population;
        for (int a = 0; a < p.Count; a++)
            if (p.Kind[a] == AgentKind.Student) Assert.Equal(Activity.Away, p.CurrentActivity[a]);
            else Assert.NotEqual(Activity.Away, p.CurrentActivity[a]);
    }

    [Fact]
    public void AResidenceHall_HousesStudentsFromTheNextMoveIn()
    {
        var w = Chapter1(cash: 100_000);
        var g = w.Campus.Grid;
        var pl = w.Placement!;
        var house = pl.Catalog.Find("boarding_house")!;
        // A spot near Old Main with a path at the door.
        var oldMain = w.Campus.Buildings[0];
        Pose? spot = null;
        for (int r = 0; r < 40 && spot is null; r++)
            for (int dy = -r; dy <= r && spot is null; dy++)
                for (int dx = -r; dx <= r && spot is null; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    var pose = FootprintMath.Snap(oldMain.X + dx + 0.5f, oldMain.Y + dy + 0.5f, house.W, house.H, 0);
                    var q = PlacementSystem.Check(pl.Catalog, house, pose, pl.LiveMap(), w.Simulation.Time.Date);
                    if (q.Problem.StartsWith("The entrance needs a path"))
                    {
                        w.Land!.Enqueue(new LandCommand(LandAction.Path, q.Entrance % g.Width, q.Entrance / g.Width, q.Entrance % g.Width, q.Entrance / g.Width));
                        w.Simulation.ApplyPendingCommands();
                        q = PlacementSystem.Check(pl.Catalog, house, pose, pl.LiveMap(), w.Simulation.Time.Date);
                    }
                    if (q.Ok) spot = pose;
                }
        pl.Enqueue(PlacementCommand.Build(house.Id, spot!.Value));
        w.Simulation.ApplyPendingCommands();
        RunUntil(w, new DateOnly(1825, 8, 20));
        var site = Assert.Single(pl.Sites);
        Assert.True(site.Complete);
        var p = w.Population;
        var housed = Enumerable.Range(0, p.Count).Where(a => p.Kind[a] == AgentKind.Student && p.Housing[a] == HousingType.OnCampus).ToList();
        Assert.Equal(Math.Min(16, p.StudentCount), housed.Count); // 16 beds
        Assert.All(housed, a => Assert.Equal(site.CampusIndex, p.Home[a]));
        Assert.Equal(16, w.Enrollment!.View(w.Simulation.Time.Date).CampusBeds);
    }

    [Fact]
    public void Enrollment_IsDeterministicAcrossThreadCounts()
    {
        ulong Run(int threads)
        {
            var w = Chapter1(threads);
            RunUntil(w, new DateOnly(1825, 9, 1));
            var p = w.Population;
            ulong h = StateHash.Compute(w.Simulation, w.Campus.Grid);
            h = h * 31 + (ulong)p.Count;
            for (int a = 0; a < p.Count; a++) h = h * 31 + (ulong)(p.Id[a] * 7 + p.Year[a] * 3 + p.Home[a]);
            return h;
        }
        Assert.Equal(Run(1), Run(4));
    }
}
