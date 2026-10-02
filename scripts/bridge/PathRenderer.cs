using System;
using System.Collections.Generic;
using Godot;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.View;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Draws paths and roads as meshes (Phase 1 1g, <see cref="PathMeshBuilder"/>): one mesh per terrain chunk, rebuilt
/// only for chunks whose paths changed (or all of them when the era's road surface changes), plus a translucent preview
/// of a path route while the player drags one. Colours: player paths by their surface (paths.json), other paths and
/// roads as the terrain painted them before (road surface by era, path classes in the modern era). Main thread only.
/// </summary>
public sealed class PathRenderer
{
    private readonly RealMap _map;
    private readonly RenderingConfig _render;
    private readonly PathConfig _paths;
    private readonly EraTable _eras;
    private readonly int _chunk;
    private readonly MeshInstance3D[] _meshes;
    private readonly ulong[] _signatures;
    private readonly MeshInstance3D _preview;
    private readonly StandardMaterial3D _material;
    private readonly Rgb[] _surfaceColors;
    private readonly Dictionary<string, Rgb> _roadColors = [];
    private readonly Rgb[] _classColors;
    private readonly float[] _classWidths;
    private string _roadType = "";

    public long Triangles { get; private set; }
    public double LastUpdateMs { get; private set; }

    public PathRenderer(Node3D parent, RealMap map, RenderingConfig render, Palette palette, PathConfig paths, EraTable eras)
    {
        _map = map;
        _render = render;
        _paths = paths;
        _eras = eras;
        _chunk = render.Terrain.ChunkSizeTiles;
        int chunks = (map.Grid.Width / _chunk) * (map.Grid.Height / _chunk);
        _meshes = new MeshInstance3D[chunks];
        _signatures = new ulong[chunks];
        _material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.95f };
        var root = new Node3D { Name = "Paths" };
        parent.AddChild(root);
        for (int i = 0; i < chunks; i++)
        {
            int cx = i % (map.Grid.Width / _chunk), cy = i / (map.Grid.Width / _chunk);
            float half = _chunk * map.Grid.TileSizeM / 2;
            _meshes[i] = new MeshInstance3D
            {
                Name = $"Paths_{i}", MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                // At the chunk's centre, so the visibility range is measured from there; paths vanish when sub-pixel.
                Position = new Vector3(cx * _chunk * map.Grid.TileSizeM + half, 0, cy * _chunk * map.Grid.TileSizeM + half),
                VisibilityRangeEnd = render.PathMeshes.VisibleToM, VisibilityRangeEndMargin = 100,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
            };
            root.AddChild(_meshes[i]);
            _signatures[i] = ulong.MaxValue;
        }
        _preview = new MeshInstance3D
        {
            Name = "PathPreview",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        parent.AddChild(_preview);

        _surfaceColors = new Rgb[paths.Surfaces.Length];
        for (int i = 0; i < paths.Surfaces.Length; i++) _surfaceColors[i] = palette.Resolve(paths.Surfaces[i].Color);
        foreach (var (k, v) in render.Terrain.RoadSurfaceColors) _roadColors[k] = palette.Resolve(v);
        var classes = Enum.GetValues<PathType>();
        _classColors = new Rgb[classes.Length];
        _classWidths = new float[classes.Length];
        foreach (var c in classes)
        {
            if (c == PathType.None) continue;
            string key = c.ToString().ToLowerInvariant();
            _classColors[(int)c] = palette.Resolve(render.Terrain.PathColors[key]);
            _classWidths[(int)c] = render.PathMeshes.WidthsM.GetValueOrDefault(key, 3f);
        }
    }

    /// <summary>Rebuilds the chunks whose paths changed in this snapshot's land layer (call when LandVersion changes).</summary>
    public void Update(SimSnapshot s) => Update(s.Date, s.Surfaces, s.PathTypes, s.PathSurfaces);

    /// <summary>Rebuilds the chunks whose paths changed, from map layers (live, or reconstructed for the time-lapse).</summary>
    public void Update(DateOnly date, TileType[] surfaces, PathType[] types, byte[] player)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var g = _map.Grid;
        string roadType = _eras.At(date.Year).RoadType;
        bool all = roadType != _roadType;
        _roadType = roadType;
        bool modern = roadType == "asphalt";
        var eraRoad = _roadColors.GetValueOrDefault(roadType, _roadColors["dirt"]);
        var footway = roadType is "brick" or "asphalt" ? _classColors[(int)PathType.Footway] : _roadColors["dirt"];

