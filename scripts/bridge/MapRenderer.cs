using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.View;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Bridge;

/// <summary>Extra ground colouring on top of the land/season colours (desire paths are always drawn).</summary>
public enum MapOverlay { None, Ownership, FootTraffic }

/// <summary>
/// Draws the real Oxford map (shared by the Spike B scene and the game, Phase 1 1c): custom low-poly terrain chunks
/// with LODs, the per-tile colour texture (land state by year, seasons, roads by era, overlays), extruded building
/// footprints shown by year, and the sun/ambient light for the date and hour. Builds its nodes under a parent;
/// main thread only, except that tile repaints can run on a worker (<see cref="RepaintAsync"/>).
/// </summary>
public sealed class MapRenderer
{
    public RealMap Map { get; }
    public RenderingConfig Render { get; }
    public TimelineData Timeline { get; }
    public EraTable Eras { get; }
    public LandHistory History { get; }
    public IReadOnlyList<HistoricBuilding> Buildings => _buildings;
    public IReadOnlyList<TimelineEntry> Unplaced => _unplaced;

    public int Year { get; private set; }
    public int DayOfYear { get; private set; } = 196;
    public float Hour { get; private set; } = 14f;
    public float SunElevation { get; private set; }
    public float SunAzimuth { get; private set; }
    public bool ShowUndated { get; private set; }
    /// <summary>The game draws the real buildings of its start year only (later ones are the player's to build, 1e);
    /// null = buildings follow <see cref="Year"/> (the Spike B timeline).</summary>
    public int? BuildingsYear { get; private set; }
    private int ShownBuildingsYear => BuildingsYear ?? Year;
    public bool GridVisible { get; private set; } = true;
    public MapOverlay Overlay { get; private set; }

    public int Chunks { get; private set; }
    public long TrianglesPerLod0 { get; private set; }
    public long BuildingTriangles { get; private set; }
    public double LoadMs { get; }
    public double BuildMs { get; }
    public double PaintMs { get; private set; }
    public float ReliefM { get; }

    public int HoverTile { get; private set; } = -1;
    public Vector3 HoverPoint { get; private set; }

    private readonly TileColorizer _colorizer;
    private readonly Palette _palette;
    private readonly List<HistoricBuilding> _buildings;
    private readonly List<TimelineEntry> _unplaced;
    private readonly float[] _bounds; // per building: minX, minY, maxX, maxY (tiles)
    private readonly ShaderMaterial _terrainMaterial;
    private readonly ShaderMaterial _buildingMaterial;
    private readonly Image _tileImage;
    private readonly ImageTexture _tileTexture;
    private readonly DirectionalLight3D? _sun;
    private readonly Godot.Environment? _environment;

    // Overlay data (copies owned by the renderer) and the background repaint.
    private float[] _wear = [];
    private int[] _traffic = [];
    // Live land layer from the sim (game); null = land states from the land-history model (Spike B timeline).
    private LandState[]? _liveStates;
    private TileType[]? _liveSurfaces;
    private Ownership[]? _liveOwners;
    private byte[]? _liveClearing;
    private Task? _paintTask;
    private bool _repaintQueued;

    private int _hoverBuilding = -1, _hoverKeyTile = -2, _hoverKeyYear = -1;

