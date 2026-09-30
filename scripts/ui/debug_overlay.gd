extends CanvasLayer
## Spike A debug overlay: sim tick time, agents simulated/rendered, FPS, in-game date/time, speed controls.
## Hotkeys: Space pause/resume · 1-4 = 1×/2×/4×/8× · O foot-traffic heatmap · Esc back to menu.
## Launch options (after `--`): `--spike-smoke` prints stats after a few seconds and quits (headless checks);
## `--speed=N` starts at speed index N (0 = pause … 4 = 8×).

const SMOKE_SECONDS := 6.0

@export var host_path: NodePath

var _host: Node
var _label: Label
var _speed_buttons: Array[Button] = []
var _resume_index := 1
var _elapsed := 0.0
var _smoke := false


func _ready() -> void:
	_host = get_node(host_path)
	_smoke = "--spike-smoke" in OS.get_cmdline_user_args()
	_build_ui()
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--speed="):
			_set_speed(int(arg.trim_prefix("--speed=")))


func _build_ui() -> void:
	var panel := PanelContainer.new()
	panel.position = Vector2(12, 12)
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.08, 0.08, 0.1, 0.78)
	style.set_content_margin_all(10)
	style.set_corner_radius_all(6)
	panel.add_theme_stylebox_override("panel", style)
	add_child(panel)

	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 8)
	panel.add_child(box)

	_label = Label.new()
	_label.add_theme_font_size_override("font_size", 14)
	box.add_child(_label)

	var row := HBoxContainer.new()
	box.add_child(row)
	var speeds: PackedFloat32Array = _host.GetSpeeds()
	for i in speeds.size():
		var b := Button.new()
		b.text = "Pause" if speeds[i] == 0.0 else "%d×" % int(speeds[i])
		b.toggle_mode = true
		b.focus_mode = Control.FOCUS_NONE
		b.custom_minimum_size = Vector2(56, 32)
		b.pressed.connect(_set_speed.bind(i))
		row.add_child(b)
		_speed_buttons.append(b)
	_sync_buttons()


func _process(delta: float) -> void:
	var s: Dictionary = _host.GetDebugStats()
	var over: bool = s["tick_ms_p95"] > s["budget_ms"]
	var lines := PackedStringArray([
		"Love & Honor — Spike A: population at scale",
		"%s   %s   speed %s" % [s["date"], s["time"], "paused" if s["speed"] == 0.0 else "%d×" % int(s["speed"])],
		"Sim tick: last %.2f ms · avg %.2f · p95 %.2f · max %.2f  (last %d ticks)  budget %.1f ms%s" % [
			s["tick_ms_last"], s["tick_ms_avg"], s["tick_ms_p95"], s["tick_ms_max"], s["ticks_in_window"],
			s["budget_ms"], "  ⚠ OVER" if over else ""],
		"Agents simulated: %s   rendered: %s   sim threads: %d" % [
			_thousands(s["agents_simulated"]), _thousands(s["agents_rendered"]), s["sim_threads"]],
		"FPS: %d   walks this hour: %s   avg happiness: %.1f" % [
			Engine.get_frames_per_second(), _thousands(s["walks_last_tick"]), s["avg_happiness"]],
		"Dropped ticks (sim behind): %s" % _thousands(s["dropped_ticks"]),
		"Sim build: %s" % ("optimized" if s["optimized"] else "DEBUG — timings pessimistic; use the benchmark for real numbers"),
		"Ground: %s" % ("foot-traffic heatmap" if s["heatmap"] else "desire-path wear on grass"),
		"[Space] pause  [1-4] speed  [O] heatmap  [Esc] menu",
		"Camera: WASD / middle-drag pan · wheel zoom · Q/E / right-drag orbit · R/F tilt",
	])
	_label.text = "\n".join(lines)
	_sync_buttons()

	if _smoke:
		_elapsed += delta
		if _elapsed >= SMOKE_SECONDS:
			s["fps"] = Engine.get_frames_per_second()
			print("SPIKE_A_SMOKE %s" % JSON.stringify(s))
			get_tree().quit()


func _unhandled_key_input(event: InputEvent) -> void:
	if not event.pressed or event.echo:
		return
	match event.physical_keycode:
		KEY_SPACE:
			var idx: int = _host.GetSpeedIndex()
			if idx == 0:
				_set_speed(_resume_index)
			else:
				_resume_index = idx
				_set_speed(0)
		KEY_1: _set_speed(1)
		KEY_2: _set_speed(2)
		KEY_3: _set_speed(3)
		KEY_4: _set_speed(4)
		KEY_O: _host.ToggleHeatmap()
		KEY_ESCAPE: get_tree().change_scene_to_file("res://scenes/boot.tscn")


func _set_speed(index: int) -> void:
	_host.SetSpeedIndex(index)
	if index != 0:
		_resume_index = index
	_sync_buttons()


func _sync_buttons() -> void:
	var idx: int = _host.GetSpeedIndex()
	for i in _speed_buttons.size():
		_speed_buttons[i].set_pressed_no_signal(i == idx)


func _thousands(n: int) -> String:
	var s := str(abs(n))
	var out := ""
	while s.length() > 3:
		out = "," + s.substr(s.length() - 3) + out
		s = s.substr(0, s.length() - 3)
	return ("-" if n < 0 else "") + s + out
