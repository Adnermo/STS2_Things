shader_type canvas_item;

// Mega Crit Crystal-Sphere-inspired stepped painterly water caustic bed for Underdocks Bilge Grate.
uniform vec4 deep_color : source_color = vec4(0.024, 0.082, 0.114, 1.0);
uniform vec4 mid_color : source_color = vec4(0.047, 0.145, 0.188, 1.0);
uniform vec4 ripple_color : source_color = vec4(0.095, 0.255, 0.305, 1.0);
uniform vec4 sheen_color : source_color = vec4(0.18, 0.42, 0.44, 1.0);

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

void fragment() {
    vec2 uv = UV;
    float t = TIME * 0.18;

    // Faceted multi-scale drifting canal ripples
    float n1 = vnoise(uv * 5.5 + vec2(t * 0.7, -t * 0.45));
    float n2 = vnoise(uv * 11.0 + vec2(-t * 0.55, t * 0.65) + n1 * 1.4);
    float wave = n1 * 0.55 + n2 * 0.45;

    // Stepped Mega Crit cel/gouache water bands (sharp transitions, zero blurry clouds)
    vec3 col = deep_color.rgb;
    col = mix(col, mid_color.rgb, smoothstep(0.42, 0.46, wave));
    col = mix(col, ripple_color.rgb, smoothstep(0.66, 0.69, wave));
    col = mix(col, sheen_color.rgb, smoothstep(0.81, 0.84, wave) * 0.55);

    // Subtle sunken vignette near the rusted iron pit walls
    vec2 edge = abs(uv - 0.5) * 2.0;
    float pit_shadow = smoothstep(0.76, 1.02, max(edge.x, edge.y));
    col *= (1.0 - pit_shadow * 0.58);

    COLOR = vec4(col, 1.0);
}
