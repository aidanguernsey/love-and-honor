extends CanvasLayer
## Main HUD (§27.1), Phase 1 checkpoints 1c–1e. Systems that don't exist yet show "—" with a tooltip saying which
## checkpoint brings them. Keys (§27.5): Space pause/resume · 1–4 speed (1×/2×/4×/8×) · 5 skip to next event (reserved)
## · O overlays · G build grid · C clear forest · L buy land · P lay path · Shift+P remove path · V pave a desire path
## · B build menu · Z/X turn the building · Del cancel
## construction · H Heritage sites · N day/night cycle · Esc stop tool / close menu / main menu.
## The day/night and Heritage-site settings are remembered in user://settings.cfg (`--day-night=on|off` overrides
## the first for one run). More launch options: `--cash=N` (starting cash), `--demo-land`, `--demo-build` (GameHost.cs), `--build-menu` (open the build list), `--budget` (open the budget), `--codex` (open the History Book).
## Launch options (after `--`; start from the boot scene with `--play`): `--speed=N` (0–4), `--camera=...` (see camera_rig.gd), `--game-smoke[=seconds]`
## (print GAME_SMOKE stats once the sim has run that long, then quit), `--screenshot=<file.png>` (with --game-smoke).

const BUILD_CATEGORIES := ["Heritage Projects", "Academic", "Housing", "Dining", "Student Life", "Athletics", "Admin / Utilities", "Landscape", "Landmarks"]
const SPEED_LABELS := ["Pause", "1×", "2×", "4×", "8×"]
const SETTINGS_PATH := "user://settings.cfg"

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
var _notice_text := ""
var _scenario_label: Label
var _tool_buttons := {}
var _tool_hint: Label
var _day_night_button: CheckButton
var _heritage_button: CheckButton
var _demand_label: Label
var _budget_panel: PanelContainer
var _budget_label: Label
var _tuition_label: Label
var _dismissed_label: Label
var _goals_label: Label
var _ticker_label: Label
var _card_panel: PanelContainer
var _card_title: Label
var _card_date: Label
var _card_text: Label
var _card_sources: Label
var _card_seq := 0
var _speed_before_card := 1
var _codex_panel: PanelContainer
var _codex_list: VBoxContainer
var _codex_text: Label
var _outcome_shown := false
var _codex_refresh := 0.0
var _codex_selected := ""
var _outcome_buttons: HBoxContainer
var _category_buttons := {}
var _build_popup: PanelContainer
var _build_list: VBoxContainer
var _build_title: Label
var _build_category := ""
var _build_refresh := 0.0
var _settings := ConfigFile.new()
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
	_build_budget_panel()
	_build_card_and_codex()
	_loading_label = _label(22, "Loading…")
	_anchor(_loading_label, 0.5, 0.5, 0.5, 0.5)
	_loading_label.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_loading_label.grow_vertical = Control.GROW_DIRECTION_BOTH
	_loading_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_loading_label)
	_hover_label = _label(13)
	add_child(_hover_label)
	_tool_hint = _label(15)
	_anchor(_tool_hint, 0.5, 1, 0.5, 1, 0, -64, 0, -64)
	_tool_hint.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_tool_hint.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_tool_hint.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.9))
	_tool_hint.add_theme_constant_override("outline_size", 6)
	add_child(_tool_hint)
	_settings.load(SETTINGS_PATH)
	var day_night: bool = _settings.get_value("view", "day_night_cycle", true)
	if "--day-night=off" in OS.get_cmdline_user_args(): day_night = false  # testing; not saved
	elif "--day-night=on" in OS.get_cmdline_user_args(): day_night = true
	_day_night_button.set_pressed_no_signal(day_night)
	_host.SetDayNight(day_night)
	var heritage: bool = _settings.get_value("view", "heritage_sites", true)
	_heritage_button.set_pressed_no_signal(heritage)
	_host.SetShowHeritage(heritage)


