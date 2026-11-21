shader_type canvas_item;

// Mega Crit Crystal-Sphere-inspired stepped mossy slate & breathing golden sap bed for Overgrown Rune Loom.
uniform vec4 deep_moss : source_color = vec4(0.032, 0.072, 0.048, 1.0);
uniform vec4 slate_moss : source_color = vec4(0.058, 0.118, 0.078, 1.0);
uniform vec4 sap_tint : source_color = vec4(0.32, 0.22, 0.07, 1.0);

float hash21(vec2 p) {
    p = fract(p * vec2(234.17, 789.31));
    p += dot(p, p + 31.41);
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
    float t = TIME * 0.35;

    float n1 = vnoise(uv * 6.0);
    float n2 = vnoise(uv * 14.0 + n1 * 1.2);
    float stone = n1 * 0.6 + n2 * 0.4;

    // Stepped carved stone planes
    vec3 col = mix(deep_moss.rgb, slate_moss.rgb, smoothstep(0.45, 0.50, stone));

    // Gentle warm amber sap breath pulsing inside the deep altar grooves
    float center_glow = 1.0 - smoothstep(0.15, 0.78, length(uv - 0.5));
    float pulse = 0.65 + 0.35 * sin(t + n1 * 4.0);
    col += sap_tint.rgb * center_glow * pulse * 0.22;

    vec2 edge = abs(uv - 0.5) * 2.0;
    float rim_shadow = smoothstep(0.78, 1.02, max(edge.x, edge.y));
    col *= (1.0 - rim_shadow * 0.52);

    COLOR = vec4(col, 1.0);
}
