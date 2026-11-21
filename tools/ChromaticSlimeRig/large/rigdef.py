"""Chromatic Slime (Large) — v4 Vanilla-STS2 Peristaltic Floor-Locked 39-Bone Spine 4.2 Rig.

Architecture:
- 39 bones, 27 slots (20 body/mineral/eye layers + 3 matte-gouache projectiles + 3 hardware eye CLIPS + prism_glow)
- Strict ZERO bone scaling (scaleX == 1.0, scaleY == 1.0 across every bone & frame!)
- Strict ZERO body/lobe rotation (r == 0.0 deg on body_base, puddle_*_foot, flank_*, belly_sag, mid_torus,
  brow_crimson, hump_amber, lobe_azure, amber_goo_* across all 8 animations!)
- Floor-locked puddle sole skinning (bottom puddle stays 100% horizontal & flush on y=0!)
- 3 hand-painted matte-gouache projectiles (proj_crimson, proj_amber, proj_azure; default alpha = 0.0)
- 3 independent unscaled ochre irises + 3 unscaled gouache eyelids + hardware eye_clip_* polygons
"""
import numpy as np

NAME = "chromatic_slime_large"
ORIGIN = (518, 606)

# Exact eye socket clipping polygons (in 1082x635 cutout space, matching extract_large.py)
EYE_CLIP_MAIN = [
    (244, 285), (275, 290), (318, 294), (355, 295), (383, 296),
    (382, 309), (373, 326), (357, 341), (336, 350), (309, 353),
    (281, 346), (258, 328), (246, 305), (244, 292),
]
EYE_CLIP_UP = [
    (412, 255), (424, 245), (442, 234), (460, 223), (462, 234),
    (457, 249), (447, 259), (433, 265), (419, 263), (412, 259),
]
EYE_CLIP_RT = [
    (470, 319), (485, 309), (508, 297), (533, 284), (536, 295),
    (533, 308), (524, 320), (508, 325), (487, 325), (473, 324),
]

POS = {
    "root": (518, 606),
    "shadow_b": (518, 606),
    "drop_near_b": (893, 592),
    "drop_far_b": (1038, 550),
    "proj_c_b": (270, 180),
    "proj_a_b": (562, 132),
    "proj_z_b": (754, 370),
    "body_base": (518, 558),
    "puddle_l_foot": (145, 566),
    "puddle_c_foot": (525, 588),
    "puddle_r_foot": (885, 555),
    "flank_l": (228, 456),
    "flank_r": (778, 452),
    "belly_sag": (465, 432),
    "inner_core": (495, 415),
    "drip_1": (432, 515),
    "drip_2": (532, 492),
    "drip_3": (735, 498),
    "drip_4": (804, 502),
    "mid_torus": (468, 305),
    "brow_crimson": (298, 185),
    "hump_amber": (575, 165),
    "lobe_azure": (768, 378),
    "amber_goo_b": (582, 152),
    "amber_goo_tip": (692, 198),
    "crimson_shard_l_b": (226, 190),
    "crimson_shard_tall_b": (278, 138),
    "crimson_shard_r_b": (356, 146),
    "amber_rock_l_b": (526, 72),
    "amber_rock_main_b": (618, 86),
    "amber_rock_r_b": (668, 128),
    "azure_froth_main_b": (782, 388),
    "azure_froth_spores_b": (805, 398),
    "iris_main_b": (304, 304),
    "lid_main_b": (312, 268),
    "iris_up_b": (433, 242),
    "lid_up_b": (436, 222),
    "iris_rt_b": (496, 305),
    "lid_rt_b": (502, 282),
}

