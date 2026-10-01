extends CanvasLayer
## Main HUD skeleton (§27.1), Phase 1 checkpoint 1c. Systems that don't exist yet show "—" with a tooltip saying
## which checkpoint brings them. Keys (§27.5): Space pause/resume · 1–4 speed (1×/2×/4×/8×) · 5 skip to next event
## (reserved) · O overlays · G build grid · Esc main menu.
## Launch options (after `--`; start from the boot scene with `--play`): `--speed=N` (0–4), `--camera=...` (see camera_rig.gd), `--game-smoke[=seconds]`
## (print GAME_SMOKE stats once the sim has run that long, then quit), `--screenshot=<file.png>` (with --game-smoke).

const BUILD_CATEGORIES := ["Academic", "Housing", "Dining", "Student Life", "Athletics", "Admin / Utilities", "Landscape", "Landmarks"]
const SPEED_LABELS := ["Pause", "1×", "2×", "4×", "8×"]

@export var host_path: NodePath

var _host: Node
var _date_label: Label
var _phase_label: Label
var _speed_buttons: Array[Button] = []
var _stat_labels := {}
var _loading_label: Label
var _hover_label: Label
var _debug_label: Label
var _overlay_button: Button
var _notice_label: Label
var _notice_timer := 0.0
var _start_speed := 1
var _smoke_seconds := -1.0
var _smoke_elapsed := 0.0
var _screenshot := ""
var _started := false


func _ready() -> void:
	_host = get_node(host_path)
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--speed="): _start_speed = int(arg.trim_prefix("--speed="))
		elif arg == "--game-smoke": _smoke_seconds = 4.0
		elif arg.begins_with("--game-smoke="): _smoke_seconds = float(arg.trim_prefix("--game-smoke="))
		elif arg.begins_with("--screenshot="): _screenshot = arg.trim_prefix("--screenshot=")
	_build_top_bar()
	_build_build_menu()
	_build_right_panel()
	_build_ticker()
	_loading_label = _label(22, "Loading…")
	_anchor(_loading_label, 0.5, 0.5, 0.5, 0.5)
	_loading_label.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_loading_label.grow_vertical = Control.GROW_DIRECTION_BOTH
	_loading_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_loading_label)
	_hover_label = _label(13)
	add_child(_hover_label)


func _process(delta: float) -> void:
	var hud: Dictionary = _host.GetHud()
	var ready: bool = hud["ready"]
	_loading_label.visible = not ready
	if not ready:
		_loading_label.text = hud["load_status"]
		return
	if not _started:
		_started = true
		_host.SetSpeedIndex(_start_speed)

	_date_label.text = "%s   %s" % [hud["date"], hud["time"]]
	var phase: String = hud["phase"]
	if hud["events"] != "": phase += "  ·  " + hud["events"]
	_phase_label.text = phase
	_phase_label.tooltip_text = "Academic calendar (§6.2). Dates are approximate%s." % ("" if hud["calendar_verified"] else " and not yet verified")
	var speed_index: int = hud["speed_index"]
	for i in _speed_buttons.size():
		_speed_buttons[i].button_pressed = i == speed_index
	_stat_labels["enrollment"].text = "%s students · %s faculty" % [_thousands(hud["students"]), _thousands(hud["faculty"])]
	_stat_labels["happiness"].text = "%d%%" % roundi(hud["happiness"])

	var hover: String = hud["hover"]
	_hover_label.visible = hover != ""
	if hover != "":
		_hover_label.text = hover
		_hover_label.position = get_viewport().get_mouse_position() + Vector2(16, 12)

	_debug_label.text = "sim p95 %.2f ms · %s walkers drawn · GPU %.2f ms · %d FPS%s" % [
		hud["tick_ms_p95"], _thousands(hud["walkers_drawn"]), hud["gpu_ms"], Engine.get_frames_per_second(),
		"" if hud["dropped_ticks"] == 0 else " · %d ticks dropped" % hud["dropped_ticks"]]

	if _notice_timer > 0.0:
		_notice_timer -= delta
		if _notice_timer <= 0.0: _notice_label.text = "No notifications yet."

	if _smoke_seconds >= 0.0:
		_smoke_elapsed += delta
		if _smoke_elapsed >= _smoke_seconds:
			hud["fps"] = Engine.get_frames_per_second()
			print("GAME_SMOKE %s" % JSON.stringify(hud))
			if _screenshot != "":
				get_viewport().get_texture().get_image().save_png(_screenshot)
			get_tree().quit()


func _unhandled_key_input(event: InputEvent) -> void:
	if not event.pressed or event.echo:
		return
	match event.physical_keycode:
		KEY_SPACE: _host.TogglePause()
		KEY_1: _host.SetSpeedIndex(1)
		KEY_2: _host.SetSpeedIndex(2)
		KEY_3: _host.SetSpeedIndex(3)
		KEY_4: _host.SetSpeedIndex(4)
		KEY_5: _notify("Skip to next event comes with events (checkpoint 1j).")
		KEY_O: _cycle_overlay()
		KEY_G: _host.ToggleGrid()
		KEY_B: _notify("Building comes in checkpoint 1e.")
		KEY_ESCAPE: get_tree().change_scene_to_file("res://scenes/boot.tscn")


# ---------------- layout ----------------

