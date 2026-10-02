extends CanvasLayer
## Overlay for the 1f building-kit check: stats, N toggles night (window glow), Esc back to the menu.
## Launch options (after `--`): `--night`, `--camera=x,z,distance,pitch,yaw`, `--kit-smoke[=seconds]` (print stats and
## quit), `--screenshot=<file.png>` (with --kit-smoke).

@export var host_path: NodePath

var _host: Node
var _label: Label
var _smoke := -1.0
var _elapsed := 0.0
var _screenshot := ""


func _ready() -> void:
	_host = get_node(host_path)
	_label = Label.new()
	_label.position = Vector2(12, 10)
	_label.add_theme_font_size_override("font_size", 14)
	_label.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.9))
	_label.add_theme_constant_override("outline_size", 6)
	add_child(_label)
	for arg in OS.get_cmdline_user_args():
		if arg == "--kit-smoke": _smoke = 3.0
		elif arg.begins_with("--kit-smoke="): _smoke = float(arg.trim_prefix("--kit-smoke="))
		elif arg.begins_with("--screenshot="): _screenshot = arg.trim_prefix("--screenshot=")


func _process(delta: float) -> void:
	_label.text = "Building kit check (Phase 1 1f) · Esc menu\n" + _host.GetStats()
	if _smoke >= 0.0:
		_elapsed += delta
		if _elapsed >= _smoke:
			print("KIT_SMOKE " + _host.GetStats().replace("\n", " | "))
			if _screenshot != "":
				get_viewport().get_texture().get_image().save_png(_screenshot)
			get_tree().quit()


func _unhandled_key_input(event: InputEvent) -> void:
	if not event.pressed or event.echo:
		return
	match event.physical_keycode:
		KEY_N: _host.SetNight(not _host.GetNight())
		KEY_ESCAPE: get_tree().change_scene_to_file("res://scenes/boot.tscn")
