@tool
extends EditorScenePostImport
## Default post-import script for every 3D model (project.godot [importer_defaults] scene → import_script/path).
## docs/ART_PIPELINE.md: turns hand-made LODs (<model>_LOD0, _LOD1, ...) into visibility ranges using the
## category's lod_distances_m from data/art_pipeline.json, and tags nodes with metadata (lh_category, lh_lod).
## Materials are left as imported: the game swaps pal_* / mat_* materials at runtime (PaletteMaterials.cs) so
## colours follow branding.json without re-importing.

const ART_PIPELINE := "res://data/art_pipeline.json"


func _post_import(scene: Node) -> Object:
	var stem := get_source_file().get_file().get_basename()
	var prefix := stem.get_slice("_", 0)
	var art = JSON.parse_string(FileAccess.get_file_as_string(ART_PIPELINE))
	if not art is Dictionary or not art["categories"].has(prefix):
		push_warning("%s: unknown model category '%s' (see %s); imported without LOD setup" % [stem, prefix, ART_PIPELINE])
		return scene
	var category: Dictionary = art["categories"][prefix]
	var distances: Array = category["lod_distances_m"]
	var margin: float = art["lod"]["range_margin_m"]
	scene.set_meta("lh_category", prefix)

	var lod_nodes: Array[GeometryInstance3D] = []
	_collect_lods(scene, lod_nodes)
	var last := -1
	for node in lod_nodes:
		last = maxi(last, _lod_of(node))
	for node in lod_nodes:
		var lod := _lod_of(node)
		if lod > distances.size():
			push_warning("%s: %s has no switch distance in %s" % [stem, node.name, ART_PIPELINE])
			continue
		node.visibility_range_begin = 0.0 if lod == 0 else float(distances[lod - 1])
		node.visibility_range_begin_margin = 0.0 if lod == 0 else margin
		node.visibility_range_end = 0.0 if lod == last else float(distances[lod])
		node.visibility_range_end_margin = 0.0 if lod == last else margin
		node.set_meta("lh_lod", lod)
	return scene


func _collect_lods(node: Node, out: Array[GeometryInstance3D]) -> void:
	if node is GeometryInstance3D and _lod_of(node) >= 0:
		out.append(node)
	for child in node.get_children():
		_collect_lods(child, out)


func _lod_of(node: Node) -> int:
	var name := String(node.name)
	var at := name.rfind("_LOD")
	if at < 0 or not name.substr(at + 4).is_valid_int():
		return -1
	return name.substr(at + 4).to_int()
