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
/// money, the ownership overlay, and a day/night switch. The HUD (GDScript) reads <see cref="GetHud"/> and calls the
/// control methods; the sim world is built on a worker thread behind a loading message.
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

    // Land tools.
    private LandAction? _tool;
    private int _dragStart = -1, _dragEnd = -1;
    private LandQuote? _quote;
    private readonly List<string> _messages = [];

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

        var map = RealMapLoader.Load(source);
        _map = new MapRenderer(this, source, map, GetNodeOrNull<DirectionalLight3D>(SunPath),
            GetNodeOrNull<WorldEnvironment>(EnvironmentPath)?.Environment);
        var start = _scenario.Start;
        _map.SetDate(_scenario.MapYear, Math.Min(365, start.DayOfYear));
        _startFocus = FocusPoint(map);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);

        // The sim shares the map (it sets that year's land, surfaces and building tiles; the renderer keeps its own
        // copy of today's land states and roads). Built on a worker: flow fields + population take a moment.
        _loadStatus = $"{_scenario.Name}: building {start.Year} Oxford and its {_scenario.Students + _scenario.Faculty:N0} people…";
        _loading = Task.Run(() => SimWorld.CreateScenario(_data, source, scenarioId, map: map));
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

        if (s.LandVersion != _landVersion && s.LandVersion >= 0)
        {
            _landVersion = s.LandVersion;
            _map.SetLiveLand(s.LandStates, s.Surfaces, s.Owners, s.Clearing);
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
        }

        UpdateLandTool();
        if (camera is not null) DrawWalkers(camera, (float)delta);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_tool is null || _runner is null) return;
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
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed && _dragStart >= 0)
            {
                _dragStart = _dragEnd = -1; // right-click cancels the drag (camera orbit stays on the right button otherwise)
                _quote = null;
                _map.SetSelection(null, true);
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

    /// <summary>Land tool: "clear", "buy" or "" (off). While a tool is on, ownership is shown.</summary>
    public void SetLandTool(string tool)
    {
        _tool = tool switch { "clear" => LandAction.Clear, "buy" => LandAction.Buy, _ => null };
        _dragStart = _dragEnd = -1;
        _quote = null;
        _map.SetSelection(null, true);
        _map.SetOverlayMode(EffectiveOverlay);
    }

    public string GetLandTool() => _tool switch { LandAction.Clear => "clear", LandAction.Buy => "buy", _ => "" };

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
            ["tool"] = GetLandTool(),
            ["tool_hint"] = ToolHint(),
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
        hud["students"] = _students;
        hud["faculty"] = _faculty;
        hud["happiness"] = s.AverageHappiness;
        hud["cash"] = LandSystem.Money(s.CashCents);
        hud["clearing_tiles"] = s.ClearingTiles;
        hud["walks_last_hour"] = s.WalksLastTick;
        hud["tick_ms_p95"] = s.P95TickMs;
        hud["dropped_ticks"] = s.DroppedTicks;
        hud["walkers_drawn"] = _crowd?.ActiveCount ?? 0;
        hud["tick"] = s.Tick;
        return hud;
    }

    // ---------------- land tools ----------------

    private MapOverlay EffectiveOverlay => _tool is not null && _overlay == MapOverlay.None ? MapOverlay.Ownership : _overlay;

    private LandCommand Command()
    {
        int w = _map.Map.Grid.Width;
        return new LandCommand(_tool!.Value, _dragStart % w, _dragStart / w, _dragEnd % w, _dragEnd / w);
    }

    private void UpdateLandTool()
    {
        if (_tool is null || _dragStart < 0 || _snapshot is null) return;
        if (_map.HoverTile >= 0) _dragEnd = _map.HoverTile;
        var g = _map.Map.Grid;
        var c = Command();
        var s = _snapshot;
        _quote = LandSystem.Quote(_landCfg, _eras, c, g.Width, g.Height, s.LandStates, s.Owners, s.Surfaces, s.Clearing, s.Date);
        int x0 = Math.Min(c.X0, c.X1), y0 = Math.Min(c.Y0, c.Y1);
        _map.SetSelection(new Rect2I(x0, y0, Math.Abs(c.X1 - c.X0) + 1, Math.Abs(c.Y1 - c.Y0) + 1),
            _quote.Value.Ok && s.CashCents >= _quote.Value.Cents);
    }

    private string ToolHint()
    {
        if (_tool is null) return "";
        string what = _tool == LandAction.Clear ? "Clear forest: drag over university-owned woods" : "Buy land: drag over land next to the campus";
        if (_quote is not { } q || _snapshot is null) return what + " (right-click or Esc to stop).";
        if (!q.Ok) return q.Problem;
        string cost = LandSystem.Money(q.Cents);
        string afford = _snapshot.CashCents >= q.Cents ? "" : " (not enough money)";
        return _tool == LandAction.Clear
            ? $"Clear {q.Tiles} tiles: {cost}, about {Math.Ceiling(q.Days)} days{afford}. Release to order."
            : $"Buy {q.Tiles} tiles: {cost}{afford}. Release to buy.";
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
        if (OS.GetCmdlineUserArgs().Contains("--demo-land")) DemoLand();
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
        int b = _map.BuildingUnderHover();
        if (b < 0) return "";
        var hb = _map.Buildings[b];
        if (hb.Entry is null) return hb.Footprint.Name ?? "Building";
        string years = hb.Entry.BuiltYear is int y ? $"built {y}" : "build year unknown";
        return $"{hb.Name} · {years}{(hb.Entry.Verified ? "" : " (unverified)")}";
    }
}
