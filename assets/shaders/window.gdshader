// Sash windows for the kit (§28.1a, Phase 1 1f): the glass quad of a window piece carries 0..1 UVs, and this shader
// draws the white frame, the two sashes with their meeting rail, and the muntins, so any pane pattern (6-over-6,
// 9-over-9 ...) costs the same two triangles. At night a share of the windows glow warm (§28.1 "warm window glow in
// halls at night"): COLOR.r is a per-window random number written by the assembler's mesh builder.
// No textures, no transparency: Mobile-renderer safe (Locked Decision: iPad later).
shader_type spatial;
render_mode cull_back, diffuse_lambert;

uniform vec3 frame_color : source_color = vec3(0.957, 0.945, 0.918);
uniform vec3 glass_color : source_color = vec3(0.18, 0.227, 0.275);
uniform vec3 glow_color : source_color = vec3(1.0, 0.824, 0.541);
uniform float glow_energy = 1.6;
uniform int cols = 3;
uniform int rows_upper = 2;
uniform int rows_lower = 2;
uniform vec2 frame_width = vec2(0.075, 0.045);   // in UV units (x across, y down)
uniform float muntin_width = 0.022;              // in UV units of the width
uniform float rail_width = 0.03;                 // meeting rail, UV units of the height
uniform float night = 0.0;                       // 0 = day, 1 = full night (set from the sun)
uniform float lit_fraction = 0.55;               // share of windows lit at night

float line_mask(float v, float count, float half_width) {
	// 1 on the grid lines between panes (not on the outer edges), 0 elsewhere; anti-aliased with fwidth.
	float f = v * count;
	float d = abs(fract(f + 0.5) - 0.5) / count;   // distance to the nearest line, in UV units
	float aa = fwidth(v) * 0.75;
	float inner = step(0.5 / count, min(v, 1.0 - v) + 0.0001); // ignore the outer edges
	return (1.0 - smoothstep(half_width - aa, half_width + aa, d)) * inner;
}

void fragment() {
	vec2 uv = UV;
	float aa = fwidth(uv.x) + fwidth(uv.y);
	// Outer frame.
	float frame = 1.0 - smoothstep(0.0, aa, min(min(uv.x - frame_width.x, 1.0 - frame_width.x - uv.x),
	                                             min(uv.y - frame_width.y, 1.0 - frame_width.y - uv.y)));
	// Inside the frame: two sashes split by the meeting rail.
	vec2 inner = (uv - frame_width) / (1.0 - 2.0 * frame_width);
	float rail = 1.0 - smoothstep(rail_width * 0.5 - aa, rail_width * 0.5 + aa, abs(inner.y - 0.5));
	bool upper = inner.y < 0.5;
	float sash_v = upper ? inner.y * 2.0 : (inner.y - 0.5) * 2.0;
	float rows = float(upper ? rows_upper : rows_lower);
	float muntins = max(line_mask(inner.x, float(cols), muntin_width * 0.5), line_mask(sash_v, rows, muntin_width * 0.5 * 2.0));
	float wood = clamp(max(frame, max(rail, muntins)), 0.0, 1.0);

	// Glass: a soft sky reflection toward the top of each pane; lit windows glow at night.
	float pane_v = fract(sash_v * rows);
	vec3 glass = glass_color * (0.85 + 0.35 * (1.0 - pane_v));
	bool lit = COLOR.r < lit_fraction;
	float glow = lit ? night : 0.0;

	ALBEDO = mix(glass * (1.0 - 0.8 * glow), frame_color, wood);
	ROUGHNESS = mix(0.2, 0.8, wood);
	METALLIC = mix(0.2, 0.0, wood);
	EMISSION = glow_color * glow_energy * glow * (1.0 - wood);
}
