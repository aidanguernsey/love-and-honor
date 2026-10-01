using System.Collections.Concurrent;
using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Sim.World;

/// <summary>data/land.json: clearing, purchase and town-growth settings (§5.1b, §5.3).</summary>
public sealed class LandConfig
{
    public ClearingSection Clearing { get; init; } = new();
    public PurchaseSection Purchase { get; init; } = new();
    public TownGrowthSection TownGrowth { get; init; } = new();

    public sealed class ClearingSection
    {
        public double CostPerTile { get; init; }
        public double TilesPerDay { get; init; }
        public double WinterSpeed { get; init; }
        public int[] WinterMonths { get; init; } = [];
    }

    public sealed class PurchaseSection
    {
        public Dictionary<string, double> PricePerTile { get; init; } = [];
        public double TownRelationsPenaltyPerTile { get; init; }
        public bool MustTouchUniversityLand { get; init; }
    }

    public sealed class TownGrowthSection
    {
        public int UpdateEveryDays { get; init; }
    }

    public const string File = "land.json";

    public static LandConfig Load(IDataSource source) => SimJson.Parse<LandConfig>(source.ReadText(File), File);
}

/// <summary>Money (§8.1 operating cash), kept as whole cents so it's exact and deterministic. Phase 1 1d: just a
/// balance and a ledger; the budget (revenue, expenses, the yearly screen) comes in 1i.</summary>
public sealed class Treasury(long startingCents)
{
    public long Cents { get; private set; } = startingCents;
    public double Dollars => Cents / 100.0;
    public List<(DateOnly Date, long Cents, string Reason)> Ledger { get; } = [];

    public bool CanAfford(long cents) => cents <= Cents;

    public void Spend(DateOnly date, long cents, string reason)
    {
        Cents -= cents;
        Ledger.Add((date, -cents, reason));
    }
}

public enum LandAction : byte { Clear, Buy }

/// <summary>A player land order for a rectangle of tiles (inclusive).</summary>
public readonly record struct LandCommand(LandAction Action, int X0, int Y0, int X1, int Y1);

/// <summary>What an order would do: tiles it applies to, total cost (cents), and for clearing, days of work.</summary>
public readonly record struct LandQuote(int Tiles, int Skipped, long Cents, double Days, string Problem)
{
    public bool Ok => Tiles > 0 && Problem.Length == 0;
}

/// <summary>
/// The live land layer and the player's land actions (§5.1b, §5.3), Phase 1 1d:
///  - per tile: land state (forest, pasture, farmland, town …), owner (university / town / private), whether its
///    path or road exists yet, and pending clearing;
///  - Clear: university-owned forest becomes cleared land (pasture) over days of work (slower in winter), paid up front;
///  - Buy: forest/farmland/town tiles touching university land become university land (town land costs far more);
///  - the town grows by the land-history rules once a month on land the university doesn't own.
/// Every change that alters a walking surface is returned as <see cref="Engine.TileEdit"/>s for the flow fields.
/// Sim thread only; the UI reads copies from the snapshot and quotes with <see cref="Quote"/>.
/// </summary>
public sealed class LandSystem
{
    private readonly TileGrid _grid;
    private readonly LandConfig _cfg;
    private readonly EraTable _eras;
    private readonly LandHistory? _history;
    private readonly bool[] _pathOn;
    private readonly byte[] _clearing;    // 1 = ordered for clearing
    private readonly List<ClearingJob> _jobs = [];
    private readonly ConcurrentQueue<LandCommand> _commands = new();
    private readonly List<string> _messages = [];
    private int _lastGrowthDay; // first update a month in

    public Treasury Treasury { get; }
    public double TownRelationsPenalty { get; private set; }
    /// <summary>Bumped whenever land state, ownership, paths or clearing orders change (the UI re-copies then).</summary>
    public int Version { get; private set; }
    public int ActiveClearingTiles => _jobs.Sum(j => j.Tiles.Count - j.Next);
    public byte[] Clearing => _clearing;
    public bool[] PathOn => _pathOn;

    private sealed class ClearingJob(List<int> tiles)
    {
        public readonly List<int> Tiles = tiles;
        public int Next;
        public double Progress; // tiles' worth of work done but not yet finished
    }

    public LandSystem(TileGrid grid, LandConfig cfg, EraTable eras, LandHistory? history, bool[] pathOn, Treasury treasury)
    {
        _grid = grid;
        _cfg = cfg;
        _eras = eras;
        _history = history;
        _pathOn = pathOn;
        _clearing = new byte[grid.Width * grid.Height];
        Treasury = treasury;
    }