func _process(delta: float) -> void:
	var hud: Dictionary = _host.GetHud()
	var ready: bool = hud["ready"]
	_loading_label.visible = not ready
	_scenario_label.text = hud["scenario"]
	if not ready:
		_loading_label.text = hud["load_status"]
		return
	if not _started:
		_started = true
		_host.SetSpeedIndex(_start_speed)
		if "--build-menu" in OS.get_cmdline_user_args(): _open_build_list("")  # screenshots
		if "--budget" in OS.get_cmdline_user_args(): _toggle_budget()  # screenshots
		for arg in OS.get_cmdline_user_args():  # screenshots: --codex or --codex=<entry id>
			if arg == "--codex" or arg.begins_with("--codex="):
				_codex_selected = arg.trim_prefix("--codex").trim_prefix("=")
				_toggle_codex()

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
	_stat_labels["cash"].text = hud["cash"]
	_stat_labels["trustees"].text = hud["confidence"]
	if _budget_panel.visible:
		_budget_label.text = hud["budget"]
		_tuition_label.text = hud["tuition"]
	if _codex_panel.visible:
		_codex_refresh -= delta
		if _codex_refresh <= 0.0: _fill_codex()
	_goals_label.text = hud["goals"]
	_goals_label.visible = hud["goals"] != ""
	if hud["ticker"] != "": _ticker_label.text = hud["ticker"]
	var seq: int = hud["card_seq"]
	if seq > _card_seq:
		_card_seq = seq
		_show_card(hud)
	if hud["outcome"] != "Playing" and not _outcome_shown:
		_outcome_shown = true
		_dismissed_label.text = hud["outcome_text"]
		_dismissed_label.visible = true
		_outcome_buttons.visible = true
		_host.SetSpeedIndex(0)
	_overlay_button.text = "Overlays: %s (O)" % hud["overlay"]
	_demand_label.text = hud["demand"]

	var tool: String = hud["tool"]
	for key in _tool_buttons:
		_tool_buttons[key].set_pressed_no_signal(key == tool)
	_tool_hint.text = hud["tool_hint"]
	_tool_hint.visible = tool != ""
	if _build_popup.visible:
		_build_refresh -= delta
		if _build_refresh <= 0.0: _fill_build_list()

	var notes := PackedStringArray()
	if _notice_timer > 0.0: notes.append(_notice_text)
	if hud["clearing_tiles"] > 0: notes.append("Clearing in progress: %d tiles left." % hud["clearing_tiles"])
	if hud["construction"] != "": notes.append(hud["construction"])
	if hud["messages"] != "": notes.append(hud["messages"])
	_notice_label.text = "\n".join(notes) if notes.size() > 0 else "No notifications yet."

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
		KEY_B: _open_build_list("")
		KEY_C: _toggle_tool("clear")
		KEY_P: _toggle_tool("remove_path" if event.shift_pressed else "path")
		KEY_V: _toggle_tool("pave")
		KEY_L: _toggle_tool("buy")
		KEY_DELETE, KEY_BACKSPACE: _toggle_tool("cancel")
		KEY_Z: _host.RotateBuild(-1)
		KEY_X: _host.RotateBuild(1)
		KEY_H: _heritage_button.button_pressed = not _heritage_button.button_pressed
		KEY_Y: _toggle_budget()
		KEY_K: _toggle_codex()
		KEY_N: _day_night_button.button_pressed = not _day_night_button.button_pressed
		KEY_ESCAPE:
			if _card_panel.visible: _close_card()
			elif _codex_panel.visible: _codex_panel.visible = false
			elif _budget_panel.visible: _budget_panel.visible = false
			elif _build_popup.visible: _build_popup.visible = false
			elif _host.GetTool() != "": _host.SetTool("")
			else: get_tree().change_scene_to_file("res://scenes/boot.tscn")


# ---------------- layout ----------------

