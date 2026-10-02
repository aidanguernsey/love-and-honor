using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.View;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Turns assembled buildings (<see cref="BuildingAssembler"/>, Phase 1 1f) into Godot meshes. Loads the Georgian kit
/// pieces from assets/models/kit/georgian/*.glb once, then merges every piece of a building into one mesh per material
/// at three distances: full detail, flat facades (pieces' _LOD1, small details dropped), and a plain block beyond
/// massing_distance_m. Materials: walls use building_wall.gdshader in the recipe's finish, glass uses window.gdshader in
/// the recipe's window style, everything else the shared palette materials. Vertex colours carry a random number per
/// piece (which windows glow at night), the height above the floor and the weathering. Main thread only.
/// </summary>
public sealed class BuildingKit
{
    private const string KitFolder = "res://assets/models/kit/georgian/";

    public BuildingRecipesConfig Recipes { get; }
    public ArtPipelineConfig Art { get; }
    public int PiecesLoaded => _pieces.Count;

    private readonly Palette _palette;
    private readonly PaletteMaterials _materials;
    private readonly Dictionary<string, PieceMesh[]> _pieces = [];
    private readonly Dictionary<string, Material> _walls = [];
    private readonly Dictionary<string, ShaderMaterial> _windows = [];
    private readonly Shader _wallShader, _windowShader;
    private float _night;
    private bool _lit = true;

    /// <summary>One level of detail of a kit piece: expanded triangle lists per pipeline material, in Godot winding.</summary>
    private sealed class PieceMesh
    {
        public readonly List<(string Material, Vector3[] Pos, Vector3[] Nrm, Vector2[] Uv)> Surfaces = [];
    }

    private sealed class SurfaceBuilder
    {
        public readonly List<Vector3> V = [];
        public readonly List<Vector3> N = [];
        public readonly List<Vector2> Uv = [];
        public readonly List<Color> C = [];
    }

