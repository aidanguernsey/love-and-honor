using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.Population;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Phase 1 (1c): academic calendar (§6.2, data/calendar.json).</summary>
public class CalendarTests
{
    private static readonly SimData Data = SimData.Load(new FileSystemDataSource(RepoPaths.Data));
    private static AcademicCalendar Cal => Data.Calendar;

    [Theory]
    [InlineData("2026-08-24", "fall_classes", true, 1)]
    [InlineData("2026-08-30", "fall_classes", true, 1)]
    [InlineData("2026-08-31", "fall_classes", true, 2)]
    [InlineData("2026-11-25", "thanksgiving_break", false, 14)]
    [InlineData("2026-12-01", "fall_classes", true, 15)]
    [InlineData("2026-12-15", "fall_finals", false, 17)]
    [InlineData("2026-12-25", "winter_break", false, 0)]
    [InlineData("2027-01-02", "winter_break", false, 0)] // wraps past New Year
    [InlineData("2027-01-26", "spring_classes", true, 1)]
    [InlineData("2027-03-16", "spring_break", false, 8)]
    [InlineData("2027-07-04", "summer", false, 0)]
    [InlineData("2026-08-20", "move_in", false, 0)]
    public void Dates_MapToPhases(string date, string phase, bool classes, int week)
    {
        var p = Cal.At(DateOnly.Parse(date));
        Assert.Equal(phase, p.Id);
        Assert.Equal(classes, p.Classes);
        Assert.Equal(classes, Cal.ClassesHeld(DateOnly.Parse(date)));
        Assert.Equal(week, p.WeekOfTerm);
    }

    [Fact]
    public void Events_AreFoundOnTheirDates()
    {
        Assert.Contains("Love & Honor Convocation", Cal.EventsOn(new DateOnly(2026, 8, 23)));
        Assert.Contains("Homecoming Weekend", Cal.EventsOn(new DateOnly(2026, 10, 10)));
        Assert.Empty(Cal.EventsOn(new DateOnly(2026, 9, 15)));
        Assert.False(Cal.Verified); // approximate dates until checked
    }

    [Fact]
    public void NoClasses_OutsideTermTime()
    {
        var world = SimWorld.CreateSynthetic(Data, threads: 2, students: 1000, faculty: 100, chunkSize: 256);
        var sim = world.Simulation;
        // The spike starts on 2026-08-24, the first day of fall classes; jump to Thanksgiving break by ticking through.
        while (sim.Time.Date < new DateOnly(2026, 11, 23)) sim.Tick();
        for (int h = 0; h < 24 * 3; h++)
        {
            sim.Tick();
            Assert.DoesNotContain(world.Population.CurrentActivity, a => a is Activity.Class or Activity.Teach);
        }
    }
}
