using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Pathing;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Population;

/// <summary>data/enrollment.json (§10.1 lifecycle, §14 simplified), Phase 1 1h.</summary>
public sealed class EnrollmentConfig
{
    public string IntakeDate { get; init; } = "08-19";
    public string GraduationDate { get; init; } = "05-16";
    public float AssumedGpa { get; init; } = 3f;
    public Dictionary<string, EraSection> Eras { get; init; } = [];
    public TownBoardingSection TownBoarding { get; init; } = new();
    public Dictionary<string, int> DefaultCapacity { get; init; } = [];
    public ScholarshipSection Scholarship { get; init; } = new();

    public sealed class EraSection
    {
        public double ApplicantsPerYear { get; init; }
        public double ApplicantGrowthPerYear { get; init; } = 1;
        public double AdmitRate { get; init; }
        public double StudentFacultyRatio { get; init; }
        public int MinFaculty { get; init; }
        public int StudentSections { get; init; }
        public IntRange FacultySections { get; init; } = new();
        public int[] ClassHours { get; init; } = [];
        public string[] ClassDays { get; init; } = [];
        /// <summary>Hours when everyone goes to chapel (first academic building), on class days in term.</summary>
        public int[] ChapelHours { get; init; } = [];
        public IntRange? StudentWakeHour { get; init; }
        public IntRange? StudentBedHour { get; init; }
        public bool Verified { get; init; }
    }

    public sealed class TownBoardingSection
    {
        public double BedsPerTownTile { get; init; }
        public float MaxDistanceM { get; init; }
    }

    public sealed class ScholarshipSection
    {
        public double WorksPerYear { get; init; }
    }

    public EraSection EraFor(string eraId) => Eras.TryGetValue(eraId, out var e) ? e : Eras["default"];

    public const string File = "enrollment.json";

    public static EnrollmentConfig Load(IDataSource source) => SimJson.Parse<EnrollmentConfig>(source.ReadText(File), File);
}

/// <summary>What the UI shows about enrollment (immutable, published in snapshots when it changes).</summary>
public sealed record EnrollmentView(int Version, int Students, int Faculty, int[] ByYear, int OnCampus, int InTown,
    int CampusBeds, int TownBeds, int Seats, int LastApplicants, int LastAdmitted, int LastIntake, int LastGraduates, int LastLeavers,
    int Alumni, double Scholarship, DateOnly NextIntake, DateOnly NextCommencement);

/// <summary>
/// Enrollment for historic play (Phase 1 1h; §10.1 lifecycle, §14 simplified). Sim thread only, at hour 0:
///  - Commencement: seniors graduate (alumni), the others either leave (retention, §32 formula with happiness and
///    belonging) or move up a year; faculty scholarship for the year is tallied (§13.7 minimal).
///  - Move-in: everyone is rehoused (hall beds first, by year, then boarding in town), the year's timetable is handed
///    out (the era's plan, using the academic buildings finished by then), faculty are hired to the era's
///    student/faculty ratio, and freshmen arrive: applicants × admit rate, capped by free beds (halls + town boarding)
///    and free classroom seats.
/// Students are away over the summer and winter breaks (calendar). Deterministic: one random stream, fixed order.
/// </summary>
public sealed class EnrollmentSystem
{
    private readonly SimData _data;
    private readonly EnrollmentConfig _cfg;
    private readonly EraTable _eras;
    private readonly Campus _campus;
    private readonly FlowFieldSet _fields;
    private readonly PopulationStore _pop;
    private readonly DeterministicRng _random;
    private readonly Func<CampusBuilding, int> _capacityOf;
    private readonly int _startYear;
    private readonly List<string> _messages = [];
    private EnrollmentView? _view;
    private int _lastApplicants, _lastAdmitted, _lastIntake, _lastGraduates, _lastLeavers;

