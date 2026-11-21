"""Chromatic Slime (Medium) — v4 Bespoke 3-Strain Multi-Skin Spine 4.2 Rig (Zero-Body-Rotation Edition).

- 29 bones, 25 slots (2 hardware Spine 4.2 ClippingAttachments + 2 per-skin flying projectile slots!)
- STRICT ZERO bone sx/sy scaling AND STRICT ZERO rotation on all gel body/base bones!
- Crimson 3-layer harpoon crystal sockets (body back-collar -> +40px crystal shaft -> c_collars front lip)
- Amber 4 interlocking sandstone armor slabs; Azure 3 bubbling seafoam froth clusters
"""
import json
from pathlib import Path
import numpy as np

HERE = Path(__file__).resolve().parent

NAME = "chromatic_slime_medium"
ORIGIN = (220, 386)
SKINS = ["crimson", "amber", "azure"]
SHARED_SLOTS = {"shadow"}

_EYE_CLIPS = json.loads((HERE / "parts" / "eye_clips.json").read_text(encoding="utf-8"))

POS = {
    # Core volumetric gel cage (11 bones — ALL strictly r == 0.0 deg across ALL animations!)
    "root": (220, 386),
    "shadow_b": (220, 386),
    "body_base": (220, 344),
    "foot_l": (96, 376),
    "foot_c": (220, 380),
    "foot_r": (344, 376),
    "flank_l": (112, 286),
    "flank_r": (328, 286),
    "belly_mid": (216, 248),
    "inner_core": (216, 248),
    "crown": (216, 150),
    # Unscaled dual eyes (4 bones)
    "iris_main_b": (166, 265),
    "lid_main_b": (166, 212),
    "iris_sec_b": (274, 232),
    "lid_sec_b": (274, 186),
    # Crimson bespoke 100%-rigid harpoon ruby crystal bones (5 bones)
    "c_shard_back_b": (191, 136),
    "c_shard_tall_l_b": (136, 163),
    "c_shard_tall_r_b": (298, 174),
    "c_shard_low_l_b": (70, 255),
    "c_shard_low_r_b": (340, 268),
    # Amber bespoke 100%-rigid weathered sandstone slab bones (4 bones)
    "a_stone_top_b": (286, 156),
    "a_stone_flank_r_b": (356, 252),
    "a_stone_l_b": (122, 168),
    "a_stone_cheek_b": (284, 318),
    # Azure bespoke 100%-rigid seafoam froth cluster bones (3 bones)
    "z_froth_crown_b": (266, 172),
    "z_froth_left_b": (68, 252),
    "z_froth_puddle_b": (316, 352),
    # Per-skin hand-painted flying projectiles (2 bones parented to root!)
    "proj_1_b": (150, 236),
    "proj_2_b": (168, 210),
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
    # Eyes
    ("iris_main_b", "belly_mid", POS["iris_main_b"]),
    ("lid_main_b", "belly_mid", POS["lid_main_b"]),
    ("iris_sec_b", "belly_mid", POS["iris_sec_b"]),
    ("lid_sec_b", "belly_mid", POS["lid_sec_b"]),
    # Crimson harpoon shards
    ("c_shard_back_b", "crown", POS["c_shard_back_b"]),
    ("c_shard_tall_l_b", "crown", POS["c_shard_tall_l_b"]),
    ("c_shard_tall_r_b", "crown", POS["c_shard_tall_r_b"]),
    ("c_shard_low_l_b", "flank_l", POS["c_shard_low_l_b"]),
    ("c_shard_low_r_b", "flank_r", POS["c_shard_low_r_b"]),
    # Amber armor slabs
    ("a_stone_top_b", "crown", POS["a_stone_top_b"]),
    ("a_stone_flank_r_b", "flank_r", POS["a_stone_flank_r_b"]),
    ("a_stone_l_b", "crown", POS["a_stone_l_b"]),
    ("a_stone_cheek_b", "belly_mid", POS["a_stone_cheek_b"]),
    # Azure froth clusters
    ("z_froth_crown_b", "crown", POS["z_froth_crown_b"]),
    ("z_froth_left_b", "flank_l", POS["z_froth_left_b"]),
    ("z_froth_puddle_b", "foot_r", POS["z_froth_puddle_b"]),
    # Projectiles
    ("proj_1_b", "root", POS["proj_1_b"]),
    ("proj_2_b", "root", POS["proj_2_b"]),
]

