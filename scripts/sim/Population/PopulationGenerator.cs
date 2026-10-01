using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Pathing;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Population;

/// <summary>
/// Creates the Spike A population: students and faculty with homes, majors/departments, class sections and
/// daily rhythms. Deterministic for a given seed. Capacity is not enforced (see population_spike.json).
/// </summary>
public static class PopulationGenerator
{
    private static readonly string[] DayKeys = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];
    private static readonly FacultyRank[] RankOrder =
        [FacultyRank.Lecturer, FacultyRank.Assistant, FacultyRank.Associate, FacultyRank.Full, FacultyRank.Distinguished];

    public static PopulationStore Generate(SimData data, Campus campus, FlowFieldSet fields, RngStreams rng,
        int? studentsOverride = null, int? facultyOverride = null)
    {
        int students = studentsOverride ?? data.Balance.Population.TargetStudents;
        int faculty = facultyOverride ?? data.Balance.Population.TargetFaculty;
        var pop = new PopulationStore(students, faculty);
        var random = rng.For("population");
        var sched = data.Schedules;
        var spike = data.Spike;
        var needs = data.Balance.Needs;

        short[] academic = Indices(campus, BuildingKind.Academic);
        short[] residence = Indices(campus, BuildingKind.Residence);
        short[] offCampus = Indices(campus, BuildingKind.OffCampusHousing);
        short[] dining = Indices(campus, BuildingKind.Dining);
        short[] library = Indices(campus, BuildingKind.Library);
        short[] recreation = Indices(campus, BuildingKind.Recreation);
        // A campus needs somewhere to teach and somewhere to live. Everything else falls back (an early campus such as
        // 1824 has one building): no dining hall → meals at home (boarding), no library → study at home, no
        // recreation → exercise near home, no residence halls → everyone lives in town.
        if (academic.Length == 0 || (residence.Length == 0 && offCampus.Length == 0))
            throw new InvalidOperationException("A campus needs at least one academic building and somewhere to live.");

        int deptCount = data.Departments.Departments.Length;
        short DeptBuilding(int dept) => academic[dept % academic.Length];
        short Nearest(short from, short[] candidates)
        {
            if (candidates.Length == 0) return from;
            short best = candidates[0];
            foreach (var c in candidates)
                if (fields.Distance(from, c) < fields.Distance(from, best)) best = c;
            return best;
        }

        // Off-campus homes are picked in proportion to Weight (real-map housing zones stand for different numbers
        // of houses); with equal weights this is a plain uniform pick, as in Spike A.
        float[] offWeights = offCampus.Select(i => campus.Buildings[i].Weight).ToArray();
        bool weighted = offWeights.Any(x => x != offWeights[0]);
        short OffCampusHome() => offCampus.Length == 0 ? residence[random.NextInt(residence.Length)]
            : weighted ? offCampus[PickWeighted(offWeights, random)] : offCampus[random.NextInt(offCampus.Length)];

        var slots = BuildSlots(sched.ClassSlots);
        var onCampusYears = new HashSet<int>(spike.Students.OnCampusYears);

        for (int a = 0; a < pop.Count; a++)
        {
            pop.Id[a] = a;
            pop.CurrentActivity[a] = Activity.Sleep;
            for (int k = 0; k < PopulationStore.NeedCount; k++)
                pop.Needs[a * PopulationStore.NeedCount + k] = Lerp(needs.InitialMin, needs.InitialMax, random.NextFloat());
        }

        // ---- Students ----
        var st = sched.Student;
        for (int a = 0; a < students; a++)
        {
            int year = 1 + PickWeighted(spike.Students.YearShares, random);
            int dept = random.NextInt(deptCount);
            pop.Kind[a] = AgentKind.Student;
            pop.Year[a] = (byte)year;
            pop.Age[a] = (byte)(17 + year + random.NextInt(2));
            pop.Department[a] = (ushort)dept;

            bool onCampus = onCampusYears.Contains(year);
            short home = onCampus && residence.Length > 0 ? residence[random.NextInt(residence.Length)] : OffCampusHome();
            onCampus = residence.Length > 0 && onCampus;
            pop.Housing[a] = onCampus ? HousingType.OnCampus : HousingType.OffCampus;
            pop.Home[a] = home;
            pop.CurrentBuilding[a] = home;
            pop.Dining[a] = Nearest(home, dining);
            pop.StudySpot[a] = random.NextDouble() < st.StudyAtLibraryProbability ? Nearest(home, library) : home;
            pop.ExerciseSpot[a] = Nearest(home, recreation);
            pop.SocialSpot[a] = random.NextInt(3) switch { 0 => pop.ExerciseSpot[a], 1 => pop.Dining[a], _ => home };

            pop.WakeHour[a] = (byte)random.NextInt(st.WakeHour.Min, st.WakeHour.Max + 1);
            pop.BedHour[a] = (byte)random.NextInt(st.BedHour.Min, st.BedHour.Max + 1);
            pop.WeekendWakeHour[a] = (byte)random.NextInt(st.WeekendWakeHour.Min, st.WeekendWakeHour.Max + 1);

            int count = Math.Min(st.Sections, PopulationStore.MaxSections);
            AssignSections(pop, a, count, slots, 0, 24, random,
                k => k < st.SectionsInMajorBuilding ? DeptBuilding(dept) : academic[random.NextInt(academic.Length)]);
        }

        // ---- Faculty ----
        var fc = sched.Faculty;
        float[] rankShares = RankOrder.Select(r => spike.Faculty.RankShares.GetValueOrDefault(r.ToString().ToLowerInvariant())).ToArray();
        for (int a = students; a < pop.Count; a++)
        {
            int dept = random.NextInt(deptCount);
            short office = DeptBuilding(dept);
            short home = OffCampusHome();
            pop.Kind[a] = AgentKind.Faculty;
            pop.Department[a] = (ushort)dept;
            pop.Rank[a] = RankOrder[PickWeighted(rankShares, random)];
            pop.Age[a] = (byte)random.NextInt(28, 71);
            pop.TeachingScore[a] = (byte)random.NextInt(spike.Faculty.ScoreMin, spike.Faculty.ScoreMax + 1);
            pop.ResearchScore[a] = (byte)random.NextInt(spike.Faculty.ScoreMin, spike.Faculty.ScoreMax + 1);
            pop.Housing[a] = HousingType.OffCampus;
            pop.Home[a] = home;
            pop.CurrentBuilding[a] = home;
            pop.Office[a] = office;
            pop.Dining[a] = Nearest(office, dining);
            pop.StudySpot[a] = office;
            pop.SocialSpot[a] = home;
            pop.ExerciseSpot[a] = Nearest(home, recreation);

            pop.WakeHour[a] = (byte)random.NextInt(fc.WakeHour.Min, fc.WakeHour.Max + 1);
            pop.BedHour[a] = (byte)random.NextInt(fc.BedHour.Min, fc.BedHour.Max + 1);
            pop.ArriveHour[a] = (byte)random.NextInt(fc.ArriveHour.Min, fc.ArriveHour.Max + 1);
            pop.LeaveHour[a] = (byte)random.NextInt(fc.LeaveHour.Min, fc.LeaveHour.Max + 1);

            int count = Math.Min(random.NextInt(fc.Sections.Min, fc.Sections.Max + 1), PopulationStore.MaxSections);
            AssignSections(pop, a, count, slots, pop.ArriveHour[a], pop.LeaveHour[a], random, _ => office);
        }

        return pop;
    }

    private static List<(byte days, byte hour)> BuildSlots(ScheduleConfig.ClassSlotsSection cfg)
    {
        var slots = new List<(byte, byte)>();
        foreach (var (_, days) in cfg.Patterns.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            byte mask = 0;
            foreach (var d in days) mask |= (byte)(1 << Array.IndexOf(DayKeys, d));
            for (int h = cfg.FirstHour; h <= cfg.LastHour; h++) slots.Add((mask, (byte)h));
        }
        return slots;
    }

    /// <summary>Picks non-overlapping class slots with hours in [minHour, maxHourExclusive).</summary>
    private static void AssignSections(PopulationStore pop, int a, int count, List<(byte days, byte hour)> slots,
        int minHour, int maxHourExclusive, DeterministicRng random, Func<int, short> buildingFor)
    {
        int assigned = 0, attempts = 0, baseIdx = a * PopulationStore.MaxSections;
        while (assigned < count && attempts++ < 64)
        {
            var (days, hour) = slots[random.NextInt(slots.Count)];
            if (hour < minHour || hour >= maxHourExclusive) continue;
            bool clash = false;
            for (int k = 0; k < assigned; k++)
                if (pop.SectionHour[baseIdx + k] == hour && (pop.SectionDays[baseIdx + k] & days) != 0) { clash = true; break; }
            if (clash) continue;
            pop.SectionDays[baseIdx + assigned] = days;
            pop.SectionHour[baseIdx + assigned] = hour;
            pop.SectionBuilding[baseIdx + assigned] = buildingFor(assigned);
            assigned++;
        }
        pop.SectionCount[a] = (byte)assigned;
    }

    private static int PickWeighted(float[] weights, DeterministicRng random)
    {
        float total = weights.Sum();
        double r = random.NextDouble() * total;
        for (int i = 0; i < weights.Length; i++)
        {
            r -= weights[i];
            if (r < 0) return i;
        }
        return weights.Length - 1;
    }

    private static short[] Indices(Campus campus, BuildingKind kind) => campus.OfKind(kind).Select(b => b.Index).ToArray();
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
