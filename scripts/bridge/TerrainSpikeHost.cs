using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.View;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Spike B host: real Oxford terrain (custom chunked low-poly mesh, 3b) plus the evolving map (3c): a 1809 → 2026
/// timeline that repaints land states and roads (tile-colour texture) and shows/hides extruded building footprints
/// (by year, in the shader), seasonal colours, and a sun positioned for Oxford's latitude, day and hour.
/// GDScript UI talks to it through the public methods below.
/// </summary>
[GlobalClass]
public partial class TerrainSpikeHost : Node3D
{
    [Export] public NodePath SunPath { get; set; } = new();
    [Export] public NodePath EnvironmentPath { get; set; } = new();

    private RealMap _map = null!;
    private RenderingConfig _render = null!;
    private TimelineData _timeline = null!;
    private EraTable _eras = null!;
    private LandHistory _history = null!;
    private TileColorizer _colorizer = null!;
    private List<HistoricBuilding> _buildings = [];
    private List<TimelineEntry> _unplaced = [];
    private float[] _bounds = []; // per building: minX, minY, maxX, maxY (tiles)

    private ShaderMaterial _terrainMaterial = null!;
    private ShaderMaterial _buildingMaterial = null!;
    private Image _tileImage = null!;
    private ImageTexture _tileTexture = null!;
    private DirectionalLight3D? _sun;
    private Godot.Environment? _environment;

    private double _loadMs, _buildMs, _paintMs;
    private float _reliefM;
    private int _chunks;
    private long _trianglesPerLod0, _buildingTriangles;
    private bool _grid = true, _showUndated;
    private int _year, _day = 196;
    private float _hour = 14f, _sunElevation, _sunAzimuth;
    private int _hoverTile = -1;
    private float _hoverY, _hoverX, _hoverZ;
    private int _hoverBuilding = -1, _hoverKeyTile = -2, _hoverKeyYear = -1;

    public override void _Ready()
    {
        var source = new GodotDataSource();
        var sw = Stopwatch.StartNew();
        _map = RealMapLoader.Load(source);
        _render = RenderingConfig.Load(source);
        _timeline = TimelineData.Load(source);
        _eras = EraTable.Load(source);
        var historyCfg = LandHistoryConfig.Load(source);
        var palette = new Palette(source.ReadText("branding.json"));
        var features = FeatureBuilding.Load(source, _map.Meta.FeaturesFile);
        _buildings = HistoricBuilding.Build(features, _timeline, historyCfg.PresentYear, out _unplaced);
        var seeds = _buildings.Where(b => b.Entry?.BuiltYear is not null)
            .Select(b => { var c = b.Footprint.Centroid(); return (c.X, c.Y, b.BuiltYear); });
        _history = new LandHistory(_map.Grid, historyCfg, seeds);
        _colorizer = new TileColorizer(_map, _history, _eras, _render.Terrain, palette);
        _year = historyCfg.PresentYear;
        _loadMs = sw.Elapsed.TotalMilliseconds;
        foreach (float h in _map.Heights.Heights) _reliefM = Math.Max(_reliefM, h);

        sw.Restart();
        var g = _map.Grid;
        _tileImage = Image.CreateEmpty(g.Width, g.Height, false, Image.Format.Rgba8);
        _tileTexture = ImageTexture.CreateFromImage(_tileImage);
        _terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/terrain.gdshader") };
        var t = _render.Terrain;
        _terrainMaterial.SetShaderParameter("tile_colors", _tileTexture);
        _terrainMaterial.SetShaderParameter("map_size", _map.SizeM);
        _terrainMaterial.SetShaderParameter("tile_size", g.TileSizeM);
        _terrainMaterial.SetShaderParameter("grid_color", ToColor(palette.Resolve(t.GridColor), t.GridOpacity));
        _terrainMaterial.SetShaderParameter("grid_fade_start", t.GridFadeStartM);
        _terrainMaterial.SetShaderParameter("grid_fade_end", t.GridFadeEndM);
        _terrainMaterial.SetShaderParameter("hover_color", ToColor(palette.Resolve(t.HoverColor), t.HoverOpacity));
        BuildTerrain();

        _buildingMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/buildings.gdshader") };
        foreach (var mi in BuildingMeshBuilder.Build(_buildings, _map, _render.Extrusion, palette, t.ChunkSizeTiles, _buildingMaterial))
        {
            AddChild(mi);
            _buildingTriangles += mi.Mesh.GetFaces().Length / 3;
        }
        _bounds = new float[_buildings.Count * 4];
        for (int i = 0; i < _buildings.Count; i++)
        {
            var o = _buildings[i].Footprint.Outline;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int k = 0; k < o.Length; k += 2)
            {
                x0 = Math.Min(x0, o[k]); x1 = Math.Max(x1, o[k]);
                y0 = Math.Min(y0, o[k + 1]); y1 = Math.Max(y1, o[k + 1]);
            }
            _bounds[i * 4] = x0; _bounds[i * 4 + 1] = y0; _bounds[i * 4 + 2] = x1; _bounds[i * 4 + 3] = y1;
        }
        _buildMs = sw.Elapsed.TotalMilliseconds;

