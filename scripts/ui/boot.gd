extends Control
## Boot screen for Phase 0: proves data loading (branding.json), the GDScript -> C# bridge,
## and the Godot-independent sim library are wired together. Not gameplay.

const BRANDING_PATH := "res://data/branding.json"

@onready var _title: Label = %Title
@onready var _subtitle: Label = %Subtitle
@onready var _disclaimer: Label = %Disclaimer
@onready var _sim_info: Label = %SimInfo
@onready var _bridge: Node = $SimBridge
@onready var _spike_a: Button = %SpikeA


func _ready() -> void:
	var branding := _load_json(BRANDING_PATH)
	if branding.is_empty():
		push_error("Could not load %s" % BRANDING_PATH)
		return

	var uni: Dictionary = branding["university"]
	_title.text = branding["game_title"]
	_title.add_theme_color_override("font_color", Color.html(branding["colors"]["primary"]))
	_subtitle.text = "%s · %s, %s · est. %d" % [uni["name"], uni["town"], uni["state"], uni["founded_year"]]
	_disclaimer.visible = branding["show_disclaimer"]
	_disclaimer.text = branding["disclaimer"]
	_sim_info.text = _bridge.GetSimDescription()
	_spike_a.pressed.connect(func(): get_tree().change_scene_to_file("res://scenes/spikes/population_spike.tscn"))

	if "--smoke-test" in OS.get_cmdline_user_args():
		print("SMOKE title=%s | university=%s | branding=%s | sim=%s" % [
			_title.text, uni["name"], branding["profile_id"], _sim_info.text])
		get_tree().quit()


func _load_json(path: String) -> Dictionary:
	var text := FileAccess.get_file_as_string(path)
	if text.is_empty():
		return {}
	var parsed = JSON.parse_string(text)
	return parsed if parsed is Dictionary else {}
