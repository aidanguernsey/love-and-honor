using System.Text;
using System.Text.Json.Nodes;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Tools;

namespace LoveAndHonor.Sim.Tests;

public class ModelValidationTests
{
    private static string ModelsDir => Path.Combine(RepoPaths.Root, "assets", "models");

    [Fact]
    public void RepoModels_FollowThePipelineRules()
    {
        var issues = ModelValidation.ValidateDirectory(ModelsDir, RepoPaths.Data);
        Assert.True(issues.Count == 0, "Model checks failed:\n" + string.Join("\n", issues));
        Assert.NotEmpty(Directory.EnumerateFiles(ModelsDir, "*.glb", SearchOption.AllDirectories));
    }

    [Fact]
    public void RepoModels_UseOnlyColoursTheStandInBrandingAlsoHas()
    {
        // Swapping branding.json for the stand-in profile (§36.1) must not break any model's materials.
        var data = Path.Combine(Path.GetTempPath(), "lh-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        try
        {
            File.Copy(Path.Combine(RepoPaths.Data, ArtPipelineConfig.File), Path.Combine(data, ArtPipelineConfig.File));
            File.Copy(Path.Combine(RepoPaths.Data, "branding", "standin.json"), Path.Combine(data, "branding.json"));
            Assert.Empty(ModelValidation.ValidateDirectory(ModelsDir, data));
        }
        finally { Directory.Delete(data, true); }
    }

    [Fact]
    public void ExampleKitPiece_HasThreeLodsWithinBudget()
    {
        var glb = ModelValidation.ReadGlb(File.ReadAllBytes(Path.Combine(ModelsDir, "kit", "georgian", "kit_georgian_wall_window_3m.glb")));
        Assert.Equal(["kit_georgian_wall_window_3m_LOD0", "kit_georgian_wall_window_3m_LOD1", "kit_georgian_wall_window_3m_LOD2"],
            glb.MeshNodes.Select(n => n.Name));
        var tris = glb.MeshNodes.Select(n => n.Triangles).ToArray();
        Assert.True(tris[0] <= 800 && tris[1] < tris[0] && tris[2] < tris[1], string.Join(",", tris));
        Assert.All(glb.Materials, m => Assert.False(m.DoubleSided));
    }

    [Theory]
    [InlineData("kit/georgian/Kit_Georgian_Wall.glb", "lowercase snake_case")]
    [InlineData("kit/georgian/wall_window.glb", "unknown category prefix 'wall'")]
    [InlineData("buildings/kit_georgian_wall.glb", "go in assets/models/kit/georgian/")]
    [InlineData("kit/georgian/kit_georgian_wall_weathered.glb", "variant 'weathered' isn't allowed for 'kit'")]
    public void BadFileNames_AreReported(string path, string expected)
    {
        var issues = CheckOne(path, Model(("x_LOD0", 12)));
        Assert.Contains(issues, i => i.Message.Contains(expected));
    }

    [Fact]
    public void BuildingVariant_IsAllowed()
    {
        Assert.Empty(CheckOne("buildings/bldg_elliott_weathered.glb", Model(("bldg_elliott_LOD0", 3000))));
    }

    [Fact]
    public void BudgetsAndLodRules_AreEnforced()
    {
        Assert.Contains(CheckOne("props/prop_bench.glb", Model(("prop_bench", 301))), i => i.Message.Contains("budget is 300"));
        Assert.Contains(CheckOne("props/prop_bench.glb", Model(("prop_bench_LOD0", 200), ("prop_bench_LOD1", 150))),
            i => i.Message.Contains("_LOD1 has 150 triangles; at most 50"));
        Assert.Contains(CheckOne("props/prop_bench.glb", Model(("prop_bench_LOD0", 200), ("prop_bench_LOD2", 20))),
            i => i.Message.Contains("without gaps"));
        // Characters need 3-4 LOD levels (§28.1a).
        Assert.Contains(CheckOne("characters/char_student.glb", Model(("char_student", 400))), i => i.Message.Contains("needs 3-4"));
        Assert.Empty(CheckOne("characters/char_student.glb",
            Model(("char_student_LOD0", 480), ("char_student_LOD1", 200), ("char_student_LOD2", 100))));
    }

    [Fact]
    public void MaterialsScaleAndPivot_AreChecked()
    {
        Assert.Contains(CheckOne("props/prop_lamp.glb", Model(("prop_lamp", 50)).With(material: "Material.001")),
            i => i.Message.Contains("must be pal_<branding colour>"));
        Assert.Contains(CheckOne("props/prop_lamp.glb", Model(("prop_lamp", 50)).With(material: "pal_brick_7")),
            i => i.Message.Contains("has no 'brick:7'"));
        Assert.Contains(CheckOne("props/prop_lamp.glb", Model(("prop_lamp", 50)).With(doubleSided: true)),
            i => i.Message.Contains("double-sided"));
        Assert.Empty(CheckOne("trees/tree_oak_fall.glb", Model(("tree_oak", 500)).With(doubleSided: true)));
        Assert.Contains(CheckOne("props/prop_lamp.glb", Model(("prop_lamp", 50)).With(scale: 2)), i => i.Message.Contains("unapplied scale"));
        Assert.Contains(CheckOne("props/prop_lamp.glb", Model(("prop_lamp", 50)).With(minY: -1.5f)), i => i.Message.Contains("pivot on the ground"));
    }

    [Fact]
    public void LfsPointerAndSourceFiles_AreReported()
    {
        var dir = TempModels();
        try
        {
            File.WriteAllText(Path.Combine(dir, "props", "prop_bench.glb"), "version https://git-lfs.github.com/spec/v1\n");
            File.WriteAllText(Path.Combine(dir, "props", "prop_bench.blend"), "x");
            var issues = ModelValidation.ValidateDirectory(dir, RepoPaths.Data);
            Assert.Contains(issues, i => i.Message.Contains("git lfs pull"));
            Assert.Contains(issues, i => i.Message.Contains("belong in art/blend/"));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- helpers: tiny synthetic .glb files (JSON chunk only; the checks never read vertex data) ----

    private sealed record FakeModel((string Name, int Tris)[] Nodes, string Material = "pal_brick", bool DoubleSided = false,
        float Scale = 1, float MinY = 0)
    {
        public FakeModel With(string? material = null, bool? doubleSided = null, float? scale = null, float? minY = null) =>
            this with { Material = material ?? Material, DoubleSided = doubleSided ?? DoubleSided, Scale = scale ?? Scale, MinY = minY ?? MinY };
    }

    private static FakeModel Model(params (string Name, int Tris)[] nodes) => new(nodes);

    private static List<ValidationIssue> CheckOne(string relPath, FakeModel model)
    {
        var dir = TempModels();
        try
        {
            var file = Path.Combine(dir, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, BuildGlb(model));
            return ModelValidation.ValidateDirectory(dir, RepoPaths.Data);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string TempModels()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lh-models-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "props"));
        return dir;
    }

    private static byte[] BuildGlb(FakeModel model)
    {
        var accessors = new JsonArray();
        var meshes = new JsonArray();
        var nodes = new JsonArray();
        foreach (var (name, tris) in model.Nodes)
        {
            accessors.Add(new JsonObject { ["count"] = 3, ["type"] = "VEC3", ["min"] = new JsonArray(0, model.MinY, 0), ["max"] = new JsonArray(1, 1, 1) });
            accessors.Add(new JsonObject { ["count"] = tris * 3, ["type"] = "SCALAR" });
            meshes.Add(new JsonObject
            {
                ["primitives"] = new JsonArray(new JsonObject
                {
                    ["attributes"] = new JsonObject { ["POSITION"] = accessors.Count - 2 },
                    ["indices"] = accessors.Count - 1,
                    ["material"] = 0,
                }),
            });
            var node = new JsonObject { ["name"] = name, ["mesh"] = meshes.Count - 1 };
            if (model.Scale != 1) node["scale"] = new JsonArray(model.Scale, model.Scale, model.Scale);
            nodes.Add(node);
        }
        var root = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0" },
            ["accessors"] = accessors,
            ["meshes"] = meshes,
            ["nodes"] = nodes,
            ["materials"] = new JsonArray(new JsonObject { ["name"] = model.Material, ["doubleSided"] = model.DoubleSided }),
        };
        var json = Encoding.UTF8.GetBytes(root.ToJsonString());
        int padded = (json.Length + 3) & ~3;
        var bytes = new byte[20 + padded];
        Encoding.ASCII.GetBytes("glTF").CopyTo(bytes, 0);
        BitConverter.GetBytes(2u).CopyTo(bytes, 4);
        BitConverter.GetBytes((uint)bytes.Length).CopyTo(bytes, 8);
        BitConverter.GetBytes((uint)padded).CopyTo(bytes, 12);
        Encoding.ASCII.GetBytes("JSON").CopyTo(bytes, 16);
        json.CopyTo(bytes, 20);
        for (int i = 20 + json.Length; i < bytes.Length; i++) bytes[i] = (byte)' ';
        return bytes;
    }
}
