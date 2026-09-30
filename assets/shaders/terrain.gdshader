// Low-poly terrain: flat-shaded mesh with per-tile vertex colours (linear), a build-grid overlay computed in world
// space (so it drapes exactly over the terrain), and a highlight for the tile under the mouse. §5.1, §30.3.
shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_disabled;

uniform bool grid_enabled = true;
uniform float tile_size = 10.0;
uniform vec4 grid_color : source_color = vec4(1.0, 1.0, 1.0, 0.35);
uniform float grid_fade_start = 250.0;
uniform float grid_fade_end = 900.0;
uniform vec2 hover_tile = vec2(-1.0, -1.0);
uniform vec4 hover_color : source_color = vec4(1.0, 0.82, 0.25, 0.6);

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 base = COLOR.rgb;
	vec2 t = world_pos.xz / tile_size;
	vec2 cell = floor(t);
	if (grid_enabled) {
		// Anti-aliased 1-pixel lines at tile borders, fading out with distance.
		vec2 d = abs(fract(t - 0.5) - 0.5) / max(fwidth(t), vec2(1e-5));
		float line = 1.0 - min(min(d.x, d.y), 1.0);
		float fade = 1.0 - smoothstep(grid_fade_start, grid_fade_end, length(world_pos - CAMERA_POSITION_WORLD));
		base = mix(base, grid_color.rgb, line * grid_color.a * fade);
	}
	if (all(equal(cell, hover_tile))) {
		base = mix(base, hover_color.rgb, hover_color.a);
	}
	ALBEDO = base;
	ROUGHNESS = 1.0;
}
