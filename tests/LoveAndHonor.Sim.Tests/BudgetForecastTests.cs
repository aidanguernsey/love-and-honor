using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Chapter 1 balance pass: the cash forecast, the hiring pause and the finance advisor's warning.</summary>
public class BudgetForecastTests
{
    private static readonly FileSystemDataSource Source = new(RepoPaths.Data);
    private static readonly SimData Data = SimData.Load(Source);

    private static SimWorld Chapter1(double? cash = null) =>
        SimWorld.CreateScenario(Data, Source, "chapter1_the_hill", threads: 2, chunkSize: 16, startingCash: cash);

    private static void RunUntil(SimWorld w, DateOnly date)
    {
        while (w.Simulation.Time.Date < date) w.Simulation.Tick();
    }

    [Fact]
    public void Forecast_MatchesWhatHappens_WhenNothingChanges()
    {
        var w = Chapter1();
        RunUntil(w, new DateOnly(1824, 11, 2));
        var forecast = w.Budget!.Forecast(w.Simulation.Time.Date);
        Assert.Equal(12, forecast.Months.Length);
        Assert.Null(forecast.RunsOut);
        // Nothing changes the people or buildings before commencement (May 16); only chance events (a hard winter, a
        // few tens of dollars) aren't in the forecast.
        var lowest = new Dictionary<DateOnly, long>();
        while (w.Simulation.Time.Date < new DateOnly(1825, 5, 1))
        {
            var d = w.Simulation.Time.Date; // the day this tick belongs to (the clock moves on after it)
            w.Simulation.Tick();
            var month = new DateOnly(d.Year, d.Month, 1);
            lowest[month] = Math.Min(lowest.GetValueOrDefault(month, long.MaxValue), w.Land!.Treasury.Cents);
        }
        for (int m = 1; m < 6; m++)
        {
            long actual = lowest[forecast.Months[m]];
            long events = w.Land!.Treasury.Ledger.Where(e => e.Category == "other" && e.Date < forecast.Months[m].AddMonths(1)).Sum(e => e.Cents);
            Assert.Equal(forecast.LowestCents[m] + events, actual);
        }
    }

    [Fact]
    public void Forecast_SeesASpringShortfall_AndWarnsBeforeBuilding()
    {
        var w = Chapter1(cash: 600);
        RunUntil(w, new DateOnly(1825, 2, 2)); // after the land rents and the spring tuition
        var b = w.Budget!;
        var f = b.Forecast(w.Simulation.Time.Date);
        long cash = w.Land!.Treasury.Cents;
        Assert.True(cash > 0);
        // Spending all but a little now would leave nothing for the months before the next tuition (August).
        var runsOut = f.RunsOutAfterSpending(cash - 1000);
        Assert.NotNull(runsOut);
        Assert.True(runsOut < new DateOnly(1825, 8, 19));
        Assert.Null(f.RunsOutAfterSpending(0));
    }

    [Fact]
    public void FinanceAdvisor_WarnsWhenTheForecastGoesNegative()
    {
        var w = Chapter1();
        RunUntil(w, new DateOnly(1825, 2, 2));
        w.Land!.Treasury.Spend(w.Simulation.Time.Date, w.Land.Treasury.Cents - 20_000, "Test: a big building", "construction");
        RunUntil(w, new DateOnly(1825, 2, 16)); // advisors speak on the 15th
        Assert.Contains(w.Campaign!.TakeMessages(), m => m.StartsWith("VP Finance: At today's numbers our cash runs out in"));
    }

    [Fact]
    public void PausingHiring_KeepsFacultyAtTheMinimum_UntilResumed()
    {
        int FacultyAfterMoveIn(bool paused)
        {
            var w = Chapter1();
            var p = w.Population;
            if (paused) { w.Budget!.SetHiringPaused(true); w.Simulation.ApplyPendingCommands(); }
            RunUntil(w, new DateOnly(1825, 8, 18));
            // Enough students that the ratio asks for more professors than the minimum.
            while (p.StudentCount < 120)
            {
                int s = p.Add(AgentKind.Student);
                p.Year[s] = 2;
                p.Home[s] = p.CurrentBuilding[s] = p.Dining[s] = p.StudySpot[s] = p.SocialSpot[s] = p.ExerciseSpot[s] = p.Home[0];
            }
            RunUntil(w, new DateOnly(1825, 8, 20));
            Assert.Equal(paused, w.Budget!.HiringPaused);
            if (paused) Assert.Contains(w.Enrollment!.TakeMessages(), m => m.Contains("hiring paused"));
            return p.FacultyCount;
        }
        int min = EnrollmentConfig.Load(Source).Eras["founding"].MinFaculty;
        Assert.Equal(min, FacultyAfterMoveIn(paused: true));
        Assert.True(FacultyAfterMoveIn(paused: false) > min);
    }
}
