using System.Collections.Concurrent;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Pathing;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Economy;

/// <summary>data/budget.json (§3, §8), Phase 1 1i.</summary>
public sealed class BudgetConfig
{
    public string FiscalYearStart { get; init; } = "08-01";
    public string SpringTermStart { get; init; } = "01-26";
    public string LandRentsDate { get; init; } = "01-01";
    public Dictionary<string, EraSection> Eras { get; init; } = [];
    public Dictionary<string, double> DefaultUpkeep { get; init; } = [];
    public TuitionSection Tuition { get; init; } = new();
    public TrusteesSection Trustees { get; init; } = new();

    public sealed class EraSection
    {
        public double TuitionPerYear { get; init; }
        public double RoomRentPerYear { get; init; }
        public double LandRentsPerYear { get; init; }
        public double StateSupportPerYear { get; init; }
        public double FacultySalaryPerYear { get; init; }
        public double AdministrationPerYear { get; init; }
        public bool Verified { get; init; }
    }

    public sealed class TuitionSection
    {
        public double MinLevel { get; init; } = 0.5;
        public double MaxLevel { get; init; } = 2;
        public double Step { get; init; } = 0.1;
        public double Elasticity { get; init; }
    }

    public sealed class TrusteesSection
    {
        public double BalancedBudget { get; init; }
        public double Deficit { get; init; }
        public double EnrollmentUp { get; init; }
        public double EnrollmentDown { get; init; }
        public double NegativeCashPerMonth { get; init; }
    }

    public EraSection EraFor(string id) => Eras.TryGetValue(id, out var e) ? e : Eras["default"];

    public const string File = "budget.json";

    public static BudgetConfig Load(IDataSource source) => SimJson.Parse<BudgetConfig>(source.ReadText(File), File);
}

/// <summary>One line of a budget report: category id, display name, amount (cents; revenue positive, spending negative).</summary>
public sealed record BudgetLine(string Category, string Name, long Cents);

/// <summary>Money in and out over a period, by category. Operating = revenue minus running costs (what "balanced" is
/// judged on); capital = construction, land and grounds.</summary>
public sealed record BudgetReport(string Title, DateOnly From, DateOnly To, BudgetLine[] Lines, long Revenue, long Operating, long Capital, long Net);

/// <summary>Cash over the coming months at today's numbers (people, buildings, tuition): the lowest balance in each
/// month (<paramref name="Months"/>: first day of each month), and the first day it would go below zero, if any.</summary>
public sealed record CashForecast(DateOnly[] Months, long[] LowestCents, DateOnly? RunsOut)
{
    public long Lowest => LowestCents.Length > 0 ? LowestCents.Min() : 0;

    /// <summary>The first month in which cash would go below zero after spending <paramref name="cents"/> now, or null.</summary>
    public DateOnly? RunsOutAfterSpending(long cents)
    {
        for (int i = 0; i < Months.Length; i++)
            if (LowestCents[i] - cents < 0) return Months[i];
        return null;
    }
}

/// <summary>What the UI shows about money and the Trustees (immutable; published in snapshots).</summary>
public sealed record BudgetView(int Version, double Confidence, double TuitionLevel, long TuitionPerYearCents, BudgetReport YearToDate,
    BudgetReport? LastYear, DateOnly NextReview, bool Dismissed, bool Verified, CashForecast Forecast, bool HiringPaused);

