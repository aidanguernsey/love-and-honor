extends Control
## Boot screen for Phase 0: proves data loading (branding.json), the GDScript -> C# bridge,
## and the Godot-independent sim library are wired together. Not gameplay.

const BRANDING_PATH := "res://data/branding.json"

@onready var _title: Label = %Title
@onready var _subtitle: Label = %Subtitle
@onready var _disclaimer: Label = %Disclaimer
@onready var _sim_info: Label = %SimInfo
@onready var _bridge: Node = $SimBridge
@onready var _play: Button = %Play
@onready var _preview: Button = %Preview2026
@onready var _spike_a: Button = %SpikeA
@onready var _spike_b: Button = %SpikeB
@onready var _art_test: Button = %ArtTest
@onready var _kit_test: Button = %KitTest


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
	_play.pressed.connect(func(): _start_game("chapter1_the_hill"))
	_preview.pressed.connect(func(): _start_game("preview_2026"))
	_spike_a.pressed.connect(func(): get_tree().change_scene_to_file("res://scenes/spikes/population_spike.tscn"))
	_spike_b.pressed.connect(func(): get_tree().change_scene_to_file("res://scenes/spikes/terrain_spike.tscn"))
	_art_test.pressed.connect(func(): get_tree().change_scene_to_file("res://scenes/spikes/art_import_test.tscn"))
	_add_save_buttons()
	_kit_test.pressed.connect(func(): get_tree().change_scene_to_file("res://scenes/spikes/building_kit_test.tscn"))

	if "--smoke-test" in OS.get_cmdline_user_args():
		print("SMOKE title=%s | university=%s | branding=%s | sim=%s" % [
			_title.text, uni["name"], branding["profile_id"], _sim_info.text])
		get_tree().quit()
	elif "--play" in OS.get_cmdline_user_args():
		# Straight into the game (exported release builds can't take a scene path on the command line).
		_start_game.call_deferred("chapter1_the_hill")


## Saved games (§31): "Continue" loads the newest; "Load a saved game" lists them all.
func _add_save_buttons() -> void:
	var saves: Array = _bridge.ListSaves()
	if saves.is_empty():
		return
	var box := _play.get_parent()
	var latest: Dictionary = saves[0]
	var cont := Button.new()
	cont.text = "Continue — %s, %s (%s)" % [latest["name"], latest["date"], latest["scenario"]]
	cont.custom_minimum_size = Vector2(320, 48)
	cont.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	cont.pressed.connect(func(): _load(latest["path"]))
	box.add_child(cont)
	box.move_child(cont, _play.get_index())
	var load := Button.new()
	load.text = "Load a saved game…"
	load.custom_minimum_size = Vector2(320, 40)
	load.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	box.add_child(load)
	box.move_child(load, _play.get_index())
	var list := VBoxContainer.new()
	list.visible = false
	box.add_child(list)
	box.move_child(list, _play.get_index())
	load.pressed.connect(func(): list.visible = not list.visible)
	for s in saves:
		var b := Button.new()
		b.text = "%s — %s · %d students · %s · saved %s" % [s["name"], s["date"], s["students"], s["cash"], s["saved_at"]]
		b.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		var path: String = s["path"]
		b.pressed.connect(func(): _load(path))
		list.add_child(b)


func _load(path: String) -> void:
	get_tree().root.set_meta("load_save", path)
	get_tree().change_scene_to_file("res://scenes/game/game.tscn")


## Scenario ids are data/scenarios/<id>.json; the game scene reads the choice from the root's "scenario" meta
## (a `--scenario=<id>` launch option overrides it).
func _start_game(scenario: String) -> void:
	get_tree().root.set_meta("scenario", scenario)
	get_tree().change_scene_to_file("res://scenes/game/game.tscn")


func _load_json(path: String) -> Dictionary:
	var text := FileAccess.get_file_as_string(path)
	if text.is_empty():
		return {}
	var parsed = JSON.parse_string(text)
	return parsed if parsed is Dictionary else {}