    public int Alumni { get; private set; }
    /// <summary>Scales applicants (the budget's tuition level, 1i); 1 by default.</summary>
    public Func<double> ApplicantFactor { get; set; } = () => 1.0;
    /// <summary>One-off boost to the next move-in's applicants (events, 1j); reset after use.</summary>
    public double ApplicantBoost { get; set; } = 1.0;
    /// <summary>Scales town boarding beds from now on (events, 1j).</summary>
    public double TownBoardingMultiplier { get; set; } = 1.0;

    public void AddScholarship(double works) => Scholarship += works;
    /// <summary>This year's move-in date.</summary>
    public DateOnly IntakeDateIn(int year) => On(_cfg.IntakeDate, year);
    public double Scholarship { get; private set; }
    public int Version { get; private set; }

    /// <param name="capacityOf">Seats / beds / places of a campus building (from its building definition).</param>
    public EnrollmentSystem(SimData data, EnrollmentConfig cfg, EraTable eras, Campus campus, FlowFieldSet fields, PopulationStore pop,
        RngStreams rng, Func<CampusBuilding, int> capacityOf, DateOnly start)
    {
        _data = data;
        _cfg = cfg;
        _eras = eras;
        _campus = campus;
        _fields = fields;
        _pop = pop;
        _random = rng.For("enrollment");
        _capacityOf = capacityOf;
        _startYear = start.Year;
    }

    public EnrollmentConfig Config => _cfg;

    public void WriteState(BinaryWriter w)
    {
        w.Write(Alumni); w.Write(Scholarship); w.Write(Version); w.Write(ApplicantBoost); w.Write(TownBoardingMultiplier);
        w.Write(_lastApplicants); w.Write(_lastAdmitted); w.Write(_lastIntake); w.Write(_lastGraduates); w.Write(_lastLeavers);
        var (a, b, c, d) = _random.State;
        w.Write(a); w.Write(b); w.Write(c); w.Write(d);
    }

    public void ReadState(BinaryReader r)
    {
        Alumni = r.ReadInt32(); Scholarship = r.ReadDouble(); Version = r.ReadInt32(); ApplicantBoost = r.ReadDouble(); TownBoardingMultiplier = r.ReadDouble();
        _lastApplicants = r.ReadInt32(); _lastAdmitted = r.ReadInt32(); _lastIntake = r.ReadInt32(); _lastGraduates = r.ReadInt32(); _lastLeavers = r.ReadInt32();
        _random.State = (r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64());
        _view = null;
    }

    public List<string> TakeMessages()
    {
        var m = new List<string>(_messages);
        _messages.Clear();
        return m;
    }

    private static DateOnly On(string monthDay, int year) =>
        new(year, int.Parse(monthDay[..2], System.Globalization.CultureInfo.InvariantCulture), int.Parse(monthDay[3..], System.Globalization.CultureInfo.InvariantCulture));

    private static DateOnly Next(string monthDay, DateOnly from) => On(monthDay, from.Year) >= from ? On(monthDay, from.Year) : On(monthDay, from.Year + 1);

    /// <summary>The section plan for the era of <paramref name="year"/>.</summary>
    public SectionPlan PlanFor(int year) =>
        SectionPlan.FromEra(_cfg.EraFor(_eras.At(year).Id), _data.Schedules.Student.SectionsInMajorBuilding);

    /// <summary>Call once a day at hour 0. Returns true if the population changed.</summary>
    public bool DailyUpdate(DateOnly date)
    {
        bool changed = false;
        if (date == On(_cfg.GraduationDate, date.Year)) { Commencement(date); changed = true; }
        if (date == On(_cfg.IntakeDate, date.Year)) { MoveIn(date); changed = true; }
        if (changed) Version++;
        return changed;
    }

    // ---------------- capacity ----------------

    private IEnumerable<CampusBuilding> Ready(BuildingKind kind) =>
        _campus.OfKind(kind).Where(b => b.Index < _fields.BuildingCount);

    public int CampusBeds => Ready(BuildingKind.Residence).Sum(_capacityOf);
    public int Seats => Ready(BuildingKind.Academic).Sum(_capacityOf);