BONES = [
    ("root", None, POS["root"]),
    ("shadow_b", "root", POS["shadow_b"]),
    ("drop_near_b", "root", POS["drop_near_b"]),
    ("drop_far_b", "root", POS["drop_far_b"]),
    ("proj_c_b", "root", POS["proj_c_b"]),
    ("proj_a_b", "root", POS["proj_a_b"]),
    ("proj_z_b", "root", POS["proj_z_b"]),
    ("body_base", "root", POS["body_base"]),
    ("puddle_l_foot", "body_base", POS["puddle_l_foot"]),
    ("puddle_c_foot", "body_base", POS["puddle_c_foot"]),
    ("puddle_r_foot", "body_base", POS["puddle_r_foot"]),
    ("flank_l", "body_base", POS["flank_l"]),
    ("flank_r", "body_base", POS["flank_r"]),
    ("belly_sag", "body_base", POS["belly_sag"]),
    ("inner_core", "body_base", POS["inner_core"]),
    ("drip_1", "belly_sag", POS["drip_1"]),
    ("drip_2", "belly_sag", POS["drip_2"]),
    ("drip_3", "flank_r", POS["drip_3"]),
    ("drip_4", "flank_r", POS["drip_4"]),
    ("mid_torus", "belly_sag", POS["mid_torus"]),
    ("brow_crimson", "mid_torus", POS["brow_crimson"]),
    ("hump_amber", "mid_torus", POS["hump_amber"]),
    ("lobe_azure", "flank_r", POS["lobe_azure"]),
    ("amber_goo_b", "hump_amber", POS["amber_goo_b"]),
    ("amber_goo_tip", "amber_goo_b", POS["amber_goo_tip"]),
    ("crimson_shard_l_b", "brow_crimson", POS["crimson_shard_l_b"]),
    ("crimson_shard_tall_b", "brow_crimson", POS["crimson_shard_tall_b"]),
    ("crimson_shard_r_b", "brow_crimson", POS["crimson_shard_r_b"]),
    ("amber_rock_l_b", "hump_amber", POS["amber_rock_l_b"]),
    ("amber_rock_main_b", "hump_amber", POS["amber_rock_main_b"]),
    ("amber_rock_r_b", "hump_amber", POS["amber_rock_r_b"]),
    ("azure_froth_main_b", "lobe_azure", POS["azure_froth_main_b"]),
    ("azure_froth_spores_b", "lobe_azure", POS["azure_froth_spores_b"]),
    ("iris_main_b", "mid_torus", POS["iris_main_b"]),
    ("lid_main_b", "mid_torus", POS["lid_main_b"]),
    ("iris_up_b", "mid_torus", POS["iris_up_b"]),
    ("lid_up_b", "mid_torus", POS["lid_up_b"]),
    ("iris_rt_b", "mid_torus", POS["iris_rt_b"]),
    ("lid_rt_b", "mid_torus", POS["lid_rt_b"]),
]

ORDER = [
    "shadow",
    "drop_far",
    "body_dome",
    "inner_gel",
    "amber_goo_cap",
    "eye_clip_main",
    "iris_main",
    "lid_main",
    "eye_clip_up",
    "iris_up",
    "lid_up",
    "eye_clip_rt",
    "iris_rt",
    "lid_rt",
    "crimson_shard_l",
    "crimson_shard_tall",
    "crimson_shard_r",
    "amber_rock_l",
    "amber_rock_main",
    "amber_rock_r",
    "azure_froth_main",
    "azure_froth_spores",
    "drop_near",
    "proj_crimson",
    "proj_amber",
    "proj_azure",
]

CLIPS = {
    "eye_clip_main": {"host": "body_dome", "end": "lid_main", "poly": EYE_CLIP_MAIN},
    "eye_clip_up": {"host": "body_dome", "end": "lid_up", "poly": EYE_CLIP_UP},
    "eye_clip_rt": {"host": "body_dome", "end": "lid_rt", "poly": EYE_CLIP_RT},
}

SLOT_DEFAULT_ALPHA = {
    "proj_crimson": 0.0,
    "proj_amber": 0.0,
    "proj_azure": 0.0,
}

GLOW_OF = {"prism_glow": "body_dome"}
GLOW_SKIN = {"prism_glow": "body_dome"}
GLOW_SCALE = 0.5
GLOW_ALPHA = 0.14