        _sun = GetNodeOrNull<DirectionalLight3D>(SunPath);
        _environment = GetNodeOrNull<WorldEnvironment>(EnvironmentPath)?.Environment;
        Repaint();
        ApplyBuildings();
        UpdateSun();
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        GD.Print($"Spike B: {_chunks} terrain chunks × {t.LodSteps.Length} LODs ({_trianglesPerLod0:N0} tris), " +
                 $"{_buildings.Count:N0} building footprints ({_buildingTriangles:N0} tris), {_timeline.Entries.Length} timeline entries " +
                 $"({_unplaced.Count} without a footprint), load {_loadMs:F0} ms, build {_buildMs:F0} ms");
    }

    public override void _Process(double delta)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        var mouse = GetViewport().GetMousePosition();
        var o = camera.ProjectRayOrigin(mouse);
        var d = camera.ProjectRayNormal(mouse);
        _hoverTile = _map.Heights.Raycast(o.X, o.Y, o.Z, d.X, d.Y, d.Z, camera.Far, out _hoverX, out _hoverY, out _hoverZ)
            ? _map.TileAt(_hoverX, _hoverZ) : -1;
        var cell = _hoverTile < 0 ? new Vector2(-1, -1) : new Vector2(_hoverTile % _map.Grid.Width, _hoverTile / _map.Grid.Width);
        _terrainMaterial.SetShaderParameter("hover_tile", cell);
    }

    // ---------------- API for GDScript ----------------

    public Vector2 GetMapSizeMeters() => new(_map.SizeM, _map.SizeM);

    public float GetGroundHeight(float x, float z) => _map.Heights.HeightAt(x, z);

    public Vector2I GetYearRange() => new(_history.StartYear, _history.PresentYear);

    public int GetYear() => _year;

    public void SetYear(int year)
    {
        year = Math.Clamp(year, _history.StartYear, _history.PresentYear);
        if (year == _year) return;
        _year = year;
        Repaint();
        ApplyBuildings();
    }

    public int GetDayOfYear() => _day;

    public void SetDayOfYear(int day)
    {
        day = Math.Clamp(day, 1, 365);
        if (day == _day) return;
        _day = day;
        Repaint();
        UpdateSun();
    }

    public float GetHour() => _hour;

    public void SetHour(float hour)
    {
        _hour = ((hour % 24f) + 24f) % 24f;
        UpdateSun();
    }

    public void SetShowUndated(bool show)
    {
        _showUndated = show;
        ApplyBuildings();
    }

    public bool ToggleGrid()
    {
        _grid = !_grid;
        _terrainMaterial.SetShaderParameter("grid_enabled", _grid);
        return _grid;
    }

    public Godot.Collections.Dictionary GetStats() => new()
    {
        ["chunks"] = _chunks,
        ["lods"] = _render.Terrain.LodSteps.Length,
        ["triangles_full_detail"] = _trianglesPerLod0,
        ["building_footprints"] = _buildings.Count,
        ["building_triangles"] = _buildingTriangles,
        ["load_ms"] = _loadMs,
        ["build_ms"] = _buildMs,
        ["paint_ms"] = _paintMs,
        ["grid"] = _grid,
        ["primitives_in_frame"] = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
        ["draw_calls"] = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
        ["video_mem_mb"] = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0),
        ["gpu_ms"] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()),
        ["render_cpu_ms"] = RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()),
        ["elevation_min_m"] = _map.Heights.BaseElevationM,
        ["elevation_max_m"] = _map.Heights.BaseElevationM + _reliefM,
    };

    public Godot.Collections.Dictionary GetYearInfo()
    {
        var events = new Godot.Collections.Array<string>();
        foreach (var e in _timeline.Entries)
        {
            if (e.BuiltYear == _year) events.Add($"+ {e.Name} built ({e.Confidence})");
            if (e.DemolishedYear == _year)
                events.Add($"− {e.Name} {e.Status switch { "incorporated" => "absorbed into another building", "moved" => "moved away", _ => "demolished" }} ({e.Confidence})");
            foreach (var ev in e.Events)
                if (ev.Year == _year) events.Add($"• {e.Name}: {ev.Event}");
        }
        var era = _eras.At(_year);
        return new()
        {
            ["year"] = _year,
            ["era"] = era.Name,
            ["road_type"] = era.RoadType,
            ["day"] = _day,
            ["hour"] = _hour,
            ["sun_elevation"] = _sunElevation,
            ["sun_azimuth"] = _sunAzimuth,
            ["standing_timeline"] = _timeline.Entries.Count(e => e.StandsIn(_year, _history.PresentYear)),
            ["events"] = events,
            ["unplaced"] = _unplaced.Count,
            ["show_undated"] = _showUndated,
        };
    }

    public Godot.Collections.Dictionary GetHoverInfo()
    {
        if (_hoverTile < 0) return new() { ["valid"] = false };
        var g = _map.Grid;
        int i = _hoverTile;
        var info = new Godot.Collections.Dictionary
        {
            ["valid"] = true,
            ["tile_x"] = i % g.Width,
            ["tile_y"] = i / g.Width,
            ["elevation_m"] = _map.Heights.BaseElevationM + _hoverY,
            ["land_state"] = _history.StateAt(i, _year).ToString(),
            ["land_state_present"] = g.LandState[i].ToString(),
            ["land_cover"] = _map.LandCoverName(i),
            ["ownership"] = g.Ownership[i].ToString(),
            ["protected"] = g.Protected[i],
            ["path"] = _history.PathVisible(i, _year) ? g.PathType[i].ToString() : "None",
            ["walk"] = g.Types[i].ToString(),
        };
        int b = BuildingUnderHover();
        if (b >= 0)
        {
            var hb = _buildings[b];
            info["building"] = hb.Name;
            info["building_years"] = hb.Entry is null ? "no date (OSM)"
                : $"{(hb.Entry.BuiltYear?.ToString() ?? "?")}–{(hb.Entry.DemolishedYear?.ToString() ?? "")}";
            info["building_confidence"] = hb.Entry?.Confidence ?? "";
            info["building_approximate"] = hb.ApproximateSite;
        }
        return info;
    }

    // ---------------- updates ----------------

    private void Repaint()
    {
        var sw = Stopwatch.StartNew();
        _colorizer.Paint(_year, _day);
        _tileImage.SetData(_map.Grid.Width, _map.Grid.Height, false, Image.Format.Rgba8, _colorizer.Pixels);
        _tileTexture.Update(_tileImage);
        _paintMs = sw.Elapsed.TotalMilliseconds;
    }

    private void ApplyBuildings()
    {
        _buildingMaterial.SetShaderParameter("current_year", (float)_year);
        _buildingMaterial.SetShaderParameter("show_undated_always", _showUndated);
    }

    private void UpdateSun()
    {
        var s = _render.Sun;
        var (el, az) = SolarPosition.Compute(_map.Meta.Center.Lat, _map.Meta.Center.Lon, _day, _hour, s.UtcOffsetHours);
        _sunElevation = (float)el;
        _sunAzimuth = (float)az;
        if (_sun is null) return;
        var (dx, dy, dz) = SolarPosition.Direction(el, az);
        var toSun = new Vector3(dx, dy, dz);
        var up = Math.Abs(toSun.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
        _sun.LookAt(_sun.GlobalPosition - toSun, up); // the light shines along its −Z axis, away from the sun
        float daylight = Mathf.SmoothStep(-s.TwilightElevationDeg, s.TwilightElevationDeg, (float)el);
        _sun.LightEnergy = s.MaxEnergy * daylight;
        _sun.Visible = daylight > 0.001f;
        if (_environment is not null)
        {
            float ambient = Mathf.Lerp(s.NightAmbientEnergy, s.DayAmbientEnergy, daylight);
            _environment.AmbientLightEnergy = ambient;
            _environment.BackgroundEnergyMultiplier = ambient;
        }
    }

    private int BuildingUnderHover()
    {
        if (_hoverTile == _hoverKeyTile && _year == _hoverKeyYear) return _hoverBuilding;
        _hoverKeyTile = _hoverTile;
        _hoverKeyYear = _year;
        _hoverBuilding = -1;
        float tx = _hoverX / _map.Grid.TileSizeM, ty = _hoverZ / _map.Grid.TileSizeM;
        for (int i = 0; i < _buildings.Count; i++)
        {
            if (tx < _bounds[i * 4] || ty < _bounds[i * 4 + 1] || tx > _bounds[i * 4 + 2] || ty > _bounds[i * 4 + 3]) continue;
            if (!_buildings[i].StandsIn(_year, _showUndated) || !_buildings[i].Footprint.Contains(tx, ty)) continue;
            _hoverBuilding = i;
            break;
        }
        return _hoverBuilding;
    }

    // ---------------- construction ----------------

    private void BuildTerrain()
    {
        var t = _render.Terrain;
        var g = _map.Grid;
        int size = t.ChunkSizeTiles;
        if (g.Width % size != 0) throw new InvalidOperationException($"chunk size {size} must divide the {g.Width}-tile map");
        var white = new Rgb(1, 1, 1); // colours come from the tile texture
        for (int cy = 0; cy < g.Height / size; cy++)
            for (int cx = 0; cx < g.Width / size; cx++)
            {
                var chunk = new Node3D { Name = $"Chunk_{cx}_{cy}" };
                AddChild(chunk);
                for (int lod = 0; lod < t.LodSteps.Length; lod++)
                {
                    var mesh = TerrainMesher.BuildChunk(_map.Heights, g.TileSizeM, cx * size, cy * size, size, t.LodSteps[lod], (_, _) => white, t.SkirtDepthM);
                    if (lod == 0) _trianglesPerLod0 += mesh.TriangleCount;
                    chunk.AddChild(new MeshInstance3D
                    {
                        Name = $"LOD{lod}",
                        Mesh = ToArrayMesh(mesh),
                        MaterialOverride = _terrainMaterial,
                        VisibilityRangeBegin = lod == 0 ? 0 : t.LodSwitchM[lod - 1],
                        VisibilityRangeEnd = lod < t.LodSwitchM.Length ? t.LodSwitchM[lod] : 0,
                    });
                }
                _chunks++;
            }
    }

    private static ArrayMesh ToArrayMesh(TerrainChunkMesh m)
    {
        int n = m.VertexCount;
        var verts = new Vector3[n];
        var normals = new Vector3[n];
        var cols = new Color[n];
        for (int i = 0; i < n; i++)
        {
            verts[i] = new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
            normals[i] = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
            cols[i] = new Color(m.Colors[i * 4], m.Colors[i * 4 + 1], m.Colors[i * 4 + 2], m.Colors[i * 4 + 3]);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = cols;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static Color ToColor(Rgb c, float alpha) => new(c.R, c.G, c.B, alpha);
}
