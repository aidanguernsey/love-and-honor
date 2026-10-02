using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Pathing;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Population;

/// <summary>Class slots and how many sections people take (schedules.json, or an era's plan from enrollment.json).</summary>
public sealed class SectionPlan
{
    private static readonly string[] DayKeys = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    public required List<(byte Days, byte Hour)> Slots { get; init; }
    public required int StudentSections { get; init; }
    public required int MajorBuildingSections { get; init; }
    public required int FacultyMin { get; init; }
    public required int FacultyMax { get; init; }
    /// <summary>Chapel hours on class days (empty = none).</summary>
    public int[] ChapelHours { get; init; } = [];
    /// <summary>Saturday is a class day (the weekend is only Sunday).</summary>
    public bool SaturdayClasses { get; init; }
    /// <summary>Era overrides for students' rising and bed hours (schedules.json otherwise).</summary>
    public IntRange? StudentWakeHour { get; init; }
    public IntRange? StudentBedHour { get; init; }

    public static SectionPlan FromSchedules(ScheduleConfig cfg)
    {
        var slots = new List<(byte, byte)>();
        foreach (var (_, days) in cfg.ClassSlots.Patterns.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            byte mask = Mask(days);
            for (int h = cfg.ClassSlots.FirstHour; h <= cfg.ClassSlots.LastHour; h++) slots.Add((mask, (byte)h));
        }
        return new SectionPlan
        {
            Slots = slots, StudentSections = cfg.Student.Sections, MajorBuildingSections = cfg.Student.SectionsInMajorBuilding,
            FacultyMin = cfg.Faculty.Sections.Min, FacultyMax = cfg.Faculty.Sections.Max,
        };
    }

    /// <summary>One slot per class hour, meeting on all the era's class days (an 1820s recitation timetable).</summary>
    public static SectionPlan FromEra(EnrollmentConfig.EraSection era, int majorBuildingSections) => new()
    {
        Slots = era.ClassHours.Select(h => (Mask(era.ClassDays), (byte)h)).ToList(),
        StudentSections = era.StudentSections, MajorBuildingSections = majorBuildingSections,
        FacultyMin = era.FacultySections.Min, FacultyMax = era.FacultySections.Max,
        ChapelHours = era.ChapelHours, SaturdayClasses = era.ClassDays.Contains("sat"),
        StudentWakeHour = era.StudentWakeHour, StudentBedHour = era.StudentBedHour,
    };

    private static byte Mask(IEnumerable<string> days)
    {
        byte mask = 0;
        foreach (var d in days) mask |= (byte)(1 << Array.IndexOf(DayKeys, d));
        return mask;
    }
}

/// <summary>
/// What's needed to set up a person on the current campus: the buildings usable now (only those whose flow fields
/// exist), the section plan, and the data tables. Used for the starting population and for each year's intake and
/// hires (1h). Deterministic for a given random stream.
/// </summary>
public sealed class PeopleContext
{
    private static readonly FacultyRank[] RankOrder =
        [FacultyRank.Lecturer, FacultyRank.Assistant, FacultyRank.Associate, FacultyRank.Full, FacultyRank.Distinguished];

    private readonly SimData _data;
    private readonly FlowFieldSet _fields;
    private readonly float[] _offWeights;
    private readonly bool _weighted;
    private readonly float[] _rankShares;
    private readonly int _deptCount;

    public SectionPlan Plan { get; }
    public short[] Academic { get; }
    public short[] Residence { get; }
    public short[] OffCampus { get; }
    public short[] Dining { get; }
    public short[] Library { get; }
    public short[] Recreation { get; }
    public Campus Campus { get; }

    public PeopleContext(SimData data, Campus campus, FlowFieldSet fields, SectionPlan plan)
    {
        _data = data;
        _fields = fields;
        Campus = campus;
        Plan = plan;
        int ready = fields.BuildingCount; // buildings finished since the last flow-field swap aren't usable yet
        short[] Of(BuildingKind kind) => campus.OfKind(kind).Where(b => b.Index < ready).Select(b => b.Index).ToArray();
        Academic = Of(BuildingKind.Academic);
        Residence = Of(BuildingKind.Residence);
        OffCampus = Of(BuildingKind.OffCampusHousing);
        Dining = Of(BuildingKind.Dining);
        Library = Of(BuildingKind.Library);
        Recreation = Of(BuildingKind.Recreation);
        // A campus needs somewhere to teach and somewhere to live. Everything else falls back (an early campus such as
        // 1824 has one building): no dining hall → meals at home (boarding), no library → study at home, no
        // recreation → exercise near home, no residence halls → everyone lives in town.
        if (Academic.Length == 0 || (Residence.Length == 0 && OffCampus.Length == 0))
            throw new InvalidOperationException("A campus needs at least one academic building and somewhere to live.");
        _offWeights = OffCampus.Select(i => campus.Buildings[i].Weight).ToArray();
        _weighted = _offWeights.Any(x => x != _offWeights[0]);
        _rankShares = RankOrder.Select(r => data.Spike.Faculty.RankShares.GetValueOrDefault(r.ToString().ToLowerInvariant())).ToArray();
        _deptCount = data.Departments.Departments.Length;
    }