/// <summary>
/// The operating budget and Trustee Confidence (§3, §8), Phase 1 1i. Sim thread only, at hour 0 after enrollment:
///  - tuition (× the player's tuition level) and room rent for hall residents, half at move-in and half at the spring
///    term; land rents once a year; state support at the start of the fiscal year;
///  - on the 1st of each month: faculty salaries, administration and building upkeep (building definitions' upkeep ×
///    the era's price multiplier), a twelfth each; the Trustees lose patience every month cash is negative;
///  - at the fiscal year's end: a budget report and the Trustees' review (balanced operating budget +, deficit −,
///    enrollment up +, down −). Confidence 0 = dismissed (§3).
/// The player's levers: the tuition level and pausing faculty hiring (enrollment reads <see cref="HiringPaused"/>);
/// <see cref="Forecast"/> projects cash a year ahead so a coming shortfall can be seen in time (balance pass).
/// Construction, land and paths are paid when ordered (their systems) and show up here as capital spending.
/// </summary>
public sealed class BudgetSystem
{
    public static readonly (string Id, string Name, bool Revenue, bool Capital)[] Categories =
    [
        ("tuition", "Tuition", true, false),
        ("room", "Room rent", true, false),
        ("land_rents", "Land rents", true, false),
        ("state", "State support", true, false),
        ("salaries", "Faculty salaries", false, false),
        ("administration", "Administration", false, false),
        ("upkeep", "Building upkeep", false, false),
        ("construction", "Construction", false, true),
        ("land", "Land (clearing, purchases)", false, true),
        ("grounds", "Paths and grounds", false, true),
        ("other", "Other", false, false),
    ];

    private readonly BudgetConfig _cfg;
    private readonly EraTable _eras;
    private readonly Treasury _treasury;
    private readonly PopulationStore _pop;
    private readonly Campus _campus;
    private readonly Func<CampusBuilding, double> _upkeepUsd;
    private readonly ConcurrentQueue<double> _tuitionOrders = new();
    private readonly ConcurrentQueue<bool> _hiringOrders = new();
    private readonly string _moveIn;
    private readonly List<string> _messages = [];
    private DateOnly _fiscalStart;
    // Fall enrollment (after move-in) this year and the year before: the Trustees compare the two.
    private int _fallEnrollment, _previousFallEnrollment;
    private BudgetReport? _lastYear;
    private BudgetView? _view;
    private int _viewLedger = -1;

    public double Confidence { get; private set; }
    public double TuitionLevel { get; private set; } = 1.0;
    /// <summary>The player has paused faculty hiring: move-in hires only up to the era's minimum.</summary>
    public bool HiringPaused { get; private set; }
    public bool Dismissed { get; private set; }
    public int Version { get; private set; }
    public BudgetConfig Config => _cfg;

    /// <param name="upkeepUsd">Yearly upkeep of a campus building in modern dollars (building definition, else default).</param>
    public BudgetSystem(BudgetConfig cfg, EraTable eras, Treasury treasury, PopulationStore pop, Campus campus,
        Func<CampusBuilding, double> upkeepUsd, double startingConfidence, DateOnly start, string moveInMonthDay = "08-19")
    {
        _moveIn = moveInMonthDay;
        _cfg = cfg;
        _eras = eras;
        _treasury = treasury;
        _pop = pop;
        _campus = campus;
        _upkeepUsd = upkeepUsd;
        Confidence = startingConfidence;
        _fiscalStart = start;
        _fallEnrollment = _previousFallEnrollment = pop.StudentCount;
    }

    public void WriteState(BinaryWriter w)
    {
        w.Write(Confidence); w.Write(TuitionLevel); w.Write(Dismissed); w.Write(Version);
        Engine.SaveIO.Write(w, _fiscalStart); w.Write(_fallEnrollment); w.Write(_previousFallEnrollment);
        w.Write(_lastYear is not null);
        if (_lastYear is { } y)
        {
            w.Write(y.Title); Engine.SaveIO.Write(w, y.From); Engine.SaveIO.Write(w, y.To);
            w.Write(y.Revenue); w.Write(y.Operating); w.Write(y.Capital); w.Write(y.Net);
            w.Write(y.Lines.Length);
            foreach (var l in y.Lines) { w.Write(l.Category); w.Write(l.Name); w.Write(l.Cents); }
        }
        w.Write(HiringPaused); // format 4
    }

