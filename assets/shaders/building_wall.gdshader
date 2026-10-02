// Walls of kit-assembled buildings (§28.1, §28.1a, Phase 1 1f). "A subtle brick pattern on walls is the main
// exception" to untextured materials: drawn here in world space (no UVs needed), fading out with distance so it
// never shimmers. pattern 1 = brick (running bond), 2 = clapboard (painted horizontal boards), 0 = plain.
// Era looks are shader settings (§28.1a): COLOR.a = weathering (0 new … 1 old: darker, browner, grimy near the
// ground; COLOR.g = height above the floor / 10 m), written per building by the mesh builder. Mobile-renderer safe: no textures, no transparency.
shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_disabled;

uniform vec3 albedo : source_color = vec3(0.663, 0.29, 0.227);
uniform int pattern = 1;
uniform vec3 mortar_color : source_color = vec3(0.78, 0.74, 0.68);
uniform float brick_length = 0.215;
uniform float course_height = 0.075;
uniform float strength = 0.35;        // how visible the pattern is up close
uniform float fade_distance = 60.0;   // metres: the pattern is gone by here
uniform float board_height = 0.18;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void fragment() {
	vec3 base = albedo;
	float dist = length(VERTEX);
	float detail = (1.0 - smoothstep(fade_distance * 0.4, fade_distance, dist)) * strength;
	// Along-the-wall coordinate: whichever horizontal axis the wall runs along.
	float along = abs(world_normal.x) > abs(world_normal.z) ? world_pos.z : world_pos.x;
	bool vertical = abs(world_normal.y) < 0.7;
	if (pattern == 1 && vertical && detail > 0.001) {
		float course = floor(world_pos.y / course_height);
		float shifted = along / brick_length + 0.5 * mod(course, 2.0);
		vec2 cell = vec2(floor(shifted), course);
		vec2 f = vec2(fract(shifted), fract(world_pos.y / course_height));
		vec2 aa = fwidth(vec2(shifted, world_pos.y / course_height));
		float mortar = 1.0 - smoothstep(0.0, aa.x * 1.5 + 0.04, min(f.x, 1.0 - f.x)) * smoothstep(0.0, aa.y * 1.5 + 0.08, min(f.y, 1.0 - f.y));
		vec3 brick = base * (0.88 + 0.24 * hash(cell));
		base = mix(base, mix(brick, mortar_color, mortar * 0.6), detail);
	} else if (pattern == 2 && vertical && detail > 0.001) {
		float b = fract(world_pos.y / board_height);
		float shade = mix(0.82, 1.0, smoothstep(0.0, 0.25, b)); // shadow under each board's lap
		base = mix(base, base * shade, detail * 1.6);
	}
	// Weathering: darker and browner, with grime rising from the ground (COLOR.g = height above the floor / 10 m).
	float age = COLOR.a;
	float grime = age * (1.0 - smoothstep(0.0, 0.35, COLOR.g));
	base = mix(base, base * vec3(0.78, 0.74, 0.70), age * 0.6);
	base *= 1.0 - 0.25 * grime;
	ALBEDO = base;
	ROUGHNESS = 0.9;
}
