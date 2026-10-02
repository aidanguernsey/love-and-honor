using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Economy;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Engine;

/// <summary>An event card (data/events/*.json, §23, §30.4).</summary>
public sealed class EventDef
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Kind { get; init; } = "historical";
    public string? Date { get; init; }
    public string? DatePrecision { get; init; }
    public WindowSection? Window { get; init; }
    public string Text { get; init; } = "";
    public string? HeritageProject { get; init; }
    public string? TextIfBuilt { get; init; }
    public EffectsSection Effects { get; init; } = new();
    public string[] Codex { get; init; } = [];
    public SourceRef[] Sources { get; init; } = [];
    public bool Verified { get; init; }

    public sealed class WindowSection
    {
        public int FromYear { get; init; }
        public int ToYear { get; init; }
        public int[] Months { get; init; } = [];
        public double ChancePerYear { get; init; }
    }

    public sealed class EffectsSection
    {
        public double Cash { get; init; }
        public double Confidence { get; init; }
        public Dictionary<string, float> StudentNeeds { get; init; } = [];
        public double Applicants { get; init; } = 1;
        public double TownBoarding { get; init; } = 1;
        public HireSection? HireFaculty { get; init; }
        public string? FacultyLeaves { get; init; }
        public double Scholarship { get; init; }
    }

    public sealed class HireSection
    {
        public string Name { get; init; } = "";
        public int Teaching { get; init; }
        public int Research { get; init; }
    }

    public DateOnly? FixedDate => Date is null ? null : DateOnly.Parse(Date, System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A source citation: title and link.</summary>
public sealed class SourceRef
{
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
}

/// <summary>A History Book entry (data/codex/*.json, §19.3).</summary>
public sealed class CodexEntry
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string EraYears { get; init; } = "";
    public string Text { get; init; } = "";
    public string Unlock { get; init; } = "start";
    public SourceRef[] Sources { get; init; } = [];
    public bool Verified { get; init; }
}

/// <summary>data/advisors.json (§3, minimal).</summary>
public sealed class AdvisorsConfig
{
    public Dictionary<string, string> Advisors { get; init; } = [];
    public Message[] Messages { get; init; } = [];

    public sealed class Message
    {
        public string Id { get; init; } = "";
        public string Advisor { get; init; } = "";
        public string Condition { get; init; } = "";
        public double Value { get; init; }
        public int CooldownDays { get; init; }
        public string Text { get; init; } = "";
    }
}

/// <summary>All campaign content, loaded once (events, codex, advisors).</summary>
public sealed class CampaignContent
{
    public required IReadOnlyList<EventDef> Events { get; init; }
    public required IReadOnlyList<CodexEntry> Codex { get; init; }
    public required AdvisorsConfig Advisors { get; init; }

    public static CampaignContent Load(IDataSource source) => new()
    {
        Events = source.ListJson("events").Select(p => SimJson.Parse<EventDef>(source.ReadText(p), p)).ToList(),
        Codex = source.ListJson("codex").Select(p => SimJson.Parse<CodexEntry>(source.ReadText(p), p)).ToList(),
        Advisors = SimJson.Parse<AdvisorsConfig>(source.ReadText("advisors.json"), "advisors.json"),
    };
}

/// <summary>A scenario's goals (scenarios/*.json "goals", §4.1).</summary>
public sealed class GoalsConfig
{
    public int Students { get; init; }
    public bool ResidenceHall { get; init; }
    public string Deadline { get; init; } = "";
    public double BankruptBelow { get; init; }
    public int BankruptReviews { get; init; } = 2;
    /// <summary>Building definitions (or timeline ids) that count as the residence hall; empty = any residence.</summary>
    public string[] ResidenceHallDefs { get; init; } = [];

    public DateOnly DeadlineDate => DateOnly.Parse(Deadline, System.Globalization.CultureInfo.InvariantCulture);
}

public enum CampaignOutcome : byte { Playing, Won, Dismissed, Bankrupt, OutOfTime }

/// <summary>An event that fired, for the UI's card.</summary>
public sealed record EventCard(int Seq, string Id, string Title, string Text, DateOnly Date, string DatePrecision, bool Historical,
    string[] Codex, SourceRef[] Sources);

/// <summary>What the UI shows about the chapter (immutable; published in snapshots).</summary>
public sealed record CampaignView(int Version, EventCard[] Cards, string[] Unlocked, int Students, int StudentsGoal, bool HasHall,
    bool HallGoal, DateOnly Deadline, CampaignOutcome Outcome, string OutcomeText);

