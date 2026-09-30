using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Tools;

/// <summary>Triangle counts and names read from one .glb (JSON chunk only; the binary buffer isn't needed).</summary>
public sealed record GlbSummary(
    IReadOnlyList<GlbMeshNode> MeshNodes,
    IReadOnlyList<GlbMaterial> Materials,
    bool HasUnmaterialedPrimitives);

public sealed record GlbMeshNode(string Name, int Triangles, float MinY, bool HasScale);
public sealed record GlbMaterial(string Name, bool DoubleSided);

/// <summary>
/// Checks every model in assets/models/ against docs/ART_PIPELINE.md and data/art_pipeline.json:
/// file name / folder / variant, Git LFS pulled, triangle budget per LOD, LOD naming and ratios, material names
/// (pal_* from branding.json or mat_* special materials), applied scale, pivot on the ground.
/// </summary>
public static partial class ModelValidation
{
    private const string LfsPointerStart = "version https://git-lfs";

    [GeneratedRegex(@"^[a-z][a-z0-9]*(_[a-z0-9]+)+$")]
    private static partial Regex FileStem();

    [GeneratedRegex(@"^(?<base>[a-z][a-z0-9_]*?)(?:_LOD(?<lod>\d))?$")]
    private static partial Regex NodeName();

    public static List<ValidationIssue> ValidateDirectory(string modelsDir, string dataDir)
    {
        var issues = new List<ValidationIssue>();
        if (!Directory.Exists(modelsDir)) return issues;
        ArtPipelineConfig art;
        Palette palette;
        try
        {
            art = ArtPipelineConfig.Parse(File.ReadAllText(Path.Combine(dataDir, ArtPipelineConfig.File)));
            palette = new Palette(File.ReadAllText(Path.Combine(dataDir, "branding.json")));
        }
        catch (Exception ex)
        {
            issues.Add(new("assets/models", "", $"can't check models: {ex.Message}"));
            return issues;
        }

        foreach (var file in Directory.EnumerateFiles(modelsDir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var rel = "assets/models/" + Path.GetRelativePath(modelsDir, file).Replace('\\', '/');
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".glb") ValidateModel(file, rel, art, palette, issues);
            else if (ext == ".gltf" || ext == ".blend" || ext == ".fbx" || ext == ".obj")
                issues.Add(new(rel, "", $"only .glb files go in assets/models/ ({ext} sources belong in art/blend/)"));
        }
        return issues;
    }

    private static void ValidateModel(string file, string rel, ArtPipelineConfig art, Palette palette, List<ValidationIssue> issues)
    {
        void Error(string msg, string where = "") => issues.Add(new(rel, where, msg));

        // --- name, folder, variant ---
        var stem = Path.GetFileNameWithoutExtension(file);
        if (!FileStem().IsMatch(stem))
        {
            Error("file name must be lowercase snake_case '<category>_<name>.glb', e.g. kit_georgian_wall_window_3m.glb");
            return;
        }
        var tokens = stem.Split('_');
        if (!art.Categories.TryGetValue(tokens[0], out var cat))
        {
            Error($"unknown category prefix '{tokens[0]}' (known: {string.Join(", ", art.Categories.Keys)})");
            return;
        }
        var folders = rel.Split('/')[2..^1];
        var expected = tokens[0] == "kit" ? $"{cat.Folder}/{tokens[1]}" : cat.Folder;
        if (string.Join('/', folders) != expected)
            Error($"'{tokens[0]}_' models go in assets/models/{expected}/");
        var allVariants = art.Categories.Values.SelectMany(c => c.Variants).ToHashSet();
        if (allVariants.Contains(tokens[^1]) && !cat.Variants.Contains(tokens[^1]))
            Error($"variant '{tokens[^1]}' isn't allowed for '{tokens[0]}' (allowed: {(cat.Variants.Length > 0 ? string.Join(", ", cat.Variants) : "none")})");

        // --- parse ---
        GlbSummary glb;
        try
        {
            glb = ReadGlb(File.ReadAllBytes(file));
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            return;
        }

        // --- materials ---
        foreach (var mat in glb.Materials)
        {
            if (MaterialSlot.TryParsePalette(mat.Name, out var key, out var index))
            {
                if (!palette.Has(key, index))
                    Error($"material '{mat.Name}': branding.json colors has no '{MaterialSlot.PaletteSpec(key, index)}'");
            }
            else if (!art.SpecialMaterials.ContainsKey(mat.Name))
                Error($"material '{mat.Name}' must be pal_<branding colour>[_<n>] or one of: {string.Join(", ", art.SpecialMaterials.Keys)}");
            if (mat.DoubleSided && !cat.AllowDoubleSided)
                Error($"material '{mat.Name}' is double-sided; turn on Backface Culling in Blender ('{tokens[0]}' doesn't allow double-sided)");
        }
        if (glb.HasUnmaterialedPrimitives)
            Error("a mesh has faces with no material; assign a pal_* or mat_* material");

        // --- nodes, LODs, triangles ---
        if (glb.MeshNodes.Count == 0)
        {
            Error("no meshes");
            return;
        }
        var trisPerLevel = new SortedDictionary<int, int>();
        int shared = 0; // mesh nodes without a _LODn suffix are visible at every distance
        foreach (var node in glb.MeshNodes)
        {
            var m = NodeName().Match(node.Name);
            if (!m.Success)
            {
                Error($"node '{node.Name}': names are lowercase snake_case with an optional _LOD<n> suffix");
                continue;
            }
            if (node.HasScale) Error($"node '{node.Name}' has unapplied scale; apply it in Blender (Ctrl+A > Scale)");
            if (m.Groups["lod"].Success)
            {
                int lod = int.Parse(m.Groups["lod"].Value);
                trisPerLevel[lod] = trisPerLevel.GetValueOrDefault(lod) + node.Triangles;
            }
            else shared += node.Triangles;
        }
        if (trisPerLevel.Count == 0) trisPerLevel[0] = 0;
        var levels = trisPerLevel.Keys.ToArray();
        if (levels.Where((l, i) => l != i).Any())
            Error($"LOD levels must run _LOD0, _LOD1, ... without gaps (found {string.Join(", ", levels.Select(l => "_LOD" + l))})");
        int min = cat.LodLevels[0], max = cat.LodLevels[1];
        if (levels.Length < min || levels.Length > max)
            Error($"has {levels.Length} LOD level(s); '{tokens[0]}' needs {min}-{max}");

        int lod0 = trisPerLevel[levels[0]] + shared;
        if (lod0 > cat.MaxTris)
            Error($"LOD0 has {lod0:N0} triangles; the '{tokens[0]}' budget is {cat.MaxTris:N0}");
        for (int i = 1; i < levels.Length; i++)
        {
            int tris = trisPerLevel[levels[i]] + shared;
            float ratio = levels[i] < art.Lod.MaxRatioToLod0.Length ? art.Lod.MaxRatioToLod0[levels[i]] : art.Lod.MaxRatioToLod0[^1];
            if (tris > lod0 * ratio)
                Error($"_LOD{levels[i]} has {tris:N0} triangles; at most {ratio:P0} of LOD0 ({(int)(lod0 * ratio):N0})");
        }

        // --- pivot on the ground ---
        float lowest = glb.MeshNodes.Min(n => n.MinY);
        if (MathF.Abs(lowest) > 0.05f)
            Error($"lowest point is at y = {lowest:0.00} m; put the pivot on the ground (y = 0) so models sit on the terrain");
    }

