using System.Globalization;
using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Sim.Core;

/// <summary>
/// The academic year (§6.2, data/calendar.json): which phase a date falls in (semester, break, finals, summer …),
/// whether classes are held, the week of the term, and named events (Convocation, Homecoming). Phases are month-day
/// anchors that repeat every year; each runs until the next one starts.
/// </summary>
public sealed class AcademicCalendar
{
    public sealed class PhaseDef
    {
        public string Start { get; init; } = "";
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Term { get; init; }
        public bool Classes { get; init; }
    }

    public sealed class EventDef
    {
        public string Start { get; init; } = "";
        public string End { get; init; } = "";
        public string Name { get; init; } = "";
    }

    public sealed class Config
    {
        public bool Verified { get; init; }
        public PhaseDef[] Phases { get; init; } = [];
        public EventDef[] Events { get; init; } = [];
    }

    public readonly record struct Phase(string Id, string Name, string? Term, bool Classes, int WeekOfTerm);

    public const string File = "calendar.json";

    private readonly (int Key, PhaseDef Def)[] _phases; // sorted by month*100+day
    private readonly (int Start, int End, string Name)[] _events;

    public bool Verified { get; }

    public AcademicCalendar(Config cfg)
    {
        if (cfg.Phases.Length == 0) throw new InvalidDataException("calendar.json: no phases");
        _phases = cfg.Phases.Select(p => (Key(p.Start), p)).OrderBy(p => p.Item1).ToArray();
        _events = cfg.Events.Select(e => (Key(e.Start), Key(e.End), e.Name)).ToArray();
        Verified = cfg.Verified;
    }

    public static AcademicCalendar Load(IDataSource source) => new(SimJson.Parse<Config>(source.ReadText(File), File));

    private static int Key(string monthDay)
    {
        var parts = monthDay.Split('-');
        return int.Parse(parts[0], CultureInfo.InvariantCulture) * 100 + int.Parse(parts[1], CultureInfo.InvariantCulture);
    }

    private static int Key(DateOnly d) => d.Month * 100 + d.Day;

    /// <summary>Index of the phase containing <paramref name="date"/> (the last anchor on or before it, wrapping).</summary>
    private int IndexAt(DateOnly date)
    {
        int key = Key(date), idx = _phases.Length - 1; // before the first anchor → last phase of the previous year
        for (int i = 0; i < _phases.Length; i++)
            if (_phases[i].Key <= key) idx = i;
        return idx;
    }

    /// <summary>Start date of phase <paramref name="index"/> in the occurrence that contains <paramref name="date"/>.</summary>
    private DateOnly StartOf(int index, DateOnly date)
    {
        int key = _phases[index].Key;
        var start = new DateOnly(date.Year, key / 100, key % 100);
        return start > date ? start.AddYears(-1) : start;
    }

    public bool ClassesHeld(DateOnly date) => _phases[IndexAt(date)].Def.Classes;

    public Phase At(DateOnly date)
    {
        int idx = IndexAt(date);
        var def = _phases[idx].Def;
        int week = 0;
        if (def.Term is not null)
        {
            // The term's first class day: walk back through consecutive phases of the same term.
            DateOnly termStart = default;
            bool found = false;
            for (int k = 0, i = idx; k < _phases.Length && _phases[i].Def.Term == def.Term; k++, i = (i - 1 + _phases.Length) % _phases.Length)
                if (_phases[i].Def.Classes) { termStart = StartOf(i, date); found = true; }
            if (found && date >= termStart) week = (date.DayNumber - termStart.DayNumber) / 7 + 1;
        }
        return new Phase(def.Id, def.Name, def.Term, def.Classes, week);
    }

    /// <summary>Named events on this date (e.g. Homecoming Weekend).</summary>
    public IEnumerable<string> EventsOn(DateOnly date)
    {
        int key = Key(date);
        foreach (var (start, end, name) in _events)
            if (start <= end ? key >= start && key <= end : key >= start || key <= end) yield return name;
    }
}
