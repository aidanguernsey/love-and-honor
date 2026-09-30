namespace LoveAndHonor.Sim.Data;

/// <summary>
/// Where the sim reads /data files from. The sim never touches res:// itself: in an exported game res:// lives in
/// a .pck that System.IO can't open, so the Godot bridge supplies an implementation backed by FileAccess.
/// Paths are relative to the data root and use forward slashes ("buildings/dining_commons.json").
/// </summary>
public interface IDataSource
{
    string ReadText(string relativePath);

    /// <summary>Relative paths of the *.json files directly inside <paramref name="relativeDir"/>, sorted ordinally.</summary>
    IReadOnlyList<string> ListJson(string relativeDir);
}

/// <summary>Plain file-system data source for tests, benchmarks and tools.</summary>
public sealed class FileSystemDataSource(string root) : IDataSource
{
    public string Root { get; } = Path.GetFullPath(root);

    public string ReadText(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    public IReadOnlyList<string> ListJson(string relativeDir) =>
        Directory.EnumerateFiles(Path.Combine(Root, relativeDir), "*.json")
            .Select(f => relativeDir.TrimEnd('/') + "/" + Path.GetFileName(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
}