    /// <summary>Beds with families in town: town land within reach of the campus.</summary>
    public int TownBeds()
    {
        var g = _campus.Grid;
        var academic = Ready(BuildingKind.Academic).ToList();
        if (academic.Count == 0) return 0;
        double cx = academic.Average(b => b.EntranceTile % g.Width), cy = academic.Average(b => b.EntranceTile / g.Width);
        float r = _cfg.TownBoarding.MaxDistanceM / g.TileSizeM;
        int town = 0;
        for (int y = Math.Max(0, (int)(cy - r)); y <= Math.Min(g.Height - 1, (int)(cy + r)); y++)
            for (int x = Math.Max(0, (int)(cx - r)); x <= Math.Min(g.Width - 1, (int)(cx + r)); x++)
                if (g.LandState[y * g.Width + x] == LandState.Town && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) town++;
        return (int)(town * _cfg.TownBoarding.BedsPerTownTile * TownBoardingMultiplier);
    }

    // ---------------- commencement ----------------

    private void Commencement(DateOnly date)
    {
        var p = _pop;
        var rc = _data.Balance.Retention;
        int graduates = 0, leavers = 0;
        int belonging = Array.IndexOf(_data.Balance.Needs.Ids, "belonging"), money = Array.IndexOf(_data.Balance.Needs.Ids, "money");
        // Faculty scholarship for the year (§13.7, minimal).
        double works = 0;
        for (int a = 0; a < p.Count; a++)
            if (p.Kind[a] == AgentKind.Faculty) works += p.ResearchScore[a] / 100.0 * _cfg.Scholarship.WorksPerYear;
        Scholarship += works;

        // Walk from the end so removals (the last agent moves into the gap) don't skip anyone.
        for (int a = p.Count - 1; a >= 0; a--)
        {
            if (p.Kind[a] != AgentKind.Student) continue;
            if (p.Year[a] >= 4) { p.RemoveAt(a); graduates++; continue; }
            float happy = p.Happiness[a];
            float bel = belonging >= 0 ? p.Needs[a * PopulationStore.NeedCount + belonging] : 50;
            float stress = money >= 0 ? 100 - p.Needs[a * PopulationStore.NeedCount + money] : 0;
            double retention = Math.Clamp(rc.Base + rc.Happiness * happy / 100 + rc.Gpa * _cfg.AssumedGpa / 4 + rc.Belonging * bel / 100
                                          - rc.FinancialStress * stress / 100, rc.Min, rc.Max);
            if (_random.NextDouble() > retention) { p.RemoveAt(a); leavers++; continue; }
            p.Year[a]++;
        }
        Alumni += graduates;
        _lastGraduates = graduates;
        _lastLeavers = leavers;
        _messages.Add($"Commencement {date.Year}: {graduates} graduate{(graduates == 1 ? "" : "s")}" +
                      (leavers > 0 ? $"; {leavers} student{(leavers == 1 ? "" : "s")} won't return" : "") +
                      $". Faculty scholarship this year: {works:0.#} works.");
    }

    // ---------------- move-in ----------------