        bool IsPath(int t) => surfaces[t] == TileType.Path;
        float Width(int t) => player[t] > 0 ? _paths.Surfaces[player[t] - 1].WidthM
            : types[t] == PathType.None ? _classWidths[(int)PathType.Footway] : _classWidths[(int)types[t]];
        Rgb Color(int t) => player[t] > 0 ? _surfaceColors[player[t] - 1]
            : types[t] switch
            {
                PathType.Railway => _classColors[(int)PathType.Railway],
                PathType.Footway or PathType.None => footway,
                _ => modern ? _classColors[(int)types[t]] : eraRoad,
            };

        int chunksX = g.Width / _chunk;
        long tris = 0;
        for (int c = 0; c < _meshes.Length; c++)
        {
            int tx0 = c % chunksX * _chunk, ty0 = c / chunksX * _chunk;
            ulong sig = 14695981039346656037UL;
            // Signature over the chunk plus a one-tile border: strips reach into neighbouring tiles.
            for (int y = Math.Max(0, ty0 - 1); y < Math.Min(g.Height, ty0 + _chunk + 1); y++)
                for (int x = Math.Max(0, tx0 - 1); x < Math.Min(g.Width, tx0 + _chunk + 1); x++)
                {
                    int t = y * g.Width + x;
                    if (!IsPath(t)) continue;
                    sig = (sig ^ (ulong)t) * 1099511628211UL;
                    sig = (sig ^ ((ulong)types[t] << 8 | player[t])) * 1099511628211UL;
                }
            if (!all && sig == _signatures[c]) { tris += (_meshes[c].Mesh?.GetFaces().Length ?? 0) / 3; continue; }
            _signatures[c] = sig;
            // One tile beyond the chunk is read for connections; strips are owned by the tile they start from.
            var origin = _meshes[c].Position;
            var m = PathMeshBuilder.BuildChunk(_map.Heights, g.TileSizeM, g.Width, g.Height, tx0, ty0, _chunk, IsPath, Width, Color,
                _render.PathMeshes.LiftM, _render.PathMeshes.Segments, origin.X, origin.Z);
            _meshes[c].Mesh = ToMesh(m);
            tris += m.TriangleCount;
        }
        Triangles = tris;
        LastUpdateMs = sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>Shows a planned route (green = can be laid, red = not), or hides it.</summary>
    public void SetPreview(IReadOnlyList<int>? tiles, bool ok)
    {
        if (tiles is null || tiles.Count == 0) { _preview.Visible = false; return; }
        var g = _map.Grid;
        var set = new HashSet<int>(tiles);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
        foreach (int t in tiles) { minX = Math.Min(minX, t % g.Width); maxX = Math.Max(maxX, t % g.Width); minY = Math.Min(minY, t / g.Width); maxY = Math.Max(maxY, t / g.Width); }
        var color = ok ? new Rgb(0.36f, 0.88f, 0.48f) : new Rgb(1f, 0.33f, 0.27f);
        var m = PathMeshBuilder.BuildChunk(_map.Heights, g.TileSizeM, g.Width, g.Height, minX, minY, Math.Max(maxX - minX, maxY - minY) + 1,
            set.Contains, _ => 4f, _ => color, _render.PathMeshes.LiftM + 0.1f, 1);
        _preview.Mesh = ToMesh(m, alpha: 0.7f);
        _preview.Visible = true;
    }

    private static ArrayMesh? ToMesh(TerrainChunkMesh m, float alpha = 1f)
    {
        int n = m.VertexCount;
        if (n == 0) return null;
        var verts = new Vector3[n];
        var normals = new Vector3[n];
        var cols = new Color[n];
        for (int i = 0; i < n; i++)
        {
            verts[i] = new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
            normals[i] = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
            cols[i] = new Color(m.Colors[i * 4], m.Colors[i * 4 + 1], m.Colors[i * 4 + 2], alpha);
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
}
