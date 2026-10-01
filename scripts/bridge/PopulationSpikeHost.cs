using System;
using Godot;
using LoveAndHonor.Sim;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.View;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Spike A scene host: builds the sim world, runs it on the sim thread (SimRunner), and draws a camera-chosen
/// subset of walkers with one MultiMesh (§30.2). Ground and buildings are simple placeholders; the ground texture
/// shows desire-path wear (or a foot-traffic heatmap). GDScript UI reads stats via GetDebugStats().
/// </summary>
[GlobalClass]
public partial class PopulationSpikeHost : Node3D
{
    private SimData _data = null!;
    private SimWorld _world = null!;
    private SimRunner _runner = null!;
    private SimSnapshot _snapshot = null!;
    private RenderingConfig _render = null!;
    private Palette _palette = null!;
    private VisualCrowd _crowd = null!;

    private MultiMesh _walkerMesh = null!;
    private float[] _walkerBuffer = [];
    private float[] _categoryRgba = [];

    private Image _groundImage = null!;
    private ImageTexture _groundTexture = null!;
    private byte[] _groundPixels = [];
    private int[] _latestTraffic = [];
    private float[] _latestWear = [];
    private int _latestTrafficVersion;
    private double _trafficTimer;
    private bool _heatmap;

    private float _mapWidthM, _mapHeightM;

    public override void _Ready()
    {
        var source = new GodotDataSource();
        _data = SimData.Load(source);
        _render = RenderingConfig.Load(source);
        _palette = new Palette(source.ReadText("branding.json"));
        _world = SimWorld.CreateSynthetic(_data);

        var grid = _world.Campus.Grid;
        _mapWidthM = grid.Width * grid.TileSizeM;
        _mapHeightM = grid.Height * grid.TileSizeM;

        BuildGround();
        BuildBuildings();
        BuildWalkers();

        var time = _data.Balance.Time;
        _runner = new SimRunner(_world.Simulation, grid, time.Speeds, time.RealSecondsPerGameDay, time.TicksPerGameDay);
        _runner.Start();
        _snapshot = _runner.AcquireLatest();
        GD.Print($"Spike A ready: {_world.Population.Count:N0} agents, {_world.Fields.BuildingCount} buildings, " +
                 $"flow fields {_world.FlowFieldsMs:F0} ms, {SimInfo.Describe()}, optimized={SimInfo.IsOptimizedBuild}");
    }

    public override void _ExitTree() => _runner?.Dispose();

    public override void _Process(double delta)
    {
        _runner.AdvanceRealTime(delta);
        _snapshot = _runner.AcquireLatest();

        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        var crowd = _render.Crowd;
        var view = ComputeView(camera, out float centerDistance, out Vector3 center);
        // Only populate the area near the look-at point: at a low tilt the view reaches kilometres away, and
        // walkers there would be sub-pixel. (Far crowds become impostors later, §30.2.)
        float radius = Math.Max(crowd.DetailRadiusMinM, centerDistance * crowd.DetailRadiusFactor);
        view = new GroundRect(Math.Max(view.MinX, center.X - radius), Math.Max(view.MinZ, center.Z - radius),
                              Math.Min(view.MaxX, center.X + radius), Math.Min(view.MaxZ, center.Z + radius));

        _crowd.Update((float)delta, _runner.CurrentSpeed, view, crowd.ViewMarginM, _snapshot);
        float scale = Math.Clamp(centerDistance / crowd.SizeCompensationReferenceDistanceM, 1f, crowd.SizeCompensationMaxScale);
        int n = _crowd.WriteInstances(_walkerBuffer, scale, crowd.AgentHeightM * 0.5f, _categoryRgba);
        RenderingServer.MultimeshSetBuffer(_walkerMesh.GetRid(), _walkerBuffer);
        _walkerMesh.VisibleInstanceCount = n;

        _trafficTimer += delta;
        if (_trafficTimer >= _render.Ground.RefreshSeconds)
        {
            _trafficTimer = 0;
            _runner.RequestTraffic();
        }
        if (_snapshot.TrafficVersion > _latestTrafficVersion)
        {
            Array.Copy(_snapshot.Traffic, _latestTraffic, _latestTraffic.Length);
            Array.Copy(_snapshot.Wear, _latestWear, _latestWear.Length);
            _latestTrafficVersion = _snapshot.TrafficVersion;
            PaintGround();
        }
    }

