namespace LoveAndHonor.Sim.Core;

/// <summary>Game clock. 1 tick = 1 in-game hour (§30.2). Tick 0 is 00:00 on the start date.</summary>
public sealed class SimTime(DateOnly startDate)
{
    public DateOnly StartDate { get; } = startDate;
    public int Tick { get; private set; }

    public int HourOfDay => Tick % 24;
    public int Day => Tick / 24;
    public DateOnly Date => StartDate.AddDays(Day);

    /// <summary>0 = Monday … 6 = Sunday (ISO order, matches the schedule day bitmask).</summary>
    public int WeekdayIndex => ((int)Date.DayOfWeek + 6) % 7;
    public bool IsWeekend => WeekdayIndex >= 5;

    /// <summary>Absolute game minute at the start of the current tick.</summary>
    public int TickStartMinute => Tick * 60;

    public void Advance() => Tick++;

    /// <summary>Sets the clock when a save is loaded (§31).</summary>
    public void Restore(int tick) => Tick = tick;

    public override string ToString() => $"{Date:yyyy-MM-dd} ({Date.DayOfWeek}) {HourOfDay:00}:00";
}