SKIN = {
    "shadow": [("shadow_b", (40, 606), (1040, 606))],
    "drop_near": [("drop_near_b", (0, 0), (0, 0))],
    "drop_far": [("drop_far_b", (0, 0), (0, 0))],
    "proj_crimson": [("proj_c_b", (0, 0), (0, 0))],
    "proj_amber": [("proj_a_b", (0, 0), (0, 0))],
    "proj_azure": [("proj_z_b", (0, 0), (0, 0))],
    "body_dome": [
        ("puddle_l_foot", (28, 568), (285, 582)),
        ("puddle_c_foot", (355, 594), (725, 592)),
        ("puddle_r_foot", (785, 564), (985, 550)),
        ("flank_l", (145, 488), (285, 422)),
        ("flank_r", (695, 476), (835, 442)),
        ("belly_sag", (325, 448), (585, 422)),
        ("drip_1", (432, 515), (432, 558)),
        ("drip_2", (512, 502), (550, 538)),
        ("drip_3", (735, 502), (735, 552)),
        ("drip_4", (804, 504), (804, 535)),
        ("mid_torus", (270, 315), (540, 285)),
        ("brow_crimson", (215, 196), (372, 148)),
        ("hump_amber", (465, 118), (685, 185)),
        ("lobe_azure", (725, 345), (825, 418)),
    ],
    "inner_gel": [("inner_core", (360, 480), (620, 350))],
    "amber_goo_cap": [
        ("hump_amber", (450, 102), (565, 132)),
        ("amber_goo_b", (545, 105), (655, 165)),
        ("amber_goo_tip", (645, 178), (712, 224)),
        ("mid_torus", (460, 215), (560, 245)),
    ],
    "crimson_shard_l": [("crimson_shard_l_b", (0, 0), (0, 0))],
    "crimson_shard_tall": [("crimson_shard_tall_b", (0, 0), (0, 0))],
    "crimson_shard_r": [("crimson_shard_r_b", (0, 0), (0, 0))],
    "amber_rock_l": [("amber_rock_l_b", (0, 0), (0, 0))],
    "amber_rock_main": [("amber_rock_main_b", (0, 0), (0, 0))],
    "amber_rock_r": [("amber_rock_r_b", (0, 0), (0, 0))],
    "azure_froth_main": [("azure_froth_main_b", (0, 0), (0, 0))],
    "azure_froth_spores": [("azure_froth_spores_b", (0, 0), (0, 0))],
    "iris_main": [("iris_main_b", (0, 0), (0, 0))],
    "lid_main": [("lid_main_b", (0, 0), (0, 0))],
    "iris_up": [("iris_up_b", (0, 0), (0, 0))],
    "lid_up": [("lid_up_b", (0, 0), (0, 0))],
    "iris_rt": [("iris_rt_b", (0, 0), (0, 0))],
    "lid_rt": [("lid_rt_b", (0, 0), (0, 0))],
}

SPACING = {
    "shadow": 180,
    "drop_near": 24,
    "drop_far": 24,
    "proj_crimson": 28,
    "proj_amber": 28,
    "proj_azure": 28,
    "body_dome": 22,
    "prism_glow": 64,
    "inner_gel": 38,
    "amber_goo_cap": 20,
    "crimson_shard_l": 28,
    "crimson_shard_tall": 28,
    "crimson_shard_r": 28,
    "amber_rock_l": 28,
    "amber_rock_main": 32,
    "amber_rock_r": 28,
    "azure_froth_main": 26,
    "azure_froth_spores": 26,
    "iris_main": 20,
    "lid_main": 22,
    "iris_up": 16,
    "lid_up": 18,
    "iris_rt": 16,
    "lid_rt": 18,
}

CHAINS = {
    "drip_1": dict(bones=["drip_1"], freq=2.8, zeta=0.48, clamp=7.0, gain=0.005, rgain=0.015),
    "drip_2": dict(bones=["drip_2"], freq=2.6, zeta=0.48, clamp=7.0, gain=0.005, rgain=0.015),
    "drip_3": dict(bones=["drip_3"], freq=2.5, zeta=0.50, clamp=7.0, gain=0.005, rgain=0.015),
    "drip_4": dict(bones=["drip_4"], freq=3.0, zeta=0.52, clamp=6.0, gain=0.005, rgain=0.015),
}

