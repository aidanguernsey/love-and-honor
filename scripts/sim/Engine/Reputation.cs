using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Engine;

/// <summary>data/reputation.json (§9, §32), the historic-play stand-in.</summary>
public sealed class ReputationConfig
{
    public double DriftPerYear { get; init; } = 0.1;
    public Dictionary<string, double> Weights { get; init; } = [];
    public double ClassroomComfort { get; init; } = 0.75;
    public double HeritageForFull { get; init; } = 20;
    public ApplicantsSection Applicants { get; init; } = new();

    public sealed class ApplicantsSection
    {
        public double PerPoint { get; init; }
        public double MinFactor { get; init; } = 0.4;
        public double MaxFactor { get; init; } = 2.5;
    }

    public const string File = "reputation.json";

    public static ReputationConfig Load(IDataSource source) => SimJson.Parse<ReputationConfig>(source.ReadText(File), File);
}

/// <summary>One part of Quality: id, display name, score 0-100, normalised weight, and a short note for the player.</summary>
public sealed record QualityPart(string Id, string Name, double Score, double Weight, string Note);

/// <summary>What the UI shows about Reputation (immutable; published in snapshots).</summary>
public sealed record ReputationView(int Version, double Reputation, double Quality, QualityPart[] Parts, double ApplicantFactor);

/// <summary>
/// Reputation for historic play (§9), the Chapter 1 stand-in until Phase 2's full version. Sim thread only, at hour 0
/// on the 1st of each month (after enrollment and the budget): Quality (0-100) is a weighted mix of what the player
/// can shape now (teaching, students per professor, hall housing, classroom crowding, dining, happiness, Heritage),
/// and Reputation moves toward it by <see cref="ReputationConfig.DriftPerYear"/> a year (§32, lagging perception).
/// Applicants scale with Reputation relative to where it started, so the scenario's applicant numbers stay the
/// baseline. Deterministic (no randomness).
/// </summary>
public sealed class ReputationSystem
{
    private static readonly (string Id, string Name)[] PartNames =
    [
        ("teaching", "Teaching"), ("faculty_ratio", "Students per professor"), ("housing", "Students in halls"),
        ("classrooms", "Classroom space"), ("dining", "Dining"), ("happiness", "Student happiness"), ("heritage", "Heritage"),
    ];

    private readonly ReputationConfig _cfg;
    private readonly PopulationStore _pop;
    private readonly EnrollmentSystem _enrollment;
    private readonly Func<double> _heritage;
    private ReputationView? _view;

    public double Reputation { get; private set; }
    public double StartReputation { get; private set; }
    public double Quality { get; private set; }
    public QualityPart[] Parts { get; private set; } = [];
    public int Version { get; private set; }
    public ReputationConfig Config => _cfg;

    /// <param name="heritage">Heritage earned so far (Heritage Projects on their sites, the Slant Walk).</param>
    public ReputationSystem(ReputationConfig cfg, PopulationStore pop, EnrollmentSystem enrollment, Func<double> heritage, DateOnly start)
    {
        _cfg = cfg;
        _pop = pop;
        _enrollment = enrollment;
        _heritage = heritage;
        Measure(start);
        Reputation = StartReputation = Quality;
    }

    /// <summary>Applicants multiplier: 1 + per_point × (Reputation − start), clamped.</summary>
    public double ApplicantFactor => Math.Clamp(1 + _cfg.Applicants.PerPoint * (Reputation - StartReputation),
        _cfg.Applicants.MinFactor, _cfg.Applicants.MaxFactor);

    public void WriteState(BinaryWriter w)
    {
        w.Write(Reputation); w.Write(StartReputation); w.Write(Version);
    }

    public void ReadState(BinaryReader r, DateOnly today)
    {
        Reputation = r.ReadDouble(); StartReputation = r.ReadDouble(); Version = r.ReadInt32();
        Measure(today);
        _view = null;
    }

    /// <summary>Saves from before Reputation existed: start where Quality is now.</summary>
    public void ResetTo(DateOnly today)
    {
        Measure(today);
        Reputation = StartReputation = Quality;
        _view = null;
    }

    /// <summary>Call once a day at hour 0, after enrollment and the budget.</summary>
    public void DailyUpdate(DateOnly date)
    {
        if (date.Day != 1) return;
        Measure(date);
        double monthly = 1 - Math.Pow(1 - _cfg.DriftPerYear, 1.0 / 12);
        Reputation += (Quality - Reputation) * monthly;
        Version++;
    }

    /// <summary>Recomputes Quality and its parts from the live state.</summary>
    public void Measure(DateOnly date)
    {
        var p = _pop;
        int students = 0, inHalls = 0, faculty = 0;
        double teaching = 0, happiness = 0;
        for (int a = 0; a < p.Count; a++)
        {
            if (p.Kind[a] == AgentKind.Faculty) { faculty++; teaching += p.TeachingScore[a]; }
            else if (p.Kind[a] == AgentKind.Student)
            {
                students++;
                happiness += p.Happiness[a];
                if (p.Housing[a] == HousingType.OnCampus) inHalls++;
            }
        }
        int seats = _enrollment.Seats, places = _enrollment.CapacityOfKind(BuildingKind.Dining);
        double target = _enrollment.TargetRatio(date);
        double ratio = faculty > 0 ? (double)students / faculty : double.PositiveInfinity;
        double crowding = seats > 0 ? (double)students / seats : 1;
        double heritage = _heritage();

        var scores = new Dictionary<string, (double Score, string Note)>
        {
            ["teaching"] = (faculty > 0 ? teaching / faculty : 0, $"{faculty} faculty, mean teaching score {(faculty > 0 ? teaching / faculty : 0):0}"),
            ["faculty_ratio"] = (students == 0 ? 100 : 100 * Math.Min(1, target / ratio), $"{(faculty > 0 ? ratio : 0):0.#} students per professor (the era's norm: {target:0})"),
            ["housing"] = (students > 0 ? 100.0 * inHalls / students : 0, $"{inHalls} of {students} students in university halls"),
            ["classrooms"] = (100 * Math.Clamp((1 - crowding) / (1 - _cfg.ClassroomComfort), 0, 1), $"{students} students for {seats} seats"),
            ["dining"] = (students > 0 ? 100 * Math.Min(1, (double)places / students) : 0, places > 0 ? $"{places} places at table for {students} students" : "No steward's hall: students eat in town"),
            ["happiness"] = (students > 0 ? happiness / students : 0, "Students' mean happiness"),
            ["heritage"] = (100 * Math.Min(1, heritage / _cfg.HeritageForFull), $"Heritage {heritage:0} (projects on their real sites, the Slant Walk)"),
        };
        double total = PartNames.Sum(n => _cfg.Weights.GetValueOrDefault(n.Id));
        Parts = PartNames.Select(n =>
        {
            var (score, note) = scores[n.Id];
            return new QualityPart(n.Id, n.Name, score, total > 0 ? _cfg.Weights.GetValueOrDefault(n.Id) / total : 0, note);
        }).ToArray();
        Quality = Parts.Sum(x => x.Score * x.Weight);
    }

    public ReputationView View()
    {
        if (_view is { } v && v.Version == Version) return v;
        return _view = new ReputationView(Version, Reputation, Quality, Parts, ApplicantFactor);
    }
}
