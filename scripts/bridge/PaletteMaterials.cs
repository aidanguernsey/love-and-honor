using System.Collections.Generic;
using Godot;
using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Runtime material hookup for imported models (docs/ART_PIPELINE.md): a surface whose imported material is named
/// pal_&lt;colour&gt;[_&lt;n&gt;] gets a shared material in that branding.json colour, and mat_* gets the special
/// material from art_pipeline.json. One shared material per name keeps draw-call batching possible, and colours
/// follow the active branding profile without re-importing any model.
/// </summary>
public sealed class PaletteMaterials
{
    private readonly Palette _palette;
    private readonly ArtPipelineConfig _art;
    private readonly Dictionary<string, StandardMaterial3D?> _cache = new();

    public int Assigned { get; private set; }
    public HashSet<string> Unknown { get; } = new();

    public PaletteMaterials(IDataSource data)
    {
        _palette = new Palette(data.ReadText("branding.json"));
        _art = ArtPipelineConfig.Load(data);
    }

    /// <summary>Shared material for a pipeline material name, or null if the name breaks the naming rule.</summary>
    public StandardMaterial3D? For(string name)
    {
        if (_cache.TryGetValue(name, out var cached)) return cached;
        StandardMaterial3D? mat = null;
        if (MaterialSlot.TryParsePalette(name, out var key, out var index) && _palette.Has(key, index))
            mat = Make(name, _palette.Resolve(MaterialSlot.PaletteSpec(key, index)), 0.85f, 0f, 0f);
        else if (_art.SpecialMaterials.TryGetValue(name, out var s))
            mat = Make(name, Palette.ParseHex(s.Color), s.Roughness, s.Metallic, s.EmissionEnergy);
        _cache[name] = mat;
        return mat;
    }

    /// <summary>Overrides the materials of every mesh under <paramref name="root"/>.</summary>
    public void Apply(Node root)
    {
        if (root is MeshInstance3D mi && mi.Mesh is { } mesh)
        {
            for (int i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                var name = mesh.SurfaceGetMaterial(i)?.ResourceName ?? "";
                if (For(name) is { } mat)
                {
                    mi.SetSurfaceOverrideMaterial(i, mat);
                    Assigned++;
                }
                else if (Unknown.Add(name))
                    GD.PushWarning($"PaletteMaterials: '{name}' on {mi.Name} isn't a pal_*/mat_* material (docs/ART_PIPELINE.md)");
            }
        }
        foreach (var child in root.GetChildren()) Apply(child);
    }

    private static StandardMaterial3D Make(string name, Rgb c, float roughness, float metallic, float emission)
    {
        var color = new Color(c.R, c.G, c.B); // sRGB, like the hex in the data files
        var mat = new StandardMaterial3D
        {
            ResourceName = name,
            AlbedoColor = color,
            Roughness = roughness,
            Metallic = metallic,
        };
        if (emission > 0)
        {
            mat.EmissionEnabled = true;
            mat.Emission = color;
            mat.EmissionEnergyMultiplier = emission;
        }
        return mat;
    }
}