    /// <param name="format">Save format (the hiring pause from format 4).</param>
    public void ReadState(BinaryReader r, int format)
    {
        Confidence = r.ReadDouble(); TuitionLevel = r.ReadDouble(); Dismissed = r.ReadBoolean(); Version = r.ReadInt32();
        _fiscalStart = Engine.SaveIO.ReadDate(r); _fallEnrollment = r.ReadInt32(); _previousFallEnrollment = r.ReadInt32();
        _lastYear = null;
        if (r.ReadBoolean())
        {
            string title = r.ReadString(); var from = Engine.SaveIO.ReadDate(r); var to = Engine.SaveIO.ReadDate(r);
            long revenue = r.ReadInt64(), operating = r.ReadInt64(), capital = r.ReadInt64(), net = r.ReadInt64();
            var lines = new BudgetLine[r.ReadInt32()];
            for (int i = 0; i < lines.Length; i++) lines[i] = new BudgetLine(r.ReadString(), r.ReadString(), r.ReadInt64());
            _lastYear = new BudgetReport(title, from, to, lines, revenue, operating, capital, net);
        }
        HiringPaused = format >= 4 && r.ReadBoolean();
        _view = null;
    }

    /// <summary>Applicants scale with price (§8.4): 1 − elasticity × (level − 1), at least 0.1.</summary>
    public double ApplicantFactor => Math.Max(0.1, 1 - _cfg.Tuition.Elasticity * (TuitionLevel - 1));

    public void SetTuitionLevel(double level) => _tuitionOrders.Enqueue(level);

    public void SetHiringPaused(bool paused) => _hiringOrders.Enqueue(paused);

    public bool HasPendingCommands => !_tuitionOrders.IsEmpty || !_hiringOrders.IsEmpty;

    public void ApplyCommands()
    {
        while (_tuitionOrders.TryDequeue(out double level))
        {
            double clamped = Math.Clamp(Math.Round(level / _cfg.Tuition.Step) * _cfg.Tuition.Step, _cfg.Tuition.MinLevel, _cfg.Tuition.MaxLevel);
            if (Math.Abs(clamped - TuitionLevel) < 1e-9) continue;
            TuitionLevel = clamped;
            _messages.Add($"Tuition set to {TuitionLevel:P0} of the usual rate (applies from the next term; applicants respond at the next move-in).");
            Version++;
        }
        while (_hiringOrders.TryDequeue(out bool paused))
        {
            if (paused == HiringPaused) continue;
            HiringPaused = paused;
            _messages.Add(paused
                ? "Faculty hiring paused: no new professors at move-in beyond the minimum. Salaries stay flat, but more students per professor lowers Quality."
                : "Faculty hiring resumed: move-in hires to the usual students-per-professor ratio again.");
            Version++;
        }
    }

    public List<string> TakeMessages()
    {
        var m = new List<string>(_messages);
        _messages.Clear();
        return m;
    }

    private static DateOnly On(string monthDay, int year) =>
        new(year, int.Parse(monthDay[..2], System.Globalization.CultureInfo.InvariantCulture), int.Parse(monthDay[3..], System.Globalization.CultureInfo.InvariantCulture));

    private static long Cents(double dollars) => (long)Math.Round(dollars * 100);

