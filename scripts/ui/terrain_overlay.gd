extends CanvasLayer
## Spike B overlay: terrain stats, tile/building inspector, and the 1809 → 2026 timeline controls (§5.1b time-lapse).
## Keys: Space play/pause the timeline · [ / ] step a year · G build grid · Esc back to menu.
## Launch options (after `--`): `--year=N`, `--day=N` (1–365), `--hour=H`, `--play`, `--spike-smoke` (print stats
## after a few seconds and quit), `--no-vsync` (uncap the frame rate for measurements).

const SMOKE_SECONDS := 4.0
const PLAY_SPEEDS := [1, 5, 10, 25]  # years per second
const DAY_ANIMATION_HOURS_PER_SECOND := 2.0
const MONTHS := ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
const MONTH_STARTS := [1, 32, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335]

@export var host_path: NodePath

var _host: Node
var _stats_label: Label
var _info_label: Label
var _year_slider: HSlider
var _year_label: Label
var _play_button: Button
var _speed_option: OptionButton
var _day_slider: HSlider
var _day_label: Label
var _hour_slider: HSlider
var _hour_label: Label
var _animate_day: CheckBox
var _undated: CheckBox
var _playing := false
var _year_float := 0.0
var _elapsed := 0.0
var _smoke := false


func _ready() -> void:
	_host = get_node(host_path)
	var args := OS.get_cmdline_user_args()
	_smoke = "--spike-smoke" in args
	if "--no-vsync" in args:
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	_build_ui()
	for arg in args:
		if arg.begins_with("--year="): _host.SetYear(int(arg.trim_prefix("--year=")))
		elif arg.begins_with("--day="): _host.SetDayOfYear(int(arg.trim_prefix("--day=")))
		elif arg.begins_with("--hour="): _host.SetHour(float(arg.trim_prefix("--hour=")))
	_playing = "--play" in args
	_year_float = _host.GetYear()
	_sync_controls()


func _build_ui() -> void:
	# Top-left: stats + inspector.
	var stats := _panel(Vector2(12, 12))
	_stats_label = _label(13)
	stats.add_child(_stats_label)

	# Right: year info and events.
	var info := _panel(Vector2(0, 12))
	info.anchor_left = 1.0
	info.anchor_right = 1.0
	info.offset_left = -440
	info.offset_right = -12
	info.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	_info_label = _label(13)
	_info_label.custom_minimum_size = Vector2(410, 0)
	_info_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	info.add_child(_info_label)

	# Bottom: timeline controls.
	var bottom := _panel(Vector2(12, 0))
	bottom.anchor_top = 1.0
	bottom.anchor_bottom = 1.0
	bottom.anchor_right = 1.0
	bottom.offset_top = -118
	bottom.offset_bottom = -12
	bottom.offset_right = -12
	var rows := VBoxContainer.new()
	rows.add_theme_constant_override("separation", 6)
	bottom.add_child(rows)

	var range: Vector2i = _host.GetYearRange()
	var row1 := HBoxContainer.new()
	rows.add_child(row1)
	row1.add_child(_label(14, "Year"))
	_year_slider = _slider(range.x, range.y, 1, func(v): _host.SetYear(int(v)); _year_float = v; _sync_controls())
	row1.add_child(_year_slider)
	_year_label = _label(18)
	_year_label.custom_minimum_size = Vector2(60, 0)
	row1.add_child(_year_label)
	_play_button = Button.new()
	_play_button.focus_mode = Control.FOCUS_NONE
	_play_button.custom_minimum_size = Vector2(80, 30)
	_play_button.pressed.connect(func(): _toggle_play())
	row1.add_child(_play_button)
	_speed_option = OptionButton.new()
	_speed_option.focus_mode = Control.FOCUS_NONE
	for s in PLAY_SPEEDS:
		_speed_option.add_item("%d yr/s" % s)
	_speed_option.select(2)
	row1.add_child(_speed_option)

	var row2 := HBoxContainer.new()
	rows.add_child(row2)
	row2.add_child(_label(14, "Season"))
	_day_slider = _slider(1, 365, 1, func(v): _host.SetDayOfYear(int(v)); _sync_controls())
	row2.add_child(_day_slider)
	_day_label = _label(14)
	_day_label.custom_minimum_size = Vector2(60, 0)
	row2.add_child(_day_label)
	row2.add_child(_label(14, "Time"))
	_hour_slider = _slider(0, 24, 0.25, func(v): _host.SetHour(v); _sync_controls())
	row2.add_child(_hour_slider)
	_hour_label = _label(14)
	_hour_label.custom_minimum_size = Vector2(52, 0)
	row2.add_child(_hour_label)
	_animate_day = CheckBox.new()
	_animate_day.text = "Animate day"
	_animate_day.focus_mode = Control.FOCUS_NONE
	row2.add_child(_animate_day)

	var row3 := HBoxContainer.new()
	rows.add_child(row3)
	_undated = CheckBox.new()
	_undated.text = "Show buildings with no known date in every year (otherwise they appear only in %d)" % range.y
	_undated.focus_mode = Control.FOCUS_NONE
	_undated.toggled.connect(func(on): _host.SetShowUndated(on))
	row3.add_child(_undated)


