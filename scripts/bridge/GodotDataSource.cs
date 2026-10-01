using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Reads /data through Godot's FileAccess/DirAccess so it works inside an exported .pck, where System.IO can't
/// see res://. (Exports must include "*.json" in the export filter — see README.)
/// </summary>
public sealed class GodotDataSource(string root = "res://data") : IDataSource
{
	public string ReadText(string relativePath)
	{
		string path = $"{root}/{relativePath}";
		if (!Godot.FileAccess.FileExists(path)) throw new FileNotFoundException($"Data file not found: {path}");
		return Godot.FileAccess.GetFileAsString(path);
	}

	public byte[] ReadBytes(string relativePath)
	{
		string path = $"{root}/{relativePath}";
		if (!Godot.FileAccess.FileExists(path)) throw new FileNotFoundException($"Data file not found: {path}");
		return Godot.FileAccess.GetFileAsBytes(path);
	}

	public IReadOnlyList<string> ListJson(string relativeDir) =>
		DirAccess.GetFilesAt($"{root}/{relativeDir}")
			.Where(f => f.EndsWith(".json"))
			.OrderBy(f => f, System.StringComparer.Ordinal)
			.Select(f => $"{relativeDir.TrimEnd('/')}/{f}")
			.ToList();
}
