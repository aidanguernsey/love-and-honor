using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LoveAndHonor.Sim;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.View;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Bridge;

/// <summary>
/// The game scene's host: the real Oxford map (<see cref="MapRenderer"/>), the simulation running on its own thread
/// (<see cref="SimRunner"/>), walkers drawn on the terrain, and the sun, seasons and desire paths following the sim's
/// clock (1c). Phase 1 1d adds scenarios (Chapter 1 starts in 1824), the live land layer with clearing and buying land,
/// money, the ownership overlay, and a day/night switch. 1e adds building: the era's catalogue and Heritage Projects,
/// a ghost preview with the placement checks, rotation (Z/X), construction sites, cancelling, and the Heritage site
/// layer. 1f draws buildings from the kit; 1g adds paths: laying routes (P), removing them, paving desire paths (V,
/// the Slant Walk), all drawn as meshes. The HUD (GDScript) reads <see cref="GetHud"/> and calls the control methods;
/// the sim world is built on a worker thread behind a loading message.
/// </summary>
[GlobalClass]
public partial class GameHost : Node3D
{
    [Export] public NodePath SunPath { get; set; } = new();
    [Export] public NodePath EnvironmentPath { get; set; } = new();
    [Export] public string DefaultScenario { get; set; } = "chapter1_the_hill";

    private static readonly string[] Weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    private const float FixedDaylightHour = 13f;

    private SimData _data = null!;
    private MapRenderer _map = null!;
    private ScenarioConfig _scenario = null!;
    private LandConfig _landCfg = null!;
    private EraTable _eras = null!;
    private Task<SimWorld>? _loading;
    private string _loadStatus = "Loading Oxford…";
    private SimWorld? _world;
    private SimRunner? _runner;
    private SimSnapshot? _snapshot;
    private VisualCrowd? _crowd;
    private MultiMesh? _walkerMesh;
    private float[] _walkerBuffer = [];
    private float[] _categoryRgba = [];
    private int _students, _faculty;
    private int _lastSpeedIndex = 1;
    private int _trafficVersion, _landVersion = -1;
    private double _trafficTimer;
    private MapOverlay _overlay = MapOverlay.None;
    private bool _dayNight = true;
    private Vector2 _startFocus;

    // Tools: land orders (drag a rectangle), building (ghost under the cursor), cancelling construction.
    private enum Tool { None, Clear, Buy, Build, Cancel, Path, RemovePath, Pave }
    private Tool _tool;
    private int _dragStart = -1, _dragEnd = -1;
    private LandQuote? _quote;
    private readonly List<string> _messages = [];

    // Building (1e).
    private BuildingCatalog? _catalog;
    private BuildingKit _kit = null!;
    // Paths (1g).
    private PathConfig _pathCfg = null!;
    private PathRenderer _pathRenderer = null!;
    private float[] _wear = [];
    private List<int> _pathTiles = [];
    private (int A, int B, int Land) _pathKey = (-1, -1, -1);
    private bool _demoPave, _demoPaved;
    private Dictionary<string, CatalogItem> _items = [];
    private PlacementRenderer _placementRenderer = null!;
    private CatalogItem? _buildItem;
    private int _rotation;
    private Pose? _pose;
    private PlacementQuote? _buildQuote;
    private int _quoteKeyLand = -1, _quoteKeyPlacement = -1;
    private Pose? _quoteKeyPose;
    private bool _showHeritage = true;
    private double? _cashOverride;
    private bool _demoBuild;
    private int _demoStep, _demoVersion;
    // Screenshot options: --build-item=<id> picks a building once it's on offer, --build-rotation=<deg> turns it, and
    // --ghost-at=x,z (metres) pins the ghost there instead of following the mouse.
    private string _pendingBuildItem = "";
    private Vector2? _ghostAt;
    private int _pendingRotation;

    public override void _Ready()
    {
        var source = new GodotDataSource();
        _data = SimData.Load(source);
        _landCfg = LandConfig.Load(source);
        _eras = EraTable.Load(source);
        string scenarioId = DefaultScenario;
        if (GetTree().Root.HasMeta("scenario")) scenarioId = GetTree().Root.GetMeta("scenario").AsString();
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--scenario=")) scenarioId = arg["--scenario=".Length..];
        _scenario = ScenarioConfig.Load(source, scenarioId);
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--cash=") && double.TryParse(arg["--cash=".Length..], System.Globalization.CultureInfo.InvariantCulture, out var cash))
                _cashOverride = cash;
            else if (arg.StartsWith("--build-item=")) _pendingBuildItem = arg["--build-item=".Length..];
            else if (arg.StartsWith("--build-rotation=")) _pendingRotation = int.Parse(arg["--build-rotation=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--ghost-at=") && arg["--ghost-at=".Length..].Split(',') is [var gx, var gz])
                _ghostAt = new Vector2(float.Parse(gx, System.Globalization.CultureInfo.InvariantCulture), float.Parse(gz, System.Globalization.CultureInfo.InvariantCulture));

        var map = RealMapLoader.Load(source);
        _kit = new BuildingKit(source);
        // Real buildings with a kit recipe that stand at the start are assembled from the kit (1f) instead of extruded.
        bool FromKit(HistoricBuilding b) => b.Entry is { } e && _kit.Recipes.RecipeForTimeline(e.Id) is not null
                                            && b.StandsIn(_scenario.MapYear, showUndatedAlways: false);
        _map = new MapRenderer(this, source, map, GetNodeOrNull<DirectionalLight3D>(SunPath),
            GetNodeOrNull<WorldEnvironment>(EnvironmentPath)?.Environment, FromKit);
        foreach (var b in _map.Buildings.Where(FromKit)) AddRealBuilding(b, map);
        var start = _scenario.Start;
        _map.SetDate(_scenario.MapYear, Math.Min(365, start.DayOfYear));
        _map.SetBuildingsYear(_scenario.MapYear); // later real buildings are the player's to build
        _placementRenderer = new PlacementRenderer(this, map, _map.Render, new Palette(source.ReadText("branding.json")), _kit);
        _pathCfg = PathConfig.Load(source);
        _pathRenderer = new PathRenderer(this, map, _map.Render, new Palette(source.ReadText("branding.json")), _pathCfg, _eras);
        _map.PathsAsMeshes = true;
        _wear = new float[map.Grid.Width * map.Grid.Height];
        _startFocus = FocusPoint(map);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);