func _build_top_bar() -> void:
	var bar := _panel()
	_anchor(bar, 0, 0, 1, 0, 8, 8, -8, 8)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 18)
	bar.add_child(row)

	var when := VBoxContainer.new()
	when.custom_minimum_size = Vector2(300, 0)
	_date_label = _label(17)
	_phase_label = _label(13)
	_phase_label.mouse_filter = Control.MOUSE_FILTER_PASS
	when.add_child(_date_label)
	when.add_child(_phase_label)
	row.add_child(when)

	var speeds := HBoxContainer.new()
	for i in SPEED_LABELS.size():
		var b := Button.new()
		b.text = SPEED_LABELS[i]
		b.toggle_mode = true
		b.focus_mode = Control.FOCUS_NONE
		b.custom_minimum_size = Vector2(52, 32)
		b.tooltip_text = "Space" if i == 0 else "Key %d" % i
		b.pressed.connect(func(): _host.SetSpeedIndex(i))
		speeds.add_child(b)
		_speed_buttons.append(b)
	var skip := Button.new()
	skip.text = "Next event"
	skip.disabled = true
	skip.focus_mode = Control.FOCUS_NONE
	skip.tooltip_text = "Key 5. Skip to the next event comes with events (checkpoint 1j)."
	speeds.add_child(skip)
	row.add_child(speeds)

	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(spacer)

	_add_stat(row, "cash", "Cash", "—", "Budget and money come in checkpoint 1i.")
	_add_stat(row, "enrollment", "Enrollment", "—", "Everyone simulated on the real campus. Admissions and enrollment change come in 1h.")
	_add_stat(row, "happiness", "Happiness", "—", "Average student and faculty happiness from their needs (§10.1).")
	_add_stat(row, "reputation", "Reputation", "—", "Rankings come later (Phase 2).")
	_add_stat(row, "trustees", "Trustee Confidence", "—", "Trustee Confidence comes with the budget and Chapter 1 goals (1i, 1j).")


func _build_build_menu() -> void:
	var panel := _panel()
	_anchor(panel, 0, 0.5, 0, 0.5, 8, 0, 8, 0)
	panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	var col := VBoxContainer.new()
	panel.add_child(col)
	col.add_child(_label(14, "Build"))
	for category in BUILD_CATEGORIES:
		var b := Button.new()
		b.text = category
		b.disabled = true
		b.focus_mode = Control.FOCUS_NONE
		b.tooltip_text = "Building comes in checkpoint 1e (key B)."
		b.custom_minimum_size = Vector2(150, 30)
		col.add_child(b)


func _build_right_panel() -> void:
	var panel := _panel()
	_anchor(panel, 1, 0.5, 1, 0.5, -8, 0, -8, 0)
	panel.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	var col := VBoxContainer.new()
	col.custom_minimum_size = Vector2(240, 0)
	panel.add_child(col)
	_overlay_button = Button.new()
	_overlay_button.text = "Overlays: None (O)"
	_overlay_button.focus_mode = Control.FOCUS_NONE
	_overlay_button.tooltip_text = "Foot-traffic heatmap. Desire paths worn into the lawns are always shown."
	_overlay_button.pressed.connect(_cycle_overlay)
	col.add_child(_overlay_button)
	col.add_child(_label(14, "Demand"))
	var demand := _label(12, "Demand bars come with building (1e).")
	demand.modulate = Color(1, 1, 1, 0.6)
	col.add_child(demand)
	col.add_child(_label(14, "Notifications"))
	_notice_label = _label(12, "No notifications yet.")
	_notice_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_notice_label.custom_minimum_size = Vector2(240, 0)
	col.add_child(_notice_label)


func _build_ticker() -> void:
	var bar := _panel()
	_anchor(bar, 0, 1, 1, 1, 8, -8, -8, -8)
	bar.grow_vertical = Control.GROW_DIRECTION_BEGIN
	var row := HBoxContainer.new()
	bar.add_child(row)
	var ticker := _label(13, "News ticker: headlines come with events (checkpoint 1j).")
	ticker.modulate = Color(1, 1, 1, 0.6)
	ticker.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(ticker)
	_debug_label = _label(12)
	_debug_label.modulate = Color(1, 1, 1, 0.6)
	row.add_child(_debug_label)


func _add_stat(row: HBoxContainer, key: String, title: String, value: String, tooltip: String) -> void:
	var box := VBoxContainer.new()
	box.tooltip_text = tooltip
	box.mouse_filter = Control.MOUSE_FILTER_PASS
	var t := _label(11, title)
	t.modulate = Color(1, 1, 1, 0.65)
	var v := _label(16, value)
	box.add_child(t)
	box.add_child(v)
	row.add_child(box)
	_stat_labels[key] = v


# ---------------- actions ----------------

func _cycle_overlay() -> void:
	var name: String = _host.CycleOverlay()
	_overlay_button.text = "Overlays: %s (O)" % name


func _notify(text: String) -> void:
	_notice_label.text = text
	_notice_timer = 4.0


# ---------------- helpers ----------------

## Anchors (fractions of the screen) and offsets (pixels) in one go; the control grows from there to fit its content.
func _anchor(c: Control, al: float, at: float, ar: float, ab: float, ol := 0.0, ot := 0.0, orr := 0.0, ob := 0.0) -> void:
	c.anchor_left = al; c.anchor_top = at; c.anchor_right = ar; c.anchor_bottom = ab
	c.offset_left = ol; c.offset_top = ot; c.offset_right = orr; c.offset_bottom = ob

func _panel() -> PanelContainer:
	var panel := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.08, 0.08, 0.1, 0.8)
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


func _thousands(n: int) -> String:
	var s := str(abs(n))
	var out := ""
	while s.length() > 3:
		out = "," + s.substr(s.length() - 3) + out
		s = s.substr(0, s.length() - 3)
	return ("-" if n < 0 else "") + s + out
