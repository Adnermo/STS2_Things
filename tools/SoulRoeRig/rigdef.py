"""SoulRoeRig — Spine 4.2 pure single-roe rig definition (`soul_roe_1`, `soul_roe_2`, `soul_roe_3`).

- 340x340 rig canvas, ORIGIN = (170, 210), MAIN_CENTER = (170, 140)
- ZERO orbiting micro-roes (`sat_*` completely removed!)
- 14 bones:
  * Deformable 5-bone zero-rotation, zero-scale main roe surface-tension cage (`main_center`, `rim_n`, `rim_s`, `rim_w`, `rim_e`)
  * Liquid phase-lagged golden yolk bone (`yolk_b`, r == 0.0 deg)
  * 4-bone zero-rotation, zero-scale radial expansion cage (`halo_n`, `halo_s`, `halo_w`, `halo_e`) for `beckon_halo_ring` & `intangible_shimmer`
  * Single-body comet slipstream bone (`body_trail_b`, child of `main_center`) + front impact shock bone (`impact_b`, r == 0.0 deg)
- 12 slots with dual bioluminescence & full-body kinetic strike VFX
"""
from __future__ import annotations

import numpy as np

from extract_parts import CANVAS_H, CANVAS_W, IMPACT_ANCHOR, MAIN_CENTER, ORIGIN, VARIANT_SPECS

ORDER = [
    "shadow",
    "rear_glow",
    "body_trail",
    "cytoplasm",
    "nucleus",
    "core_beckon_glow",
    "core_intangible_glow",
    "veins_highlight",
    "membrane_ring",
    "beckon_halo_ring",
    "intangible_shimmer",
    "impact_flare",
]

ADDITIVE_SLOTS = {
    "body_trail",
    "core_beckon_glow",
    "core_intangible_glow",
    "beckon_halo_ring",
    "intangible_shimmer",
    "impact_flare",
}

SLOT_DEFAULT_ALPHA = {
    "shadow": 1.0,
    "rear_glow": 0.78,
    "body_trail": 0.0,
    "cytoplasm": 1.0,
    "nucleus": 1.0,
    "core_beckon_glow": 0.0,
    "core_intangible_glow": 0.0,
    "veins_highlight": 0.94,
    "membrane_ring": 1.0,
    "beckon_halo_ring": 0.0,
    "intangible_shimmer": 0.0,
    "impact_flare": 0.0,
}

SPACING = {
    "shadow": 64,
    "rear_glow": 24,
    "body_trail": 28,
    "cytoplasm": 16,
    "nucleus": 18,
    "core_beckon_glow": 24,
    "core_intangible_glow": 24,
    "veins_highlight": 18,
    "membrane_ring": 14,
    "beckon_halo_ring": 18,
    "intangible_shimmer": 18,
    "impact_flare": 26,
}

ZERO_ROT_BONES = [
    "root",
    "shadow_b",
    "main_center",
    "rim_n",
    "rim_s",
    "rim_w",
    "rim_e",
    "yolk_b",
    "halo_n",
    "halo_s",
    "halo_w",
    "halo_e",
    "impact_b",
]