ORDER = [
    "shadow",
    "c_shard_back",
    "body",
    "inner_blob",
    "c_shard_tall_r",
    "c_shard_tall_l",
    "c_shard_low_r",
    "c_shard_low_l",
    "c_collars",
    "eye_clip_main",
    "iris_main",
    "lid_main",
    "eye_clip_sec",
    "iris_sec",
    "lid_sec",
    "a_stone_top",
    "a_stone_flank_r",
    "a_stone_l",
    "a_stone_cheek",
    "z_froth_crown",
    "z_froth_left",
    "z_froth_puddle",
    "proj_1",
    "proj_2",
]

SLOT_DEFAULT_ALPHA = {
    "proj_1": 0.0,
    "proj_2": 0.0,
}

CLIPS = {
    "eye_clip_main": {
        "end": "lid_main",
        "host": "body",
        "poly_by_skin": _EYE_CLIPS["eye_clip_main"],
    },
    "eye_clip_sec": {
        "end": "lid_sec",
        "host": "body",
        "poly_by_skin": _EYE_CLIPS["eye_clip_sec"],
    },
}

GLOW_OF = {"aura": "body"}
GLOW_SKIN = {"aura": "body"}
GLOW_SCALE = 0.5
GLOW_ALPHA = 0.14

SKIN = {
    "shadow": [("shadow_b", (48, 386), (392, 386))],
    "body": [
        ("foot_l", (52, 376), (145, 376)),
        ("foot_c", (165, 382), (275, 382)),
        ("foot_r", (295, 376), (388, 376)),
        ("flank_l", (92, 316), (128, 250)),
        ("flank_r", (312, 250), (350, 316)),
        ("belly_mid", (216, 305), (216, 212)),
        ("crown", (175, 166), (255, 120)),
    ],
    "c_collars": [("belly_mid", (0, 0), (0, 0))],
    "inner_blob": [("inner_core", (216, 305), (216, 190))],
    # 100%-rigid unscaled eyes
    "iris_main": [("iris_main_b", (0, 0), (0, 0))],
    "lid_main": [("lid_main_b", (0, 0), (0, 0))],
    "iris_sec": [("iris_sec_b", (0, 0), (0, 0))],
    "lid_sec": [("lid_sec_b", (0, 0), (0, 0))],
    # 100%-rigid Crimson ruby harpoon shards
    "c_shard_back": [("c_shard_back_b", (0, 0), (0, 0))],
    "c_shard_tall_l": [("c_shard_tall_l_b", (0, 0), (0, 0))],
    "c_shard_tall_r": [("c_shard_tall_r_b", (0, 0), (0, 0))],
    "c_shard_low_l": [("c_shard_low_l_b", (0, 0), (0, 0))],
    "c_shard_low_r": [("c_shard_low_r_b", (0, 0), (0, 0))],
    # 100%-rigid Amber sandstone slabs
    "a_stone_top": [("a_stone_top_b", (0, 0), (0, 0))],
    "a_stone_flank_r": [("a_stone_flank_r_b", (0, 0), (0, 0))],
    "a_stone_l": [("a_stone_l_b", (0, 0), (0, 0))],
    "a_stone_cheek": [("a_stone_cheek_b", (0, 0), (0, 0))],
    # 100%-rigid Azure seafoam froth clusters
    "z_froth_crown": [("z_froth_crown_b", (0, 0), (0, 0))],
    "z_froth_left": [("z_froth_left_b", (0, 0), (0, 0))],
    "z_froth_puddle": [("z_froth_puddle_b", (0, 0), (0, 0))],
    # 100%-rigid flying projectiles
    "proj_1": [("proj_1_b", (0, 0), (0, 0))],
    "proj_2": [("proj_2_b", (0, 0), (0, 0))],
}