    /// <param name="drawnElsewhere">Buildings drawn by someone else (the game's kit-assembled real buildings); they get
    /// no extruded block.</param>
    public MapRenderer(Node3D parent, IDataSource source, RealMap map, DirectionalLight3D? sun, Godot.Environment? environment,
        Func<HistoricBuilding, bool>? drawnElsewhere = null)
    {
        var sw = Stopwatch.StartNew();
        Map = map;
        Render = RenderingConfig.Load(source);
        Timeline = TimelineData.Load(source);
        Eras = EraTable.Load(source);
        var historyCfg = LandHistoryConfig.Load(source);
        _palette = new Palette(source.ReadText("branding.json"));
        var features = FeatureBuilding.Load(source, map.Meta.FeaturesFile);
        _buildings = HistoricBuilding.Build(features, Timeline, historyCfg.PresentYear, out _unplaced);
        var seeds = _buildings.Where(b => b.Entry?.BuiltYear is not null)
            .Select(b => { var c = b.Footprint.Centroid(); return (c.X, c.Y, b.BuiltYear); });
        History = new LandHistory(map.Grid, historyCfg, seeds);
        _colorizer = new TileColorizer(map, History, Eras, Render.Terrain, _palette);
        Year = historyCfg.PresentYear;
        LoadMs = sw.Elapsed.TotalMilliseconds;
        foreach (float h in map.Heights.Heights) ReliefM = Math.Max(ReliefM, h);

        sw.Restart();
        var g = map.Grid;
        _tileImage = Image.CreateEmpty(g.Width, g.Height, false, Image.Format.Rgba8);
        _tileTexture = ImageTexture.CreateFromImage(_tileImage);
        _terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/terrain.gdshader") };
        var t = Render.Terrain;
        _terrainMaterial.SetShaderParameter("tile_colors", _tileTexture);
        _terrainMaterial.SetShaderParameter("map_size", map.SizeM);
        _terrainMaterial.SetShaderParameter("tile_size", g.TileSizeM);
        _terrainMaterial.SetShaderParameter("grid_color", ToColor(_palette.Resolve(t.GridColor), t.GridOpacity));
        _terrainMaterial.SetShaderParameter("grid_fade_start", t.GridFadeStartM);
        _terrainMaterial.SetShaderParameter("grid_fade_end", t.GridFadeEndM);
        _terrainMaterial.SetShaderParameter("hover_color", ToColor(_palette.Resolve(t.HoverColor), t.HoverOpacity));
        BuildTerrain(parent);

        _buildingMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/buildings.gdshader") };
        var extruded = drawnElsewhere is null ? _buildings : _buildings.Where(b => !drawnElsewhere(b)).ToList();
        foreach (var mi in BuildingMeshBuilder.Build(extruded, map, Render.Extrusion, _palette, t.ChunkSizeTiles, _buildingMaterial))
        {
            parent.AddChild(mi);
            BuildingTriangles += mi.Mesh.GetFaces().Length / 3;
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
        BuildMs = sw.Elapsed.TotalMilliseconds;

        _sun = sun;
        _environment = environment;
        Repaint();
        ApplyBuildings();
        UpdateSun();
    }

    public float GroundHeight(float x, float z) => Map.Heights.HeightAt(x, z);

    // ---------------- state ----------------

    /// <summary>Sets year and day together; repaints (in the background when <paramref name="async"/>) if either changed.</summary>
    public void SetDate(int year, int dayOfYear, bool async = false)
    {
        year = Math.Clamp(year, History.StartYear, History.PresentYear);
        dayOfYear = Math.Clamp(dayOfYear, 1, 365);
        bool yearChanged = year != Year, dayChanged = dayOfYear != DayOfYear;
        if (!yearChanged && !dayChanged) return;
        Year = year;
        DayOfYear = dayOfYear;
        if (async) RepaintAsync(); else Repaint();
        if (yearChanged) ApplyBuildings();
        if (dayChanged) UpdateSun();
    }

    public void SetHour(float hour)
    {
        Hour = ((hour % 24f) + 24f) % 24f;
        UpdateSun();
    }

    /// <summary>Freezes the drawn real buildings at a year (null = follow the date).</summary>
    public void SetBuildingsYear(int? year)
    {
        BuildingsYear = year;
        ApplyBuildings();
    }

    public void SetShowUndated(bool show)
    {
        ShowUndated = show;
        ApplyBuildings();
    }

    public bool ToggleGrid()
    {
        GridVisible = !GridVisible;
        _terrainMaterial.SetShaderParameter("grid_enabled", GridVisible);
        return GridVisible;
    }

    /// <summary>Paint from the sim's live land layer from now on (copies the arrays). Repaints in the background.</summary>
    public void SetLiveLand(ReadOnlySpan<LandState> states, ReadOnlySpan<TileType> surfaces, ReadOnlySpan<Ownership> owners, ReadOnlySpan<byte> clearing)
    {
        _liveStates = states.ToArray();
        _liveSurfaces = surfaces.ToArray();
        _liveOwners = owners.ToArray();
        _liveClearing = clearing.ToArray();
        RepaintAsync();
    }

    /// <summary>Changes the overlay mode without new data. Repaints in the background.</summary>
    public void SetOverlayMode(MapOverlay overlay)
    {
        if (overlay == Overlay) return;
        Overlay = overlay;
        RepaintAsync();
    }

    /// <summary>Land-tool selection (tile rectangle, inclusive) or null; green when the order is possible, red when not.</summary>
    public void SetSelection(Rect2I? tiles, bool ok)
    {
        var r = tiles is { } t ? new Vector4(t.Position.X, t.Position.Y, t.End.X - 1, t.End.Y - 1) : new Vector4(-1, -1, -1, -1);
        _terrainMaterial.SetShaderParameter("selection_rect", r);
        _terrainMaterial.SetShaderParameter("selection_color", ok ? new Color(0.35f, 0.95f, 0.45f, 0.45f) : new Color(1f, 0.3f, 0.25f, 0.45f));
    }

    /// <summary>Overlay mode plus the data it needs (copied). Repaints in the background.</summary>
    public void SetOverlay(MapOverlay overlay, ReadOnlySpan<float> wear, ReadOnlySpan<int> traffic)
    {
        Overlay = overlay;
        // Fresh arrays, so a repaint already running on a worker keeps reading the ones it started with.
        _wear = wear.ToArray();
        _traffic = traffic.ToArray();
        RepaintAsync();
    }

    // ---------------- per frame ----------------

    /// <summary>Call every frame: finishes background repaints and updates the hovered tile.</summary>
    public void Process(Camera3D? camera, Vector2 mouse)
    {
        if (_paintTask is { IsCompleted: true })
        {
            _paintTask = null;
            Upload();
            if (_repaintQueued) { _repaintQueued = false; RepaintAsync(); }
        }
        if (camera is null) return;
        var o = camera.ProjectRayOrigin(mouse);
        var d = camera.ProjectRayNormal(mouse);
        HoverTile = Map.Heights.Raycast(o.X, o.Y, o.Z, d.X, d.Y, d.Z, camera.Far, out float hx, out float hy, out float hz)
            ? Map.TileAt(hx, hz) : -1;
        HoverPoint = new Vector3(hx, hy, hz);
        var cell = HoverTile < 0 ? new Vector2(-1, -1) : new Vector2(HoverTile % Map.Grid.Width, HoverTile / Map.Grid.Width);
        _terrainMaterial.SetShaderParameter("hover_tile", cell);
    }

    /// <summary>Index into <see cref="Buildings"/> of the footprint under the cursor, or -1.</summary>
    public int BuildingUnderHover()
    {
        if (HoverTile < 0) return -1;
        if (HoverTile == _hoverKeyTile && ShownBuildingsYear == _hoverKeyYear) return _hoverBuilding;
        _hoverKeyTile = HoverTile;
        _hoverKeyYear = ShownBuildingsYear;
        _hoverBuilding = -1;
        float tx = HoverPoint.X / Map.Grid.TileSizeM, ty = HoverPoint.Z / Map.Grid.TileSizeM;
        for (int i = 0; i < _buildings.Count; i++)
        {
            if (tx < _bounds[i * 4] || ty < _bounds[i * 4 + 1] || tx > _bounds[i * 4 + 2] || ty > _bounds[i * 4 + 3]) continue;
            if (!_buildings[i].StandsIn(ShownBuildingsYear, ShowUndated) || !_buildings[i].Footprint.Contains(tx, ty)) continue;
            _hoverBuilding = i;
            break;
        }
        return _hoverBuilding;
    }

    // ---------------- painting ----------------

    public void Repaint()
    {
        _paintTask?.Wait();
        _paintTask = null;
        PaintPixels();
        Upload();
    }

    /// <summary>Repaints the tile colours on a worker thread; <see cref="Process"/> uploads them when done.</summary>
    public void RepaintAsync()
    {
        if (_paintTask is not null) { _repaintQueued = true; return; }
        _paintTask = Task.Run(PaintPixels);
    }

    private void PaintPixels()
    {
        var sw = Stopwatch.StartNew();
        var states = _liveStates; var surfaces = _liveSurfaces; var owners = _liveOwners; var clearing = _liveClearing;
        if (states is not null && surfaces is not null) _colorizer.PaintLive(states, surfaces, Year, DayOfYear);
        else _colorizer.Paint(Year, DayOfYear);
        var g = Render.Ground;
        if (Overlay == MapOverlay.FootTraffic && _traffic.Length > 0)
            _colorizer.ApplyTraffic(_traffic, _palette.Resolve(g.HeatmapLow), _palette.Resolve(g.HeatmapHigh));
        else
        {
            if (_wear.Length > 0) _colorizer.ApplyWear(_wear, _palette.Resolve(g.Wear));
            if (owners is not null)
                _colorizer.ApplyOwnership(owners, clearing ?? [], _palette.Resolve("primary"), _palette.Resolve("slate"),
                    new Rgb(0.93f, 0.62f, 0.18f), Overlay == MapOverlay.Ownership);
        }
        PaintMs = sw.Elapsed.TotalMilliseconds;
    }

    private void Upload()
    {
        _tileImage.SetData(Map.Grid.Width, Map.Grid.Height, false, Image.Format.Rgba8, _colorizer.Pixels);
        _tileTexture.Update(_tileImage);
    }

    private void ApplyBuildings()
    {
        _buildingMaterial.SetShaderParameter("current_year", (float)ShownBuildingsYear);
        _buildingMaterial.SetShaderParameter("show_undated_always", ShowUndated);
    }

    private void UpdateSun()
    {
        var s = Render.Sun;
        var (el, az) = SolarPosition.Compute(Map.Meta.Center.Lat, Map.Meta.Center.Lon, DayOfYear, Hour, s.UtcOffsetHours);
        SunElevation = (float)el;
        SunAzimuth = (float)az;
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

    // ---------------- construction ----------------

    private void BuildTerrain(Node3D parent)
    {
        var t = Render.Terrain;
        var g = Map.Grid;
        int size = t.ChunkSizeTiles;
        if (g.Width % size != 0) throw new InvalidOperationException($"chunk size {size} must divide the {g.Width}-tile map");
        var white = new Rgb(1, 1, 1); // colours come from the tile texture
        for (int cy = 0; cy < g.Height / size; cy++)
            for (int cx = 0; cx < g.Width / size; cx++)
            {
                var chunk = new Node3D { Name = $"Chunk_{cx}_{cy}" };
                parent.AddChild(chunk);
                for (int lod = 0; lod < t.LodSteps.Length; lod++)
                {
                    var mesh = TerrainMesher.BuildChunk(Map.Heights, g.TileSizeM, cx * size, cy * size, size, t.LodSteps[lod], (_, _) => white, t.SkirtDepthM);
                    if (lod == 0) TrianglesPerLod0 += mesh.TriangleCount;
                    chunk.AddChild(new MeshInstance3D
                    {
                        Name = $"LOD{lod}",
                        Mesh = ToArrayMesh(mesh),
                        MaterialOverride = _terrainMaterial,
                        VisibilityRangeBegin = lod == 0 ? 0 : t.LodSwitchM[lod - 1],
                        VisibilityRangeEnd = lod < t.LodSwitchM.Length ? t.LodSwitchM[lod] : 0,
                    });
                }
                Chunks++;
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
