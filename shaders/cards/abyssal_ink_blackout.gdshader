shader_type canvas_item;
render_mode unshaded;

// Authentic STS2-proportioned custom Abyssal Ink-Drenched Card shader.
// TEXTURE = res://images/ui/cards/abyssal_ink_card_frame.png (654x844 RGBA)
uniform float wash_progress : hint_range(0.0, 1.0) = 0.0;
uniform float drench_progress : hint_range(0.0, 1.0) = 1.0;
uniform float card_seed = 0.0;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
	float v = 0.0;
	float a = 0.65;
	mat2 rot = mat2(vec2(0.80, 0.60), vec2(-0.60, 0.80));
	for (int i = 0; i < 2; i++) {
		v += a * vnoise(p);
		p = rot * p * 2.05 + vec2(1.7, 9.2);
		a *= 0.5;
	}
	return v;
}

void fragment() {
	vec2 uv = UV;
	vec4 tex = texture(TEXTURE, uv);
	if (tex.a <= 0.004) {
		discard;
	}

	float t = TIME * 0.65 + card_seed * 6.2831;

	// Portrait vortex cavity (matching canonical vanilla STS2 portrait box inside 654x844 frame)
	float in_port_x = smoothstep(0.13, 0.17, uv.x) * (1.0 - smoothstep(0.83, 0.87, uv.x));
	float in_port_y = smoothstep(0.15, 0.20, uv.y) * (1.0 - smoothstep(0.49, 0.53, uv.y));
	float portrait_mask = in_port_x * in_port_y;

	// Lower dark ink-splattered description box
	float in_desc_x = smoothstep(0.12, 0.16, uv.x) * (1.0 - smoothstep(0.84, 0.88, uv.x));
	float in_desc_y = smoothstep(0.57, 0.61, uv.y) * (1.0 - smoothstep(0.87, 0.91, uv.y));
	float desc_mask = in_desc_x * in_desc_y;

	// Gently lift the hand-painted STS2 frame, curved banner, plaque & dark matte box midtones
	// so the card silhouette & pitch-black glossy squid-ink drips pop clearly at hand scale!
	float base_luma = dot(tex.rgb, vec3(0.299, 0.587, 0.114));
	float non_pitch = smoothstep(0.035, 0.14, base_luma);
	vec3 lifted = pow(max(tex.rgb, vec3(0.0001)), vec3(0.82)) * 1.24;
	vec3 col = mix(tex.rgb * 0.92, lifted, non_pitch);
	// Subtle crisp rim highlight around the card's dark matte lower panel so ink blotches stand out
	float desc_lift = desc_mask * smoothstep(0.045, 0.12, base_luma);
	col += vec3(0.045, 0.055, 0.085) * desc_lift;

	// Subtle hypnotic swirling shimmer along the painted seafoam-cyan vortex strokes in portrait
	if (portrait_mask > 0.001) {
		vec2 vp = uv - vec2(0.50, 0.34);
		float ang = atan(vp.y, vp.x);
		float rad = length(vp);
		float vortex_wave = 0.5 + 0.5 * sin(ang * 3.0 - rad * 26.0 + t * 2.4);
		float cyan_ink = clamp((tex.g + tex.b) * 0.5 - tex.r, 0.0, 1.0);
		col += vec3(0.08, 0.26, 0.28) * portrait_mask * smoothstep(0.05, 0.22, cyan_ink) * vortex_wave * 0.62;
	}

	// Subtle flowing abyssal indigo current inside dark portrait & lower ink-stained text box
	float inner_mask = portrait_mask * 0.45 + desc_mask * 0.70;
	if (inner_mask > 0.001) {
		vec2 q = vec2(
			fbm(uv * vec2(3.8, 5.0) + vec2(t * 0.22, -t * 0.28)),
			fbm(uv * vec2(4.0, 4.8) + vec2(-t * 0.24 + 2.3, t * 0.18 + 3.9))
		);
		float swirl = fbm(uv * vec2(4.6, 5.8) + q * 1.65 + vec2(0.0, -t * 0.34));
		col += vec3(0.045, 0.040, 0.110) * inner_mask * smoothstep(0.46, 0.82, swirl);
	}

	// Wet glossy highlight gliding gently down the pitch-black squid-ink drips & blotches
	float dark_ink = smoothstep(0.14, 0.04, base_luma);
	float wet_band = exp(-pow(fract(uv.y * 1.45 - t * 0.24 + uv.x * 0.3) - 0.5, 2.0) / 0.012);
	col += vec3(0.11, 0.24, 0.30) * dark_ink * wet_band * 0.55;

	float alpha = tex.a;

	// Viscous top-to-bottom ink drench entrance sweep (drench_progress: 0 -> 1)
	if (drench_progress < 0.999) {
		float drip_wave = uv.y - 0.08 * fbm(vec2(uv.x * 8.0 + card_seed * 5.0, 1.3));
		float front = mix(-0.18, 1.22, drench_progress);
		float drenched = 1.0 - smoothstep(front - 0.07, front + 0.04, drip_wave);
		float crest = exp(-pow((drip_wave - front) / 0.045, 2.0));
		col = mix(col, vec3(0.32, 0.74, 0.80), crest * 0.50);
		alpha *= clamp(drenched + crest * 0.45, 0.0, 1.0);
	}

	// Diagonal glowing bioluminescent seafoam wave-wash dissolve when a tentacle is severed!
	if (wash_progress > 0.001) {
		float diag = (uv.x * 0.66 + uv.y * 0.74) + 0.055 * sin(uv.x * 16.0 - uv.y * 11.0 + TIME * 4.5);
		float front = mix(-0.20, 1.58, wash_progress);
		float remain = smoothstep(front - 0.075, front + 0.05, diag);
		float foam = exp(-pow((diag - front) / 0.055, 2.0));
		float sparkle = smoothstep(0.68, 0.92, vnoise(uv * 28.0 + vec2(TIME * 3.0, -TIME * 2.0)));
		col = mix(col, vec3(0.66, 0.96, 0.94), clamp(foam * 0.95 + foam * sparkle * 0.35, 0.0, 1.0));
		alpha *= clamp(remain + foam * 0.85, 0.0, 1.0);
	}

	COLOR = vec4(col, alpha) * COLOR;
}