/// <summary>
/// Chapter 1 content (Phase 1 1j): historical events on their real dates (sourced, unverified) and generic era events
/// by chance, with effects (cash, Trustee Confidence, student needs, applicants, town boarding, named faculty, scholarship);
/// History Book entries they unlock; monthly advisor messages with cooldowns; and the chapter's goals: win with the
/// target enrollment and a residence hall before the deadline; lose when dismissed, bankrupt, or out of time.
/// Sim thread only, at hour 0 after the budget. Deterministic (its own random stream).
/// </summary>
public sealed class CampaignSystem
{
    private readonly CampaignContent _content;
    private readonly GoalsConfig? _goals;
    private readonly DateOnly _start;
    private readonly DeterministicRng _random;
    private readonly PopulationStore _pop;
    private readonly Campus _campus;
    private readonly TileGrid _grid;
    private readonly EnrollmentSystem? _enrollment;
    private readonly BudgetSystem? _budget;
    private readonly PlacementSystem? _placement;
    private readonly Treasury? _treasury;
    private readonly Func<DateOnly, PeopleContext> _people;
    private readonly string[] _needIds;
    private readonly HashSet<string> _fired = [];
    private readonly HashSet<string> _unlocked = [];
    private readonly Dictionary<string, int> _named = []; // faculty hired by events: name → agent Id
    private readonly Dictionary<string, DateOnly> _advisorLast = [];
    private readonly List<EventCard> _cards = [];
    private readonly List<string> _messages = [];
    private int _seq, _badReviews;
    private CampaignView? _view;

    public CampaignOutcome Outcome { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyCollection<string> Unlocked => _unlocked;

    public CampaignSystem(CampaignContent content, GoalsConfig? goals, DateOnly start, RngStreams rng, PopulationStore pop, Campus campus,
        EnrollmentSystem? enrollment, BudgetSystem? budget, PlacementSystem? placement, Treasury? treasury, Func<DateOnly, PeopleContext> people,
        string[] needIds)
    {
        _content = content;
        _goals = goals;
        _start = start;
        _random = rng.For("campaign");
        _pop = pop;
        _campus = campus;
        _grid = campus.Grid;
        _enrollment = enrollment;
        _budget = budget;
        _placement = placement;
        _treasury = treasury;
        _people = people;
        _needIds = needIds;
        foreach (var c in content.Codex) if (c.Unlock == "start") _unlocked.Add(c.Id);
        // Historical events dated before the start already happened.
        foreach (var e in content.Events) if (e.FixedDate is { } d && d < start) _fired.Add(e.Id);
    }

    public void WriteState(BinaryWriter w)
    {
        w.WriteStrings(_fired.Order(StringComparer.Ordinal));
        w.WriteStrings(_unlocked.Order(StringComparer.Ordinal));
        w.Write(_named.Count);
        foreach (var (k, v) in _named.OrderBy(kv => kv.Key, StringComparer.Ordinal)) { w.Write(k); w.Write(v); }
        w.Write(_advisorLast.Count);
        foreach (var (k, v) in _advisorLast.OrderBy(kv => kv.Key, StringComparer.Ordinal)) { w.Write(k); w.Write(v); }
        w.Write(_cards.Count);
        foreach (var c in _cards) { w.Write(c.Seq); w.Write(c.Id); w.Write(c.Title); w.Write(c.Text); w.Write(c.Date); w.Write(c.DatePrecision); w.Write(c.Historical); }
        w.Write(_seq); w.Write(_badReviews); w.Write((byte)Outcome); w.Write(_outcomeText); w.Write(Version);
        var (a, b, c2, d) = _random.State;
        w.Write(a); w.Write(b); w.Write(c2); w.Write(d);
    }

    public void ReadState(BinaryReader r)
    {
        _fired.Clear(); foreach (var s in r.ReadStrings()) _fired.Add(s);
        _unlocked.Clear(); foreach (var s in r.ReadStrings()) _unlocked.Add(s);
        _named.Clear(); for (int i = r.ReadInt32(); i > 0; i--) _named[r.ReadString()] = r.ReadInt32();
        _advisorLast.Clear(); for (int i = r.ReadInt32(); i > 0; i--) _advisorLast[r.ReadString()] = r.ReadDate();
        _cards.Clear();
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            int seq = r.ReadInt32(); string id = r.ReadString(), title = r.ReadString(), text = r.ReadString();
            var date = r.ReadDate(); string precision = r.ReadString(); bool historical = r.ReadBoolean();
            var def = _content.Events.FirstOrDefault(e => e.Id == id);
            _cards.Add(new EventCard(seq, id, title, text, date, precision, historical, def?.Codex ?? [], def?.Sources ?? []));
        }
        _seq = r.ReadInt32(); _badReviews = r.ReadInt32(); Outcome = (CampaignOutcome)r.ReadByte(); _outcomeText = r.ReadString(); Version = r.ReadInt32();
        _random.State = (r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64());
        _view = null;
    }

