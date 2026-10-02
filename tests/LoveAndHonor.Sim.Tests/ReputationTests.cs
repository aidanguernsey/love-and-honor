using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Chapter 1 balance pass: Reputation (§9 stand-in) and the Heritage Projects' own upkeep.</summary>
public class ReputationTests
{
    private static readonly FileSystemDataSource Source = new(RepoPaths.Data);
    private static readonly SimData Data = SimData.Load(Source);

    private static SimWorld Chapter1() =>
        SimWorld.CreateScenario(Data, Source, "chapter1_the_hill", threads: 2, chunkSize: 16, startingCash: 20_000);

    private static void RunUntil(SimWorld w, DateOnly date)
    {
        while (w.Simulation.Time.Date < date) w.Simulation.Tick();
    }

    [Fact]
    public void StartsAtTheOpeningQuality_WithNeutralApplicants()
    {
        var rep = Chapter1().Reputation!;
        Assert.Equal(rep.Quality, rep.Reputation, 6);
        Assert.Equal(1.0, rep.ApplicantFactor, 6);
        Assert.InRange(rep.Quality, 20, 80);
        Assert.Equal(1.0, rep.Parts.Sum(p => p.Weight), 6);
        // Everyone boards in town and there's no steward's hall yet.
        Assert.Equal(0, rep.Parts.Single(p => p.Id == "housing").Score);
        Assert.Equal(0, rep.Parts.Single(p => p.Id == "dining").Score);
    }

    [Fact]
    public void AHall_RaisesQuality_AndReputationDriftsTowardItEachMonth()
    {
        var w = Chapter1();
        var rep = w.Reputation!;
        var p = w.Population;
        w.Simulation.Tick(); // past Nov 1's monthly update
        double before = rep.Quality;
        // Everyone moves into a university hall (a stand-in for building one and the move-in that fills it).
        for (int a = 0; a < p.Count; a++)
            if (p.Kind[a] == AgentKind.Student) p.Housing[a] = HousingType.OnCampus;
        RunUntil(w, new DateOnly(1824, 12, 1));
        double reputationBefore = rep.Reputation;
        w.Simulation.Tick(); // hour 0 of Dec 1: the monthly update
        Assert.Equal(100, rep.Parts.Single(x => x.Id == "housing").Score);
        Assert.True(rep.Quality > before + 15);
        double monthly = 1 - Math.Pow(1 - rep.Config.DriftPerYear, 1.0 / 12);
        Assert.Equal(reputationBefore + (rep.Quality - reputationBefore) * monthly, rep.Reputation, 6);
        Assert.True(rep.ApplicantFactor > 1);
        Assert.Equal(1 + rep.Config.Applicants.PerPoint * (rep.Reputation - rep.StartReputation), rep.ApplicantFactor, 9);
    }

    [Fact]
    public void ApplicantsFollowReputation_AtMoveIn()
    {
        int Applicants(bool halls)
        {
            var w = Chapter1();
            var p = w.Population;
            // Keep students in halls all year (a hall-sized stand-in), so Reputation climbs before the 1825 move-in.
            while (w.Simulation.Time.Date < new DateOnly(1825, 8, 20))
            {
                if (halls && w.Simulation.Time.HourOfDay == 23)
                    for (int a = 0; a < p.Count; a++)
                        if (p.Kind[a] == AgentKind.Student) p.Housing[a] = HousingType.OnCampus;
                w.Simulation.Tick();
            }
            return w.Enrollment!.View(w.Simulation.Time.Date).LastApplicants;
        }
        Assert.True(Applicants(halls: true) > Applicants(halls: false));
    }

    [Fact]
    public void ElliottBuiltAsAHeritageProject_UsesTheProjectsUpkeep_AndItsRealId()
    {
        var w = Chapter1();
        var cfg = HeritageProjectsConfig.Load(Source).Projects.Single(x => x.Id == "elliott_hall");
        Assert.NotNull(cfg.UpkeepUsdPerYear);
        var old = w.Campus.Buildings[0];
        // What Simulation.AddFinishedBuilding adds for a finished Heritage Project.
        w.Campus.Add(new CampusBuilding { Index = (short)w.Campus.Buildings.Count, DefId = "elliott_hall", Kind = BuildingKind.Residence,
            X = old.X, Y = old.Y, W = 1, H = 1, EntranceTile = old.EntranceTile });
        RunUntil(w, new DateOnly(1824, 12, 1));
        w.Simulation.Tick();
        long upkeep = -w.Land!.Treasury.Ledger.Last(e => e.Category == "upkeep").Cents;
        // Old Main (college_building) plus Elliott at the project's figure, x the 1820s price multiplier, a twelfth.
        double multiplier = EraTable.Load(Source).At(1824).PriceMultiplier;
        double expected = (Data.Buildings["college_building"].UpkeepUsdPerYear + cfg.UpkeepUsdPerYear!.Value) * multiplier / 12;
        Assert.Equal((long)Math.Round(expected * 100), upkeep);
        Assert.True(cfg.UpkeepUsdPerYear.Value * multiplier < 100, "an 1820s brick hall costs tens of dollars a year to keep, not thousands");
    }
}