    /// <summary>Call once a day at hour 0, after enrollment. <paramref name="moveInDate"/>: enrollment's intake date this year.</summary>
    public void DailyUpdate(DateOnly date, DateOnly moveInDate)
    {
        var era = _cfg.EraFor(_eras.At(date.Year).Id);
        bool changed = false;

        if (date == On(_cfg.FiscalYearStart, date.Year) && date > _fiscalStart)
        {
            Review(date);
            changed = true;
            if (era.StateSupportPerYear > 0) _treasury.Receive(date, Cents(era.StateSupportPerYear), "State support", "state");
        }
        if (date == moveInDate)
        {
            _previousFallEnrollment = _fallEnrollment;
            _fallEnrollment = _pop.StudentCount;
        }
        if (date == moveInDate || date == On(_cfg.SpringTermStart, date.Year))
        {
            int students = _pop.StudentCount, inHalls = 0;
            for (int a = 0; a < _pop.Count; a++)
                if (_pop.Kind[a] == AgentKind.Student && _pop.Housing[a] == HousingType.OnCampus) inHalls++;
            if (students > 0) _treasury.Receive(date, Cents(students * era.TuitionPerYear * TuitionLevel / 2), $"Tuition, {students} students", "tuition");
            if (inHalls > 0) _treasury.Receive(date, Cents(inHalls * era.RoomRentPerYear / 2), $"Room rent, {inHalls} students", "room");
            changed = true;
        }
        if (date == On(_cfg.LandRentsDate, date.Year) && era.LandRentsPerYear > 0)
        {
            _treasury.Receive(date, Cents(era.LandRentsPerYear), "Land rents", "land_rents");
            changed = true;
        }
        if (date.Day == 1)
        {
            var (salaries, administration, upkeep) = MonthlyCosts(date, _pop.FacultyCount);
            _treasury.Spend(date, salaries, $"Salaries, {_pop.FacultyCount} faculty", "salaries");
            _treasury.Spend(date, administration, "Administration", "administration");
            _treasury.Spend(date, upkeep, "Building upkeep", "upkeep");
            if (_treasury.Cents < 0) AdjustConfidence(_cfg.Trustees.NegativeCashPerMonth, "Cash has run out: the Trustees are alarmed.");
            changed = true;
        }
        if (changed) Version++;
    }

    /// <summary>One month's salaries, administration and upkeep (cents), as charged on the 1st.</summary>
    private (long Salaries, long Administration, long Upkeep) MonthlyCosts(DateOnly date, int faculty)
    {
        var era = _cfg.EraFor(_eras.At(date.Year).Id);
        double multiplier = _eras.At(date.Year).PriceMultiplier;
        double upkeep = _campus.Buildings.Where(b => b.Kind != BuildingKind.OffCampusHousing).Sum(b => _upkeepUsd(b)) * multiplier;
        return (Cents(faculty * era.FacultySalaryPerYear / 12), Cents(era.AdministrationPerYear / 12), Cents(upkeep / 12));
    }

    /// <summary>
    /// Cash for the next <paramref name="months"/> months at today's numbers: the same people, buildings, tuition and
    /// hall residents, the usual calendar of income (terms, land rents, state support) and monthly costs. Move-in's
    /// new students and hires aren't guessed at, so it errs on the careful side once enrollment is growing.
    /// </summary>
    public CashForecast Forecast(DateOnly today, int months = 12)
    {
        int students = _pop.StudentCount, inHalls = 0;
        for (int a = 0; a < _pop.Count; a++)
            if (_pop.Kind[a] == AgentKind.Student && _pop.Housing[a] == HousingType.OnCampus) inHalls++;
        int faculty = _pop.FacultyCount;
        long cash = _treasury.Cents;
        var first = new DateOnly(today.Year, today.Month, 1);
        var monthStarts = new DateOnly[months];
        var lowest = new long[months];
        for (int m = 0; m < months; m++) { monthStarts[m] = first.AddMonths(m); lowest[m] = long.MaxValue; }
        lowest[0] = cash;
        DateOnly? runsOut = cash < 0 ? today : null;
        var end = first.AddMonths(months);
        for (var d = today.AddDays(1); d < end; d = d.AddDays(1))
        {
            var era = _cfg.EraFor(_eras.At(d.Year).Id);
            if (d == On(_cfg.FiscalYearStart, d.Year)) cash += Cents(era.StateSupportPerYear);
            if (d == On(_moveIn, d.Year) || d == On(_cfg.SpringTermStart, d.Year))
                cash += Cents(students * era.TuitionPerYear * TuitionLevel / 2) + Cents(inHalls * era.RoomRentPerYear / 2);
            if (d == On(_cfg.LandRentsDate, d.Year)) cash += Cents(era.LandRentsPerYear);
            if (d.Day == 1)
            {
                var (s, a, u) = MonthlyCosts(d, faculty);
                cash -= s + a + u;
            }
            int month = (d.Year - first.Year) * 12 + d.Month - first.Month;
            lowest[month] = Math.Min(lowest[month], cash);
            if (cash < 0 && runsOut is null) runsOut = d;
        }
        for (int m = 1; m < months; m++) if (lowest[m] == long.MaxValue) lowest[m] = lowest[m - 1];
        return new CashForecast(monthStarts, lowest, runsOut);
    }