    public List<string> TakeMessages()
    {
        var m = new List<string>(_messages);
        _messages.Clear();
        return m;
    }

    /// <summary>Call once a day at hour 0, after enrollment and the budget. Returns true if the population changed.</summary>
    public bool DailyUpdate(DateOnly date, bool fiscalReviewToday)
    {
        int before = _pop.Count;
        if (Outcome == CampaignOutcome.Playing)
        {
            foreach (var e in _content.Events)
            {
                if (_fired.Contains(e.Id)) continue;
                if (e.FixedDate is { } d) { if (d == date) Fire(e, date); }
                else if (e.Window is { } w && date.Day == 1 && w.Months.Contains(date.Month) && date.Year >= w.FromYear && date.Year <= w.ToYear
                         && !_fired.Contains($"{e.Id}@{date.Year}") && _random.NextDouble() < w.ChancePerYear / w.Months.Length)
                {
                    _fired.Add($"{e.Id}@{date.Year}"); // generic events: at most once a year
                    Fire(e, date);
                }
            }
            if (date.Day == 15) Advisors(date);
            CheckGoals(date, fiscalReviewToday);
        }
        return _pop.Count != before;
    }

    // ---------------- events ----------------

    private void Fire(EventDef e, DateOnly date)
    {
        if (e.FixedDate is not null) _fired.Add(e.Id);
        var fx = e.Effects;
        bool built = e.HeritageProject is { } h && _placement is not null && _placement.HeritageTaken.Contains(h);
        string text = built && e.TextIfBuilt is { } alt ? alt : e.Text;

        if (fx.Cash != 0 && _treasury is not null)
        {
            long cents = (long)Math.Round(Math.Abs(fx.Cash) * 100);
            if (fx.Cash > 0) _treasury.Receive(date, cents, e.Title, "other"); else _treasury.Spend(date, cents, e.Title, "other");
        }
        if (fx.Confidence != 0) _budget?.AdjustConfidence(fx.Confidence, $"{e.Title}: confidence {(fx.Confidence > 0 ? "+" : "")}{fx.Confidence:0}.");
        if (fx.StudentNeeds.Count > 0)
            for (int a = 0; a < _pop.Count; a++)
            {
                if (_pop.Kind[a] != AgentKind.Student) continue;
                foreach (var (need, delta) in fx.StudentNeeds)
                {
                    int k = Array.IndexOf(_needIds, need);
                    if (k < 0) continue;
                    ref float v = ref _pop.Needs[a * PopulationStore.NeedCount + k];
                    v = Math.Clamp(v + delta, 0f, 100f);
                }
            }
        if (_enrollment is not null)
        {
            _enrollment.ApplicantBoost *= fx.Applicants;
            _enrollment.TownBoardingMultiplier *= fx.TownBoarding;
            if (fx.Scholarship != 0) _enrollment.AddScholarship(fx.Scholarship);
        }
        if (fx.HireFaculty is { } hire && _pop.Count < _pop.Capacity)
        {
            var ctx = _people(date);
            int a = _pop.Add(AgentKind.Faculty);
            ctx.InitNeeds(_pop, a, _random);
            ctx.InitFaculty(_pop, a, _random);
            _pop.TeachingScore[a] = (byte)hire.Teaching;
            _pop.ResearchScore[a] = (byte)hire.Research;
            _named[hire.Name] = _pop.Id[a];
        }
        if (fx.FacultyLeaves is { } name && _named.TryGetValue(name, out int id))
        {
            for (int a = 0; a < _pop.Count; a++)
                if (_pop.Id[a] == id) { _pop.RemoveAt(a); break; }
            _named.Remove(name);
        }
        foreach (var c in e.Codex) _unlocked.Add(c);

        _cards.Add(new EventCard(++_seq, e.Id, e.Title, text, date, e.DatePrecision ?? "day", e.Kind == "historical", e.Codex, e.Sources));
        if (_cards.Count > 20) _cards.RemoveAt(0);
        _messages.Add($"{e.Title}." + (e.Codex.Length > 0 ? " (New in the History Book.)" : ""));
        Version++;
    }

    // ---------------- advisors ----------------

