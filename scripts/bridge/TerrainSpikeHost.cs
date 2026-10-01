using System;
using System.Linq;
using Godot;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Spike B host: real Oxford terrain and the 1809 → 2026 timeline (land states and roads repaint by year, building
/// footprints shown by year, seasons, sun for Oxford). The drawing lives in <see cref="MapRenderer"/> (shared with the
/// game, Phase 1 1c); this node adds the timeline/inspector API the GDScript overlay uses.
/// </summary>
[GlobalClass]
public partial class TerrainSpikeHost : Node3D
{
    [Export] public NodePath SunPath { get; set; } = new();
    [Export] public NodePath EnvironmentPath { get; set; } = new();

    private MapRenderer _r = null!;

    public override void _Ready()
    {
        var source = new GodotDataSource();
        var map = RealMapLoader.Load(source);
        _r = new MapRenderer(this, source, map, GetNodeOrNull<DirectionalLight3D>(SunPath),
            GetNodeOrNull<WorldEnvironment>(EnvironmentPath)?.Environment);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        GD.Print($"Spike B: {_r.Chunks} terrain chunks × {_r.Render.Terrain.LodSteps.Length} LODs ({_r.TrianglesPerLod0:N0} tris), " +
                 $"{_r.Buildings.Count:N0} building footprints ({_r.BuildingTriangles:N0} tris), {_r.Timeline.Entries.Length} timeline entries " +
                 $"({_r.Unplaced.Count} without a footprint), load {_r.LoadMs:F0} ms, build {_r.BuildMs:F0} ms");
    }

    public override void _Process(double delta) => _r.Process(GetViewport().GetCamera3D(), GetViewport().GetMousePosition());

    // ---------------- API for GDScript ----------------

    public Vector2 GetMapSizeMeters() => new(_r.Map.SizeM, _r.Map.SizeM);

    public float GetGroundHeight(float x, float z) => _r.GroundHeight(x, z);

    public Vector2I GetYearRange() => new(_r.History.StartYear, _r.History.PresentYear);

    public int GetYear() => _r.Year;

    public void SetYear(int year) => _r.SetDate(year, _r.DayOfYear);

    public int GetDayOfYear() => _r.DayOfYear;

    public void SetDayOfYear(int day) => _r.SetDate(_r.Year, day);

    public float GetHour() => _r.Hour;

    public void SetHour(float hour) => _r.SetHour(hour);

    public void SetShowUndated(bool show) => _r.SetShowUndated(show);

    public bool ToggleGrid() => _r.ToggleGrid();

    public Godot.Collections.Dictionary GetStats() => new()
    {
        ["chunks"] = _r.Chunks,
        ["lods"] = _r.Render.Terrain.LodSteps.Length,
        ["triangles_full_detail"] = _r.TrianglesPerLod0,
        ["building_footprints"] = _r.Buildings.Count,
        ["building_triangles"] = _r.BuildingTriangles,
        ["load_ms"] = _r.LoadMs,
        ["build_ms"] = _r.BuildMs,
        ["paint_ms"] = _r.PaintMs,
        ["grid"] = _r.GridVisible,
        ["primitives_in_frame"] = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
        ["draw_calls"] = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
        ["video_mem_mb"] = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0),
        ["gpu_ms"] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()),
        ["render_cpu_ms"] = RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()),
        ["elevation_min_m"] = _r.Map.Heights.BaseElevationM,
        ["elevation_max_m"] = _r.Map.Heights.BaseElevationM + _r.ReliefM,
    };

    public Godot.Collections.Dictionary GetYearInfo()
    {
        int year = _r.Year;
        var events = new Godot.Collections.Array<string>();
        foreach (var e in _r.Timeline.Entries)
        {
            if (e.BuiltYear == year) events.Add($"+ {e.Name} built ({e.Confidence})");
            if (e.DemolishedYear == year)
                events.Add($"− {e.Name} {e.Status switch { "incorporated" => "absorbed into another building", "moved" => "moved away", _ => "demolished" }} ({e.Confidence})");
            foreach (var ev in e.Events)
                if (ev.Year == year) events.Add($"• {e.Name}: {ev.Event}");
        }
        var era = _r.Eras.At(year);
        return new()
        {
            ["year"] = year,
            ["era"] = era.Name,
            ["road_type"] = era.RoadType,
            ["day"] = _r.DayOfYear,
            ["hour"] = _r.Hour,
            ["sun_elevation"] = _r.SunElevation,
            ["sun_azimuth"] = _r.SunAzimuth,
            ["standing_timeline"] = _r.Timeline.Entries.Count(e => e.StandsIn(year, _r.History.PresentYear)),
            ["events"] = events,
            ["unplaced"] = _r.Unplaced.Count,
            ["show_undated"] = _r.ShowUndated,
        };
    }

    public Godot.Collections.Dictionary GetHoverInfo()
    {
        int i = _r.HoverTile;
        if (i < 0) return new() { ["valid"] = false };
        var g = _r.Map.Grid;
        int year = _r.Year;
        var info = new Godot.Collections.Dictionary
        {
            ["valid"] = true,
            ["tile_x"] = i % g.Width,
            ["tile_y"] = i / g.Width,
            ["elevation_m"] = _r.Map.Heights.BaseElevationM + _r.HoverPoint.Y,
            ["land_state"] = _r.History.StateAt(i, year).ToString(),
            ["land_state_present"] = g.LandState[i].ToString(),
            ["land_cover"] = _r.Map.LandCoverName(i),
            ["ownership"] = g.Ownership[i].ToString(),
            ["protected"] = g.Protected[i],
            ["path"] = _r.History.PathVisible(i, year) ? g.PathType[i].ToString() : "None",
            ["walk"] = g.Types[i].ToString(),
        };
        int b = _r.BuildingUnderHover();
        if (b >= 0)
        {
            var hb = _r.Buildings[b];
            info["building"] = hb.Name;
            info["building_years"] = hb.Entry is null ? "no date (OSM)"
                : $"{(hb.Entry.BuiltYear?.ToString() ?? "?")}–{(hb.Entry.DemolishedYear?.ToString() ?? "")}";
            info["building_confidence"] = hb.Entry?.Confidence ?? "";
            info["building_approximate"] = hb.ApproximateSite;
        }
        return info;
    }
}
