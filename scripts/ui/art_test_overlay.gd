extends CanvasLayer
## Step 4 art-pipeline check overlay: model stats, LOD table, which LOD the facade shows at the current distance.
## Keys: Esc back to menu. Launch options (after `--`): `--spike-smoke` prints ART_SMOKE stats after a few seconds
## and quits; with `--screenshot=<file.png>` it also saves the frame. `--camera=` works as in camera_rig.gd.

const SMOKE_SECONDS := 3.0

@export var host_path: NodePath

var _host: Node
var _label: Label
var _smoke := false
var _elapsed := 0.0


func _ready() -> void:
	_host = get_node(host_path)
	_smoke = "--spike-smoke" in OS.get_cmdline_user_args()
	var panel := PanelContainer.new()
	panel.position = Vector2(12, 12)
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.08, 0.08, 0.1, 0.78)
	style.set_content_margin_all(10)
	style.set_corner_radius_all(6)
	panel.add_theme_stylebox_override("panel", style)
	add_child(panel)
	_label = Label.new()
	_label.add_theme_font_size_override("font_size", 13)
	panel.add_child(_label)


func _process(delta: float) -> void:
	var s: Dictionary = _host.GetStats()
	var lines := PackedStringArray([
		"Love & Honor — Step 4: art pipeline import check (placeholder model, not final art)",
		"FPS %d · GPU %.2f ms · render CPU %.2f ms · triangles in frame %d" % [
			Engine.get_frames_per_second(), s["gpu_ms"], s["render_cpu_ms"], s["primitives_in_frame"]],
		"",
		"Model: %s" % s["model"],
		"Category: %s · facade pieces: %d · palette materials assigned: %d%s" % [
			s["category"], s["facade_pieces"], s["materials_assigned"],
			"" if s["materials_unknown"] == "" else " · UNKNOWN: " + s["materials_unknown"]],
	])
	for l in s["lods"]:
		lines.append("  LOD%d  %4d tris   visible %s" % [l["lod"], l["tris"], _range_text(l["begin_m"], l["end_m"])])
	lines.append("")
	lines.append("Camera %.0f m from the facade → facade shows LOD%d (zoom out to switch)" % [s["camera_distance_m"], s["facade_lod"]])
	lines.append("Camera: WASD / middle-drag pan · wheel zoom · Q/E / right-drag orbit · R/F tilt · [Esc] menu")
	_label.text = "\n".join(lines)

	if _smoke:
		_elapsed += delta
		if _elapsed >= SMOKE_SECONDS:
			s["fps"] = Engine.get_frames_per_second()
			print("ART_SMOKE %s" % JSON.stringify(s))
			for arg in OS.get_cmdline_user_args():
				if arg.begins_with("--screenshot="):
					get_viewport().get_texture().get_image().save_png(arg.trim_prefix("--screenshot="))
			get_tree().quit()


func _unhandled_key_input(event: InputEvent) -> void:
	if event.pressed and event.physical_keycode == KEY_ESCAPE:
		get_tree().change_scene_to_file("res://scenes/boot.tscn")


func _range_text(begin_m: float, end_m: float) -> String:
	if begin_m <= 0.0 and end_m <= 0.0:
		return "at every distance"
	if end_m <= 0.0:
		return "beyond %.0f m" % begin_m
	return "%.0f–%.0f m" % [begin_m, end_m]