func _build_top_bar() -> void:
	var bar := _panel()
	_anchor(bar, 0, 0, 1, 0, 8, 8, -8, 8)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 18)
	bar.add_child(row)

	var when := VBoxContainer.new()
	when.custom_minimum_size = Vector2(300, 0)
	_scenario_label = _label(11)
	_scenario_label.modulate = Color(1, 1, 1, 0.65)
	when.add_child(_scenario_label)
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

	_add_stat(row, "cash", "Cash", "—", "Operating cash (§8.1). Spent on clearing and buying land; income and the budget come in 1i.")
	_add_stat(row, "enrollment", "Enrollment", "—", "Everyone is simulated. Students arrive at move-in (August), graduate or leave at commencement (May), and are away over the summer and winter breaks. Details under Demand.")
	_add_stat(row, "happiness", "Happiness", "—", "Average student and faculty happiness from their needs (§10.1).")
	_add_stat(row, "reputation", "Reputation", "—", "Rankings come later (Phase 2).")
	_add_stat(row, "trustees", "Trustee Confidence", "—", "Trustee Confidence (0-100, §3): reviewed every August 1 (balanced budget and growing enrollment raise it); falls whenever cash runs out. At 0 the Trustees dismiss you. Budget: Y.")
	var budget_button := Button.new()
	budget_button.text = "Budget (Y)"
	budget_button.focus_mode = Control.FOCUS_NONE
	budget_button.pressed.connect(_toggle_budget)
	row.add_child(budget_button)


func _build_build_menu() -> void:
	var panel := _panel()
	_anchor(panel, 0, 0.5, 0, 0.5, 8, 0, 8, 0)
	panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	var col := VBoxContainer.new()
	panel.add_child(col)
	col.add_child(_label(14, "Land"))
	for spec in [["clear", "Clear forest (C)", "Clear university-owned woods: drag a rectangle. Costs money and takes days of work (slower in winter)."],
			["buy", "Buy land (L)", "Buy land next to the campus: drag a rectangle. Town land costs far more and annoys the town."],
			["path", "Lay path (P)", "Drag from one end to the other: the route goes round obstacles and joins existing paths. Dirt in the 1820s, gravel, then brick."],
			["remove_path", "Remove path (Shift+P)", "Drag over footpaths on university land to remove them (roads stay)."],
			["pave", "Pave desire path (V)", "Click a shortcut students have worn into the lawn to pave it. A long diagonal becomes the Slant Walk."]]:
		var t := Button.new()
		t.text = spec[1]
		t.tooltip_text = spec[2]
		t.toggle_mode = true
		t.focus_mode = Control.FOCUS_NONE
		t.custom_minimum_size = Vector2(150, 30)
		var key: String = spec[0]
		t.pressed.connect(func(): _toggle_tool(key))
		col.add_child(t)
		_tool_buttons[key] = t
	col.add_child(_label(14, "Build (B)"))
	for category in BUILD_CATEGORIES:
		var b := Button.new()
		b.text = category
		b.focus_mode = Control.FOCUS_NONE
		b.custom_minimum_size = Vector2(150, 30)
		var c: String = category
		b.pressed.connect(func(): _open_build_list(c))
		col.add_child(b)
		_category_buttons[category] = b
	var cancel := Button.new()
	cancel.text = "Cancel construction (Del)"
	cancel.tooltip_text = "Click a building under construction to cancel it. Half of the unspent part is refunded (all of it on the day it was ordered)."
	cancel.toggle_mode = true
	cancel.focus_mode = Control.FOCUS_NONE
	cancel.custom_minimum_size = Vector2(150, 30)
	cancel.pressed.connect(func(): _toggle_tool("cancel"))
	col.add_child(cancel)
	_tool_buttons["cancel"] = cancel

	# The list of buildings in a category, beside the menu.
	_build_popup = _panel()
	_anchor(_build_popup, 0, 0.5, 0, 0.5, 240, 0, 240, 0)
	_build_popup.grow_vertical = Control.GROW_DIRECTION_BOTH
	_build_popup.visible = false
	var list_col := VBoxContainer.new()
	list_col.custom_minimum_size = Vector2(300, 0)
	_build_popup.add_child(list_col)
	_build_title = _label(14)
	list_col.add_child(_build_title)
	_build_list = VBoxContainer.new()
	list_col.add_child(_build_list)


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
	_overlay_button.tooltip_text = "Ownership (university / town land) and the foot-traffic heatmap. Desire paths worn into the lawns are always shown."
	_overlay_button.pressed.connect(_cycle_overlay)
	col.add_child(_overlay_button)
	_goals_label = _label(13)
	_goals_label.tooltip_text = "Chapter goals (§4.1). Old Miami reached 250 students in 1839."
	_goals_label.mouse_filter = Control.MOUSE_FILTER_PASS
	col.add_child(_label(14, "Goals"))
	col.add_child(_goals_label)
	_day_night_button = CheckButton.new()
	_day_night_button.text = "Day/night cycle (N)"
	_day_night_button.button_pressed = true
	_day_night_button.focus_mode = Control.FOCUS_NONE
	_day_night_button.tooltip_text = "Off: always early afternoon light. The clock and the simulation keep running."
	_day_night_button.toggled.connect(_set_day_night)
	col.add_child(_day_night_button)
	_heritage_button = CheckButton.new()
	_heritage_button.text = "Heritage sites (H)"
	_heritage_button.button_pressed = true
	_heritage_button.focus_mode = Control.FOCUS_NONE
	_heritage_button.tooltip_text = "Outlines where real Miami buildings on offer as Heritage Projects stood. Always shown while building."
	_heritage_button.toggled.connect(_set_heritage)
	col.add_child(_heritage_button)
	col.add_child(_label(14, "Demand"))
	var demand := _label(12, "")
	demand.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	demand.custom_minimum_size = Vector2(240, 0)
	_demand_label = demand
	demand.modulate = Color(1, 1, 1, 0.9)
	col.add_child(demand)
	col.add_child(_label(14, "Notifications"))
	_notice_label = _label(12, "No notifications yet.")
	_notice_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_notice_label.custom_minimum_size = Vector2(240, 0)
	col.add_child(_notice_label)


