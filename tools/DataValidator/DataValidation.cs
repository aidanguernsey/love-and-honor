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

        CrossChecks(docs, issues);
        return issues;
    }

    private static void CrossChecks(Dictionary<string, JsonNode> docs, List<ValidationIssue> issues)
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