SPACING = {
    "shadow": 120,
    "body": 18,
    "c_collars": 18,
    "aura": 32,
    "inner_blob": 28,
    "iris_main": 18,
    "lid_main": 28,
    "iris_sec": 16,
    "lid_sec": 24,
    "c_shard_back": 24,
    "c_shard_tall_l": 24,
    "c_shard_tall_r": 24,
    "c_shard_low_l": 22,
    "c_shard_low_r": 24,
    "a_stone_top": 28,
    "a_stone_flank_r": 26,
    "a_stone_l": 28,
    "a_stone_cheek": 22,
    "z_froth_crown": 26,
    "z_froth_left": 24,
    "z_froth_puddle": 24,
    "proj_1": 32,
    "proj_2": 32,
}

# Tight secondary inertia on rigid mineral/froth attachments ONLY (ZERO gel body bones in CHAINS!)
CHAINS = {
    "c_tall_l": dict(bones=["c_shard_tall_l_b"], freq=6.0, zeta=0.58, clamp=2.8, gain=0.004, rgain=0.0),
    "c_tall_r": dict(bones=["c_shard_tall_r_b"], freq=5.8, zeta=0.58, clamp=2.8, gain=0.004, rgain=0.0),
    "c_back": dict(bones=["c_shard_back_b"], freq=6.2, zeta=0.60, clamp=2.5, gain=0.004, rgain=0.0),
    "c_low_l": dict(bones=["c_shard_low_l_b"], freq=6.0, zeta=0.60, clamp=2.5, gain=0.004, rgain=0.0),
    "c_low_r": dict(bones=["c_shard_low_r_b"], freq=5.8, zeta=0.60, clamp=2.5, gain=0.004, rgain=0.0),
    "a_top": dict(bones=["a_stone_top_b"], freq=2.8, zeta=0.72, clamp=3.5, gain=0.006, rgain=0.0),
    "a_left": dict(bones=["a_stone_l_b"], freq=2.9, zeta=0.72, clamp=3.5, gain=0.006, rgain=0.0),
    "a_flank": dict(bones=["a_stone_flank_r_b"], freq=3.0, zeta=0.72, clamp=3.5, gain=0.005, rgain=0.0),
    "a_cheek": dict(bones=["a_stone_cheek_b"], freq=3.2, zeta=0.72, clamp=3.0, gain=0.005, rgain=0.0),
    "z_crown": dict(bones=["z_froth_crown_b"], freq=3.8, zeta=0.45, clamp=6.0, gain=0.008, rgain=0.0),
    "z_left": dict(bones=["z_froth_left_b"], freq=4.1, zeta=0.46, clamp=5.5, gain=0.007, rgain=0.0),
    "z_puddle": dict(bones=["z_froth_puddle_b"], freq=4.4, zeta=0.48, clamp=5.0, gain=0.007, rgain=0.0),
}

VIEW = dict(scale=0.80, W=820, H=520, ox=520, oy=455)


def _seg_dist(p, a, b):
    a = np.asarray(a, dtype=np.float64)
    b = np.asarray(b, dtype=np.float64)
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(float(ab @ ab), 1e-6), 0.0, 1.0)
    return np.hypot(*(p - (a + t[:, None] * ab)).T)


def weights(slot_name, world):
    world = np.asarray(world, dtype=np.float64)
    if slot_name == "c_collars":
        return weights("body", world)
    segs = SKIN[slot_name]
    if len(segs) == 1:
        return [[(segs[0][0], 1.0)] for _ in range(len(world))]
    D = np.stack([_seg_dist(world, a, b) for _, a, b in segs], axis=1)
    Wt = np.exp(-((D / 116.0) ** 2)) + 1e-9
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
