using System;
using System.Collections.Generic;
using Godot;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Extrudes building footprints into simple flat-roofed prisms and merges them per terrain chunk. Each vertex carries
/// the building's lifetime (UV2 = built, demolished) and an "undated" flag (COLOR.a) for buildings.gdshader.
/// </summary>
public static class BuildingMeshBuilder
{
    private const float NeverDemolished = 1.0e6f;

    private sealed class Surface
    {
        public readonly List<Vector3> Verts = [];
        public readonly List<Vector3> Normals = [];
        public readonly List<Color> Colors = [];
        public readonly List<Vector2> Life = [];

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Color color, Vector2 life)
        {
            var n = (c - a).Cross(b - a); // front-face normal for Godot's clockwise winding
            if (n.Dot(facing) < 0) { (b, c) = (c, b); n = -n; }
            n = n.Normalized();
            foreach (var v in new[] { a, b, c })
            {
                Verts.Add(v); Normals.Add(n); Colors.Add(color); Life.Add(life);
            }
        }
    }

    public static List<MeshInstance3D> Build(IReadOnlyList<HistoricBuilding> buildings, RealMap map, RenderingConfig.ExtrusionSection cfg,
        Palette palette, int chunkTiles, Material material)
    {
        var grid = map.Grid;
        float tile = grid.TileSizeM;
        int chunksX = grid.Width / chunkTiles;
        var surfaces = new Dictionary<int, Surface>();

        foreach (var b in buildings)
        {
            var f = b.Footprint;
            int n = f.Outline.Length / 2;
            if (n < 3) continue;
            var (ctx, cty) = f.Centroid();
            int chunk = Math.Clamp((int)(cty / chunkTiles), 0, chunksX - 1) * chunksX + Math.Clamp((int)(ctx / chunkTiles), 0, chunksX - 1);
            if (!surfaces.TryGetValue(chunk, out var s)) surfaces[chunk] = s = new Surface();

            bool campus = grid.InBounds((int)ctx, (int)cty) && grid.Ownership[grid.Index((int)ctx, (int)cty)] == Ownership.University;
            float height = f.HeightM ?? (f.Levels is { } lv ? lv * cfg.LevelHeightM
                : b.Entry is not null ? cfg.KindHeightM.GetValueOrDefault(b.Entry.Kind, cfg.KindHeightM["other"])
                : campus ? cfg.UndatedCampusHeightM : cfg.TownHeightM);
            height = Math.Max(height, cfg.MinHeightM);
            var rgb = palette.Resolve(b.ApproximateSite ? cfg.ApproximateSiteColor
                : b.Entry is not null ? cfg.KindColors.GetValueOrDefault(b.Entry.Kind, cfg.KindColors["other"])
                : campus ? cfg.UndatedCampusColor : cfg.TownColor);
            var color = new Color(rgb.R, rgb.G, rgb.B).SrgbToLinear();
            color.A = b.Undated ? 1f : 0f;
            var life = new Vector2(b.BuiltYear, b.DemolishedYear ?? NeverDemolished);

            // Footprint in world metres; ground under each corner.
            var pts = new Vector2[n];
            float minGround = float.MaxValue, maxGround = float.MinValue, area2 = 0;
            for (int i = 0; i < n; i++)
            {
                pts[i] = new Vector2(f.Outline[2 * i] * tile, f.Outline[2 * i + 1] * tile);
                float g = map.Heights.HeightAt(pts[i].X, pts[i].Y);
                minGround = Math.Min(minGround, g);
                maxGround = Math.Max(maxGround, g);
            }
            for (int i = 0; i < n; i++)
            {
                var p = pts[i]; var q = pts[(i + 1) % n];
                area2 += p.X * q.Y - q.X * p.Y;
            }
            float bottom = minGround - 0.5f, top = maxGround + height;

            for (int i = 0; i < n; i++)
            {
                var p = pts[i]; var q = pts[(i + 1) % n];
                var d = q - p;
                if (d.LengthSquared() < 1e-6f) continue;
                var outward = area2 > 0 ? new Vector3(d.Y, 0, -d.X) : new Vector3(-d.Y, 0, d.X);
                Vector3 pb = new(p.X, bottom, p.Y), qb = new(q.X, bottom, q.Y), qt = new(q.X, top, q.Y), pt = new(p.X, top, p.Y);
                s.Triangle(pb, qb, qt, outward, color, life);
                s.Triangle(pb, qt, pt, outward, color, life);
            }

            int[] roof = Geometry2D.TriangulatePolygon(pts);
            if (roof.Length == 0) // self-intersecting outline: fall back to a fan
            {
                var fan = new List<int>();
                for (int i = 1; i < n - 1; i++) fan.AddRange([0, i, i + 1]);
                roof = [.. fan];
            }
            for (int i = 0; i + 2 < roof.Length; i += 3)
            {
                var a = pts[roof[i]]; var c1 = pts[roof[i + 1]]; var c2 = pts[roof[i + 2]];
                s.Triangle(new Vector3(a.X, top, a.Y), new Vector3(c1.X, top, c1.Y), new Vector3(c2.X, top, c2.Y), Vector3.Up, color, life);
            }
        }

        var result = new List<MeshInstance3D>();
        foreach (var (chunk, s) in surfaces)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = s.Verts.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = s.Normals.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = s.Colors.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV2] = s.Life.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            result.Add(new MeshInstance3D { Name = $"Buildings_{chunk}", Mesh = mesh, MaterialOverride = material });
        }
        return result;
    }
}
