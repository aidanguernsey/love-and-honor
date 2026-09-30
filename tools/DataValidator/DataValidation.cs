using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace LoveAndHonor.Tools;

public sealed record ValidationIssue(string File, string Location, string Message)
{
    public override string ToString() => $"{File}{(Location.Length > 0 ? " " + Location : "")}: {Message}";
}

/// <summary>
/// Validates everything under /data:
///  1. Every data file declares "$schema" and validates against it (JSON Schema 2020-12).
///  2. Cross-file checks the schemas can't express (unique ids, references between files, era ordering).
/// </summary>
public static class DataValidation
{
    private static readonly JsonDocumentOptions ParseOptions = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow };

    public static List<ValidationIssue> ValidateDirectory(string dataDir)
    {
        var issues = new List<ValidationIssue>();
        dataDir = Path.GetFullPath(dataDir);
        var schemaDir = Path.Combine(dataDir, "schemas");
        var schemaCache = new Dictionary<string, JsonSchema>(StringComparer.OrdinalIgnoreCase);
        var docs = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);

        var dataFiles = Directory.EnumerateFiles(dataDir, "*.json", SearchOption.AllDirectories)
            .Where(f => !Path.GetFullPath(f).StartsWith(schemaDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);

        foreach (var file in dataFiles)
        {
            var rel = Rel(dataDir, file);
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(File.ReadAllText(file), documentOptions: ParseOptions);
            }
            catch (JsonException ex)
            {
                issues.Add(new(rel, "", $"invalid JSON: {ex.Message}"));
                continue;
            }
            if (node is not JsonObject obj)
            {
                issues.Add(new(rel, "", "top level must be a JSON object"));
                continue;
            }
            docs[rel] = node;

            if (obj["$schema"]?.GetValue<string>() is not { } schemaRef)
            {
                issues.Add(new(rel, "", "missing \"$schema\" (relative path to a file in data/schemas/)"));
                continue;
            }
            var schemaPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, schemaRef));
            if (!File.Exists(schemaPath))
            {
                issues.Add(new(rel, "", $"$schema file not found: {schemaRef}"));
                continue;
            }
            if (!schemaCache.TryGetValue(schemaPath, out var schema))
            {
                // Fresh registry per schema: we don't use cross-file $ref, and it avoids id collisions.
                var build = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
                schema = JsonSchema.FromFile(schemaPath, build);
                schemaCache[schemaPath] = schema;
            }

            using var doc = JsonDocument.Parse(node.ToJsonString());
            var result = schema.Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List, IncludeApplicatorErrors = false });
            if (!result.IsValid)
            {
                foreach (var detail in result.Details ?? [])
                {
                    if (detail.Errors is null) continue;
                    foreach (var (keyword, msg) in detail.Errors)
                        issues.Add(new(rel, detail.InstanceLocation.ToString(), $"{keyword}: {msg}"));
                }
                if (result.Errors is { } topErrors)
                    foreach (var (keyword, msg) in topErrors)
                        issues.Add(new(rel, "", $"{keyword}: {msg}"));
            }
        }

        CrossChecks(dataDir, docs, issues);
        return issues;
    }

    private static void CrossChecks(string dataDir, Dictionary<string, JsonNode> docs, List<ValidationIssue> issues)
    {
        // Eras: unique ids, ordered, contiguous, only the last may be open-ended.
        var eraIds = new HashSet<string>();
        if (docs.TryGetValue("eras.json", out var eras) && eras["eras"] is JsonArray eraArr)
        {
            int? prevEnd = null;
            for (int i = 0; i < eraArr.Count; i++)
            {
                var e = eraArr[i]!;
                var id = Str(e, "id");
                if (!eraIds.Add(id)) issues.Add(new("eras.json", $"/eras/{i}", $"duplicate era id '{id}'"));
                int start = Int(e, "start_year") ?? 0;
                int? end = Int(e, "end_year");
                if (end is { } en && en < start) issues.Add(new("eras.json", $"/eras/{i}", "end_year before start_year"));
                if (i > 0 && prevEnd is null) issues.Add(new("eras.json", $"/eras/{i}", "only the last era may have end_year null"));
                if (i > 0 && prevEnd is { } pe && start != pe + 1)
                    issues.Add(new("eras.json", $"/eras/{i}", $"era starts {start} but previous ends {pe} (gap or overlap)"));
                prevEnd = end;
            }
        }

        // Buildings: file name == id, unique ids, unlock_era exists.
        var buildingIds = new HashSet<string>();
        foreach (var (rel, node) in docs.Where(d => d.Key.StartsWith("buildings/")))
        {
            var id = Str(node, "id");
            if (Path.GetFileNameWithoutExtension(rel) != id) issues.Add(new(rel, "/id", $"id '{id}' must match file name"));
            if (!buildingIds.Add(id)) issues.Add(new(rel, "/id", $"duplicate building id '{id}'"));
            var era = Str(node, "unlock_era");
            if (eraIds.Count > 0 && !eraIds.Contains(era)) issues.Add(new(rel, "/unlock_era", $"unknown era '{era}'"));
        }

        // Timeline: unique ids, building_def exists, demolished after built.
        if (docs.TryGetValue("timeline.json", out var tl) && tl["entries"] is JsonArray entries)
        {
            var ids = new HashSet<string>();
            var osmIds = new HashSet<string>();
            var sourceIds = (tl["sources"] as JsonObject)?.Select(kv => kv.Key).ToHashSet() ?? [];
            var featureIds = docs.Where(d => d.Key.StartsWith("map/") && d.Key.EndsWith("_features.json"))
                .SelectMany(d => d.Value["buildings"]?.AsArray() ?? []).Select(b => Str(b!, "osm_id")).ToHashSet();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i]!;
                var loc = $"/entries/{i}";
                var id = Str(e, "id");
                if (!ids.Add(id)) issues.Add(new("timeline.json", loc, $"duplicate id '{id}'"));
                if (e["building_def"]?.GetValue<string>() is { } def && !buildingIds.Contains(def))
                    issues.Add(new("timeline.json", loc, $"unknown building_def '{def}'"));
                if (Int(e, "demolished_year") is { } d && d < (Int(e, "built_year") ?? 0))
                    issues.Add(new("timeline.json", loc, "demolished_year before built_year"));
                if (Str(e, "status") == "standing" && Int(e, "demolished_year") is not null)
                    issues.Add(new("timeline.json", loc, "standing building has a demolished_year"));
                foreach (var s in e["sources"]?.AsArray() ?? [])
                    if (!sourceIds.Contains(s!.GetValue<string>())) issues.Add(new("timeline.json", loc, $"unknown source '{s}'"));
                if (e["approx_site_osm_id"] is JsonValue av && av.TryGetValue<string>(out var approx) && featureIds.Count > 0 && !featureIds.Contains(approx))
                    issues.Add(new("timeline.json", loc, $"approx_site_osm_id '{approx}' not in map features"));
                if (e["osm_id"] is JsonValue ov && ov.TryGetValue<string>(out var osm))
                {
                    if (featureIds.Count > 0 && !featureIds.Contains(osm)) issues.Add(new("timeline.json", loc, $"osm_id '{osm}' not in map features"));
                    if (!osmIds.Add(osm)) issues.Add(new("timeline.json", loc, $"osm_id '{osm}' linked by more than one entry"));
                }
            }
        }

        // Spike configs: building defs exist.
        foreach (var (rel, node) in docs.Where(d => d.Key.StartsWith("spikes/")))
        {
            var list = node["campus"]?["buildings"]?.AsArray() ?? [];
            for (int i = 0; i < list.Count; i++)
            {
                var def = Str(list[i]!, "def");
                if (!buildingIds.Contains(def)) issues.Add(new(rel, $"/campus/buildings/{i}", $"unknown building def '{def}'"));
            }
        }

        // Map pipeline outputs: binary files exist with the declared size, and the grid still matches balance.json.
        var balanceMap = docs.TryGetValue("balance.json", out var balanceDoc) ? balanceDoc["map"] : null;
        foreach (var (rel, node) in docs.Where(d => d.Key.StartsWith("map/") && d.Key.EndsWith("_map.json")))
        {
            string dir = Path.GetDirectoryName(Path.Combine(dataDir, rel))!;
            void CheckBinary(JsonNode? info, int bytesPerSample, string location)
            {
                if (info is null) return;
                string file = Str(info, "file");
                long expected = (long)(Int(info, "width") ?? 0) * (Int(info, "height") ?? 0) * bytesPerSample;
                string path = Path.Combine(dir, file);
                if (!File.Exists(path)) issues.Add(new(rel, location, $"missing file '{file}' (run tools/map_pipeline/build_map.py)"));
                else if (new FileInfo(path).Length != expected)
                    issues.Add(new(rel, location, $"'{file}' is {new FileInfo(path).Length} bytes, expected {expected} (is Git LFS installed and pulled?)"));
            }
            CheckBinary(node["heightmap"], 2, "/heightmap");
            if (node["rasters"] is JsonObject rasters)
                foreach (var (name, info) in rasters) CheckBinary(info, 1, $"/rasters/{name}");
            if (!File.Exists(Path.Combine(dir, Str(node, "features_file"))))
                issues.Add(new(rel, "/features_file", $"missing file '{Str(node, "features_file")}'"));

            if (balanceMap is not null)
            {
                int tiles = Int(node, "tiles") ?? -1;
                if (tiles != Int(balanceMap, "grid_width") || tiles != Int(balanceMap, "grid_height"))
                    issues.Add(new(rel, "/tiles", $"map has {tiles} tiles per side but balance.json map grid is {balanceMap["grid_width"]}x{balanceMap["grid_height"]}; re-run the map pipeline"));
                if (node["tile_size_m"]?.GetValue<double>() != balanceMap["tile_size_m"]?.GetValue<double>())
                    issues.Add(new(rel, "/tile_size_m", "tile size differs from balance.json map.tile_size_m; re-run the map pipeline"));
            }
        }

        // Land-state rules may only name land-cover classes the map pipeline produces.
        if (docs.TryGetValue("map/land_states.json", out var landStates))
        {
            var mapDoc = docs.Where(d => d.Key.StartsWith("map/") && d.Key.EndsWith("_map.json")).Select(d => d.Value).FirstOrDefault();
            var known = (mapDoc?["codes"]?["landcover"] as JsonObject)?.Select(kv => kv.Key).ToHashSet();
            var rulesArr = landStates["present_day_rules"]?.AsArray() ?? [];
            for (int i = 0; i < rulesArr.Count; i++)
                foreach (var lc in rulesArr[i]?["landcover"]?.AsArray() ?? [])
                    if (known is not null && !known.Contains(lc!.GetValue<string>()))
                        issues.Add(new("map/land_states.json", $"/present_day_rules/{i}", $"unknown land cover '{lc}'"));
            if (rulesArr.Count == 0 || rulesArr[^1]?.AsObject().Count != 1)
                issues.Add(new("map/land_states.json", "/present_day_rules", "the last rule must be a catch-all (only 'state')"));
        }

        // Departments: unique ids, colleges exist.
        if (docs.TryGetValue("departments.json", out var dep))
        {
            var colleges = new HashSet<string>();
            foreach (var c in dep["colleges"]?.AsArray() ?? [])
                if (!colleges.Add(Str(c!, "id"))) issues.Add(new("departments.json", "/colleges", $"duplicate college '{Str(c!, "id")}'"));
            var depIds = new HashSet<string>();
            var depArr = dep["departments"]?.AsArray() ?? [];
            for (int i = 0; i < depArr.Count; i++)
            {
                var d = depArr[i]!;
                var loc = $"/departments/{i}";
                if (!depIds.Add(Str(d, "id"))) issues.Add(new("departments.json", loc, $"duplicate department '{Str(d, "id")}'"));
                if (!colleges.Contains(Str(d, "college"))) issues.Add(new("departments.json", loc, $"unknown college '{Str(d, "college")}'"));
                foreach (var s in d["shared_with"]?.AsArray() ?? [])
                    if (!colleges.Contains(s!.GetValue<string>())) issues.Add(new("departments.json", loc, $"unknown shared_with college '{s}'"));
            }
        }

        // Balance: every need has a happiness weight.
        if (docs.TryGetValue("balance.json", out var bal) && bal["needs"] is JsonObject needs)
        {
            var weights = needs["happiness_weights"] as JsonObject;
            foreach (var n in needs["ids"]?.AsArray() ?? [])
                if (weights is null || !weights.ContainsKey(n!.GetValue<string>()))
                    issues.Add(new("balance.json", "/needs/happiness_weights", $"missing weight for need '{n}'"));
        }
    }

    private static string Rel(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');
    private static string Str(JsonNode n, string key) => n[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    private static int? Int(JsonNode n, string key) => n[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
}
