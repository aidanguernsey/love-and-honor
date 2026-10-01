using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Draws building placement (Phase 1 1e): the ghost preview under the cursor (green = can build, red = can't, with the
/// entrance marked), the player's buildings (rising while under construction, then in their kind colour), and
/// Heritage Project sites as translucent outlines with a label. Placed buildings are plain boxes until the kit
/// assembler (1f). Main thread only.
/// </summary>
public sealed class PlacementRenderer
{
    private readonly RealMap _map;
    private readonly RenderingConfig _render;
    private readonly Palette _palette;
    private readonly MeshInstance3D _ghost;
    private readonly MeshInstance3D _sites;
    private readonly MeshInstance3D _heritage;
    private readonly Node3D _heritageLabels;
    private readonly StandardMaterial3D _solid;
    private PlacementView? _shownView;
    private string _heritageKey = "";

    public PlacementRenderer(Node3D parent, RealMap map, RenderingConfig render, Palette palette)
    {
        _map = map;
        _render = render;
        _palette = palette;
        _solid = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.85f };
        var translucent = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false,
        };
        _ghost = new MeshInstance3D { Name = "PlacementGhost", MaterialOverride = translucent, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _sites = new MeshInstance3D { Name = "PlayerBuildings", MaterialOverride = _solid };
        _heritage = new MeshInstance3D { Name = "HeritageSites", MaterialOverride = translucent, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _heritageLabels = new Node3D { Name = "HeritageLabels" };
        parent.AddChild(_ghost);
        parent.AddChild(_sites);
        parent.AddChild(_heritage);
        parent.AddChild(_heritageLabels);
    }

    // ---------------- ghost ----------------

    /// <summary>Shows the preview for an item at a pose; null hides it.</summary>
    public void SetGhost(CatalogItem? item, Pose pose, int entrance, bool ok)
    {
        if (item is null) { _ghost.Visible = false; return; }
        var p = _render.Placement;
        var c = Color(ok ? p.GhostOkColor : p.GhostBadColor, p.GhostOpacity);
        var mesh = new MeshData();
        float height = Height(item.Def);
        mesh.Box(Corners(pose, item.W, item.H), _map, height, c);
        if (entrance >= 0)
        {
            int ex = entrance % _map.Grid.Width, ey = entrance / _map.Grid.Width;
            var door = new Pose(2 * ex + 1, 2 * ey + 1, 0);
            mesh.Box(Corners(door, 1, 1, 0.6f), _map, 0.6f, Color(p.EntranceColor, 0.9f));
        }
        _ghost.Mesh = mesh.ToMesh();
        _ghost.Visible = true;
    }

    // ---------------- player buildings ----------------

    /// <summary>Rebuilds the player's buildings when the placement view changed.</summary>
    public void SetSites(PlacementView? view, IReadOnlyDictionary<string, CatalogItem> items)
    {
        if (view is null || ReferenceEquals(view, _shownView)) return;
        _shownView = view;
        var mesh = new MeshData();
        var p = _render.Placement;
        foreach (var s in view.Sites)
        {
            if (!items.TryGetValue(s.ItemId, out var item)) continue;
            float full = Height(item.Def);
            var corners = Corners(s.Pose, s.W, s.H);
            if (s.Complete)
                mesh.Box(corners, _map, full, KindColor(item.Category));
            else
            {
                // Foundations first, then walls rising with progress (scaffold-coloured until finished).
                float h = Math.Max(0.6f, full * s.Progress);
                mesh.Box(corners, _map, h, Color(p.ConstructionColor, 1f));
            }
        }
        _sites.Mesh = mesh.IsEmpty ? null : mesh.ToMesh();
    }

    // ---------------- Heritage Project sites ----------------

    /// <summary>Shows the real sites of the given Heritage Projects (translucent outline + label), or hides them.</summary>
    public void SetHeritageSites(IReadOnlyList<CatalogItem> projects, bool visible)
    {
        _heritage.Visible = visible;
        _heritageLabels.Visible = visible;
        string key = string.Join(",", projects.Select(i => i.Id));
        if (key == _heritageKey) return;
        _heritageKey = key;
        foreach (var child in _heritageLabels.GetChildren()) child.QueueFree();
        var p = _render.Placement;
        var mesh = new MeshData();
        float tile = _map.Grid.TileSizeM;
        foreach (var item in projects)
        {
            if (item.Site is not { } site) continue;
            var o = site.Outline;
            var pts = new Vector2[o.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(o[2 * i] * tile, o[2 * i + 1] * tile);
            float top = mesh.Prism(pts, _map, p.HeritageSiteHeightM, Color(p.HeritageSiteColor, p.HeritageSiteOpacity));
            var centre = pts.Aggregate(Vector2.Zero, (a, b) => a + b) / pts.Length;
            _heritageLabels.AddChild(new Label3D
            {
                Text = $"{site.Name} (real site, built {site.RealYear})\nHeritage Project",
                Position = new Vector3(centre.X, top + 6f, centre.Y),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FixedSize = true, // readable at any zoom
                FontSize = 28,
                PixelSize = 0.0012f,
                OutlineSize = 10,
                NoDepthTest = true,
                Modulate = new Color(1, 0.97f, 0.88f),
            });
        }
        _heritage.Mesh = mesh.IsEmpty ? null : mesh.ToMesh();
    }

    // ---------------- helpers ----------------

    private float Height(BuildingDef def)
    {
        var e = _render.Extrusion;
        if (def.Style == "none") return _render.Placement.WoodenHeightM;
        return e.KindHeightM.GetValueOrDefault(KindKey(def.Category), e.KindHeightM["other"]);
    }

    private static string KindKey(string category) => category switch
    {
        "landscape" => "other",
        "town" => "house",
        _ => category,
    };

    private Color KindColor(string category)
    {
        var e = _render.Extrusion;
        return Color(e.KindColors.GetValueOrDefault(KindKey(category), e.KindColors["other"]), 1f);
    }

    private Color Color(string spec, float alpha)
    {
        var c = _palette.Resolve(spec);
        return new Color(c.R, c.G, c.B, alpha).SrgbToLinear() with { A = alpha };
    }

    /// <summary>Corners in world metres (x, z), optionally shrunk.</summary>
    private Vector2[] Corners(Pose pose, int w, int h, float scale = 1f)
    {
        float tile = _map.Grid.TileSizeM;
        return FootprintMath.Corners(pose, w, h).Select(c => new Vector2(
            (pose.Cx + (c.X - pose.Cx) * scale) * tile, (pose.Cy + (c.Y - pose.Cy) * scale) * tile)).ToArray();
    }

    /// <summary>Triangle lists with flat normals and vertex colours.</summary>
    private sealed class MeshData
    {
        private readonly List<Vector3> _v = [];
        private readonly List<Vector3> _n = [];
        private readonly List<Color> _c = [];

        public bool IsEmpty => _v.Count == 0;

        private void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Color color)
        {
            var n = (c - a).Cross(b - a);
            if (n.Dot(facing) < 0) { (b, c) = (c, b); n = -n; }
            n = n.Normalized();
            _v.Add(a); _v.Add(b); _v.Add(c);
            _n.Add(n); _n.Add(n); _n.Add(n);
            _c.Add(color); _c.Add(color); _c.Add(color);
        }

        /// <summary>A convex quad footprint extruded from below the lowest ground point to height above the highest.</summary>
        public void Box(Vector2[] corners, RealMap map, float height, Color color) => Prism(corners, map, height, color);

        /// <summary>Extrudes a polygon (world x, z); returns the roof height.</summary>
        public float Prism(Vector2[] pts, RealMap map, float height, Color color)
        {
            int n = pts.Length;
            float lo = float.MaxValue, hi = float.MinValue, area2 = 0;
            foreach (var p in pts)
            {
                float g = map.Heights.HeightAt(p.X, p.Y);
                lo = Math.Min(lo, g); hi = Math.Max(hi, g);
            }
            for (int i = 0; i < n; i++) { var p = pts[i]; var q = pts[(i + 1) % n]; area2 += p.X * q.Y - q.X * p.Y; }
            float bottom = lo - 0.5f, top = hi + height;
            for (int i = 0; i < n; i++)
            {
                var p = pts[i]; var q = pts[(i + 1) % n];
                var d = q - p;
                if (d.LengthSquared() < 1e-6f) continue;
                var outward = area2 > 0 ? new Vector3(d.Y, 0, -d.X) : new Vector3(-d.Y, 0, d.X);
                Vector3 pb = new(p.X, bottom, p.Y), qb = new(q.X, bottom, q.Y), qt = new(q.X, top, q.Y), pt = new(p.X, top, p.Y);
                Tri(pb, qb, qt, outward, color);
                Tri(pb, qt, pt, outward, color);
            }
            int[] roof = Geometry2D.TriangulatePolygon(pts);
            if (roof.Length == 0) { var fan = new List<int>(); for (int i = 1; i < n - 1; i++) fan.AddRange([0, i, i + 1]); roof = [.. fan]; }
            for (int i = 0; i + 2 < roof.Length; i += 3)
            {
                var a = pts[roof[i]]; var b = pts[roof[i + 1]]; var c = pts[roof[i + 2]];
                Tri(new Vector3(a.X, top, a.Y), new Vector3(b.X, top, b.Y), new Vector3(c.X, top, c.Y), Vector3.Up, color);
            }
            return top;
        }

        public ArrayMesh ToMesh()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = _n.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = _c.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }
}