    // ---------------- API for GDScript ----------------

    public Vector2 GetMapSizeMeters() => new(_mapWidthM, _mapHeightM);

    public float[] GetSpeeds() => [.. _runner.Speeds];

    public int GetSpeedIndex() => _runner.SpeedIndex;

    public void SetSpeedIndex(int index) => _runner.SpeedIndex = index;

    public bool ToggleHeatmap()
    {
        _heatmap = !_heatmap;
        PaintGround();
        return _heatmap;
    }

    public Godot.Collections.Dictionary GetDebugStats()
    {
        var s = _snapshot;
        string[] days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        return new Godot.Collections.Dictionary
        {
            ["date"] = $"{days[s.WeekdayIndex]} {s.Date:yyyy-MM-dd}",
            ["time"] = $"{s.HourOfDay:00}:00",
            ["tick"] = s.Tick,
            ["tick_ms_last"] = s.LastTickMs,
            ["tick_ms_avg"] = s.AvgTickMs,
            ["tick_ms_p95"] = s.P95TickMs,
            ["tick_ms_max"] = s.MaxTickMs,
            ["ticks_in_window"] = s.TicksInWindow,
            ["budget_ms"] = _data.Balance.Performance.TickBudgetMs,
            ["agents_simulated"] = _world.Population.Count,
            ["agents_rendered"] = _crowd.ActiveCount,
            ["walks_last_tick"] = s.WalksLastTick,
            ["avg_happiness"] = s.AverageHappiness,
            ["dropped_ticks"] = s.DroppedTicks,
            ["speed"] = _runner.CurrentSpeed,
            ["speed_index"] = _runner.SpeedIndex,
            ["sim_threads"] = _world.Simulation.Threads,
            ["optimized"] = SimInfo.IsOptimizedBuild,
            ["heatmap"] = _heatmap,
        };
    }

    // ---------------- Scene construction ----------------