func _process(delta: float) -> void:
	if _playing:
		var range: Vector2i = _host.GetYearRange()
		_year_float += PLAY_SPEEDS[_speed_option.selected] * delta
		if _year_float >= range.y:
			_year_float = range.y
			_playing = false
		_host.SetYear(int(_year_float))
		_sync_controls()
	if _animate_day.button_pressed:
		_host.SetHour(_host.GetHour() + DAY_ANIMATION_HOURS_PER_SECOND * delta)
		_sync_controls()

	var s: Dictionary = _host.GetStats()
	var h: Dictionary = _host.GetHoverInfo()
	var y: Dictionary = _host.GetYearInfo()
	var lines := PackedStringArray([
		"Love & Honor — Spike B: Oxford terrain & timeline",
		"FPS %d · GPU %.2f ms · render CPU %.2f ms · triangles %s · draw calls %d" % [
			Engine.get_frames_per_second(), s["gpu_ms"], s["render_cpu_ms"], _thousands(int(s["primitives_in_frame"])), int(s["draw_calls"])],
		"Terrain %d chunks × %d LODs · %s building footprints · repaint %.1f ms" % [
			s["chunks"], s["lods"], _thousands(s["building_footprints"]), s["paint_ms"]],
		"",
	])
	if h["valid"]:
		lines.append("Tile (%d, %d) · %.1f m" % [h["tile_x"], h["tile_y"], h["elevation_m"]])
		lines.append("Land state %d: %s   (today: %s, OSM: %s)" % [y["year"], h["land_state"], h["land_state_present"], h["land_cover"]])
		lines.append("Ownership today: %s%s · Path: %s" % [h["ownership"], " (protected)" if h["protected"] else "", h["path"]])
		if h.has("building"):
			lines.append("Building: %s  %s%s%s" % [h["building"], h["building_years"],
				"  [%s, unverified]" % h["building_confidence"] if h["building_confidence"] != "" else "",
				"  (approximate site)" if h["building_approximate"] else ""])
	else:
		lines.append("Point at the terrain to inspect a tile or building")
	lines.append("")
	lines.append("[Space] play/pause  [ [ ] ] step year  [G] grid %s  [Esc] menu" % ("on" if s["grid"] else "off"))
	lines.append("Camera: WASD / middle-drag pan · wheel zoom · Q/E / right-drag orbit · R/F tilt")
	lines.append("Map data © OpenStreetMap contributors · elevation USGS 3DEP")
	_stats_label.text = "\n".join(lines)

	var info := PackedStringArray([
		"%d · %s · roads: %s" % [y["year"], y["era"], y["road_type"]],
		"Sun %.0f° up, azimuth %.0f°" % [y["sun_elevation"], y["sun_azimuth"]],
		"Miami buildings standing (timeline): %d" % y["standing_timeline"],
		"",
	])
	var events: Array = y["events"]
	if events.is_empty():
		info.append("No recorded building events this year.")
	else:
		for i in min(events.size(), 10):
			info.append(events[i])
		if events.size() > 10:
			info.append("… and %d more" % (events.size() - 10))
	info.append("")
	info.append("Land use before today is a placeholder model; building dates are unverified (docs/research/BUILDING_DATES.md). %d timeline entries have no footprint and aren't drawn." % y["unplaced"])
	_info_label.text = "\n".join(info)

	if _smoke:
		_elapsed += delta
		if _elapsed >= SMOKE_SECONDS:
			s["fps"] = Engine.get_frames_per_second()
			s["year_info"] = y
			s["hover"] = h
			print("SPIKE_B_SMOKE %s" % JSON.stringify(s))
			get_tree().quit()