    private void Advisors(DateOnly date)
    {
        var view = _enrollment?.View(date);
        foreach (var m in _content.Advisors.Messages)
        {
            if (_advisorLast.TryGetValue(m.Id, out var last) && date.DayNumber - last.DayNumber < m.CooldownDays) continue;
            var fill = new Dictionary<string, string>();
            bool hit = false;
            switch (m.Condition)
            {
                case "beds_ratio_above" when view is { } v:
                    int beds = v.CampusBeds + v.TownBeds;
                    hit = beds > 0 && v.Students >= m.Value * beds;
                    fill["students"] = $"{v.Students}"; fill["beds"] = $"{beds}";
                    break;
                case "seats_ratio_above" when view is { } v:
                    hit = v.Seats > 0 && v.Students >= m.Value * v.Seats;
                    fill["students"] = $"{v.Students}"; fill["seats"] = $"{v.Seats}";
                    break;
                case "cash_months_below" when _budget is not null && _treasury is not null:
                    var era = _budget.Config.EraFor(EraOf(date));
                    double monthly = (_pop.FacultyCount * era.FacultySalaryPerYear + era.AdministrationPerYear) / 12;
                    double months = monthly > 0 ? _treasury.Dollars / monthly : 99;
                    hit = months < m.Value;
                    fill["months"] = $"{Math.Max(0, months):0}";
                    break;
                case "cash_forecast_negative" when _budget is not null:
                    var forecast = _budget.Forecast(date);
                    hit = forecast.RunsOut is not null;
                    fill["month"] = $"{forecast.RunsOut:MMMM yyyy}";
                    fill["lowest"] = LandSystem.Money(forecast.Lowest);
                    break;
                case "intake_limited" when view is { } v:
                    hit = date.Month == 9 && v.LastIntake < v.LastAdmitted;
                    fill["turned_away"] = $"{Math.Max(0, v.LastAdmitted - v.LastIntake)}";
                    break;
                case "desire_paths_above":
                    hit = _grid.CountDesirePaths() > m.Value;
                    break;
                case "no_residence_hall_after_year":
                    hit = date.Year >= m.Value && !_campus.Buildings.Any(b => b.Kind == BuildingKind.Residence);
                    break;
            }
            if (!hit) continue;
            _advisorLast[m.Id] = date;
            string text = m.Text;
            foreach (var (k, v2) in fill) text = text.Replace("{" + k + "}", v2);
            _messages.Add($"{_content.Advisors.Advisors.GetValueOrDefault(m.Advisor, m.Advisor)}: {text}");
        }
    }

    private Func<int, string>? _eraOf;
    /// <summary>Era id for a year (set by the world; budget eras key by it).</summary>
    public Func<int, string> EraIdOf { set => _eraOf = value; }
    private string EraOf(DateOnly date) => _eraOf?.Invoke(date.Year) ?? "default";

    // ---------------- goals ----------------

    private bool HasHall => _campus.Buildings.Any(b => b.Kind == BuildingKind.Residence
        && (_goals is null || _goals.ResidenceHallDefs.Length == 0 || _goals.ResidenceHallDefs.Contains(b.DefId)));

    private void CheckGoals(DateOnly date, bool fiscalReviewToday)
    {
        if (_goals is null) return;
        if (_budget is { Dismissed: true }) { End(CampaignOutcome.Dismissed, "The Trustees have dismissed you."); return; }
        if (fiscalReviewToday && _treasury is not null)
        {
            _badReviews = _treasury.Dollars < _goals.BankruptBelow ? _badReviews + 1 : 0;
            if (_badReviews >= _goals.BankruptReviews) { End(CampaignOutcome.Bankrupt, "The college is bankrupt: the state has stepped in."); return; }
        }
        bool won = _pop.StudentCount >= _goals.Students && (!_goals.ResidenceHall || HasHall);
        if (won) { End(CampaignOutcome.Won, $"Chapter complete: {_pop.StudentCount} students and a residence hall on the Hill by {date:MMMM yyyy}."); return; }
        if (date >= _goals.DeadlineDate) End(CampaignOutcome.OutOfTime, $"Time's up: the Trustees wanted {_goals.Students} students and a residence hall by {_goals.DeadlineDate:MMMM yyyy}.");
    }

    private void End(CampaignOutcome outcome, string text)
    {
        Outcome = outcome;
        _outcomeText = text;
        _messages.Add(text);
        Version++;
    }

    private string _outcomeText = "";

    public CampaignView View()
    {
        if (_view is { } v && v.Version == Version && v.Students == _pop.StudentCount && v.HasHall == HasHall) return v;
        return _view = new CampaignView(Version, [.. _cards], [.. _unlocked.Order(StringComparer.Ordinal)], _pop.StudentCount, _goals?.Students ?? 0,
            HasHall, _goals?.ResidenceHall ?? false, _goals is null ? default : _goals.DeadlineDate, Outcome, _outcomeText);
    }
}
