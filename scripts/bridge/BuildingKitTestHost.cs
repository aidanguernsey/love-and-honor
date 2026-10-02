using System.Collections.Generic;
using System.Linq;
using Godot;
using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Phase 1 (1f) building-kit check (the deferred §37 art spike, not gameplay): every recipe assembled from the
/// Georgian kit on flat ground, a hall at three construction stages, triangle counts per building at each distance,
/// and a night switch to see the window glow. Boot menu → "1f — Building kit".
/// </summary>
[GlobalClass]
public partial class BuildingKitTestHost : Node3D
{
    [Export] public NodePath SunPath { get; set; } = new();
    [Export] public NodePath EnvironmentPath { get; set; } = new();

    private BuildingKit _kit = null!;
    private DirectionalLight3D? _sun;
    private Environment? _env;
    private bool _night;
    private readonly List<string> _stats = [];

    private static readonly (string Recipe, string Label, float W, float D, float X, float Z, float Progress, float Weathering)[] Layout =
    [
        ("frame_one_storey", "Frame, 1 storey", 18, 8, 20, 40, 1, 0.1f),
        ("frame_two_storey", "Frame, 2 storeys", 18, 9, 50, 40, 1, 0.3f),
        ("georgian_house", "Georgian house", 15, 12, 80, 40, 1, 0.2f),
        ("georgian_hall", "Generic hall", 33, 15, 125, 40, 1, 0.4f),
        ("elliott_hall", "Elliott Hall", 13, 32, 172, 48, 1, 0.9f),
        ("old_main", "Old Main", 52, 21, 230, 40, 1, 0.6f),
        ("georgian_hall_l", "L-shaped hall", 48, 38, 50, 110, 1, 0.2f),
        ("georgian_hall", "Building (30%)", 33, 15, 125, 105, 0.3f, 0),
        ("georgian_hall", "Building (60%)", 33, 15, 170, 105, 0.6f, 0),
        ("georgian_hall", "Building (90%)", 33, 15, 215, 105, 0.9f, 0),
    ];

    public override void _Ready()
    {
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        var data = new GodotDataSource();
        var palette = new Palette(data.ReadText("branding.json"));
        var lawn = palette.Resolve("lawn");
        AddChild(new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(300, 200) },
            Position = new Vector3(150, 0, 100),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(lawn.R, lawn.G, lawn.B), Roughness = 1f },
        });
        _sun = GetNodeOrNull<DirectionalLight3D>(SunPath);
        _env = GetNodeOrNull<WorldEnvironment>(EnvironmentPath)?.Environment;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _kit = new BuildingKit(data);
        double loadMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        ulong seed = 1;
        foreach (var (recipe, label, w, d, x, z, progress, weathering) in Layout)
        {
            var b = _kit.Assemble(recipe, w, d, 0.5f, progress);
            AddChild(_kit.Create(b, new Vector3(x, 0, z), 0, weathering, seed++, label.Replace(" ", "_").Replace("(", "").Replace(")", "").Replace("%", "pct").Replace(",", "")));
            _stats.Add($"{label}: {_kit.Triangles(b):N0} tris, {b.Pieces.Count} pieces, far block {b.Massing.Values.Sum(m => m.TriangleCount)}");
        }
        _stats.Insert(0, $"Kit: {_kit.PiecesLoaded} pieces loaded in {loadMs:F0} ms; {Layout.Length} buildings assembled in {sw.Elapsed.TotalMilliseconds:F0} ms");
        if (OS.GetCmdlineUserArgs().Contains("--night")) SetNight(true);
    }

    public Vector2 GetMapSizeMeters() => new(300, 200);

    public float GetGroundHeight(float x, float z) => 0;

    public Vector2 GetStartFocus() => new(130, 75);

    public string GetStats() =>
        string.Join("\n", _stats) + $"\nGPU {RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()):F2} ms · " +
        $"{Engine.GetFramesPerSecond()} FPS · {(_night ? "night" : "day")} (N)";

    public bool GetNight() => _night;

    public void SetNight(bool on)
    {
        _night = on;
        _kit.SetNight(on ? 1f : 0f);
        if (_sun is not null) _sun.LightEnergy = on ? 0.05f : 1.3f;
        if (_env is not null)
        {
            _env.AmbientLightEnergy = on ? 0.15f : 1f;
            _env.BackgroundEnergyMultiplier = on ? 0.1f : 1f;
        }
    }
}