# Exact 0.42 in-game scale framing with generous left arena space to inspect full projectile arcs!
VIEW = dict(scale=0.42, W=1080, H=520, ox=650, oy=465)


def _seg_dist(p: np.ndarray, a: tuple[float, float], b: tuple[float, float]) -> np.ndarray:
    a_arr = np.asarray(a, dtype=np.float64)
    b_arr = np.asarray(b, dtype=np.float64)
    ab = b_arr - a_arr
    t = np.clip(((p - a_arr) @ ab) / max(float(ab @ ab), 1e-6), 0.0, 1.0)
    return np.hypot(*(p - (a_arr + t[:, None] * ab)).T)


def weights(slot_name: str, world: np.ndarray):
    """Smooth C-infinity Gaussian weights with Floor-Sole Lock & Eye-Socket Lock (0 inverted triangles!)."""
    world = np.asarray(world, dtype=np.float64)
    segs = SKIN[slot_name]
    if len(segs) == 1:
        return [[(segs[0][0], 1.0)] for _ in range(len(world))]

    D = np.stack([_seg_dist(world, a, b) for _, a, b in segs], axis=1)

    if slot_name == "amber_goo_cap":
        Wt = np.exp(-((D / 114.0) ** 2)) + 1e-9
    else:
        sig = np.full((1, len(segs)), 146.0, dtype=np.float64)
        for k, (bname, _, _) in enumerate(segs):
            if bname.startswith("drip_"):
                sig[0, k] = 68.0
        Wt = np.exp(-((D / sig) ** 2)) + 1e-9

        x = world[:, 0]
        y = world[:, 1]
        drip_gate = np.exp(-((np.maximum(0.0, 482.0 - y) / 28.0) ** 2)) * np.exp(-((np.maximum(0.0, y - 556.0) / 20.0) ** 2))
        for k, (bname, _, _) in enumerate(segs):
            if bname.startswith("drip_"):
                Wt[:, k] *= 0.50 * drip_gate

        # Smooth 155px Floor-Sole Gradient: keeps bottom contact rim (y >= 590) horizontal on puddle_*_foot
        # while maintaining strictly positive vertical Jacobian (0 inverted triangles even in deep puddle melt!)
        u_floor = np.clip((y - 442.0) / 156.0, 0.0, 1.0)
        floor_lock = u_floor ** 2.4
        for k, (bname, _, _) in enumerate(segs):
            if bname.startswith("puddle_"):
                Wt[:, k] *= 1.0 + 7.5 * floor_lock
            else:
                Wt[:, k] *= 1.0 - 0.90 * floor_lock

        # Smooth Eye-Socket Lock: anchors the 3 eye socket cavities to mid_torus so unscaled irises & lids never drift!
        eye_zone = (
            np.exp(-(((x - 314.0) / 104.0) ** 2 + ((y - 312.0) / 62.0) ** 2))
            + np.exp(-(((x - 436.0) / 64.0) ** 2 + ((y - 242.0) / 48.0) ** 2))
            + np.exp(-(((x - 502.0) / 68.0) ** 2 + ((y - 302.0) / 50.0) ** 2))
        )
        eye_lock = np.clip(eye_zone, 0.0, 1.0) * (1.0 - floor_lock)
        mid_idx = next(k for k, (bn, _, _) in enumerate(segs) if bn == "mid_torus")
        Wt[:, mid_idx] += 26.0 * eye_lock

    Wt /= Wt.sum(axis=1, keepdims=True)
    out = []
    for i in range(len(world)):
        acc = {}
        for k in range(len(segs)):
            if Wt[i, k] > 0.010:
                acc[segs[k][0]] = acc.get(segs[k][0], 0.0) + float(Wt[i, k])
        s = sum(acc.values())
        out.append([(b, w / s) for b, w in acc.items()])
    return out
