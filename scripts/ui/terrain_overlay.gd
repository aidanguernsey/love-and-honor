extends CanvasLayer
## Spike B debug overlay: terrain stats, render counters and the tile under the mouse (tile data layer, §30.3).
## Keys: G build grid · Esc back to menu. Launch options (after `--`): `--spike-smoke` prints stats after a few
## seconds and quits; `--no-vsync` uncaps the frame rate for measurements.

const SMOKE_SECONDS := 4.0

@export var host_path: NodePath

var _host: Node
var _label: Label
var _elapsed := 0.0
var _smoke := false


func _ready() -> void:
	_host = get_node(host_path)
	_smoke = "--spike-smoke" in OS.get_cmdline_user_args()
	if "--no-vsync" in OS.get_cmdline_user_args():
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var panel := PanelContainer.new()
	panel.position = Vector2(12, 12)
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.08, 0.08, 0.1, 0.78)
	style.set_content_margin_all(10)
	style.set_corner_radius_all(6)
	panel.add_theme_stylebox_override("panel", style)
	add_child(panel)
	_label = Label.new()
	_label.add_theme_font_size_override("font_size", 14)
	panel.add_child(_label)


func _process(delta: float) -> void:
	var s: Dictionary = _host.GetStats()
	var h: Dictionary = _host.GetHoverInfo()
	var lines := PackedStringArray([
		"Love & Honor — Spike B: Oxford terrain (custom mesh generator)",
		"FPS %d · GPU %.2f ms · render CPU %.2f ms · triangles drawn %s · draw calls %d · VRAM %.0f MB" % [
			Engine.get_frames_per_second(), s["gpu_ms"], s["render_cpu_ms"], _thousands(int(s["primitives_in_frame"])),
			int(s["draw_calls"]), s["video_mem_mb"]],
		"%d chunks × %d LODs · %s triangles at full detail · load %.0f ms · mesh build %.0f ms" % [
			s["chunks"], s["lods"], _thousands(s["triangles_full_detail"]), s["load_ms"], s["build_ms"]],
		"Elevation %.1f – %.1f m (USGS 3DEP 1 m lidar)" % [s["elevation_min_m"], s["elevation_max_m"]],
		"",
	])
	if h["valid"]:
		lines.append("Tile (%d, %d) · %.1f m" % [h["tile_x"], h["tile_y"], h["elevation_m"]])
		lines.append("Land state: %s   (OSM land cover: %s)" % [h["land_state"], h["land_cover"]])
		lines.append("Ownership: %s%s   Path: %s   Walk: %s%s" % [
			h["ownership"], " (protected)" if h["protected"] else "", h["path"], h["walk"], "   Building" if h["building"] else ""])
	else:
		lines.append("Point at the terrain to inspect a tile")
	lines.append("")
	lines.append("[G] build grid %s  [Esc] menu" % ("on" if s["grid"] else "off"))
	lines.append("Camera: WASD / middle-drag pan · wheel zoom · Q/E / right-drag orbit · R/F tilt")
	lines.append("Map data © OpenStreetMap contributors · elevation USGS 3DEP")
	_label.text = "\n".join(lines)

	if _smoke:
		_elapsed += delta
		if _elapsed >= SMOKE_SECONDS:
			s["fps"] = Engine.get_frames_per_second()
			s["hover"] = h
			print("SPIKE_B_SMOKE %s" % JSON.stringify(s))
			get_tree().quit()


func _unhandled_key_input(event: InputEvent) -> void:
	if not event.pressed or event.echo:
		return
	match event.physical_keycode:
		KEY_G: _host.ToggleGrid()
		KEY_ESCAPE: get_tree().change_scene_to_file("res://scenes/boot.tscn")


func _thousands(n: int) -> String:
	var s := str(abs(n))
	var out := ""
	while s.length() > 3:
		out = "," + s.substr(s.length() - 3) + out
		s = s.substr(0, s.length() - 3)
	return ("-" if n < 0 else "") + s + out
