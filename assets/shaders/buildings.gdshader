// Extruded building footprints (Spike B 3c). Each vertex carries its building's lifetime in UV2 = (built, demolished)
// and COLOR.a = 1 for buildings with no known build year. Buildings outside the current year collapse to a point
// (degenerate triangles aren't drawn, and cast no shadow), so changing the year costs no CPU work.
shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_disabled;

uniform float current_year = 2026.0;
uniform bool show_undated_always = false;

void vertex() {
	float built = UV2.x;
	if (show_undated_always && COLOR.a > 0.5) {
		built = -1.0e6;
	}
	if (current_year < built || current_year >= UV2.y) {
		VERTEX = vec3(0.0);
	}
}

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 0.85;
}
