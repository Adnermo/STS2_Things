shader_type canvas_item;

// Subtle Mega Crit 2D lineless gouache subterranean lake surface animation.
// Strictly masked to the painted water band (far misty shore y ~ 0.305 to stone quay curb y ~ 0.572);
// cavern ceiling, stalactites, light shafts, and aqueduct arches above 0.300 remain 100% untouched.

uniform float lake_top_y : hint_range(0.0, 1.0) = 0.304;
uniform float lake_curb_y : hint_range(0.0, 1.0) = 0.571;
uniform float drift_speed : hint_range(0.0, 1.0) = 0.14;
uniform float shimmer_strength : hint_range(0.0, 0.5) = 0.14;

float hash21(vec2 p) {
    p = fract(p * vec2(127.1, 311.7));
    p += dot(p, p + 34.23);
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
    // Strictly 0 above far misty shoreline, ramps gently across water horizon
    float top_mask = smoothstep(lake_top_y, lake_top_y + 0.045, uv.y);
    float depth = clamp((uv.y - lake_top_y) / max(0.001, lake_curb_y - lake_top_y), 0.0, 1.0);
    float t = TIME * drift_speed;

    // 1. Subtle horizontal water-reflection micro-undulation inside painted lake only
    float persp_y = pow(max(depth, 0.001), 0.58);
    float undulate =
        sin(persp_y * 42.0 - t * 4.2 + uv.x * 11.0) * 0.60 +
        sin(persp_y * 74.0 + t * 2.9 - uv.x * 19.0) * 0.40;
    vec2 sample_uv = uv;
    sample_uv.x += undulate * 0.00145 * top_mask * (0.28 + 0.72 * depth);

    vec4 base_col = texture(TEXTURE, sample_uv);
    vec3 col = base_col.rgb;

    if (top_mask > 0.001) {
        // 2. Stepped Mega Crit 2D faceted gouache horizontal drift bands (perspective-compressed near horizon)
        vec2 p1 = vec2(uv.x * (6.2 - 1.8 * depth) + t * 0.62, persp_y * 19.0 - t * 0.22);
        vec2 p2 = vec2(uv.x * (12.5 - 3.5 * depth) - t * 0.48, persp_y * 34.0 + t * 0.16);
        float w1 = vnoise(p1);
        float w2 = vnoise(p2 + vec2(w1 * 0.85, 0.0));
        float wave = w1 * 0.58 + w2 * 0.42;

        // Crisp flat-brush cel/gouache value steps (zero blurry cloud noise!)
        float dark_trough = smoothstep(0.40, 0.34, wave);
        float mid_ribbon = smoothstep(0.60, 0.65, wave);
        float fine_glint = smoothstep(0.76, 0.80, wave) * smoothstep(0.55, 0.72, w2);

        // Keep far horizon misty & gentle, foreground bands slightly crisper
        float atten = top_mask * (0.45 + 0.55 * smoothstep(0.0, 0.85, depth));
        vec3 trough_col = vec3(0.032, 0.088, 0.118);
        vec3 ribbon_col = vec3(0.155, 0.335, 0.400);
        vec3 glint_col = vec3(0.380, 0.615, 0.675);

        col = mix(col, trough_col, dark_trough * 0.22 * atten);
        col = mix(col, ribbon_col, mid_ribbon * shimmer_strength * 0.85 * atten);
        col = mix(col, glint_col, fine_glint * shimmer_strength * 0.62 * atten);

        // 3. Delicate broken lapping waterline sheen right along the wet stone quay wall (y ~ 0.562..0.573)
        float curb_dist = abs(uv.y - (lake_curb_y - 0.0045));
        float curb_band = smoothstep(0.0085, 0.0012, curb_dist);
        float curb_break = smoothstep(0.46, 0.56, vnoise(vec2(uv.x * 24.0 + t * 1.1, t * 0.55)));
        float curb_wave = 0.55 + 0.45 * sin(uv.x * 38.0 - t * 3.4);
        col = mix(col, vec3(0.34, 0.57, 0.63), curb_band * curb_break * curb_wave * 0.24 * top_mask);
    }

    COLOR = vec4(col, base_col.a);
}