    /// <summary>Walking surface implied by the land layer (water and buildings never change here).</summary>
    public static TileType SurfaceFor(TileGrid g, bool[] pathOn, int t)
    {
        var current = g.Types[t];
        if (current is TileType.Water or TileType.Building) return current;
        if (pathOn[t]) return TileType.Path;
        bool lawn = g.Ownership[t] == Ownership.University && g.LandState[t] is not (LandState.Forest or LandState.Protected or LandState.Water);
        return lawn ? TileType.Grass : TileType.Rough;
    }

    // ---------------- commands (any thread enqueues, sim thread applies) ----------------

    public void Enqueue(LandCommand command) => _commands.Enqueue(command);

    public bool HasPendingCommands => !_commands.IsEmpty;

    /// <summary>Messages for the player since the last call (sim thread).</summary>
    public List<string> TakeMessages()
    {
        var m = new List<string>(_messages);
        _messages.Clear();
        return m;
    }

    /// <summary>Applies queued commands. Returns walking-surface edits.</summary>
    public List<Engine.TileEdit> ApplyCommands(DateOnly date)
    {
        var edits = new List<Engine.TileEdit>();
        while (_commands.TryDequeue(out var c))
        {
            var q = Quote(_cfg, _eras, c, _grid.Width, _grid.Height, _grid.LandState, _grid.Ownership, _grid.Types, _clearing, date);
            if (!q.Ok) { _messages.Add(q.Problem.Length > 0 ? q.Problem : "Nothing to do there."); continue; }
            if (!Treasury.CanAfford(q.Cents)) { _messages.Add($"Not enough money: that costs {Money(q.Cents)}, you have {Money(Treasury.Cents)}."); continue; }
            var tiles = Tiles(_cfg, c, _grid.Width, _grid.Height, _grid.LandState, _grid.Ownership, _grid.Types, _clearing);
            if (c.Action == LandAction.Clear)
            {
                Treasury.Spend(date, q.Cents, $"Clearing {tiles.Count} tiles");
                foreach (int t in tiles) _clearing[t] = 1;
                _jobs.Add(new ClearingJob(tiles));
                _messages.Add($"Clearing {tiles.Count} tiles of forest: {Money(q.Cents)}, about {Math.Ceiling(q.Days)} days of work.");
            }
            else
            {
                Treasury.Spend(date, q.Cents, $"Bought {tiles.Count} tiles");
                int town = 0;
                foreach (int t in tiles)
                {
                    if (_grid.Ownership[t] == Ownership.Town) town++;
                    _grid.Ownership[t] = Ownership.University;
                    AddSurfaceEdit(t, edits);
                }
                TownRelationsPenalty += town * _cfg.Purchase.TownRelationsPenaltyPerTile;
                _messages.Add($"Bought {tiles.Count} tiles for {Money(q.Cents)}" + (town > 0 ? $" ({town} in town: the town noticed)." : "."));
            }
            Version++;
        }
        return edits;
    }

    // ---------------- daily work ----------------

    /// <summary>Call once per in-game day: clearing crews work, and the town grows monthly. Returns surface edits.</summary>
    public List<Engine.TileEdit> DailyUpdate(DateOnly date, int dayIndex)
    {
        var edits = new List<Engine.TileEdit>();
        double rate = _cfg.Clearing.TilesPerDay * (_cfg.Clearing.WinterMonths.Contains(date.Month) ? _cfg.Clearing.WinterSpeed : 1.0);
        bool changed = false;
        for (int j = _jobs.Count - 1; j >= 0; j--)
        {
            var job = _jobs[j];
            job.Progress += rate;
            while (job.Progress >= 1.0 && job.Next < job.Tiles.Count)
            {
                job.Progress -= 1.0;
                int t = job.Tiles[job.Next++];
                _clearing[t] = 0;
                if (_grid.LandState[t] == LandState.Forest) _grid.LandState[t] = LandState.Pasture;
                AddSurfaceEdit(t, edits);
                changed = true;
            }
            if (job.Next >= job.Tiles.Count)
            {
                _jobs.RemoveAt(j);
                _messages.Add($"Finished clearing {job.Tiles.Count} tiles.");
            }
        }

        if (_history is not null && dayIndex - _lastGrowthDay >= _cfg.TownGrowth.UpdateEveryDays)
        {
            _lastGrowthDay = dayIndex;
            int year = date.Year;
            for (int t = 0; t < _grid.LandState.Length; t++)
            {
                if (_grid.Ownership[t] == Ownership.University) continue;
                var s = _history.StateAt(t, year);
                bool path = _history.PathVisible(t, year);
                if (s != _grid.LandState[t] && _clearing[t] == 0)
                {
                    _grid.LandState[t] = s;
                    if (s == LandState.Town && _grid.Ownership[t] == Ownership.Private) _grid.Ownership[t] = Ownership.Town;
                    changed = true;
                    AddSurfaceEdit(t, edits);
                }
                if (path && !_pathOn[t]) { _pathOn[t] = true; changed = true; AddSurfaceEdit(t, edits); }
            }
        }
        if (changed) Version++;
        return edits;
    }

