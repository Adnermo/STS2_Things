"""Chromatic Slime (Small) v5 — 3-Strain Bespoke Multi-Skin Vanilla STS2 Gouache Spine 4.2 Rig.
- Normalized 410x380 canvas, ORIGIN=(205, 356)
- 17 bones, 11 slots across 3 strains (crimson, amber, azure + default)
- Planted-base zero-rotation (0.00 deg) volume-conserving gel cage (STRICT ZERO bone sx/sy scaling!)
- Full unscaled 3-layer LeafSlime S cyclops eye inside per-skin hardware CLIPS (`eye_clip`)
- 3 independent 100%-rigid crown pieces (`crest_l`, `crest_c`, `crest_r`) + `spit_glob` projectile per strain
"""
import json
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent

NAME = "chromatic_slime_small"
ORIGIN = (205, 356)
SKINS = ["crimson", "amber", "azure"]
SHARED_SLOTS = {"shadow"}

POS = {
    "root": (205, 356),
    "shadow_b": (205, 356),
    "body_base": (205, 316),
    "foot_l": (92, 344),
    "foot_c": (205, 348),
    "foot_r": (318, 344),
    "flank_l": (104, 268),
    "flank_r": (306, 268),
    "belly_mid": (190, 246),
    "inner_core": (212, 246),
    "crown": (205, 148),
    "crest_l_b": (156, 146),
    "crest_c_b": (224, 134),
    "crest_r_b": (292, 166),
    "iris_b": (150, 238),
    "eyelid_b": (158, 224),
    "spit_glob_b": (138, 224),
}

BONES = [
    ("root", None, POS["root"]),
    ("shadow_b", "root", POS["shadow_b"]),
    ("body_base", "root", POS["body_base"]),
    ("foot_l", "body_base", POS["foot_l"]),
    ("foot_c", "body_base", POS["foot_c"]),
    ("foot_r", "body_base", POS["foot_r"]),
    ("flank_l", "body_base", POS["flank_l"]),
    ("flank_r", "body_base", POS["flank_r"]),
    ("belly_mid", "body_base", POS["belly_mid"]),
    ("inner_core", "body_base", POS["inner_core"]),
    ("crown", "belly_mid", POS["crown"]),
    ("crest_l_b", "crown", POS["crest_l_b"]),
    ("crest_c_b", "crown", POS["crest_c_b"]),
    ("crest_r_b", "crown", POS["crest_r_b"]),
    ("iris_b", "belly_mid", POS["iris_b"]),
    ("eyelid_b", "belly_mid", POS["eyelid_b"]),
    ("spit_glob_b", "root", POS["spit_glob_b"]),
]

ORDER = [
    "shadow",
    "body",
    "inner_blob",
    "eye_clip",
    "iris",
    "eyelid",
    "crest_l",
    "crest_c",
    "crest_r",
    "spit_glob",
]

SLOT_DEFAULT_ALPHA = {"spit_glob": 0.0}

GLOW_OF = {"aura": "body"}
GLOW_SKIN = {"aura": "body"}
GLOW_SCALE = 0.5
GLOW_ALPHA = 0.14

CLIPS = {
    "eye_clip": {
        "host": "body",
        "end": "eyelid",
        "poly_by_skin": json.loads((HERE / "parts" / "eye_clips.json").read_text(encoding="utf-8")),
    }
}

SKIN = {
    "shadow": [("shadow_b", (45, 354), (365, 354))],
    "body": [
        ("foot_l", (50, 346), (138, 346)),
        ("foot_c", (158, 350), (252, 350)),
        ("foot_r", (272, 346), (360, 346)),
        ("flank_l", (86, 294), (114, 228)),
        ("flank_r", (296, 228), (324, 294)),
        ("belly_mid", (164, 272), (196, 214)),
        ("crown", (166, 162), (244, 128)),
    ],
    "inner_blob": [("inner_core", (205, 300), (205, 180))],
    "iris": [("iris_b", (0, 0), (0, 0))],
    "eyelid": [("eyelid_b", (0, 0), (0, 0))],
    "crest_l": [("crest_l_b", (0, 0), (0, 0))],
    "crest_c": [("crest_c_b", (0, 0), (0, 0))],
    "crest_r": [("crest_r_b", (0, 0), (0, 0))],
    "spit_glob": [("spit_glob_b", (0, 0), (0, 0))],
}

SPACING = {
    "shadow": 110,
    "body": 16,
    "aura": 28,
    "inner_blob": 26,
    "iris": 20,
    "eyelid": 24,
    "crest_l": 24,
    "crest_c": 24,
    "crest_r": 24,
    "spit_glob": 24,
}

CHAINS = {
    "crest_l": dict(bones=["crest_l_b"], freq=4.1, zeta=0.62, clamp=3.5, gain=0.008, rgain=0.0),
    "crest_c": dict(bones=["crest_c_b"], freq=3.6, zeta=0.58, clamp=3.8, gain=0.009, rgain=0.0),
    "crest_r": dict(bones=["crest_r_b"], freq=4.4, zeta=0.64, clamp=3.5, gain=0.008, rgain=0.0),
}

VIEW = dict(scale=1.05, W=780, H=560, ox=410, oy=475)


def _seg_dist(p, a, b):
    a = np.asarray(a, dtype=np.float64)
    b = np.asarray(b, dtype=np.float64)
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(float(ab @ ab), 1e-6), 0.0, 1.0)
    return np.hypot(*(p - (a + t[:, None] * ab)).T)


def weights(slot_name, world):
    world = np.asarray(world, dtype=np.float64)
    segs = SKIN[slot_name]
    if len(segs) == 1:
        return [[(segs[0][0], 1.0)] for _ in range(len(world))]
    D = np.stack([_seg_dist(world, a, b) for _, a, b in segs], axis=1)
    Wt = np.exp(-((D / 106.0) ** 2)) + 1e-9
    Wt /= Wt.sum(axis=1, keepdims=True)
    out = []
    for i in range(len(world)):
        acc = {}
        for k in range(len(segs)):
            if Wt[i, k] > 0.012:
                acc[segs[k][0]] = acc.get(segs[k][0], 0.0) + float(Wt[i, k])
        s = sum(acc.values())
        out.append([(b, w / s) for b, w in acc.items()])
    return out