func _build_budget_panel() -> void:
	_budget_panel = _panel()
	_anchor(_budget_panel, 0.5, 0.5, 0.5, 0.5)
	_budget_panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_budget_panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	_budget_panel.visible = false
	var col := VBoxContainer.new()
	col.custom_minimum_size = Vector2(460, 0)
	_budget_panel.add_child(col)
	col.add_child(_label(18, "Budget (§8)"))
	_budget_label = _label(13)
	_budget_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_budget_label.custom_minimum_size = Vector2(460, 0)
	col.add_child(_budget_label)
	var row := HBoxContainer.new()
	_tuition_label = _label(14)
	_tuition_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(_tuition_label)
	for spec in [["−", -1], ["+", 1]]:
		var b := Button.new()
		b.text = spec[0]
		b.custom_minimum_size = Vector2(36, 30)
		b.focus_mode = Control.FOCUS_NONE
		b.tooltip_text = "Higher tuition brings in more per student but fewer applicants (from the next move-in)."
		var d: int = spec[1]
		b.pressed.connect(func(): _host.ChangeTuition(d))
		row.add_child(b)
	col.add_child(row)
	var close := Button.new()
	close.text = "Close (Y / Esc)"
	close.focus_mode = Control.FOCUS_NONE
	close.pressed.connect(_toggle_budget)
	col.add_child(close)

	_dismissed_label = _label(26, "The Trustees have lost confidence and dismissed you.\nThe chapter ends here (game over screens come with Chapter 1, 1j).")
	_anchor(_dismissed_label, 0.5, 0.3, 0.5, 0.3)
	_dismissed_label.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_dismissed_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_dismissed_label.add_theme_color_override("font_outline_color", Color(0, 0, 0, 1))
	_dismissed_label.add_theme_constant_override("outline_size", 10)
	_dismissed_label.visible = false
	add_child(_dismissed_label)