    private short DeptBuilding(int dept) => Academic[dept % Academic.Length];

    public short Nearest(short from, short[] candidates)
    {
        if (candidates.Length == 0) return from;
        short best = candidates[0];
        foreach (var c in candidates)
            if (_fields.Distance(from, c) < _fields.Distance(from, best)) best = c;
        return best;
    }

    /// <summary>Off-campus homes are picked in proportion to Weight (a housing zone stands for many houses).</summary>
    public short OffCampusHome(DeterministicRng random) =>
        OffCampus.Length == 0 ? Residence[random.NextInt(Residence.Length)]
        : _weighted ? OffCampus[PopulationGenerator.PickWeighted(_offWeights, random)] : OffCampus[random.NextInt(OffCampus.Length)];

    /// <summary>Sets a student's home and the places that follow from it (dining, study spot, exercise, social).</summary>
    public void SetStudentHome(PopulationStore pop, int a, short home, bool onCampus, bool libraryStudier, int socialChoice)
    {
        pop.Housing[a] = onCampus ? HousingType.OnCampus : HousingType.OffCampus;
        pop.Home[a] = home;
        pop.Dining[a] = Nearest(home, Dining);
        pop.StudySpot[a] = libraryStudier ? Nearest(home, Library) : home;
        pop.ExerciseSpot[a] = Nearest(home, Recreation);
        pop.SocialSpot[a] = socialChoice switch { 0 => pop.ExerciseSpot[a], 1 => pop.Dining[a], _ => home };
    }

    public void InitNeeds(PopulationStore pop, int a, DeterministicRng random)
    {
        var needs = _data.Balance.Needs;
        pop.CurrentActivity[a] = Activity.Sleep;
        for (int k = 0; k < PopulationStore.NeedCount; k++)
            pop.Needs[a * PopulationStore.NeedCount + k] = needs.InitialMin + (needs.InitialMax - needs.InitialMin) * random.NextFloat();
    }

    /// <param name="onCampusHome">A residence hall to live in, or NoBuilding to board in town.</param>
    public void InitStudent(PopulationStore pop, int a, int year, short onCampusHome, DeterministicRng random)
    {
        var st = _data.Schedules.Student;
        int dept = random.NextInt(_deptCount);
        pop.Kind[a] = AgentKind.Student;
        pop.Year[a] = (byte)year;
        pop.Age[a] = (byte)(17 + year + random.NextInt(2));
        pop.Department[a] = (ushort)dept;
        bool onCampus = onCampusHome != PopulationStore.NoBuilding;
        short home = onCampus ? onCampusHome : OffCampusHome(random);
        bool studier = random.NextDouble() < st.StudyAtLibraryProbability;
        SetStudentHome(pop, a, home, onCampus, studier, random.NextInt(3));
        pop.CurrentBuilding[a] = home;
        var wake = Plan.StudentWakeHour ?? st.WakeHour;
        var bed = Plan.StudentBedHour ?? st.BedHour;
        pop.WakeHour[a] = (byte)random.NextInt(wake.Min, wake.Max + 1);
        pop.BedHour[a] = (byte)random.NextInt(bed.Min, bed.Max + 1);
        pop.WeekendWakeHour[a] = (byte)random.NextInt(st.WeekendWakeHour.Min, st.WeekendWakeHour.Max + 1);
        AssignStudentSections(pop, a, random);
    }

    public void AssignStudentSections(PopulationStore pop, int a, DeterministicRng random)
    {
        int dept = pop.Department[a];
        int count = Math.Min(Plan.StudentSections, PopulationStore.MaxSections);
        AssignSections(pop, a, count, 0, 24, random,
            k => k < Plan.MajorBuildingSections ? DeptBuilding(dept) : Academic[random.NextInt(Academic.Length)]);
    }