    private void MoveIn(DateOnly date)
    {
        var p = _pop;
        var era = _cfg.EraFor(_eras.At(date.Year).Id);
        var plan = PlanFor(date.Year);
        var ctx = new PeopleContext(_data, _campus, _fields, plan);

        // Freshmen: applicants x admit rate, capped by free beds (halls + town boarding) and free seats.
        int students = p.StudentCount;
        int campusBeds = CampusBeds, townBeds = TownBeds(), seats = Seats;
        int applicants = (int)Math.Round(era.ApplicantsPerYear * Math.Pow(era.ApplicantGrowthPerYear, Math.Max(0, date.Year - _startYear)) * ApplicantFactor() * ApplicantBoost);
        ApplicantBoost = 1.0;
        int admitted = (int)Math.Round(applicants * era.AdmitRate);
        int room = Math.Max(0, Math.Min(campusBeds + townBeds - students, seats - students));
        int intake = Math.Min(admitted, Math.Min(room, p.Capacity - p.Count));
        for (int i = 0; i < intake; i++)
        {
            int a = p.Add(AgentKind.Student);
            ctx.InitNeeds(p, a, _random);
            ctx.InitStudent(p, a, 1, PopulationStore.NoBuilding, _random);
        }

        // Faculty to the era's student/faculty ratio (nobody is let go when enrollment falls).
        int faculty = p.FacultyCount;
        int wanted = Math.Max(era.MinFaculty, (int)Math.Ceiling(p.StudentCount / era.StudentFacultyRatio));
        int hires = Math.Min(Math.Max(0, wanted - faculty), p.Capacity - p.Count);
        for (int i = 0; i < hires; i++)
        {
            int a = p.Add(AgentKind.Faculty);
            ctx.InitNeeds(p, a, _random);
            ctx.InitFaculty(p, a, _random);
        }

        Rehouse(ctx, campusBeds);
        // Everyone gets this year's timetable (new academic buildings are used from now on).
        for (int a = 0; a < p.Count; a++)
            if (p.Kind[a] == AgentKind.Student) ctx.AssignStudentSections(p, a, _random);

        _lastApplicants = applicants;
        _lastAdmitted = admitted;
        _lastIntake = intake;
        string limit = intake >= admitted ? "" : room <= intake && campusBeds + townBeds - students <= seats - students
            ? " (not enough beds: build a residence hall or boarding house)" : " (not enough classroom seats)";
        _messages.Add($"Move-in {date.Year}: {intake} new student{(intake == 1 ? "" : "s")} of {applicants} applicants{limit}" +
                      (hires > 0 ? $"; {hires} new faculty" : "") + $". Enrollment {p.StudentCount}.");
    }

    /// <summary>Hall beds first (by year, then by id), the rest board in town.</summary>
    private void Rehouse(PeopleContext ctx, int campusBeds)
    {
        var p = _pop;
        var halls = ctx.Residence.Select(i => (Index: i, Free: _capacityOf(_campus.Buildings[i]))).ToArray();
        var order = Enumerable.Range(0, p.Count).Where(a => p.Kind[a] == AgentKind.Student)
            .OrderBy(a => p.Year[a]).ThenBy(a => p.Id[a]).ToList();
        int hall = 0;
        foreach (int a in order)
        {
            while (hall < halls.Length && halls[hall].Free <= 0) hall++;
            bool studier = p.StudySpot[a] != p.Home[a];
            int social = p.SocialSpot[a] == p.ExerciseSpot[a] ? 0 : p.SocialSpot[a] == p.Dining[a] ? 1 : 2;
            if (hall < halls.Length)
            {
                halls[hall].Free--;
                ctx.SetStudentHome(p, a, halls[hall].Index, true, studier, social);
            }
            else if (p.Housing[a] == HousingType.OnCampus || ctx.OffCampus.Length == 0 || !IsOffCampus(p.Home[a]))
                ctx.SetStudentHome(p, a, ctx.OffCampusHome(_random), false, studier, social);
            else
                ctx.SetStudentHome(p, a, p.Home[a], false, studier, social); // keeps boarding where they are
        }

        bool IsOffCampus(short b) => b >= 0 && b < _campus.Buildings.Count && _campus.Buildings[b].Kind == BuildingKind.OffCampusHousing;
    }

    public EnrollmentView View(DateOnly today)
    {
        if (_view is { } v && v.Version == Version && v.NextIntake >= today && v.NextCommencement >= today) return v;
        var p = _pop;
        var byYear = new int[4];
        int onCampus = 0;
        for (int a = 0; a < p.Count; a++)
        {
            if (p.Kind[a] != AgentKind.Student) continue;
            byYear[Math.Clamp(p.Year[a] - 1, 0, 3)]++;
            if (p.Housing[a] == HousingType.OnCampus) onCampus++;
        }
        return _view = new EnrollmentView(Version, p.StudentCount, p.FacultyCount, byYear, onCampus, p.StudentCount - onCampus,
            CampusBeds, TownBeds(), Seats, _lastApplicants, _lastAdmitted, _lastIntake, _lastGraduates, _lastLeavers, Alumni, Scholarship,
            Next(_cfg.IntakeDate, today), Next(_cfg.GraduationDate, today));
    }
}
