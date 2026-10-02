using System.Runtime.CompilerServices;
using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Population;

namespace LoveAndHonor.Sim.Engine;

/// <summary>
/// Decides where each agent should be for a given hour (§10.1 student day, §10.2 faculty day).
/// Pure function of the agent's schedule fields, the clock, and a stateless per-(agent, day, hour) random
/// value, so it's thread-safe and deterministic.
/// </summary>
public sealed class ScheduleModel
{
    private static readonly Activity[] FreeChoices = [Activity.Study, Activity.Leisure, Activity.Social, Activity.Exercise];

    private readonly ulong _seed;
    private readonly int _studentLunch, _studentDinner, _eveningStart;
    private readonly int _facultyLunch;
    private readonly int _freeBlock;
    // Cumulative thresholds in [0, 65536) over FreeChoices, one table per situation.
    private readonly int[] _weekdayFree, _weekdayEvening, _weekend;

    public ScheduleModel(ScheduleConfig cfg, RngStreams rng)
    {
        _seed = RngStreams.StableHash("schedule") ^ rng.MasterSeed;
        _studentLunch = cfg.Student.LunchHour;
        _studentDinner = cfg.Student.DinnerHour;
        _eveningStart = cfg.Student.EveningStartHour;
        _facultyLunch = cfg.Faculty.LunchHour;
        _freeBlock = Math.Max(1, cfg.Student.FreeBlockHours);
        _weekdayFree = Thresholds(cfg.Student.WeekdayFree);
        _weekdayEvening = Thresholds(cfg.Student.WeekdayEvening);
        _weekend = Thresholds(cfg.Student.Weekend);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    /// <param name="classes">False outside term time (academic calendar, §6.2): nobody goes to class.</param>
    /// <param name="studentsAway">Summer and winter break: students are away from Oxford (1h).</param>
    public short Resolve(PopulationStore p, int a, int day, int weekday, int hour, out Activity activity, bool classes = true,
        bool studentsAway = false)
    {
        if (p.Kind[a] != AgentKind.Student) return ResolveFaculty(p, a, weekday, hour, out activity, classes);
        if (studentsAway)
        {
            activity = Activity.Away;
            return p.Home[a];
        }
        return ResolveStudent(p, a, day, weekday, hour, out activity, classes);
    }

    private short ResolveStudent(PopulationStore p, int a, int day, int weekday, int hour, out Activity activity, bool classes)
    {
        bool weekend = weekday >= 5;
        if (classes && !weekend && TryClass(p, a, weekday, hour, out short classBuilding))
        {
            activity = Activity.Class;
            return classBuilding;
        }

        int wake = weekend ? p.WeekendWakeHour[a] : p.WakeHour[a];
        if (hour < wake || hour >= p.BedHour[a])
        {
            activity = Activity.Sleep;
            return p.Home[a];
        }
        if (hour == wake || hour == _studentLunch || hour == _studentDinner)
        {
            activity = Activity.Eat;
            return p.Dining[a];
        }

        int[] table = weekend ? _weekend : hour >= _eveningStart ? _weekdayEvening : _weekdayFree;
        // One roll per block of free_block_hours, staggered per student, so free time doesn't change every hour.
        int block = (hour + a % _freeBlock) / _freeBlock;
        int roll = StatelessRandom.Unit16(_seed, a, day, block);
        activity = Activity.Leisure;
        for (int i = 0; i < table.Length; i++)
            if (roll < table[i]) { activity = FreeChoices[i]; break; }

        return activity switch
        {
            Activity.Study => p.StudySpot[a],
            Activity.Social => p.SocialSpot[a],
            Activity.Exercise => p.ExerciseSpot[a],
            _ => p.Home[a],
        };
    }

    private short ResolveFaculty(PopulationStore p, int a, int weekday, int hour, out Activity activity, bool classes)
    {
        if (hour < p.WakeHour[a] || hour >= p.BedHour[a])
        {
            activity = Activity.Sleep;
            return p.Home[a];
        }
        if (weekday < 5)
        {
            if (classes && TryClass(p, a, weekday, hour, out short classBuilding))
            {
                activity = Activity.Teach;
                return classBuilding;
            }
            if (hour >= p.ArriveHour[a] && hour < p.LeaveHour[a])
            {
                if (hour == _facultyLunch)
                {
                    activity = Activity.Eat;
                    return p.Dining[a];
                }
                activity = Activity.Work;
                return p.Office[a];
            }
        }
        activity = Activity.Leisure;
        return p.Home[a];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryClass(PopulationStore p, int a, int weekday, int hour, out short building)
    {
        int baseIdx = a * PopulationStore.MaxSections;
        int count = p.SectionCount[a];
        int dayBit = 1 << weekday;
        for (int k = 0; k < count; k++)
        {
            int i = baseIdx + k;
            if (p.SectionHour[i] == hour && (p.SectionDays[i] & dayBit) != 0)
            {
                building = p.SectionBuilding[i];
                return true;
            }
        }
        building = PopulationStore.NoBuilding;
        return false;
    }

    private static int[] Thresholds(Dictionary<string, float> weights)
    {
        float total = weights.Values.Sum();
        var result = new int[FreeChoices.Length];
        float running = 0;
        for (int i = 0; i < FreeChoices.Length; i++)
        {
            running += weights.GetValueOrDefault(FreeChoices[i].ToString().ToLowerInvariant());
            result[i] = total > 0 ? (int)Math.Round(running / total * 65536.0) : 0;
        }
        return result;
    }
}