    public void InitFaculty(PopulationStore pop, int a, DeterministicRng random)
    {
        var fc = _data.Schedules.Faculty;
        var spike = _data.Spike;
        int dept = random.NextInt(_deptCount);
        short office = DeptBuilding(dept);
        short home = OffCampusHome(random);
        pop.Kind[a] = AgentKind.Faculty;
        pop.Department[a] = (ushort)dept;
        pop.Rank[a] = RankOrder[PopulationGenerator.PickWeighted(_rankShares, random)];
        pop.Age[a] = (byte)random.NextInt(28, 71);
        pop.TeachingScore[a] = (byte)random.NextInt(spike.Faculty.ScoreMin, spike.Faculty.ScoreMax + 1);
        pop.ResearchScore[a] = (byte)random.NextInt(spike.Faculty.ScoreMin, spike.Faculty.ScoreMax + 1);
        pop.Housing[a] = HousingType.OffCampus;
        pop.Home[a] = home;
        pop.CurrentBuilding[a] = home;
        pop.Office[a] = office;
        pop.Dining[a] = Nearest(office, Dining);
        pop.StudySpot[a] = office;
        pop.SocialSpot[a] = home;
        pop.ExerciseSpot[a] = Nearest(home, Recreation);
        pop.WakeHour[a] = (byte)random.NextInt(fc.WakeHour.Min, fc.WakeHour.Max + 1);
        pop.BedHour[a] = (byte)random.NextInt(fc.BedHour.Min, fc.BedHour.Max + 1);
        pop.ArriveHour[a] = (byte)random.NextInt(fc.ArriveHour.Min, fc.ArriveHour.Max + 1);
        pop.LeaveHour[a] = (byte)random.NextInt(fc.LeaveHour.Min, fc.LeaveHour.Max + 1);
        int count = Math.Min(random.NextInt(Plan.FacultyMin, Plan.FacultyMax + 1), PopulationStore.MaxSections);
        AssignSections(pop, a, count, pop.ArriveHour[a], pop.LeaveHour[a], random, _ => office);
    }

    /// <summary>Picks non-overlapping class slots with hours in [minHour, maxHourExclusive).</summary>
    private void AssignSections(PopulationStore pop, int a, int count, int minHour, int maxHourExclusive, DeterministicRng random,
        Func<int, short> buildingFor)
    {
        var slots = Plan.Slots;
        int assigned = 0, attempts = 0, baseIdx = a * PopulationStore.MaxSections;
        while (assigned < count && attempts++ < 64 && slots.Count > 0)
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
}

/// <summary>
/// Creates the starting population: students and faculty with homes, majors/departments, class sections and daily
/// rhythms. Deterministic for a given seed. Capacity is not enforced for the Spike A / 2026 worlds (see
/// population_spike.json); historic scenarios hand out housing through enrollment (1h).
/// </summary>
public static class PopulationGenerator
{
    /// <param name="capacity">Room for growth (enrollment); default = the starting population.</param>
    /// <param name="plan">Class slots and section counts; default = schedules.json.</param>
    public static PopulationStore Generate(SimData data, Campus campus, FlowFieldSet fields, RngStreams rng,
        int? studentsOverride = null, int? facultyOverride = null, int? capacity = null, SectionPlan? plan = null)
    {
        int students = studentsOverride ?? data.Balance.Population.TargetStudents;
        int faculty = facultyOverride ?? data.Balance.Population.TargetFaculty;
        var pop = new PopulationStore(students, faculty, capacity);
        var random = rng.For("population");
        var ctx = new PeopleContext(data, campus, fields, plan ?? SectionPlan.FromSchedules(data.Schedules));
        var onCampusYears = new HashSet<int>(data.Spike.Students.OnCampusYears);

        for (int a = 0; a < pop.Count; a++)
        {
            pop.Id[a] = a;
            ctx.InitNeeds(pop, a, random);
        }

        for (int a = 0; a < students; a++)
        {
            int year = 1 + PickWeighted(data.Spike.Students.YearShares, random);
            // Spike A / 2026: the configured years live in halls (capacity not enforced).
            short hall = onCampusYears.Contains(year) && ctx.Residence.Length > 0 ? ctx.Residence[random.NextInt(ctx.Residence.Length)] : PopulationStore.NoBuilding;
            ctx.InitStudent(pop, a, year, hall, random);
        }
        for (int a = students; a < pop.Count; a++) ctx.InitFaculty(pop, a, random);
        return pop;
    }

    internal static int PickWeighted(float[] weights, DeterministicRng random)
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
}