def make_rig_spec(variant: int) -> dict:
    cfg = VARIANT_SPECS[variant]
    name = cfg["name"]
    cx, cy = MAIN_CENTER

    pos = {
        "root": ORIGIN,
        "shadow_b": (ORIGIN[0], ORIGIN[1] + 8.0),
        "main_center": (cx, cy),
        "rim_n": (cx, cy - 70.0),
        "rim_s": (cx, cy + 70.0),
        "rim_w": (cx - 70.0, cy),
        "rim_e": (cx + 70.0, cy),
        "yolk_b": (cx, cy),
        "halo_n": (cx, cy - 90.0),
        "halo_s": (cx, cy + 90.0),
        "halo_w": (cx - 90.0, cy),
        "halo_e": (cx + 90.0, cy),
        "body_trail_b": (cx, cy),
        "impact_b": IMPACT_ANCHOR,
    }

    bones = [
        ("root", None, pos["root"]),
        ("shadow_b", "root", pos["shadow_b"]),
        ("main_center", "root", pos["main_center"]),
        ("rim_n", "main_center", pos["rim_n"]),
        ("rim_s", "main_center", pos["rim_s"]),
        ("rim_w", "main_center", pos["rim_w"]),
        ("rim_e", "main_center", pos["rim_e"]),
        ("yolk_b", "main_center", pos["yolk_b"]),
        ("halo_n", "main_center", pos["halo_n"]),
        ("halo_s", "main_center", pos["halo_s"]),
        ("halo_w", "main_center", pos["halo_w"]),
        ("halo_e", "main_center", pos["halo_e"]),
        ("body_trail_b", "main_center", pos["body_trail_b"]),
        ("impact_b", "main_center", pos["impact_b"]),
    ]

    slot_bone = {
        "shadow": "shadow_b",
        "rear_glow": "main_center",
        "body_trail": "body_trail_b",
        "cytoplasm": "main_center",
        "nucleus": "yolk_b",
        "core_beckon_glow": "yolk_b",
        "core_intangible_glow": "main_center",
        "veins_highlight": "main_center",
        "membrane_ring": "main_center",
        "beckon_halo_ring": "main_center",
        "intangible_shimmer": "main_center",
        "impact_flare": "impact_b",
    }

    main_cage_slots = {"rear_glow", "cytoplasm", "veins_highlight", "membrane_ring", "core_intangible_glow"}
    yolk_blend_slots = {"core_beckon_glow"}
    halo_ring_slots = {"beckon_halo_ring", "intangible_shimmer"}

    def weights_fn(slot_name: str, world_pts: np.ndarray) -> list[list[tuple[str, float]]]:
        pts = np.asarray(world_pts, dtype=np.float64)
        if slot_name in main_cage_slots:
            anchors = [
                ("main_center", pos["main_center"], 74.0),
                ("rim_n", pos["rim_n"], 64.0),
                ("rim_s", pos["rim_s"], 64.0),
                ("rim_w", pos["rim_w"], 64.0),
                ("rim_e", pos["rim_e"], 64.0),
            ]
            W = np.stack(
                [np.exp(-(((pts[:, 0] - ax) ** 2 + (pts[:, 1] - ay) ** 2) / (sig**2))) for _, (ax, ay), sig in anchors],
                axis=1,
            )
            W[:, 0] += 0.22
            W /= W.sum(axis=1, keepdims=True)
            out = []
            for i in range(len(pts)):
                wl = [(anchors[k][0], float(W[i, k])) for k in range(len(anchors)) if W[i, k] > 0.015]
                s = sum(w for _, w in wl)
                out.append([(b, w / s) for b, w in wl])
            return out

        if slot_name in yolk_blend_slots:
            anchors = [
                ("yolk_b", pos["yolk_b"], 56.0),
                ("main_center", pos["main_center"], 78.0),
                ("rim_n", pos["rim_n"], 74.0),
                ("rim_s", pos["rim_s"], 74.0),
                ("rim_w", pos["rim_w"], 74.0),
                ("rim_e", pos["rim_e"], 74.0),
            ]
            W = np.stack(
                [np.exp(-(((pts[:, 0] - ax) ** 2 + (pts[:, 1] - ay) ** 2) / (sig**2))) for _, (ax, ay), sig in anchors],
                axis=1,
            )
            W[:, 0] *= 1.45
            W /= W.sum(axis=1, keepdims=True)
            out = []
            for i in range(len(pts)):
                wl = [(anchors[k][0], float(W[i, k])) for k in range(len(anchors)) if W[i, k] > 0.015]
                s = sum(w for _, w in wl)
                out.append([(b, w / s) for b, w in wl])
            return out

        if slot_name in halo_ring_slots:
            anchors = [
                ("halo_n", pos["halo_n"], 96.0),
                ("halo_s", pos["halo_s"], 96.0),
                ("halo_w", pos["halo_w"], 96.0),
                ("halo_e", pos["halo_e"], 96.0),
            ]
            W = np.stack(
                [np.exp(-(((pts[:, 0] - ax) ** 2 + (pts[:, 1] - ay) ** 2) / (sig**2))) for _, (ax, ay), sig in anchors],
                axis=1,
            ) + 0.08
            W /= W.sum(axis=1, keepdims=True)
            out = []
            for i in range(len(pts)):
                s = float(W[i].sum())
                out.append([(anchors[k][0], float(W[i, k] / s)) for k in range(4)])
            return out

        b_single = slot_bone[slot_name]
        return [[(b_single, 1.0)] for _ in range(len(pts))]

    return dict(
        variant=variant,
        name=name,
        canvas=(CANVAS_W, CANVAS_H),
        origin=ORIGIN,
        main_center=MAIN_CENTER,
        pos=pos,
        bones=bones,
        order=list(ORDER),
        slot_bone=slot_bone,
        additive_slots=set(ADDITIVE_SLOTS),
        slot_default_alpha=dict(SLOT_DEFAULT_ALPHA),
        spacing=dict(SPACING),
        zero_rot_bones=list(ZERO_ROT_BONES),
        weights=weights_fn,
    )