    private void BuildGround()
    {
        var grid = _world.Campus.Grid;
        _groundPixels = new byte[grid.Width * grid.Height * 3];
        _latestTraffic = new int[grid.Width * grid.Height];
        _latestWear = new float[grid.Width * grid.Height];
        _groundImage = Image.CreateFromData(grid.Width, grid.Height, false, Image.Format.Rgb8, _groundPixels);
        _groundTexture = ImageTexture.CreateFromImage(_groundImage);
        PaintGround();

        var material = new StandardMaterial3D
        {
            AlbedoTexture = _groundTexture,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Roughness = 1f,
        };
        var plane = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(_mapWidthM, _mapHeightM) },
            MaterialOverride = material,
            Position = new Vector3(_mapWidthM / 2, 0, _mapHeightM / 2),
        };
        AddChild(plane);
    }

    private void PaintGround()
    {
        var grid = _world.Campus.Grid;
        var g = _render.Ground;
        Rgb grass = _palette.Resolve(g.Grass), path = _palette.Resolve(g.Path), building = _palette.Resolve(g.Building);
        Rgb wear = _palette.Resolve(g.Wear), low = _palette.Resolve(g.HeatmapLow), high = _palette.Resolve(g.HeatmapHigh);
        int max = 1;
        if (_heatmap) foreach (int t in _latestTraffic) if (t > max) max = t;
        float logMax = MathF.Log(1 + max);

        for (int i = 0; i < grid.Types.Length; i++)
        {
            Rgb c;
            int traffic = _latestTraffic[i];
            if (grid.Types[i] == TileType.Building) c = building;
            else if (_heatmap)
                c = traffic == 0 ? Rgb.Lerp(grass, new Rgb(0, 0, 0), 0.6f) : Rgb.Lerp(low, high, MathF.Log(1 + traffic) / logMax);
            else if (grid.Types[i] == TileType.Path) c = path;
            else c = Rgb.Lerp(grass, wear, _latestWear[i]); // desire-path wear (§12.4): regular shortcuts wear to dirt
            _groundPixels[i * 3] = (byte)(c.R * 255);
            _groundPixels[i * 3 + 1] = (byte)(c.G * 255);
            _groundPixels[i * 3 + 2] = (byte)(c.B * 255);
        }
        _groundImage.SetData(grid.Width, grid.Height, false, Image.Format.Rgb8, _groundPixels);
        _groundTexture.Update(_groundImage);
    }

    private void BuildBuildings()
    {
        var buildings = _world.Campus.Buildings;
        float tile = _world.Campus.Grid.TileSizeM;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One },
        };
        mm.InstanceCount = buildings.Count;
        foreach (var b in buildings)
        {
            string key = KindKey(b.Kind);
            float height = _render.Buildings.HeightM[key];
            var basis = Basis.Identity.Scaled(new Vector3(b.W * tile, height, b.H * tile));
            var origin = new Vector3((b.X + b.W * 0.5f) * tile, height * 0.5f, (b.Y + b.H * 0.5f) * tile);
            mm.SetInstanceTransform(b.Index, new Transform3D(basis, origin));
            var c = _palette.Resolve(_render.Buildings.Color[key]);
            mm.SetInstanceColor(b.Index, new Color(c.R, c.G, c.B));
        }
        AddChild(new MultiMeshInstance3D
        {
            Name = "Buildings",
            Multimesh = mm,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.9f },
        });
    }

    private void BuildWalkers()
    {
        var cfg = _render.Crowd;
        _crowd = new VisualCrowd(_world.Fields, _world.Campus.Grid, cfg.MaxRenderedAgents, cfg.VisualWalkSpeedMps,
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

        var colors = _render.AgentColors;
        _categoryRgba = new float[5 * 4];
        for (int i = 0; i < 5; i++)
        {
            var c = _palette.Resolve(i < 4 ? colors.StudentByYear[i] : colors.Faculty);
            _categoryRgba[i * 4] = c.R; _categoryRgba[i * 4 + 1] = c.G; _categoryRgba[i * 4 + 2] = c.B; _categoryRgba[i * 4 + 3] = 1f;
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "Walkers",
            Multimesh = _walkerMesh,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.8f },
            // Instances move every frame; a map-sized AABB avoids stale-bounds culling.
            CustomAabb = new Aabb(Vector3.Zero, new Vector3(_mapWidthM, 100, _mapHeightM)),
        });
    }

    /// <summary>Ground rectangle seen by the camera: the four viewport corner rays intersected with y = 0.</summary>
    private GroundRect ComputeView(Camera3D camera, out float centerDistance, out Vector3 center)
    {
        var size = GetViewport().GetVisibleRect().Size;
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        Vector2[] corners = [Vector2.Zero, new(size.X, 0), new(0, size.Y), size];
        float far = camera.Far * 0.5f;
        foreach (var corner in corners)
        {
            var p = GroundHit(camera, corner, far);
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
        }
        center = GroundHit(camera, size / 2, far);
        centerDistance = camera.GlobalPosition.DistanceTo(center);
        return new GroundRect(Math.Max(0, minX), Math.Max(0, minZ), Math.Min(_mapWidthM, maxX), Math.Min(_mapHeightM, maxZ));
    }

    private static Vector3 GroundHit(Camera3D camera, Vector2 screen, float fallbackDistance)
    {
        var origin = camera.ProjectRayOrigin(screen);
        var dir = camera.ProjectRayNormal(screen);
        if (dir.Y < -1e-4f)
        {
            float t = -origin.Y / dir.Y;
            if (t < fallbackDistance) return origin + dir * t;
        }
        var p = origin + dir * fallbackDistance; // ray hits the horizon: clamp to a far point
        return new Vector3(p.X, 0, p.Z);
    }

    private static string KindKey(BuildingKind kind) => kind switch
    {
        BuildingKind.Academic => "academic",
        BuildingKind.Residence => "residence",
        BuildingKind.Dining => "dining",
        BuildingKind.Library => "library",
        BuildingKind.Recreation => "recreation",
        BuildingKind.OffCampusHousing => "off_campus_housing",
        _ => "other",
    };
}