    private void Review(DateOnly date)
    {
        var report = Report(_fiscalStart, date.AddDays(-1), $"Budget {_fiscalStart.Year}–{date.Year % 100:00}");
        _lastYear = report;
        _fiscalStart = date;
        var t = _cfg.Trustees;
        double delta = report.Operating >= 0 ? t.BalancedBudget : t.Deficit;
        if (_fallEnrollment > _previousFallEnrollment) delta += t.EnrollmentUp;
        else if (_fallEnrollment < _previousFallEnrollment) delta += t.EnrollmentDown;
        string verdict = report.Operating >= 0 ? $"a surplus of {LandSystem.Money(report.Operating)}" : $"a deficit of {LandSystem.Money(-report.Operating)}";
        _messages.Add($"{report.Title}: revenue {LandSystem.Money(report.Revenue)}, running costs {LandSystem.Money(report.Revenue - report.Operating)}, " +
                      $"{verdict}; building and land {LandSystem.Money(-report.Capital)}.");
        AdjustConfidence(delta, $"The Trustees' review: confidence {(delta >= 0 ? "+" : "")}{delta:0}.");
    }

    /// <summary>Changes Trustee Confidence (events, reviews) with a message; 0 = dismissed.</summary>
    public void AdjustConfidence(double delta, string message)
    {
        if (Dismissed) return;
        Confidence = Math.Clamp(Confidence + delta, 0, 100);
        _messages.Add($"{message} Trustee Confidence {Confidence:0}.");
        if (Confidence <= 0)
        {
            Dismissed = true;
            _messages.Add("The Trustees have lost all confidence and dismissed the President.");
        }
    }

    /// <summary>The ledger between two dates (inclusive), by category.</summary>
    public BudgetReport Report(DateOnly from, DateOnly to, string title)
    {
        var sums = new Dictionary<string, long>();
        foreach (var e in _treasury.Ledger)
            if (e.Date >= from && e.Date <= to) sums[e.Category] = sums.GetValueOrDefault(e.Category) + e.Cents;
        var lines = Categories.Where(c => sums.ContainsKey(c.Id)).Select(c => new BudgetLine(c.Id, c.Name, sums[c.Id])).ToArray();
        long revenue = Categories.Where(c => c.Revenue).Sum(c => sums.GetValueOrDefault(c.Id));
        long running = Categories.Where(c => !c.Revenue && !c.Capital).Sum(c => sums.GetValueOrDefault(c.Id));
        long capital = Categories.Where(c => c.Capital).Sum(c => sums.GetValueOrDefault(c.Id));
        return new BudgetReport(title, from, to, lines, revenue, revenue + running, capital, revenue + running + capital);
    }

    public BudgetView View(DateOnly today)
    {
        if (_view is { } v && v.Version == Version && _viewLedger == _treasury.Ledger.Count) return v;
        _viewLedger = _treasury.Ledger.Count;
        var era = _cfg.EraFor(_eras.At(today.Year).Id);
        var next = On(_cfg.FiscalYearStart, today.Year);
        if (next <= today) next = On(_cfg.FiscalYearStart, today.Year + 1);
        return _view = new BudgetView(Version, Confidence, TuitionLevel, Cents(era.TuitionPerYear * TuitionLevel),
            Report(_fiscalStart, today, $"This year so far (since {_fiscalStart:MMM d, yyyy})"), _lastYear, next, Dismissed, era.Verified,
            Forecast(today), HiringPaused);
    }
}