    public BuildingKit(IDataSource source)
    {
        Recipes = BuildingRecipesConfig.Load(source);
        Art = ArtPipelineConfig.Load(source);
        _palette = new Palette(source.ReadText("branding.json"));
        _materials = new PaletteMaterials(source);
        _wallShader = GD.Load<Shader>("res://assets/shaders/building_wall.gdshader");
        _windowShader = GD.Load<Shader>("res://assets/shaders/window.gdshader");
        // In the editor the folder lists the .glb files; in an exported game only their .import files remain.
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in DirAccess.GetFilesAt(KitFolder))
        {
            if (file.EndsWith(".glb", StringComparison.Ordinal)) names.Add(file[..^4]);
            else if (file.EndsWith(".glb.import", StringComparison.Ordinal)) names.Add(file[..^11]);
        }
        foreach (var name in names) LoadPiece(name);
    }

    /// <summary>Night 0..1 for every window material (lit windows glow).</summary>
    public void SetNight(float night)
    {
        if (Math.Abs(night - _night) < 0.005f) return;
        _night = night;
        foreach (var m in _windows.Values) m.SetShaderParameter("night", night);
    }

    public AssembledBuilding Assemble(string recipeId, float widthM, float depthM, float plinthDepthM, float progress = 1f) =>
        BuildingAssembler.Assemble(Recipes, Recipes.Recipes[recipeId], widthM, depthM, Art.KitGrid.ModuleWidthM,
            Art.KitGrid.StoreyHeightM, plinthDepthM, progress);

    /// <summary>
    /// A node for an assembled building, centred at <paramref name="center"/> (world; Y = floor level), turned by
    /// <paramref name="yawRad"/> (Godot convention). <paramref name="weathering"/> 0 = new … 1 = old.
    /// </summary>
    public Node3D Create(AssembledBuilding b, Vector3 center, float yawRad, float weathering, ulong seed, string name, bool lit = true)
    {
        _lit = lit;
        var root = new Node3D { Name = name, Position = center, Rotation = new Vector3(0, yawRad, 0) };
        var lods = Art.Categories["bldg"].LodDistancesM;
        float near = lods[0], far = Recipes.Defaults.MassingDistanceM, margin = 2f;
        var recipe = b.Recipe;
        root.AddChild(Mesh("Detail", Build(b, recipe, level: 0, weathering, seed), 0, near + margin));
        root.AddChild(Mesh("Facades", Build(b, recipe, level: 1, weathering, seed), near, far + margin));
        root.AddChild(Mesh("Block", BuildMassing(b, recipe, weathering), far, 0));
        return root;
    }

    /// <summary>
    /// A building standing on the terrain: footprint centre (world x, z), rotation clockwise in degrees as seen from above
    /// (the placement convention; 0 = entrance facing south), size in metres. The floor is at the highest ground under
    /// the corners and a stone plinth reaches down to the lowest.
    /// </summary>
    public Node3D CreateOnTerrain(string recipeId, float x, float z, float rotationDeg, float widthM, float depthM,
        LoveAndHonor.Sim.World.Heightmap ground, float weathering, ulong seed, string name, float progress = 1f, bool lit = true)
    {
        float yaw = Mathf.DegToRad(-rotationDeg);
        var basis = new Basis(Vector3.Up, yaw);
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var (cx, cz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var c = basis * new Vector3(cx * widthM / 2, 0, cz * depthM / 2);
            float g = ground.HeightAt(x + c.X, z + c.Z);
            lo = Math.Min(lo, g); hi = Math.Max(hi, g);
        }
        var b = Assemble(recipeId, widthM, depthM, hi - lo + Recipes.Defaults.PlinthExtraM, progress);
        return Create(b, new Vector3(x, hi, z), yaw, weathering, seed, name, lit);
    }

    /// <summary>Triangles of a building at full detail (for stats).</summary>
    public int Triangles(AssembledBuilding b) =>
        b.Pieces.Sum(p => _pieces.TryGetValue(p.Piece, out var l) ? l[0].Surfaces.Sum(s => s.Pos.Length / 3) : 0)
        + b.Generated.Values.Sum(m => m.TriangleCount);

    // ---------------- mesh building ----------------

    private static MeshInstance3D Mesh(string name, ArrayMesh? mesh, float begin, float end) => new()
    {
        Name = name, Mesh = mesh, VisibilityRangeBegin = begin, VisibilityRangeEnd = end,
    };

    private ArrayMesh? Build(AssembledBuilding b, BuildingRecipesConfig.Recipe recipe, int level, float weathering, ulong seed)
    {
        var surfaces = new Dictionary<string, SurfaceBuilder>();
        int index = 0;
        foreach (var p in b.Pieces)
        {
            index++;
            if (!_pieces.TryGetValue(p.Piece, out var lods)) continue;
            PieceMesh? mesh = level == 0 ? lods[0] : lods.Length > 1 ? lods[1] : BuildingAssembler.KeptAtDistance(p.Piece) ? lods[0] : null;
            if (mesh is null) continue;
            var basis = new Basis(Vector3.Up, Mathf.DegToRad(p.YawDeg));
            var scale = new Vector3(p.ScaleX, p.ScaleY, p.ScaleZ);
            var origin = new Vector3(p.X, p.Y, p.Z);
            float random = _lit ? Hash(seed, (ulong)index) : 1f; // 1 = never lit (nobody inside yet)
            foreach (var (mat, pos, nrm, uv) in mesh.Surfaces)
            {
                var s = Surface(surfaces, mat);
                for (int i = 0; i < pos.Length; i++)
                {
                    var v = basis * (pos[i] * scale) + origin;
                    s.V.Add(v);
                    s.N.Add((basis * (nrm[i] / scale)).Normalized());
                    s.Uv.Add(uv[i]);
                    s.C.Add(new Color(random, Math.Clamp(v.Y / 10f, 0, 1), 0, weathering));
                }
            }
        }
        foreach (var (mat, buf) in b.Generated) Append(Surface(surfaces, mat), buf, weathering);
        return ToMesh(surfaces, recipe);
    }

    private ArrayMesh? BuildMassing(AssembledBuilding b, BuildingRecipesConfig.Recipe recipe, float weathering)
    {
        var surfaces = new Dictionary<string, SurfaceBuilder>();
        foreach (var (mat, buf) in b.Massing) Append(Surface(surfaces, mat), buf, weathering);
        return ToMesh(surfaces, recipe);
    }

    private static SurfaceBuilder Surface(Dictionary<string, SurfaceBuilder> surfaces, string mat) =>
        surfaces.TryGetValue(mat, out var s) ? s : surfaces[mat] = new SurfaceBuilder();

    /// <summary>Adds generated triangles (counter-clockwise) in Godot's clockwise order.</summary>
    private static void Append(SurfaceBuilder s, MeshBuffer buf, float weathering)
    {
        var p = buf.Positions;
        var n = buf.Normals;
        for (int t = 0; t < p.Count; t += 9)
            foreach (int k in new[] { 0, 6, 3 })
            {
                var v = new Vector3(p[t + k], p[t + k + 1], p[t + k + 2]);
                s.V.Add(v);
                s.N.Add(new Vector3(n[t + k], n[t + k + 1], n[t + k + 2]));
                s.Uv.Add(Vector2.Zero);
                s.C.Add(new Color(0, Math.Clamp(v.Y / 10f, 0, 1), 0, weathering));
            }
    }

    private ArrayMesh? ToMesh(Dictionary<string, SurfaceBuilder> surfaces, BuildingRecipesConfig.Recipe recipe)
    {
        if (surfaces.Count == 0) return null;
        var mesh = new ArrayMesh();
        foreach (var (mat, s) in surfaces.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (s.V.Count == 0) continue;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = s.V.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Normal] = s.N.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = s.Uv.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Color] = s.C.ToArray();
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, MaterialFor(mat, recipe));
        }
        return mesh.GetSurfaceCount() > 0 ? mesh : null;
    }

    // ---------------- materials ----------------

    private Material? MaterialFor(string name, BuildingRecipesConfig.Recipe recipe)
    {
        if (name == BuildingAssembler.WallMaterial) return WallMaterial(recipe.Walls);
        if (name == "mat_glass") return WindowMaterial(recipe.Windows);
        return _materials.For(name);
    }

    private Material WallMaterial(string finish)
    {
        if (_walls.TryGetValue(finish, out var m)) return m;
        string spec = Recipes.Walls.GetValueOrDefault(finish, "brick:1");
        var c = _palette.Resolve(spec);
        var mat = new ShaderMaterial { Shader = _wallShader };
        mat.SetShaderParameter("albedo", new Color(c.R, c.G, c.B));
        mat.SetShaderParameter("pattern", spec.StartsWith("brick") ? 1 : 2);
        var mortar = _palette.Resolve("limestone");
        mat.SetShaderParameter("mortar_color", new Color(mortar.R, mortar.G, mortar.B));
        return _walls[finish] = mat;
    }

    private ShaderMaterial WindowMaterial(string style)
    {
        if (_windows.TryGetValue(style, out var m)) return m;
        var s = Recipes.WindowStyles.GetValueOrDefault(style) ?? Recipes.WindowStyles.Values.First();
        var mat = new ShaderMaterial { Shader = _windowShader };
        mat.SetShaderParameter("cols", s.Cols);
        mat.SetShaderParameter("rows_upper", s.RowsUpper);
        mat.SetShaderParameter("rows_lower", s.RowsLower);
        var trim = _palette.Resolve("trim");
        mat.SetShaderParameter("frame_color", new Color(trim.R, trim.G, trim.B));
        if (Art.SpecialMaterials.TryGetValue("mat_glass", out var glass))
        {
            var g = Palette.ParseHex(glass.Color);
            mat.SetShaderParameter("glass_color", new Color(g.R, g.G, g.B));
        }
        if (Art.SpecialMaterials.TryGetValue("mat_window_glow", out var glow))
        {
            var g = Palette.ParseHex(glow.Color);
            mat.SetShaderParameter("glow_color", new Color(g.R, g.G, g.B));
            mat.SetShaderParameter("glow_energy", glow.EmissionEnergy);
        }
        mat.SetShaderParameter("night", _night);
        return _windows[style] = mat;
    }

    // ---------------- loading ----------------

    private void LoadPiece(string name)
    {
        var scene = GD.Load<PackedScene>(KitFolder + name + ".glb");
        if (scene is null) return;
        var root = scene.Instantiate<Node>();
        var lods = new SortedDictionary<int, PieceMesh>();
        Collect(root, Transform3D.Identity, lods);
        root.Free();
        if (lods.Count > 0) _pieces[name] = [.. lods.Values];
    }

    private static void Collect(Node node, Transform3D parent, SortedDictionary<int, PieceMesh> lods)
    {
        var xf = node is Node3D n3 ? parent * n3.Transform : parent;
        if (node is MeshInstance3D mi && mi.Mesh is { } mesh)
        {
            string nodeName = mi.Name;
            int lod = nodeName.Length > 5 && nodeName[^5..^1] == "_LOD" ? nodeName[^1] - '0' : 0;
            if (!lods.TryGetValue(lod, out var piece)) lods[lod] = piece = new PieceMesh();
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var arrays = mesh.SurfaceGetArrays(s);
                var verts = arrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array();
                var normals = arrays[(int)Godot.Mesh.ArrayType.Normal].AsVector3Array();
                var uvVariant = arrays[(int)Godot.Mesh.ArrayType.TexUV];
                var uvs = uvVariant.VariantType == Variant.Type.Nil ? new Vector2[verts.Length] : uvVariant.AsVector2Array();
                var idxVariant = arrays[(int)Godot.Mesh.ArrayType.Index];
                int[] idx = idxVariant.VariantType == Variant.Type.Nil ? Enumerable.Range(0, verts.Length).ToArray() : idxVariant.AsInt32Array();
                var pos = new Vector3[idx.Length];
                var nrm = new Vector3[idx.Length];
                var uv = new Vector2[idx.Length];
                for (int i = 0; i < idx.Length; i++)
                {
                    pos[i] = xf * verts[idx[i]];
                    nrm[i] = (xf.Basis * normals[idx[i]]).Normalized();
                    uv[i] = uvs[idx[i]];
                }
                string material = mesh.SurfaceGetMaterial(s)?.ResourceName ?? "";
                piece.Surfaces.Add((material, pos, nrm, uv));
            }
        }
        foreach (var child in node.GetChildren()) Collect(child, xf, lods);
    }

    private static float Hash(ulong seed, ulong i)
    {
        ulong z = seed + i * 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 40) / (float)(1UL << 24);
    }
}