func _build_card_and_codex() -> void:
	# Event card (§23): pauses the game until read.
	_card_panel = _panel()
	_anchor(_card_panel, 0.5, 0.42, 0.5, 0.42)
	_card_panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_card_panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	_card_panel.visible = false
	_opaque(_card_panel)
	var col := VBoxContainer.new()
	col.custom_minimum_size = Vector2(520, 0)
	_card_panel.add_child(col)
	_card_date = _label(13)
	_card_date.modulate = Color(1, 1, 1, 0.7)
	col.add_child(_card_date)
	_card_title = _label(22)
	col.add_child(_card_title)
	_card_text = _label(15)
	_card_text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_card_text.custom_minimum_size = Vector2(520, 0)
	col.add_child(_card_text)
	_card_sources = _label(11)
	_card_sources.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_card_sources.custom_minimum_size = Vector2(520, 0)
	_card_sources.modulate = Color(1, 1, 1, 0.65)
	col.add_child(_card_sources)
	var ok := Button.new()
	ok.text = "Continue (Esc)"
	ok.focus_mode = Control.FOCUS_NONE
	ok.pressed.connect(_close_card)
	col.add_child(ok)

	# History Book (§19.3).
	_codex_panel = _panel()
	_anchor(_codex_panel, 0.5, 0.5, 0.5, 0.5)
	_codex_panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_codex_panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	_codex_panel.visible = false
	_opaque(_codex_panel)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 14)
	_codex_panel.add_child(row)
	var left := VBoxContainer.new()
	left.add_child(_label(18, "History Book"))
	_codex_list = VBoxContainer.new()
	left.add_child(_codex_list)
	var close := Button.new()
	close.text = "Close (K / Esc)"
	close.focus_mode = Control.FOCUS_NONE
	close.pressed.connect(_toggle_codex)
	left.add_child(close)
	row.add_child(left)
	_codex_text = _label(14, "Pick an entry.")
	_codex_text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_codex_text.custom_minimum_size = Vector2(460, 300)
	row.add_child(_codex_text)

	# Chapter end: keep playing (open-ended) or back to the menu.
	_outcome_buttons = HBoxContainer.new()
	_anchor(_outcome_buttons, 0.5, 0.42, 0.5, 0.42)
	_outcome_buttons.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_outcome_buttons.visible = false
	var keep := Button.new()
	keep.text = "Keep playing"
	keep.pressed.connect(func():
		_outcome_buttons.visible = false
		_dismissed_label.visible = false
		_host.SetSpeedIndex(1))
	_outcome_buttons.add_child(keep)
	var menu := Button.new()
	menu.text = "Main menu"
	menu.pressed.connect(func(): get_tree().change_scene_to_file("res://scenes/boot.tscn"))
	_outcome_buttons.add_child(menu)
	add_child(_outcome_buttons)


func _opaque(panel: PanelContainer) -> void:
	var style: StyleBoxFlat = panel.get_theme_stylebox("panel")
	style.bg_color.a = 0.97


func _show_card(hud: Dictionary) -> void:
	_card_date.text = hud["card_date"]
	_card_title.text = hud["card_title"]
	_card_text.text = hud["card_text"] + ("\n\n" + hud["card_codex"] if hud["card_codex"] != "" else "")
	_card_sources.text = hud["card_sources"]
	if not _card_panel.visible:
		_speed_before_card = hud["speed_index"]
	_card_panel.visible = true
	if not "--cards-no-pause" in OS.get_cmdline_user_args():  # testing / screenshots
		_host.SetSpeedIndex(0)


func _close_card() -> void:
	_card_panel.visible = false
	if _speed_before_card > 0: _host.SetSpeedIndex(_speed_before_card)


