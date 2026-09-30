using System.Collections.Generic;
using System.Linq;
using Godot;
using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Step 4 art-pipeline check (not gameplay): loads one imported kit piece, tiles it into a two-storey facade on the
/// kit grid (so the pivot and module size can be checked by eye), and shows each hand-made LOD side by side.
/// Materials are assigned at runtime from the branding palette (<see cref="PaletteMaterials"/>). The facade uses
/// the visibility ranges the post-import script set, so zooming out switches LODs.
/// </summary>
[GlobalClass]
public partial class ArtImportTestHost : Node3D
{
    [Export(PropertyHint.File, "*.glb")] public string ModelPath { get; set; } = "res://assets/models/kit/georgian/kit_georgian_wall_window_3m.glb";
    [Export] public int FacadeBays { get; set; } = 6;
    [Export] public int FacadeStoreys { get; set; } = 2;

    private const float AreaM = 60f;

    private ArtPipelineConfig _art = null!;
    private PaletteMaterials _materials = null!;
    private Node3D _facade = null!;
    private Vector3 _facadeCenter;
    private readonly List<(int Lod, int Tris, float Begin, float End)> _lods = [];
    private string _category = "";

    public override void _Ready()
    {
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        var data = new GodotDataSource();
        _art = ArtPipelineConfig.Load(data);
        _materials = new PaletteMaterials(data);
        var palette = new Palette(data.ReadText("branding.json"));

        var ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(AreaM, AreaM) },
            Position = new Vector3(AreaM / 2, 0, AreaM / 2),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = ToColor(palette.Resolve("lawn")), Roughness = 1f },
        };
        AddChild(ground);

        var scene = GD.Load<PackedScene>(ModelPath);
        if (scene is null)
        {
            GD.PushError($"ArtImportTestHost: can't load {ModelPath}");
            return;
        }
        var probe = scene.Instantiate<Node3D>();
        _category = probe.HasMeta("lh_category") ? probe.GetMeta("lh_category").AsString() : "(none: post-import script didn't run)";
        foreach (var mi in MeshInstances(probe))
            _lods.Add((LodOf(mi), Triangles(mi.Mesh), mi.VisibilityRangeBegin, mi.VisibilityRangeEnd));
        _lods.Sort();
        probe.Free();

        // Facade: bays along +X, storeys stacked by the kit grid's storey height.
        var grid = _art.KitGrid;
        _facade = new Node3D { Name = "Facade" };
        AddChild(_facade);
        float width = FacadeBays * grid.ModuleWidthM;
        var origin = new Vector3(AreaM / 2 - width / 2, 0, AreaM / 2 - 4);
        for (int s = 0; s < FacadeStoreys; s++)
            for (int b = 0; b < FacadeBays; b++)
            {
                var piece = scene.Instantiate<Node3D>();
                piece.Position = origin + new Vector3(b * grid.ModuleWidthM, s * grid.StoreyHeightM, 0);
                _facade.AddChild(piece);
                _materials.Apply(piece);
            }
        _facadeCenter = origin + new Vector3(width / 2, FacadeStoreys * grid.StoreyHeightM / 2, 0);

        // One copy per LOD, forced visible (visibility ranges cleared), with a caption.
        for (int i = 0; i < _lods.Count; i++)
        {
            var piece = scene.Instantiate<Node3D>();
            piece.Name = $"Lod{_lods[i].Lod}Sample";
            piece.Position = new Vector3(AreaM / 2 + (i - (_lods.Count - 1) / 2f) * 5f - grid.ModuleWidthM / 2, 0, AreaM / 2 + 8);
            foreach (var mi in MeshInstances(piece))
            {
                mi.Visible = LodOf(mi) == _lods[i].Lod;
                mi.VisibilityRangeBegin = 0;
                mi.VisibilityRangeEnd = 0;
            }
            AddChild(piece);
            _materials.Apply(piece);
            piece.AddChild(new Label3D
            {
                Text = $"LOD{_lods[i].Lod} · {_lods[i].Tris} tris",
                Position = new Vector3(grid.ModuleWidthM / 2, grid.StoreyHeightM + 0.6f, 0.2f),
                PixelSize = 0.01f,
                FontSize = 64,
                OutlineSize = 12,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            });
        }
    }

    // ---- API for GDScript (camera rig + overlay) ----

    public Vector2 GetMapSizeMeters() => new(AreaM, AreaM);

    public float GetGroundHeight(float x, float z) => 0f;

    public Godot.Collections.Dictionary GetStats()
    {
        var camera = GetViewport().GetCamera3D();
        float distance = camera is null ? 0 : camera.GlobalPosition.DistanceTo(_facadeCenter);
        int active = _lods.FindLastIndex(l => l.Begin <= distance);
        var lods = new Godot.Collections.Array();
        foreach (var l in _lods)
            lods.Add(new Godot.Collections.Dictionary { ["lod"] = l.Lod, ["tris"] = l.Tris, ["begin_m"] = l.Begin, ["end_m"] = l.End });
        return new()
        {
            ["model"] = ModelPath,
            ["category"] = _category,
            ["lods"] = lods,
            ["facade_pieces"] = _facade?.GetChildCount() ?? 0,
            ["materials_assigned"] = _materials?.Assigned ?? 0,
            ["materials_unknown"] = string.Join(", ", _materials?.Unknown ?? []),
            ["camera_distance_m"] = distance,
            ["facade_lod"] = active >= 0 ? _lods[active].Lod : -1,
            ["gpu_ms"] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()),
            ["render_cpu_ms"] = RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()),
            ["primitives_in_frame"] = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame),
        };
    }

    // ---- helpers ----

    private static IEnumerable<MeshInstance3D> MeshInstances(Node node)
    {
        if (node is MeshInstance3D mi) yield return mi;
        foreach (var child in node.GetChildren())
            foreach (var m in MeshInstances(child)) yield return m;
    }

    private static int LodOf(Node node) => node.HasMeta("lh_lod") ? node.GetMeta("lh_lod").AsInt32() : 0;

    private static int Triangles(Mesh mesh)
    {
        if (mesh is not ArrayMesh am) return 0;
        return Enumerable.Range(0, am.GetSurfaceCount()).Sum(i =>
        {
            int indices = am.SurfaceGetArrayIndexLen(i);
            return (indices > 0 ? indices : am.SurfaceGetArrayLen(i)) / 3;
        });
    }

    private static Color ToColor(Rgb c) => new(c.R, c.G, c.B);
}