        // The sim shares the map (it sets that year's land, surfaces and building tiles; the renderer keeps its own
        // copy of today's land states and roads). Built on a worker: flow fields + population take a moment.
        _loadStatus = $"{_scenario.Name}: building {start.Year} Oxford and its {_scenario.Students + _scenario.Faculty:N0} people…";
        _loading = Task.Run(() => SimWorld.CreateScenario(_data, source, scenarioId, map: map, startingCash: _cashOverride));
    }

    public override void _ExitTree() => _runner?.Dispose();

    public override void _Process(double delta)
    {
        var camera = GetViewport().GetCamera3D();
        _map.Process(camera, GetViewport().GetMousePosition());

        if (_runner is null)
        {
            if (_loading is { IsCompleted: true }) FinishLoading();
            return;
        }

        _runner.AdvanceRealTime(delta);
        _snapshot = _runner.AcquireLatest();
        var s = _snapshot;
        while (_runner.TryTakeMessage(out var m))
        {
            _messages.Add(m);
            if (_messages.Count > 6) _messages.RemoveAt(0);
        }

        // Clock → sun and seasons (the map repaints on a worker when the day changes).
        float hour = s.HourOfDay + (float)_runner.TickFraction;
        _map.SetDate(s.Date.Year, Math.Min(365, s.Date.DayOfYear), async: true);
        _map.SetHour(_dayNight ? hour : FixedDaylightHour);
        // Windows glow from dusk to dawn (§28.1).
        _kit.SetNight(1f - Mathf.SmoothStep(-4f, 4f, _map.SunElevation));

        if (s.LandVersion != _landVersion && s.LandVersion >= 0)
        {
            _landVersion = s.LandVersion;
            _map.SetLiveLand(s.LandStates, s.Surfaces, s.Owners, s.Clearing);
            _pathRenderer.Update(s);
        }

        // Desire paths (always shown) and the overlays: traffic data refreshed from the sim about once a second.
        _trafficTimer += delta;
        if (_trafficTimer >= 1.0)
        {
            _trafficTimer = 0;
            _runner.RequestTraffic();
        }
        if (s.TrafficVersion > _trafficVersion)
        {
            _trafficVersion = s.TrafficVersion;
            _map.SetOverlay(EffectiveOverlay, s.Wear, s.Traffic);
            Array.Copy(s.Wear, _wear, _wear.Length); // our own copy: snapshot buffers are reused
            if (_demoPave && !_demoPaved) DemoPave();
        }

        UpdateLandTool();
        UpdateBuildTool();
        UpdatePaveTool();
        _placementRenderer.SetSites(s.Placement, _items);
        _placementRenderer.SetHeritageSites(AvailableHeritage(), _showHeritage || _tool == Tool.Build);
        if (_demoBuild) DemoBuildStep();
        if (_pendingBuildItem.Length > 0 && _catalog?.Find(_pendingBuildItem) is { } pending
            && _catalog.AvailableOn(s.Date, s.Placement?.HeritageTaken ?? []).Contains(pending))
        {
            SetBuildItem(_pendingBuildItem);
            _pendingBuildItem = "";
            if (_pendingRotation != 0) RotateBuild(_pendingRotation / (_catalog.Config.RotationStepDeg));
        }
        if (camera is not null) DrawWalkers(camera, (float)delta);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_tool == Tool.None || _runner is null) return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && _tool is Tool.Build or Tool.Cancel or Tool.Pave)
        {
            if (_tool == Tool.Build) PlaceBuilding();
            else if (_tool == Tool.Pave) PaveHovered();
            else CancelHoveredSite();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!IsDragTool) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left && mb.Pressed && _map.HoverTile >= 0)
            {
                _dragStart = _dragEnd = _map.HoverTile;
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed && _dragStart >= 0)
            {
                if (_quote is { Ok: true }) _runner.Submit(Command());
                else if (_quote is { } q) AddMessage(q.Problem.Length > 0 ? q.Problem : "Nothing to do there.");
                _dragStart = _dragEnd = -1;
                _quote = null;
                _map.SetSelection(null, true);
                _pathRenderer.SetPreview(null, true);
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed && _dragStart >= 0)
            {
                _dragStart = _dragEnd = -1; // right-click cancels the drag (camera orbit stays on the right button otherwise)
                _quote = null;
                _map.SetSelection(null, true);
                _pathRenderer.SetPreview(null, true);
            }
        }
    }

    // ---------------- API for GDScript ----------------

    public Vector2 GetMapSizeMeters() => new(_map.Map.SizeM, _map.Map.SizeM);

    public float GetGroundHeight(float x, float z) => _map.GroundHeight(x, z);

    /// <summary>Where the camera starts: the scenario's campus (Old Main in 1824), else the map centre.</summary>
    public Vector2 GetStartFocus() => _startFocus;

    public bool IsReady() => _runner is not null;

    public float[] GetSpeeds() => _runner is null ? [] : [.. _runner.Speeds];

    public int GetSpeedIndex() => _runner?.SpeedIndex ?? 0;

    public void SetSpeedIndex(int index)
    {
        if (_runner is null) return;
        _runner.SpeedIndex = index;
        if (_runner.SpeedIndex > 0) _lastSpeedIndex = _runner.SpeedIndex;
    }

    /// <summary>Space: pause, or resume at the last speed.</summary>
    public void TogglePause()
    {
        if (_runner is null) return;
        SetSpeedIndex(_runner.SpeedIndex == 0 ? _lastSpeedIndex : 0);
    }

    /// <summary>O: None → Ownership → Foot traffic → None (desire paths are always drawn). Returns the overlay's name.</summary>
    public string CycleOverlay()
    {
        _overlay = _overlay switch { MapOverlay.None => MapOverlay.Ownership, MapOverlay.Ownership => MapOverlay.FootTraffic, _ => MapOverlay.None };
        _map.SetOverlayMode(EffectiveOverlay);
        _runner?.RequestTraffic();
        return OverlayName(_overlay);
    }

    public bool ToggleGrid() => _map.ToggleGrid();

    /// <summary>Day/night cycle on (the sun follows the clock) or off (always early afternoon; the clock still runs).</summary>
    public void SetDayNight(bool on)
    {
        _dayNight = on;
        if (!on) _map.SetHour(FixedDaylightHour);
    }

    public bool GetDayNight() => _dayNight;

    /// <summary>Tool: "clear", "buy", "cancel" (cancel construction), "build" (needs <see cref="SetBuildItem"/>) or ""
    /// (off). While a tool is on, ownership is shown.</summary>
    public void SetTool(string tool)
    {
        _tool = tool switch
        {
            "clear" => Tool.Clear, "buy" => Tool.Buy, "cancel" => Tool.Cancel, "build" when _buildItem is not null => Tool.Build,
            "path" => Tool.Path, "remove_path" => Tool.RemovePath, "pave" => Tool.Pave, _ => Tool.None,
        };
        if (_tool != Tool.Build) _buildItem = null;
        _dragStart = _dragEnd = -1;
        _quote = null;
        _buildQuote = null;
        _pose = null;
        _map.SetSelection(null, true);
        _map.SetOverlayMode(EffectiveOverlay);
        _placementRenderer.SetGhost(null, default, -1, false);
        _pathRenderer?.SetPreview(null, true);
        _pathKey = (-1, -1, -1);
    }

    public string GetTool() => _tool switch
    {
        Tool.Clear => "clear", Tool.Buy => "buy", Tool.Build => "build", Tool.Cancel => "cancel",
        Tool.Path => "path", Tool.RemovePath => "remove_path", Tool.Pave => "pave", _ => "",
    };

    /// <summary>Starts placing a catalogue item (id from <see cref="GetCatalog"/>); "" stops.</summary>
    public void SetBuildItem(string id)
    {
        _buildItem = id.Length > 0 ? _catalog?.Find(id) : null;
        if (_buildItem?.Site is { } site) _rotation = site.Pose.RotationDeg;
        SetTool(_buildItem is null ? "" : "build");
    }

    public string GetBuildItem() => _buildItem?.Id ?? "";

    /// <summary>Z / X: turn the building being placed by one rotation step (§12.1, 15°).</summary>
    public void RotateBuild(int direction)
    {
        int step = _catalog?.Config.RotationStepDeg ?? 15;
        _rotation = ((_rotation + direction * step) % 360 + 360) % 360;
    }

    public void SetShowHeritage(bool on) => _showHeritage = on;

    public bool GetShowHeritage() => _showHeritage;

    /// <summary>
    /// What can be built now, for the build menu: id, name, group (HUD category), size, cost (this era's money),
    /// months, heritage flag, whether the treasury covers it, and a description.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetCatalog()
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (_catalog is null || _snapshot is null) return list;
        var date = _snapshot.Date;
        foreach (var item in _catalog.AvailableOn(date, _snapshot.Placement?.HeritageTaken ?? []))
        {
            long cents = _catalog.CostCents(item, date);
            var def = item.Def;
            string capacity = def.Capacity.Kind switch
            {
                "seats" => $"{def.Capacity.Value} seats", "beds" => $"{def.Capacity.Value} beds",
                "diners_per_hour" => $"{def.Capacity.Value} diners an hour", "study_seats" => $"{def.Capacity.Value} study seats",
                _ => "",
            };
            string desc = item.Site is { } site
                ? $"Heritage Project: the real {site.Name} was built in {site.RealYear}. Build it anywhere; on its real site (outlined on the map) it earns a small Heritage bonus.{(item.HeritageVerified ? "" : " Cost and build time are placeholders.")}"
                : $"{def.Name}{(capacity.Length > 0 ? " · " + capacity : "")}. Placeholder numbers until the art and economy passes.";
            list.Add(new Godot.Collections.Dictionary
            {
                ["id"] = item.Id, ["name"] = item.Name, ["group"] = item.IsHeritage ? "Heritage Projects" : Group(item.Category),
                ["size"] = $"{item.W}×{item.H}", ["cost"] = LandSystem.Money(cents), ["months"] = item.Months,
                ["heritage"] = item.IsHeritage, ["affordable"] = _snapshot.CashCents >= cents, ["description"] = desc,
            });
        }
        return list;
    }

    private static string Group(string category) => category switch
    {
        "academic" or "library" => "Academic",
        "residence" => "Housing",
        "dining" => "Dining",
        "student_life" => "Student Life",
        "athletics" => "Athletics",
        "admin" or "utility" => "Admin / Utilities",
        "landscape" => "Landscape",
        _ => "Landmarks",
    };

    public Godot.Collections.Dictionary GetHud()
    {
        var hud = new Godot.Collections.Dictionary
        {
            ["ready"] = _runner is not null,
            ["load_status"] = _loadStatus,
            ["scenario"] = _scenario.Name,
            ["overlay"] = OverlayName(_overlay),
            ["day_night"] = _dayNight,
            ["hover"] = HoverText(),
            ["tool"] = GetTool(),
            ["build_item"] = GetBuildItem(),
            ["tool_hint"] = ToolHint(),
            ["show_heritage"] = _showHeritage,
            ["messages"] = string.Join("\n", _messages.AsEnumerable().Reverse()),
            ["gpu_ms"] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()),
        };
        if (_runner is null || _snapshot is null) return hud;
        var s = _snapshot;
        var phase = _data.Calendar.At(s.Date);
        float hour = s.HourOfDay + (float)_runner.TickFraction;
        hud["date"] = $"{Weekdays[s.WeekdayIndex]}, {s.Date:MMM d, yyyy}";
        hud["time"] = $"{(int)hour:00}:{(int)(hour % 1f * 60):00}";
        hud["phase"] = phase.WeekOfTerm > 0 ? $"{phase.Name} · Week {phase.WeekOfTerm}" : phase.Name;
        hud["events"] = string.Join(" · ", _data.Calendar.EventsOn(s.Date));
        hud["calendar_verified"] = _data.Calendar.Verified;
        hud["speed_index"] = _runner.SpeedIndex;
        hud["speed"] = _runner.CurrentSpeed;
        hud["students"] = s.StudentCount;
        hud["faculty"] = s.FacultyCount;
        hud["happiness"] = s.AverageHappiness;
        hud["cash"] = LandSystem.Money(s.CashCents);
        hud["clearing_tiles"] = s.ClearingTiles;
        hud["construction"] = ConstructionText(s);
        hud["demand"] = DemandText(s);
        hud["confidence"] = s.Budget is { } bv ? $"{bv.Confidence:0}" : "—";
        hud["budget"] = BudgetText(s);
        hud["tuition"] = s.Budget is { } tv ? $"Tuition: {LandSystem.Money(tv.TuitionPerYearCents)} a year ({tv.TuitionLevel:P0} of the usual rate)" : "";
        hud["dismissed"] = s.Budget?.Dismissed ?? false;
        var cv = s.Campaign;
        hud["goals"] = GoalsText(s);
        hud["outcome"] = cv?.Outcome.ToString() ?? "Playing";
        hud["outcome_text"] = cv?.OutcomeText ?? "";
        var card = cv is { Cards.Length: > 0 } ? cv.Cards[^1] : null;
        hud["card_seq"] = card?.Seq ?? 0;
        hud["card_title"] = card?.Title ?? "";
        hud["card_date"] = card is null ? "" : card.DatePrecision switch
        {
            "year" => $"{card.Date.Year}", "month" => $"{card.Date:MMMM yyyy}", _ => $"{card.Date:MMMM d, yyyy}",
        };
        hud["card_text"] = card?.Text ?? "";
        hud["card_sources"] = card is null || card.Sources.Length == 0 ? (card is { Historical: false } ? "A typical event of the era (not a historical record)." : "")
            : "Sources (not yet checked by the University Archives):\n" + string.Join("\n", card.Sources.Select(x => $"• {x.Title}: {x.Url}"));
        hud["card_codex"] = card is { Codex.Length: > 0 } ? "New in the History Book (K)." : "";
        hud["ticker"] = card is null ? "" : $"{hud["card_date"]}: {card.Title}";
        hud["heritage_bonus"] = (s.Placement?.HeritageBonus ?? 0) + s.LandHeritageBonus;
        hud["path_triangles"] = _pathRenderer.Triangles;
        hud["walks_last_hour"] = s.WalksLastTick;
        hud["tick_ms_p95"] = s.P95TickMs;
        hud["dropped_ticks"] = s.DroppedTicks;
        hud["walkers_drawn"] = _crowd?.ActiveCount ?? 0;
        hud["tick"] = s.Tick;
        return hud;
    }

    // ---------------- land tools ----------------

    private MapOverlay EffectiveOverlay => _tool != Tool.None && _overlay == MapOverlay.None ? MapOverlay.Ownership : _overlay;

    private bool IsDragTool => _tool is Tool.Clear or Tool.Buy or Tool.Path or Tool.RemovePath;

    private LandCommand Command()
    {
        int w = _map.Map.Grid.Width;
        var action = _tool switch { Tool.Clear => LandAction.Clear, Tool.Path => LandAction.Path, Tool.RemovePath => LandAction.RemovePath, _ => LandAction.Buy };
        return new LandCommand(action, _dragStart % w, _dragStart / w, _dragEnd % w, _dragEnd / w);
    }

    private PathMap SnapshotPathMap()
    {
        var s = _snapshot!;
        var g = _map.Map.Grid;
        return new PathMap(g.Width, g.Height, g.TileSizeM, s.Surfaces, s.LandStates, s.Owners, g.Protected, s.Clearing, _wear, s.PathTypes);
    }

    private void UpdateLandTool()
    {
        if (!IsDragTool || _dragStart < 0 || _snapshot is null) return;
        if (_map.HoverTile >= 0) _dragEnd = _map.HoverTile;
        var g = _map.Map.Grid;
        var c = Command();
        var s = _snapshot;
        if (_tool is Tool.Path or Tool.RemovePath)
        {
            // Routes are searched only when an end or the map changes.
            var key = (_dragStart, _dragEnd, s.LandVersion);
            if (key == _pathKey) return;
            _pathKey = key;
            _quote = LandSystem.QuotePaths(_pathCfg, _eras, c, SnapshotPathMap(), s.Date, out _pathTiles);
            bool ok = _quote.Value.Ok && s.CashCents >= _quote.Value.Cents;
            if (_tool == Tool.Path) _pathRenderer.SetPreview(_pathTiles.Count > 0 ? _pathTiles : [_dragStart, _dragEnd], ok);
            else
            {
                int rx0 = Math.Min(c.X0, c.X1), ry0 = Math.Min(c.Y0, c.Y1);
                _map.SetSelection(new Rect2I(rx0, ry0, Math.Abs(c.X1 - c.X0) + 1, Math.Abs(c.Y1 - c.Y0) + 1), ok);
            }
            return;
        }
        _quote = LandSystem.Quote(_landCfg, _eras, c, g.Width, g.Height, s.LandStates, s.Owners, s.Surfaces, s.Clearing, s.Date);
        int x0 = Math.Min(c.X0, c.X1), y0 = Math.Min(c.Y0, c.Y1);
        _map.SetSelection(new Rect2I(x0, y0, Math.Abs(c.X1 - c.X0) + 1, Math.Abs(c.Y1 - c.Y0) + 1),
            _quote.Value.Ok && s.CashCents >= _quote.Value.Cents);
    }

    private string ToolHint()
    {
        if (_tool == Tool.Build) return BuildHint();
        if (_tool == Tool.Cancel)
            return HoveredSite() is { Complete: false } hs
                ? $"Cancel the {hs.Name} ({hs.Progress:P0} built)? Click to cancel: half of the unspent part is refunded (all of it on the day it was ordered)."
                : "Cancel construction: click a building under construction (Esc to stop).";
        if (_tool == Tool.None) return "";
        if (_tool is Tool.Path or Tool.RemovePath or Tool.Pave) return PathHint();
        string what = _tool == Tool.Clear ? "Clear forest: drag over university-owned woods" : "Buy land: drag over land next to the campus";
        if (_quote is not { } q || _snapshot is null) return what + " (right-click or Esc to stop).";
        if (!q.Ok) return q.Problem;
        string cost = LandSystem.Money(q.Cents);
        string afford = _snapshot.CashCents >= q.Cents ? "" : " (not enough money)";
        return _tool == Tool.Clear
            ? $"Clear {q.Tiles} tiles: {cost}, about {Math.Ceiling(q.Days)} days{afford}. Release to order."
            : $"Buy {q.Tiles} tiles: {cost}{afford}. Release to buy.";
    }

    // ---------------- paths (1g) ----------------

    private string SurfaceName() => _snapshot is null ? "path" : _pathCfg.Surfaces[_pathCfg.SurfaceFor(_eras, _snapshot.Date.Year)].Name.ToLowerInvariant();

    private string PathHint()
    {
        string afford = _quote is { } aq && _snapshot is not null && _snapshot.CashCents < aq.Cents ? " (not enough money)" : "";
        switch (_tool)
        {
            case Tool.Path:
                if (_dragStart < 0 || _quote is not { } q) return $"Lay a {SurfaceName()}: drag from one end to the other; the route goes round obstacles and joins existing paths (Esc to stop).";
                if (!q.Ok) return q.Problem;
                return $"{q.Tiles} new tiles of {SurfaceName()}{(q.Skipped > 0 ? $" (+{q.Skipped} on existing paths)" : "")}: {LandSystem.Money(q.Cents)}{afford}. Release to lay it.";
            case Tool.RemovePath:
                if (_dragStart < 0 || _quote is not { } r) return "Remove paths: drag over footpaths on university land (roads stay). Free.";
                return r.Ok ? $"Remove {r.Tiles} tiles of path. Release to remove." : r.Problem;
            default:
                if (_quote is not { } p) return "Pave a desire path: point at a shortcut worn into the lawn (Esc to stop).";
                if (!p.Ok) return p.Problem;
                var shape = PathPlanner.SlantWalk(_pathCfg, _pathTiles, _map.Map.Grid.Width, _map.Map.Grid.TileSizeM);
                string slant = _snapshot is { LandHeritageBonus: 0 } && shape.Qualifies ? " A long diagonal: paving it makes it the Slant Walk!" : "";
                return $"Pave this desire path: {p.Tiles} tiles of {SurfaceName()}, {LandSystem.Money(p.Cents)}{afford}. Click to pave.{slant}";
        }
    }

    /// <summary>Pave tool: preview the desire path under the cursor.</summary>
    private void UpdatePaveTool()
    {
        if (_tool != Tool.Pave || _snapshot is not { } s) return;
        int t = _map.HoverTile;
        var key = (t, t, s.LandVersion ^ (_trafficVersion << 16));
        if (key == _pathKey) return;
        _pathKey = key;
        if (t < 0) { _quote = null; _pathRenderer.SetPreview(null, true); return; }
        int w = _map.Map.Grid.Width;
        _quote = LandSystem.QuotePaths(_pathCfg, _eras, new LandCommand(LandAction.PaveDesire, t % w, t / w, t % w, t / w), SnapshotPathMap(), s.Date, out _pathTiles);
        _pathRenderer.SetPreview(_pathTiles, _quote.Value.Ok && s.CashCents >= _quote.Value.Cents);
    }

    private void PaveHovered()
    {
        if (_quote is not { } q || _map.HoverTile < 0) return;
        if (!q.Ok) { AddMessage(q.Problem); return; }
        int w = _map.Map.Grid.Width, t = _map.HoverTile;
        _runner!.Submit(new LandCommand(LandAction.PaveDesire, t % w, t / w, t % w, t / w));
    }

    /// <summary>Launch option --demo-pave: once desire paths have worn in, paves the biggest one.</summary>
    private void DemoPave()
    {
        var g = _map.Map.Grid;
        var seen = new bool[_wear.Length];
        List<int> best = [];
        var m = SnapshotPathMap();
        for (int t = 0; t < _wear.Length; t++)
        {
            if (seen[t] || _wear[t] < TileGrid.DesirePathWear) continue;
            var region = PathPlanner.DesireRegion(m, t);
            foreach (int r in region) seen[r] = true;
            if (region.Count > best.Count) best = region;
        }
        if (best.Count < 6) return;
        int pick = best[best.Count / 2];
        _runner!.Submit(new LandCommand(LandAction.PaveDesire, pick % g.Width, pick / g.Width, pick % g.Width, pick / g.Width));
        var shape = PathPlanner.SlantWalk(_pathCfg, best, g.Width, g.TileSizeM);
        GD.Print($"DEMO_PAVE {best.Count} tiles at ({pick % g.Width}, {pick / g.Width}), {shape.LengthM:0} m, {shape.AngleFromDiagonalDeg:0} deg from diagonal, slant={shape.Qualifies}");
        _demoPaved = true;
    }

    // ---------------- building ----------------

    /// <summary>Heritage Projects on offer now (their real sites are drawn).</summary>
    private List<CatalogItem> AvailableHeritage() =>
        _catalog is null || _snapshot is null ? []
            : _catalog.AvailableOn(_snapshot.Date, _snapshot.Placement?.HeritageTaken ?? []).Where(i => i.IsHeritage).ToList();

    private PlacementMap SnapshotMap()
    {
        var s = _snapshot!;
        var g = _map.Map.Grid;
        return new PlacementMap(g.Width, g.Height, g.TileSizeM, s.Surfaces, s.LandStates, s.Owners, g.Protected, s.Clearing,
            _map.Map.Heights, s.Placement?.SortedEntrances ?? []);
    }

    /// <summary>Follows the cursor with the ghost and re-checks the placement when the pose or the map changes.</summary>
    private void UpdateBuildTool()
    {
        if (_tool != Tool.Build || _buildItem is not { } item || _snapshot is not { } s || _catalog is null) return;
        if (!_catalog.AvailableOn(s.Date, s.Placement?.HeritageTaken ?? []).Contains(item)) { SetTool(""); return; }
        if (_map.HoverTile < 0 && _ghostAt is null) { _placementRenderer.SetGhost(null, default, -1, false); _pose = null; return; }
        float tile = _map.Map.Grid.TileSizeM;
        var at = _ghostAt ?? new Vector2(_map.HoverPoint.X, _map.HoverPoint.Z);
        float hx = at.X / tile, hz = at.Y / tile;
        var pose = FootprintMath.Snap(hx, hz, item.W, item.H, _rotation);
        // Heritage Projects snap onto their real site when the cursor is close to it.
        if (item.Site is { } site)
        {
            float dx = (hx - site.Pose.Cx) * tile, dz = (hz - site.Pose.Cy) * tile;
            if (dx * dx + dz * dz <= _catalog.Config.Heritage.SnapDistanceM * _catalog.Config.Heritage.SnapDistanceM) pose = site.Pose;
        }
        _pose = pose;
        int placementVersion = s.Placement?.Version ?? -1;
        if (_buildQuote is not null && _quoteKeyPose == pose && _quoteKeyLand == s.LandVersion && _quoteKeyPlacement == placementVersion) return;
        _quoteKeyPose = pose;
        _quoteKeyLand = s.LandVersion;
        _quoteKeyPlacement = placementVersion;
        _buildQuote = PlacementSystem.Check(_catalog, item, pose, SnapshotMap(), s.Date);
        _placementRenderer.SetGhost(item, pose, _buildQuote.Entrance, _buildQuote.Ok && s.CashCents >= _buildQuote.Cents);
    }

    private void PlaceBuilding()
    {
        if (_buildItem is not { } item || _pose is not { } pose || _buildQuote is not { } q || _snapshot is null) return;
        if (!q.Ok) { AddMessage($"{item.Name}: {q.Problem}"); return; }
        if (_snapshot.CashCents < q.Cents) { AddMessage($"Not enough money for the {item.Name}: it costs {LandSystem.Money(q.Cents)}."); return; }
        _runner!.Submit(PlacementCommand.Build(item.Id, pose));
    }

    private SiteView? HoveredSite()
    {
        if (_snapshot?.Placement is not { } view || _map.HoverTile < 0) return null;
        float tile = _map.Map.Grid.TileSizeM;
        float x = _map.HoverPoint.X / tile, y = _map.HoverPoint.Z / tile;
        foreach (var site in view.Sites)
        {
            var (lx, ly) = FootprintMath.ToLocal(site.Pose, x, y);
            if (Math.Abs(lx) <= site.W / 2f && Math.Abs(ly) <= site.H / 2f) return site;
        }
        return null;
    }

    private void CancelHoveredSite()
    {
        if (HoveredSite() is not { } site) { AddMessage("Click a building under construction to cancel it."); return; }
        if (site.Complete) { AddMessage($"The {site.Name} is finished; demolition comes later."); return; }
        _runner!.Submit(PlacementCommand.Cancel(site.Id));
    }

    private string BuildHint()
    {
        if (_buildItem is not { } item) return "";
        string turn = "Z/X to turn, Esc to stop";
        if (_buildQuote is not { } q || _snapshot is null) return $"{item.Name}: point at the map ({turn}).";
        string head = $"{item.Name} · {LandSystem.Money(q.Cents)} · done about {q.EstimatedFinish:MMM d, yyyy}";
        if (!q.Ok) return $"{head}\n{q.Problem}";
        string afford = _snapshot.CashCents >= q.Cents ? "" : " Not enough money.";
        string site = item.Site is null ? "" : q.OnHeritageSite ? " On the real site: Heritage bonus." : " Not on the real site (that's fine; no bonus).";
        string warn = q.Warning.Length > 0 ? "\n" + q.Warning : "";
        return $"{head}\nClick to build ({turn}).{site}{afford}{warn}";
    }

    /// <summary>History Book (K, §19.3): every entry, unlocked ones with their text and sources.</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetCodex()
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (_world?.Content is not { } content) return list;
        var unlocked = _snapshot?.Campaign?.Unlocked ?? [];
        foreach (var e in content.Codex.OrderBy(e => e.EraYears, StringComparer.Ordinal))
        {
            bool open = unlocked.Contains(e.Id);
            list.Add(new Godot.Collections.Dictionary
            {
                ["id"] = e.Id, ["title"] = open ? e.Title : "???", ["years"] = e.EraYears, ["unlocked"] = open,
                ["text"] = open ? e.Text : "Not yet discovered: it unlocks as the college's history unfolds.",
                ["sources"] = open ? "Sources (not yet checked by the University Archives):\n" + string.Join("\n", e.Sources.Select(x => $"• {x.Title}\n   {x.Url}")) : "",
            });
        }
        return list;
    }

    private static string GoalsText(SimSnapshot s)
    {
        if (s.Campaign is not { StudentsGoal: > 0 } c) return "";
        string hall = c.HallGoal ? (c.HasHall ? "✓ a residence hall" : "✗ a residence hall (none yet)") : "";
        return $"{(c.Students >= c.StudentsGoal ? "✓" : "•")} {c.Students} of {c.StudentsGoal} students\n{hall}\nby {c.Deadline:MMMM d, yyyy}";
    }

    /// <summary>Y / Budget panel: − or + one tuition step.</summary>
    public void ChangeTuition(int direction)
    {
        if (_runner is null || _snapshot?.Budget is not { } b || _world?.Budget is not { } budget) return;
        _runner.SubmitTuitionLevel(b.TuitionLevel + direction * budget.Config.Tuition.Step);
    }

    /// <summary>The budget panel (§8, 1i): this year so far and last year, by category.</summary>
    private static string BudgetText(SimSnapshot s)
    {
        if (s.Budget is not { } b) return "";
        var sb = new System.Text.StringBuilder();
        void Report(LoveAndHonor.Sim.Economy.BudgetReport r)
        {
            sb.Append(r.Title).Append('\n');
            foreach (var line in r.Lines) sb.Append($"   {line.Name}: {LandSystem.Money(line.Cents)}\n");
            sb.Append($"   Running budget: {(r.Operating >= 0 ? "surplus" : "deficit")} {LandSystem.Money(Math.Abs(r.Operating))}");
            sb.Append($" · building and land {LandSystem.Money(-r.Capital)}\n");
        }
        Report(b.YearToDate);
        if (b.LastYear is { } last) { sb.Append('\n'); Report(last); }
        sb.Append($"\nTrustee review {b.NextReview:MMM d, yyyy}: they want a running surplus and growing enrollment.");
        if (!b.Verified) sb.Append("\nThese amounts are placeholders, not historical figures.");
        return sb.ToString();
    }

    /// <summary>§12.7 demand, the 1h part: beds and seats against capacity, and the enrollment calendar.</summary>
    private static string DemandText(SimSnapshot s)
    {
        if (s.Enrollment is not { } e) return $"{s.StudentCount:N0} students, {s.FacultyCount:N0} faculty (fixed population in this preview).";
        int beds = e.CampusBeds + e.TownBeds;
        return $"Beds: {e.Students} of {beds} ({e.OnCampus} of {e.CampusBeds} in halls, {e.InTown} boarding in town)\n" +
               $"Seats: {e.Students} of {e.Seats} classroom seats\n" +
               $"Years: {e.ByYear[0]} · {e.ByYear[1]} · {e.ByYear[2]} · {e.ByYear[3]} (1st–4th)\n" +
               $"Next move-in {e.NextIntake:MMM d, yyyy} · commencement {e.NextCommencement:MMM d}\n" +
               (e.LastApplicants > 0 ? $"Last intake: {e.LastIntake} of {e.LastApplicants} applicants · " : "") +
               $"{e.Alumni} alumni · scholarship {e.Scholarship:0.#} works";
    }

    private static string ConstructionText(SimSnapshot s)
    {
        if (s.Placement is not { } v) return "";
        var lines = v.Sites.Where(x => !x.Complete).Select(x => $"Building {x.Name}: {x.Progress:P0}, done about {x.EstimatedFinish:MMM d, yyyy}.");
        return string.Join("\n", lines);
    }

    private void AddMessage(string m)
    {
        _messages.Add(m);
        if (_messages.Count > 6) _messages.RemoveAt(0);
    }

    private static string OverlayName(MapOverlay o) => o switch
    {
        MapOverlay.Ownership => "Ownership",
        MapOverlay.FootTraffic => "Foot traffic",
        _ => "None",
    };

    // ---------------- internals ----------------

    /// <summary>A real building drawn from its kit recipe on its real outline (size, centre and turn fitted to it).</summary>
    private void AddRealBuilding(HistoricBuilding b, RealMap map)
    {
        var recipe = _kit.Recipes.RecipeForTimeline(b.Entry!.Id)!;
        var (cx, cy, deg, w, h) = FootprintMath.FitExtents(b.Footprint.Outline, 15);
        float tile = map.Grid.TileSizeM;
        float weathering = Math.Clamp((_scenario.MapYear - b.BuiltYear) / 120f, 0f, 1f);
        AddChild(_kit.CreateOnTerrain(recipe, cx * tile, cy * tile, deg, w * tile, h * tile, map.Heights, weathering,
            (ulong)b.BuiltYear * 31UL + (ulong)b.Entry.Id.Length, $"Real_{b.Entry.Id}"));
    }

    private Vector2 FocusPoint(RealMap map)
    {
        if (_scenario.Campus is { } c)
        {
            var b = _map.Buildings.FirstOrDefault(x => x.Entry?.Id == c.CenterTimelineId);
            if (b is not null)
            {
                var (x, y) = b.Footprint.Centroid();
                return new Vector2(x * map.Grid.TileSizeM, y * map.Grid.TileSizeM);
            }
        }
        return new Vector2(map.SizeM / 2, map.SizeM / 2);
    }

    private void FinishLoading()
    {
        if (_loading!.IsFaulted)
        {
            _loadStatus = "Failed to build the simulation: " + _loading.Exception?.GetBaseException().Message;
            GD.PushError(_loadStatus);
            _loading = null;
            return;
        }
        _world = _loading.Result;
        _loading = null;
        var pop = _world.Population;
        _students = pop.Kind.Count(k => k == AgentKind.Student);
        _faculty = pop.Count - _students;

        BuildWalkers();
        var time = _data.Balance.Time;
        _runner = new SimRunner(_world.Simulation, _world.Campus.Grid, time.Speeds, time.RealSecondsPerGameDay, time.TicksPerGameDay);
        _runner.Start();
        _snapshot = _runner.AcquireLatest();
        _catalog = _world.Placement?.Catalog;
        _items = _catalog?.Items.ToDictionary(i => i.Id) ?? [];
        if (OS.GetCmdlineUserArgs().Contains("--demo-land")) DemoLand();
        _demoBuild = OS.GetCmdlineUserArgs().Contains("--demo-build");
        _demoPave = OS.GetCmdlineUserArgs().Contains("--demo-pave");
        var r = _world.CampusReport!;
        GD.Print($"Game ready ({_scenario.Name}): {r.CampusBuildings} Miami buildings + {r.HousingZones} housing zones, " +
                 $"{pop.Count:N0} agents, flow fields {_world.FlowFieldsMs:F0} ms, {SimInfo.Describe()}, optimized={SimInfo.IsOptimizedBuild}");
    }

    /// <summary>Launch option --demo-land (testing without a mouse): orders clearing on a patch of university forest and
    /// buys a strip of land east of the campus, and shows ownership.</summary>
    private void DemoLand()
    {
        var g = _world!.Campus.Grid;
        int minX = g.Width, minY = g.Height, maxX = 0, maxY = 0;
        for (int t = 0; t < g.Ownership.Length; t++)
            if (g.Ownership[t] == Ownership.University)
            {
                int x = t % g.Width, y = t / g.Width;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        _runner!.Submit(new LandCommand(LandAction.Clear, minX, minY, minX + 9, minY + 9));
        _runner.Submit(new LandCommand(LandAction.Buy, maxX + 1, minY, maxX + 8, maxY));
        _overlay = MapOverlay.Ownership;
        _map.SetOverlayMode(EffectiveOverlay);
    }

    /// <summary>
    /// Launch option --demo-build (testing without a mouse): orders a recitation hall and a boarding house near Old
    /// Main straight away; then, once Elliott Hall is offered (1825), buys and clears its real site as needed and builds
    /// it there (start with --cash=20000 to afford it).
    /// </summary>
    private void DemoBuildStep()
    {
        if (_catalog is null || _snapshot is not { } s || s.Placement is null) return;
        var g = _map.Map.Grid;
        Pose? Spot(CatalogItem item, int rot)
        {
            var oldMain = _map.Buildings.FirstOrDefault(b => b.Entry?.Id == "old_main");
            if (oldMain is null) return null;
            var (cx, cy) = oldMain.Footprint.Centroid();
            var map = SnapshotMap();
            for (int r = 4; r < 30; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        var pose = FootprintMath.Snap(cx + dx, cy + dy, item.W, item.H, rot);
                        var q = PlacementSystem.Check(_catalog, item, pose, map, s.Date);
                        if (q.Ok) return pose;
                        if (q.Problem.StartsWith("The entrance needs a path")) { ConnectDoor(q.Entrance); return pose; }
                    }
            return null;
        }
        if (_demoStep == 0)
        {
            // One order per snapshot, so the second building sees the first one's site.
            if (_catalog.Find("frame_recitation_hall") is { } hall && Spot(hall, 0) is { } p1) _runner!.Submit(PlacementCommand.Build(hall.Id, p1));
            _demoStep = 1;
            _demoVersion = s.Placement.Version;
            return;
        }
        if (_demoStep == 1)
        {
            if (s.Placement.Version == _demoVersion) return;
            if (_catalog.Find("boarding_house") is { } house && Spot(house, 90) is { } p2) _runner!.Submit(PlacementCommand.Build(house.Id, p2));
            _demoStep = 2;
            return;
        }
        if (_demoStep >= 5 || s.Tick % 24 != 1) return;
        var elliott = _catalog.Find("heritage:elliott_hall");
        if (elliott?.Site is not { } site || !_catalog.AvailableOn(s.Date, s.Placement.HeritageTaken).Contains(elliott)) return;
        var q = PlacementSystem.Check(_catalog, elliott, site.Pose, SnapshotMap(), s.Date);
        var tiles = site.Tiles.Append(q.Entrance).Where(t => t >= 0).ToArray();
        int x0 = tiles.Min(t => t % g.Width), x1 = tiles.Max(t => t % g.Width), y0 = tiles.Min(t => t / g.Width), y1 = tiles.Max(t => t / g.Width);
        if (q.Ok) { _runner!.Submit(PlacementCommand.Build(elliott.Id, site.Pose)); _demoStep = 5; }
        else if (q.Problem.StartsWith("The entrance needs a path")) { ConnectDoor(q.Entrance); _runner!.Submit(PlacementCommand.Build(elliott.Id, site.Pose)); _demoStep = 5; }
        else if (q.Problem.Contains("university land") && _demoStep == 2) { _runner!.Submit(new LandCommand(LandAction.Buy, x0, y0, x1, y1)); _demoStep = 3; }
        else if (q.Problem.Contains("forest") && _demoStep <= 3) { _runner!.Submit(new LandCommand(LandAction.Clear, x0, y0, x1, y1)); _demoStep = 4; }
    }

    /// <summary>Demo helper: a path from a door to the nearest existing path (or just at the door if none is reachable).</summary>
    private void ConnectDoor(int door)
    {
        var g = _map.Map.Grid;
        var s = _snapshot!;
        int dx0 = door % g.Width, dy0 = door / g.Width, target = door;
        var m = SnapshotPathMap();
        for (int r = 1; r < 25 && target == door; r++)
            for (int dy = -r; dy <= r && target == door; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !g.InBounds(dx0 + dx, dy0 + dy)) continue;
                    int t = g.Index(dx0 + dx, dy0 + dy);
                    if (s.Surfaces[t] == TileType.Path && PathPlanner.Route(_pathCfg, m, door, t, out _) is not null) { target = t; break; }
                }
        _runner!.Submit(new LandCommand(LandAction.Path, dx0, dy0, target % g.Width, target / g.Width));
    }

    private void BuildWalkers()
    {
        var cfg = _map.Render.Crowd;
        var palette = new Palette(new GodotDataSource().ReadText("branding.json"));
        _crowd = new VisualCrowd(_world!.Fields, _world.Campus.Grid, cfg.MaxRenderedAgents, cfg.VisualWalkSpeedMps,
            cfg.LateralSpreadM, cfg.SpawnAttemptsPerFreeSlot, _scenario.Seed ^ 0xC0FFEEUL);
        _walkerMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new CapsuleMesh { Radius = cfg.AgentRadiusM, Height = cfg.AgentHeightM, RadialSegments = 6, Rings = 1 },
        };
        _walkerMesh.InstanceCount = cfg.MaxRenderedAgents;
        _walkerMesh.VisibleInstanceCount = 0;
        _walkerBuffer = new float[cfg.MaxRenderedAgents * VisualCrowd.FloatsPerInstance];
        var colors = _map.Render.AgentColors;
        _categoryRgba = new float[5 * 4];
        for (int i = 0; i < 5; i++)
        {
            var c = palette.Resolve(i < 4 ? colors.StudentByYear[i] : colors.Faculty);
            _categoryRgba[i * 4] = c.R; _categoryRgba[i * 4 + 1] = c.G; _categoryRgba[i * 4 + 2] = c.B; _categoryRgba[i * 4 + 3] = 1f;
        }
        float size = _map.Map.SizeM;
        AddChild(new MultiMeshInstance3D
        {
            Name = "Walkers",
            Multimesh = _walkerMesh,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.8f },
            CustomAabb = new Aabb(new Vector3(0, -50, 0), new Vector3(size, 200, size)),
        });
    }

    private void DrawWalkers(Camera3D camera, float delta)
    {
        var cfg = _map.Render.Crowd;
        var view = ComputeView(camera, out float centerDistance, out Vector3 center);
        float radius = Math.Max(cfg.DetailRadiusMinM, centerDistance * cfg.DetailRadiusFactor);
        view = new GroundRect(Math.Max(view.MinX, center.X - radius), Math.Max(view.MinZ, center.Z - radius),
                              Math.Min(view.MaxX, center.X + radius), Math.Min(view.MaxZ, center.Z + radius));
        _crowd!.Update(delta, _runner!.CurrentSpeed, view, cfg.ViewMarginM, _snapshot!);
        float scale = Math.Clamp(centerDistance / cfg.SizeCompensationReferenceDistanceM, 1f, cfg.SizeCompensationMaxScale);
        int n = _crowd.WriteInstances(_walkerBuffer, scale, cfg.AgentHeightM * 0.5f, _categoryRgba, _map.Map.Heights);
        RenderingServer.MultimeshSetBuffer(_walkerMesh!.GetRid(), _walkerBuffer);
        _walkerMesh.VisibleInstanceCount = n;
    }

    /// <summary>Ground area the camera sees: viewport corner rays against the plane at the look-at point's height.</summary>
    private GroundRect ComputeView(Camera3D camera, out float centerDistance, out Vector3 center)
    {
        var size = GetViewport().GetVisibleRect().Size;
        var mid = camera.ProjectRayOrigin(size / 2);
        float planeY = _map.GroundHeight(mid.X, mid.Z);
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        Vector2[] corners = [Vector2.Zero, new(size.X, 0), new(0, size.Y), size];
        float far = camera.Far * 0.5f;
        foreach (var corner in corners)
        {
            var p = PlaneHit(camera, corner, planeY, far);
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
        }
        center = PlaneHit(camera, size / 2, planeY, far);
        centerDistance = camera.GlobalPosition.DistanceTo(center);
        float mapSize = _map.Map.SizeM;
        return new GroundRect(Math.Max(0, minX), Math.Max(0, minZ), Math.Min(mapSize, maxX), Math.Min(mapSize, maxZ));
    }

    private static Vector3 PlaneHit(Camera3D camera, Vector2 screen, float planeY, float fallbackDistance)
    {
        var origin = camera.ProjectRayOrigin(screen);
        var dir = camera.ProjectRayNormal(screen);
        if (dir.Y < -1e-4f)
        {
            float t = (planeY - origin.Y) / dir.Y;
            if (t > 0 && t < fallbackDistance) return origin + dir * t;
        }
        var p = origin + dir * fallbackDistance; // the ray reaches the horizon: clamp to a far point
        return new Vector3(p.X, planeY, p.Z);
    }

    private string HoverText()
    {
        if (HoveredSite() is { } site)
            return site.Complete ? $"{site.Name} · built by you" : $"{site.Name} · under construction {site.Progress:P0}, done about {site.EstimatedFinish:MMM d, yyyy}";
        int b = _map.BuildingUnderHover();
        if (b < 0) return "";
        var hb = _map.Buildings[b];
        if (hb.Entry is null) return hb.Footprint.Name ?? "Building";
        string years = hb.Entry.BuiltYear is int y ? $"built {y}" : "build year unknown";
        return $"{hb.Name} · {years}{(hb.Entry.Verified ? "" : " (unverified)")}";
    }
}