func _toggle_codex() -> void:
	_codex_panel.visible = not _codex_panel.visible
	if _codex_panel.visible: _fill_codex()


## Rebuilds the History Book list (entries unlock while it's open) and shows the selected entry.
func _fill_codex() -> void:
	_codex_refresh = 1.0
	for child in _codex_list.get_children():
		child.queue_free()
	for entry in _host.GetCodex():
		var b := Button.new()
		b.text = "%s  (%s)" % [entry["title"], entry["years"]]
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.focus_mode = Control.FOCUS_NONE
		b.disabled = not entry["unlocked"]
		var e: Dictionary = entry
		b.pressed.connect(func():
			_codex_selected = e["id"]
			_codex_text.text = "%s\n%s\n\n%s\n\n%s" % [e["title"], e["years"], e["text"], e["sources"]])
		_codex_list.add_child(b)
		if e["id"] == _codex_selected and e["unlocked"]:
			_codex_text.text = "%s\n%s\n\n%s\n\n%s" % [e["title"], e["years"], e["text"], e["sources"]]


func _toggle_budget() -> void:
	_budget_panel.visible = not _budget_panel.visible


func _build_ticker() -> void:
	var bar := _panel()
	_anchor(bar, 0, 1, 1, 1, 8, -8, -8, -8)
	bar.grow_vertical = Control.GROW_DIRECTION_BEGIN
	var row := HBoxContainer.new()
	bar.add_child(row)
	var ticker := _label(13, "")
	ticker.modulate = Color(1, 1, 1, 0.85)
	ticker.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(ticker)
	_ticker_label = ticker
	var book := Button.new()
	book.text = "History Book (K)"
	book.focus_mode = Control.FOCUS_NONE
	book.pressed.connect(_toggle_codex)
	row.add_child(book)
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
	_notice_text = text
	_notice_timer = 4.0


func _toggle_tool(tool: String) -> void:
	_build_popup.visible = false
	_host.SetTool("" if _host.GetTool() == tool else tool)


## Shows the buildings on offer now in one category ("" = all), or hides the list if it's already showing that.
func _open_build_list(category: String) -> void:
	if _build_popup.visible and _build_category == category:
		_build_popup.visible = false
		return
	_build_category = category
	_build_popup.visible = true
	_fill_build_list()


func _fill_build_list() -> void:
	_build_refresh = 1.0
	for child in _build_list.get_children():
		child.queue_free()
	var items: Array = _host.GetCatalog()
	var shown := 0
	var current: String = _host.GetBuildItem()
	for item in items:
		if _build_category != "" and item["group"] != _build_category:
			continue
		var b := Button.new()
		b.text = "%s\n%s · %s tiles · %d months" % [item["name"], item["cost"], item["size"], roundi(item["months"])]
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.tooltip_text = item["description"]
		b.focus_mode = Control.FOCUS_NONE
		b.toggle_mode = true
		b.set_pressed_no_signal(item["id"] == current)
		if not item["affordable"]:
			b.modulate = Color(1, 0.75, 0.7)
			b.tooltip_text += "\nNot enough money yet."
		var id: String = item["id"]
		b.pressed.connect(func(): _pick_building(id))
		_build_list.add_child(b)
		shown += 1
	_build_title.text = (_build_category if _build_category != "" else "Buildings") + " · %d on offer" % shown
	if shown == 0:
		var none := _label(12, "Nothing in this category in this era yet.")
		none.modulate = Color(1, 1, 1, 0.6)
		_build_list.add_child(none)


func _pick_building(id: String) -> void:
	_build_popup.visible = false
	_host.SetBuildItem(id)


func _set_heritage(on: bool) -> void:
	_host.SetShowHeritage(on)
	_settings.set_value("view", "heritage_sites", on)
	_settings.save(SETTINGS_PATH)


func _set_day_night(on: bool) -> void:
	_host.SetDayNight(on)
	_settings.set_value("view", "day_night_cycle", on)
	_settings.save(SETTINGS_PATH)


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
