using System;
using System.Diagnostics;
using Godot;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.View;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Spike B (3b) host: loads the real Oxford map (tile data layer + heightmap), builds chunked low-poly terrain with
/// distance LODs (custom mesh generator), drives the build-grid overlay and the tile under the mouse.
/// GDScript UI reads GetStats() / GetHoverInfo().
/// </summary>
[GlobalClass]
public partial class TerrainSpikeHost : Node3D
{
    private RealMap _map = null!;
    private RenderingConfig _render = null!;
    private ShaderMaterial _material = null!;
    private double _loadMs, _buildMs;
    private float _reliefM;
    private int _chunks;
    private long _trianglesPerLod0;
    private bool _grid = true;
    private int _hoverTile = -1;
    private float _hoverY;

    public override void _Ready()
    {
        var source = new GodotDataSource();
        var sw = Stopwatch.StartNew();
        _map = RealMapLoader.Load(source);
        _render = RenderingConfig.Load(source);
        var palette = new Palette(source.ReadText("branding.json"));
        _loadMs = sw.Elapsed.TotalMilliseconds;
        foreach (float h in _map.Heights.Heights) _reliefM = Math.Max(_reliefM, h);

        sw.Restart();
        var colors = TileColors(palette);
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/terrain.gdshader") };
        var t = _render.Terrain;
        _material.SetShaderParameter("tile_size", _map.Grid.TileSizeM);
        _material.SetShaderParameter("grid_color", ToColor(palette.Resolve(t.GridColor), t.GridOpacity));
        _material.SetShaderParameter("grid_fade_start", t.GridFadeStartM);
        _material.SetShaderParameter("grid_fade_end", t.GridFadeEndM);
        _material.SetShaderParameter("hover_color", ToColor(palette.Resolve(t.HoverColor), t.HoverOpacity));
        BuildTerrain(colors);
        _buildMs = sw.Elapsed.TotalMilliseconds;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        GD.Print($"Spike B terrain: {_chunks} chunks × {t.LodSteps.Length} LODs, {_trianglesPerLod0:N0} triangles at full detail, " +
                 $"load {_loadMs:F0} ms, mesh build {_buildMs:F0} ms");
    }

    public override void _Process(double delta)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        var mouse = GetViewport().GetMousePosition();
        var o = camera.ProjectRayOrigin(mouse);
        var d = camera.ProjectRayNormal(mouse);
        _hoverTile = _map.Heights.Raycast(o.X, o.Y, o.Z, d.X, d.Y, d.Z, camera.Far, out float hx, out float hy, out float hz)
            ? _map.TileAt(hx, hz) : -1;
        _hoverY = hy;
        var cell = _hoverTile < 0 ? new Vector2(-1, -1) : new Vector2(_hoverTile % _map.Grid.Width, _hoverTile / _map.Grid.Width);
        _material.SetShaderParameter("hover_tile", cell);
    }

    // ---------------- API for GDScript ----------------

    public Vector2 GetMapSizeMeters() => new(_map.SizeM, _map.SizeM);

    public float GetGroundHeight(float x, float z) => _map.Heights.HeightAt(x, z);

    public bool ToggleGrid()
    {
        _grid = !_grid;
        _material.SetShaderParameter("grid_enabled", _grid);
        return _grid;
    }

    public Godot.Collections.Dictionary GetStats() => new()
    {
        ["chunks"] = _chunks,
        ["lods"] = _render.Terrain.LodSteps.Length,
        ["triangles_full_detail"] = _trianglesPerLod0,
        ["load_ms"] = _loadMs,
        ["build_ms"] = _buildMs,
        ["grid"] = _grid,
        ["primitives_in_frame"] = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
        ["draw_calls"] = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
        ["video_mem_mb"] = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0),
        ["gpu_ms"] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()),
        ["render_cpu_ms"] = RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()),
        ["elevation_min_m"] = _map.Heights.BaseElevationM,
        ["elevation_max_m"] = _map.Heights.BaseElevationM + _reliefM,
    };

    public Godot.Collections.Dictionary GetHoverInfo()
    {
        if (_hoverTile < 0) return new() { ["valid"] = false };
        var g = _map.Grid;
        int i = _hoverTile;
        return new()
        {
            ["valid"] = true,
            ["tile_x"] = i % g.Width,
            ["tile_y"] = i / g.Width,
            ["elevation_m"] = _map.Heights.BaseElevationM + _hoverY,
            ["land_state"] = g.LandState[i].ToString(),
            ["land_cover"] = _map.LandCoverName(i),
            ["ownership"] = g.Ownership[i].ToString(),
            ["protected"] = g.Protected[i],
            ["path"] = g.PathType[i].ToString(),
            ["walk"] = g.Types[i].ToString(),
            ["building"] = _map.HasBuilding[i],
            ["foot_traffic"] = g.FootTraffic[i],
        };
    }

    // ---------------- construction ----------------

    private Rgb[] TileColors(Palette palette)
    {
        var t = _render.Terrain;
        var g = _map.Grid;
        var states = new Rgb[Enum.GetValues<LandState>().Length];
        foreach (var s in Enum.GetValues<LandState>())
            states[(int)s] = palette.Resolve(t.LandStateColors[s.ToString().ToLowerInvariant()]);
        var paths = new Rgb[Enum.GetValues<PathType>().Length];
        foreach (var p in Enum.GetValues<PathType>())
            if (p != PathType.None) paths[(int)p] = palette.Resolve(t.PathColors[p.ToString().ToLowerInvariant()]);
        var building = palette.Resolve(t.Building);

        var colors = new Rgb[g.Width * g.Height];
        for (int i = 0; i < colors.Length; i++)
        {
            var c = _map.HasBuilding[i] ? building
                : g.PathType[i] != PathType.None ? paths[(int)g.PathType[i]]
                : states[(int)g.LandState[i]];
            var lin = new Color(c.R, c.G, c.B).SrgbToLinear(); // vertex colours are fed to the shader as linear
            colors[i] = new Rgb(lin.R, lin.G, lin.B);
        }
        return colors;
    }

    private void BuildTerrain(Rgb[] colors)
    {
        var t = _render.Terrain;
        var g = _map.Grid;
        int size = t.ChunkSizeTiles;
        if (g.Width % size != 0) throw new InvalidOperationException($"chunk size {size} must divide the {g.Width}-tile map");
        Rgb ColorAt(int x, int y) => colors[g.Index(Math.Clamp(x, 0, g.Width - 1), Math.Clamp(y, 0, g.Height - 1))];

        for (int cy = 0; cy < g.Height / size; cy++)
            for (int cx = 0; cx < g.Width / size; cx++)
            {
                var chunk = new Node3D { Name = $"Chunk_{cx}_{cy}" };
                AddChild(chunk);
                for (int lod = 0; lod < t.LodSteps.Length; lod++)
                {
                    var mesh = TerrainMesher.BuildChunk(_map.Heights, g.TileSizeM, cx * size, cy * size, size, t.LodSteps[lod], ColorAt, t.SkirtDepthM);
                    if (lod == 0) _trianglesPerLod0 += mesh.TriangleCount;
                    chunk.AddChild(new MeshInstance3D
                    {
                        Name = $"LOD{lod}",
                        Mesh = ToArrayMesh(mesh),
                        MaterialOverride = _material,
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
