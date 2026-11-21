shader_type canvas_item;
render_mode unshaded;

// Full-screen Abyssal Ink Spray sweep curtain + hand-painted perimeter ink splatter & drip shader.
// TEXTURE = res://images/vfx/abyssal_ink_screen_splatter.png (1920x1080 RGBA)
uniform float sweep_progress : hint_range(-0.2, 1.4) = -0.2;
uniform float curtain_strength : hint_range(0.0, 1.0) = 0.0;
uniform float border_strength : hint_range(0.0, 1.0) = 0.0;
uniform float drip_offset : hint_range(0.0, 0.35) = 0.0;
uniform float wash_progress : hint_range(0.0, 1.0) = 0.0;

float hash21(vec2 p) {
	p = fract(p * vec2(127.1, 311.7));
	p += dot(p, p + 34.5);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x),
		mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x),
		f.y
	);
}

float fbm(vec2 p) {
	float v = 0.0;
	float a = 0.625;
	mat2 r = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));
	for (int i = 0; i < 2; i++) {
		v += a * vnoise(p);
		p = r * p * 2.03 + vec2(3.1, 7.4);
		a *= 0.5;
	}
	return v;
}

void fragment() {
	if (border_strength <= 0.001 && curtain_strength <= 0.001) {
		discard;
	}

	vec2 uv = UV;
	float t = TIME;

	// 1. Hand-painted perimeter splatter & viscous hanging ink drips (gently oozing downward!)
	vec2 splat_uv = uv;
	if (drip_offset > 0.0001) {
		float drip_n = vnoise(vec2(uv.x * 14.0, 0.5));
		splat_uv.y = clamp(uv.y - drip_offset * (0.45 + 0.9 * drip_n) * (1.0 - uv.y * 0.45), 0.0, 1.0);
	}
	vec4 splat = texture(TEXTURE, splat_uv);
	if (splat.a <= 0.002 && curtain_strength <= 0.001) {
		discard;
	}

	// Reveal splatter left-to-right as the sweeping ink cannon blasts across the viewport!
	float sweep_mask = 1.0;
	if (sweep_progress < 1.25 && curtain_strength > 0.001) {
		float jitter = (fbm(vec2(uv.y * 6.0, t * 2.0)) - 0.5) * 0.14;
		sweep_mask = 1.0 - smoothstep(sweep_progress - 0.12, sweep_progress + 0.04, uv.x + jitter);
	}

	// Wet specular shimmer slowly gliding across painted ink droplets
	float shimmer = exp(-pow(fract(uv.y * 1.6 - t * 0.30 + uv.x * 0.4) - 0.5, 2.0) / 0.018);
	vec3 splat_col = splat.rgb + vec3(0.09, 0.24, 0.28) * shimmer * splat.a * 0.42;
	// During persistent Turn-5 ambience (curtain_strength == 0), gently keep the upper-center
	// intent band and lower-right End Turn button crisp while framing the viewport corners!
	float intent_clear = 1.0 - 0.55 * (1.0 - curtain_strength)
		* smoothstep(0.16, 0.30, uv.x) * (1.0 - smoothstep(0.70, 0.84, uv.x))
		* (1.0 - smoothstep(0.18, 0.36, uv.y));
	float end_turn_clear = 1.0 - 0.55 * (1.0 - curtain_strength)
		* smoothstep(0.78, 0.88, uv.x) * smoothstep(0.72, 0.86, uv.y);
	float splat_a = splat.a * border_strength * sweep_mask * intent_clear * end_turn_clear;

	// 2. Dynamic sweeping viscous ink wall + bottom deck-drenching ink tide during INK_SPRAY
	vec3 curtain_col = vec3(0.03, 0.025, 0.065);
	float curtain_a = 0.0;
	if (curtain_strength > 0.001) {
		vec2 q = vec2(
			fbm(uv * vec2(4.2, 2.6) + vec2(-t * 1.4, t * 0.4)),
			fbm(uv * vec2(3.8, 3.2) + vec2(t * 0.9, -t * 0.7))
		);
		float swirl = fbm(uv * vec2(5.5, 3.8) + q * 1.8 - vec2(t * 1.8, -drip_offset * 3.0));

		// Moving high-pressure spray band following sweep_progress from left to right
		float band_dist = uv.x - sweep_progress;
		float leading_jet = exp(-pow(band_dist / 0.14, 2.0));
		float trailing_curtain = (1.0 - smoothstep(-0.04, 0.08, band_dist)) * smoothstep(-0.68, -0.08, band_dist);

		// Bottom hand/deck-zone dark ink wash wave (y = 0.72..1.0)
		float deck_wave = smoothstep(0.68, 0.96, uv.y + (swirl - 0.5) * 0.16) * sweep_mask;

		// Peripheral vignette darkening on impact
		float edge_dist = length((uv - vec2(0.5)) * vec2(1.15, 1.35));
		float vignette = smoothstep(0.44, 0.92, edge_dist) * sweep_mask;

		curtain_a = clamp(
			(leading_jet * 0.55 + trailing_curtain * (0.48 + swirl * 0.34) + deck_wave * 0.62 + vignette * 0.42)
			* curtain_strength,
			0.0,
			0.88
		);

		float foam_streak = exp(-pow((swirl - 0.64) / 0.075, 2.0));
		curtain_col = mix(vec3(0.025, 0.022, 0.055), vec3(0.11, 0.10, 0.23), swirl);
		curtain_col = mix(curtain_col, vec3(0.28, 0.72, 0.78), clamp(leading_jet * 0.65 + foam_streak * 0.45, 0.0, 1.0));
	}

	// Composite splatter cleanly over sweeping curtain
	float out_a = clamp(splat_a + curtain_a * (1.0 - splat_a), 0.0, 0.96);
	vec3 out_col = mix(curtain_col, splat_col, clamp(splat_a / max(out_a, 0.001), 0.0, 1.0));

	// 3. Diagonal bioluminescent seafoam wave-wash dissolve when a major tentacle is severed!
	if (wash_progress > 0.001) {
		float diag = (uv.x * 0.62 + uv.y * 0.38) + 0.045 * sin(uv.y * 14.0 - uv.x * 8.0 + t * 5.0);
		float front = mix(-0.18, 1.32, wash_progress);
		float remain = smoothstep(front - 0.07, front + 0.045, diag);
		float foam = exp(-pow((diag - front) / 0.05, 2.0));
		out_col = mix(out_col, vec3(0.68, 0.97, 0.95), clamp(foam * 0.95, 0.0, 1.0));
		out_a *= clamp(remain + foam * 0.80, 0.0, 1.0);
	}

	if (out_a <= 0.002) {
		discard;
	}
	COLOR = vec4(out_col, out_a);
}
