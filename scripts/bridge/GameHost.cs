using System;
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
/// The game scene's host (Phase 1, 1c — the shell): the real Oxford map (<see cref="MapRenderer"/>), the simulation on
/// the real campus running on its own thread (<see cref="SimRunner"/>), walkers drawn on the terrain, and the sun,
/// seasons and desire paths following the sim's clock. The HUD (GDScript) reads <see cref="GetHud"/> and calls the
/// control methods. The sim world is built on a worker thread so the window stays responsive while it loads.
/// </summary>
[GlobalClass]
public partial class GameHost : Node3D
{
    [Export] public NodePath SunPath { get; set; } = new();
    [Export] public NodePath EnvironmentPath { get; set; } = new();

    private static readonly string[] Weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    private SimData _data = null!;
    private MapRenderer _map = null!;
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
    private int _trafficVersion;
    private double _trafficTimer;
    private bool _trafficOverlay;

    public override void _Ready()
    {
        var source = new GodotDataSource();
        _data = SimData.Load(source);
        var map = RealMapLoader.Load(source);
        _map = new MapRenderer(this, source, map, GetNodeOrNull<DirectionalLight3D>(SunPath),
            GetNodeOrNull<WorldEnvironment>(EnvironmentPath)?.Environment);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);

        // The sim shares the map (it adds the walking-surface classes and building tiles; the renderer only reads
        // heights, land states and paths). Built on a worker: flow fields + population take a few seconds.
        _loadStatus = "Building the campus and its 32,500 people…";
        _loading = Task.Run(() => SimWorld.CreateReal(_data, source, map: map));
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

        // Clock → sun and seasons (the map repaints on a worker when the day changes).
        float hour = s.HourOfDay + (float)_runner.TickFraction;
        _map.SetDate(s.Date.Year, Math.Min(365, s.Date.DayOfYear), async: true);
        _map.SetHour(hour);

        // Desire paths (always shown) and the foot-traffic overlay: refreshed from the sim about once a second.
        _trafficTimer += delta;
        if (_trafficTimer >= 1.0)
        {
            _trafficTimer = 0;
            _runner.RequestTraffic();
        }
        if (s.TrafficVersion > _trafficVersion)
        {
            _trafficVersion = s.TrafficVersion;
            _map.SetOverlay(_trafficOverlay ? MapOverlay.FootTraffic : MapOverlay.DesirePaths, s.Wear, s.Traffic);
        }

        if (camera is not null) DrawWalkers(camera, (float)delta);
    }

    // ---------------- API for GDScript ----------------

    public Vector2 GetMapSizeMeters() => new(_map.Map.SizeM, _map.Map.SizeM);

    public float GetGroundHeight(float x, float z) => _map.GroundHeight(x, z);

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

    /// <summary>O: foot-traffic overlay on/off (desire paths are always drawn). Returns the overlay now shown.</summary>
    public string CycleOverlay()
    {
        _trafficOverlay = !_trafficOverlay;
        _trafficVersion = 0; // repaint with the next traffic copy
        _runner?.RequestTraffic();
        return _trafficOverlay ? "Foot traffic" : "None";
    }

    public bool ToggleGrid() => _map.ToggleGrid();

    public Godot.Collections.Dictionary GetHud()
    {
        var hud = new Godot.Collections.Dictionary
        {
            ["ready"] = _runner is not null,
            ["load_status"] = _loadStatus,
            ["overlay"] = _trafficOverlay ? "Foot traffic" : "None",
            ["hover"] = HoverText(),
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
        hud["walks_last_hour"] = s.WalksLastTick;
        hud["tick_ms_p95"] = s.P95TickMs;
        hud["dropped_ticks"] = s.DroppedTicks;
        hud["walkers_drawn"] = _crowd?.ActiveCount ?? 0;
        hud["tick"] = s.Tick;
        return hud;
    }

    // ---------------- internals ----------------

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
        var r = _world.CampusReport!;
        GD.Print($"Game ready: {r.CampusBuildings} Miami buildings + {r.HousingZones} housing zones, {pop.Count:N0} agents, " +
                 $"flow fields {_world.FlowFieldsMs:F0} ms, {SimInfo.Describe()}, optimized={SimInfo.IsOptimizedBuild}");
    }

    private void BuildWalkers()
    {
        var cfg = _map.Render.Crowd;
        var palette = new Palette(new GodotDataSource().ReadText("branding.json"));
        _crowd = new VisualCrowd(_world!.Fields, _world.Campus.Grid, cfg.MaxRenderedAgents, cfg.VisualWalkSpeedMps,
            cfg.LateralSpreadM, cfg.SpawnAttemptsPerFreeSlot, _data.Spike.Seed ^ 0xC0FFEEUL);
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