    private void AddSurfaceEdit(int t, List<Engine.TileEdit> edits)
    {
        var s = SurfaceFor(_grid, _pathOn, t);
        if (s != _grid.Types[t]) edits.Add(new Engine.TileEdit(t, s));
    }

    // ---------------- quoting (pure; also used by the UI on snapshot copies) ----------------

    public static LandQuote Quote(LandConfig cfg, EraTable eras, LandCommand c, int width, int height,
        LandState[] states, Ownership[] owners, TileType[] types, byte[] clearing, DateOnly date)
    {
        var tiles = Tiles(cfg, c, width, height, states, owners, types, clearing);
        int area = (Math.Abs(c.X1 - c.X0) + 1) * (Math.Abs(c.Y1 - c.Y0) + 1);
        double mult = eras.At(date.Year).PriceMultiplier;
        if (tiles.Count == 0)
            return new LandQuote(0, area, 0, 0, c.Action == LandAction.Clear
                ? "Nothing to clear: pick university-owned forest."
                : cfg.Purchase.MustTouchUniversityLand ? "Nothing to buy: pick land next to university land." : "Nothing to buy there.");
        double dollars = 0;
        if (c.Action == LandAction.Clear)
            dollars = tiles.Count * cfg.Clearing.CostPerTile * mult;
        else
            foreach (int t in tiles) dollars += PriceOf(cfg, states[t]) * mult;
        double days = c.Action == LandAction.Clear ? tiles.Count / cfg.Clearing.TilesPerDay : 0;
        return new LandQuote(tiles.Count, area - tiles.Count, (long)Math.Round(dollars * 100), days, "");
    }

    private static double PriceOf(LandConfig cfg, LandState s) =>
        cfg.Purchase.PricePerTile.GetValueOrDefault(s.ToString().ToLowerInvariant(), cfg.Purchase.PricePerTile.GetValueOrDefault("pasture"));

    /// <summary>The tiles in the rectangle the order applies to, in row order.</summary>
    public static List<int> Tiles(LandConfig cfg, LandCommand c, int width, int height,
        LandState[] states, Ownership[] owners, TileType[] types, byte[] clearing)
    {
        int x0 = Math.Clamp(Math.Min(c.X0, c.X1), 0, width - 1), x1 = Math.Clamp(Math.Max(c.X0, c.X1), 0, width - 1);
        int y0 = Math.Clamp(Math.Min(c.Y0, c.Y1), 0, height - 1), y1 = Math.Clamp(Math.Max(c.Y0, c.Y1), 0, height - 1);
        var list = new List<int>();
        if (c.Action == LandAction.Clear)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int t = y * width + x;
                    if (owners[t] == Ownership.University && states[t] == LandState.Forest && clearing[t] == 0) list.Add(t);
                }
            return list;
        }

        // Buy: not yet university land, not water/buildings; must connect to university land through the selection.
        var candidate = new bool[(x1 - x0 + 1) * (y1 - y0 + 1)];
        int w = x1 - x0 + 1;
        var queue = new Queue<int>();
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int t = y * width + x;
                if (owners[t] == Ownership.University || states[t] == LandState.Water || types[t] is TileType.Water or TileType.Building) continue;
                candidate[(y - y0) * w + (x - x0)] = true;
            }
        if (!cfg.Purchase.MustTouchUniversityLand)
        {
            for (int i = 0; i < candidate.Length; i++) if (candidate[i]) list.Add((y0 + i / w) * width + x0 + i % w);
            return list;
        }
        var reached = new bool[candidate.Length];
        for (int i = 0; i < candidate.Length; i++)
        {
            if (!candidate[i]) continue;
            int x = x0 + i % w, y = y0 + i / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx < (uint)width && (uint)ny < (uint)height && owners[ny * width + nx] == Ownership.University)
                    {
                        if (!reached[i]) { reached[i] = true; queue.Enqueue(i); }
                    }
                }
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % w, y = i / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= y1 - y0 + 1) continue;
                    int n = ny * w + nx;
                    if (candidate[n] && !reached[n]) { reached[n] = true; queue.Enqueue(n); }
                }
        }
        for (int i = 0; i < candidate.Length; i++) if (reached[i]) list.Add((y0 + i / w) * width + x0 + i % w);
        return list;
    }

    public static string Money(long cents) => cents >= 100_000_00 ? $"${cents / 100.0:N0}" : $"${cents / 100.0:N2}";
}