func _unhandled_key_input(event: InputEvent) -> void:
	if not event.pressed:
		return
	match event.physical_keycode:
		KEY_SPACE:
			if not event.echo: _toggle_play()
		KEY_BRACKETLEFT: _step_year(-1)
		KEY_BRACKETRIGHT: _step_year(1)
		KEY_G:
			if not event.echo: _host.ToggleGrid()
		KEY_ESCAPE: get_tree().change_scene_to_file("res://scenes/boot.tscn")


func _toggle_play() -> void:
	var range: Vector2i = _host.GetYearRange()
	_playing = not _playing
	if _playing and _host.GetYear() >= range.y:
		_host.SetYear(range.x)  # replay from the start
	_year_float = _host.GetYear()
	_sync_controls()


func _step_year(delta_years: int) -> void:
	_playing = false
	_host.SetYear(_host.GetYear() + delta_years)
	_year_float = _host.GetYear()
	_sync_controls()


func _sync_controls() -> void:
	var year: int = _host.GetYear()
	_year_slider.set_value_no_signal(year)
	_year_label.text = str(year)
	_play_button.text = "Pause" if _playing else "Play"
	var day: int = _host.GetDayOfYear()
	_day_slider.set_value_no_signal(day)
	_day_label.text = _date_label(day)
	var hour: float = _host.GetHour()
	_hour_slider.set_value_no_signal(hour)
	_hour_label.text = "%02d:%02d" % [int(hour), int(fmod(hour, 1.0) * 60)]


func _date_label(day: int) -> String:
	var m := 0
	for i in MONTH_STARTS.size():
		if day >= MONTH_STARTS[i]:
			m = i
	return "%s %d" % [MONTHS[m], day - MONTH_STARTS[m] + 1]


func _panel(pos: Vector2) -> PanelContainer:
	var panel := PanelContainer.new()
	panel.position = pos
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.08, 0.08, 0.1, 0.78)
	style.set_content_margin_all(10)
	style.set_corner_radius_all(6)
	panel.add_theme_stylebox_override("panel", style)
	add_child(panel)
	return panel


func _label(size: int, text := "") -> Label:
	var l := Label.new()
	l.add_theme_font_size_override("font_size", size)
	l.text = text
	return l


func _slider(min_v: float, max_v: float, step: float, on_change: Callable) -> HSlider:
	var s := HSlider.new()
	s.min_value = min_v
	s.max_value = max_v
	s.step = step
	s.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	s.custom_minimum_size = Vector2(200, 24)
	s.focus_mode = Control.FOCUS_NONE
	s.value_changed.connect(on_change)
	return s


func _thousands(n: int) -> String:
	var s := str(abs(n))
	var out := ""
	while s.length() > 3:
		out = "," + s.substr(s.length() - 3) + out
		s = s.substr(0, s.length() - 3)
	return ("-" if n < 0 else "") + s + out