    /// <summary>Reads the JSON chunk of a binary glTF 2.0 file. Throws with a readable message when it isn't one.</summary>
    public static GlbSummary ReadGlb(byte[] bytes)
    {
        if (bytes.Length >= LfsPointerStart.Length && Encoding.ASCII.GetString(bytes, 0, LfsPointerStart.Length) == LfsPointerStart)
            throw new InvalidDataException("this is a Git LFS pointer, not the model; run 'git lfs pull'");
        if (bytes.Length < 20 || Encoding.ASCII.GetString(bytes, 0, 4) != "glTF")
            throw new InvalidDataException("not a binary glTF (.glb) file");
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != 2)
            throw new InvalidDataException("glTF version must be 2");
        int jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
        if (Encoding.ASCII.GetString(bytes, 16, 4) != "JSON" || 20 + jsonLength > bytes.Length)
            throw new InvalidDataException("GLB JSON chunk missing or truncated");
        var root = JsonNode.Parse(Encoding.UTF8.GetString(bytes, 20, jsonLength))!;

        var accessors = root["accessors"]?.AsArray() ?? [];
        var meshes = root["meshes"]?.AsArray() ?? [];
        var materials = (root["materials"]?.AsArray() ?? [])
            .Select(m => new GlbMaterial(m?["name"]?.GetValue<string>() ?? "(unnamed)", m?["doubleSided"]?.GetValue<bool>() ?? false))
            .ToList();

        bool unmaterialed = false;
        var meshInfo = new List<(int Tris, float MinY)>();
        foreach (var mesh in meshes)
        {
            int tris = 0;
            float minY = float.MaxValue;
            foreach (var prim in mesh!["primitives"]!.AsArray())
            {
                int mode = prim!["mode"]?.GetValue<int>() ?? 4;
                var position = accessors[prim["attributes"]!["POSITION"]!.GetValue<int>()]!;
                int count = prim["indices"] is { } idx ? accessors[idx.GetValue<int>()]!["count"]!.GetValue<int>() : position["count"]!.GetValue<int>();
                tris += mode switch { 4 => count / 3, 5 or 6 => Math.Max(0, count - 2), _ => 0 };
                if (position["min"] is JsonArray pmin) minY = MathF.Min(minY, pmin[1]!.GetValue<float>());
                if (prim["material"] is null) unmaterialed = true;
            }
            meshInfo.Add((tris, minY == float.MaxValue ? 0 : minY));
        }

        var nodes = new List<GlbMeshNode>();
        foreach (var node in root["nodes"]?.AsArray() ?? [])
        {
            if (node?["mesh"] is not { } meshIndex) continue;
            var (tris, minY) = meshInfo[meshIndex.GetValue<int>()];
            float ty = node["translation"] is JsonArray t ? t[1]!.GetValue<float>() : 0f;
            bool scaled = node["scale"] is JsonArray s && s.Any(v => MathF.Abs(v!.GetValue<float>() - 1f) > 1e-4f);
            nodes.Add(new(node["name"]?.GetValue<string>() ?? "(unnamed)", tris, minY + ty, scaled));
        }
        return new GlbSummary(nodes, materials, unmaterialed);
    }
}
