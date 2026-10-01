extends Node3D
## Orbit / pan / zoom camera for the map (§27.5 controls):
##   pan: WASD or middle-drag · zoom: mouse wheel · orbit: Q/E or right-drag (horizontal)
##   tilt: R/F or right-drag (vertical)
## The rig sits on the ground at the point the camera looks at (following terrain height if the host provides
## GetGroundHeight). Settings: data/rendering.json "camera".
## Keys are read as physical keys for now; remappable input actions come with the real HUD (§33).
## Launch option for reproducible screenshots: `-- --camera=x,z,distance,pitch,yaw` (metres / degrees).

@export var host_path: NodePath
## Scene overrides for the start view (0 = use the rendering.json camera start values).
@export var start_distance_m := 0.0
@export var start_pitch_deg := 0.0

var _cfg: Dictionary = {}
var _map_size := Vector2(4000, 4000)
var _yaw := 0.0
var _pitch := 55.0
var _distance := 900.0
var _drag_pan := false
var _drag_orbit := false
var _host: Node

@onready var _camera: Camera3D = $Camera3D


func _ready() -> void:
	var parsed = JSON.parse_string(FileAccess.get_file_as_string("res://data/rendering.json"))
	_cfg = parsed["camera"]
	_host = get_node_or_null(host_path)
	if _host:
		_map_size = _host.GetMapSizeMeters()
	_distance = start_distance_m if start_distance_m > 0.0 else float(_cfg["start_distance_m"])
	_pitch = start_pitch_deg if start_pitch_deg > 0.0 else float(_cfg["start_pitch_deg"])
	position = Vector3(_map_size.x * 0.5, 0.0, _map_size.y * 0.5)
	if _host and _host.has_method("GetStartFocus"):
		var focus: Vector2 = _host.GetStartFocus()
		position = Vector3(focus.x, 0.0, focus.y)
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--camera="):
			var v := arg.trim_prefix("--camera=").split_floats(",")
			if v.size() == 5:
				position = Vector3(v[0], 0.0, v[1])
				_distance = v[2]
				_pitch = v[3]
				_yaw = v[4]
	_apply()


func _process(delta: float) -> void:
	var move := Vector2.ZERO
	if Input.is_physical_key_pressed(KEY_W): move.y -= 1.0
	if Input.is_physical_key_pressed(KEY_S): move.y += 1.0
	if Input.is_physical_key_pressed(KEY_A): move.x -= 1.0
	if Input.is_physical_key_pressed(KEY_D): move.x += 1.0
	if move != Vector2.ZERO:
		_pan_local(move.normalized() * _distance * float(_cfg["keyboard_pan_speed"]) * delta)

	var orbit_speed: float = _cfg["orbit_speed_deg_per_s"]
	if Input.is_physical_key_pressed(KEY_Q): _yaw += orbit_speed * delta
	if Input.is_physical_key_pressed(KEY_E): _yaw -= orbit_speed * delta
	if Input.is_physical_key_pressed(KEY_R): _pitch += orbit_speed * delta
	if Input.is_physical_key_pressed(KEY_F): _pitch -= orbit_speed * delta
	_apply()


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		var zoom_step: float = _cfg["zoom_step"]
		match event.button_index:
			MOUSE_BUTTON_WHEEL_UP:
				if event.pressed: _distance /= zoom_step
			MOUSE_BUTTON_WHEEL_DOWN:
				if event.pressed: _distance *= zoom_step
			MOUSE_BUTTON_MIDDLE:
				_drag_pan = event.pressed
			MOUSE_BUTTON_RIGHT:
				_drag_orbit = event.pressed
	elif event is InputEventMouseMotion:
		if _drag_pan:
			# "Grab the ground": metres per pixel at the look-at point.
			var viewport_h: float = get_viewport().get_visible_rect().size.y
			var m_per_px := 2.0 * _distance * tan(deg_to_rad(_camera.fov * 0.5)) / viewport_h
			_pan_local(Vector2(-event.relative.x, -event.relative.y) * m_per_px)
		if _drag_orbit:
			var deg_per_px: float = _cfg["mouse_orbit_deg_per_px"]
			_yaw -= event.relative.x * deg_per_px
			_pitch += event.relative.y * deg_per_px


## Moves the rig in its own yaw frame: x = right, y = toward the viewer (negative y = forward).
func _pan_local(offset: Vector2) -> void:
	var basis_yaw := Basis(Vector3.UP, deg_to_rad(_yaw))
	position += basis_yaw.x * offset.x + basis_yaw.z * offset.y
	position.x = clampf(position.x, 0.0, _map_size.x)
	position.z = clampf(position.z, 0.0, _map_size.y)


func _apply() -> void:
	if _host and _host.has_method("GetGroundHeight"):
		position.y = _host.GetGroundHeight(position.x, position.z)
	_pitch = clampf(_pitch, _cfg["min_pitch_deg"], _cfg["max_pitch_deg"])
	_distance = clampf(_distance, _cfg["min_distance_m"], _cfg["max_distance_m"])
	rotation = Vector3(0.0, deg_to_rad(_yaw), 0.0)
	var p := deg_to_rad(_pitch)
	_camera.position = Vector3(0.0, sin(p) * _distance, cos(p) * _distance)
	_camera.rotation = Vector3(-p, 0.0, 0.0)
