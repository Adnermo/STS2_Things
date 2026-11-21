"""ThingsSoulRoes (灵魂异鱼卵) — 12-Orb Brood Cluster + 6-Slingshot-Satellite Spine 4.2 Rig Definition.

Architecture:
- 640x520 rig canvas, ORIGIN = (320, 440), recommended .tscn scale = Vector2(0.36, 0.36), position = Vector2(0, 0)
  (matching ~194x150px in-battle cluster footprint aligned with CenterPos = Vector2(0, -72.5) & IntentPos = -155).
- 26 bones:
  - root, shadow_b, cluster_hub, aura_b
  - 4 pure-translation radial ring bones (ring_e, ring_w, ring_n, ring_s) driving exact Euclidean radial expansion
    of beckon_ring & intangible_shimmer with ZERO bone scaling (maxBoneScaleDev == 0, invertedTriangles == 0)!
  - 6 inner brood core bones (brood_0..5, r == 0.0 deg, sx == sy == 1.0)
  - 6 outer peripheral floating/slingshot satellite bones (sat_0..5, r == 0.0 deg, sx == sy == 1.0)
  - 6 golden slipstream comet trail bones (trail_0_b..trail_5_b, sx == sy == 1.0, allowed to orient along flight angle)
- 35 slots:
  - shadow
  - 12 high-res roe spheres (brood_0..5, sat_0..5) arranged in depth-sorted painter's order matching soul_roes.png
  - 12 individual additive yolk/membrane glows (brood_glow_0..5, sat_glow_0..5; setup alpha = 0)
  - 6 tapered golden comet slipstream trails (trail_0..5; setup alpha = 0)
  - Dual bioluminescent illumination layers:
    - beckon_aura + beckon_ring (warm sun-gold / amber brood luminescence + expanding halo ring)
    - intangible_aura + intangible_shimmer (blinding pearlescent aqua-white-gold SoulFysh spectral luminescence)
"""
import numpy as np

NAME = "soul_roes"
CANVAS = (640, 520)
ORIGIN = (320, 440)
HUB_CANVAS = (316, 232)
RING_SPAN = 260.0  # Barycentric half-span for exact 4-bone Euclidean radial expansion

# Exact 12-egg layout mapped 1:1 from soul_roes.png (357x279) onto 640x520 canvas
ORB_SPECS = {
    # 6 Inner Brood Core Orbs (brood_0..5)
    "brood_0": dict(center=(259, 105), diam=164, part="part_000"),  # Top-left crown
    "brood_1": dict(center=(394, 120), diam=158, part="part_001"),  # Top-right crown
    "brood_2": dict(center=(310, 194), diam=170, part="part_005"),  # Upper-center heart
    "brood_3": dict(center=(232, 248), diam=160, part="part_006"),  # Mid-left core
    "brood_4": dict(center=(361, 284), diam=172, part="part_002"),  # Mid-right core
    "brood_5": dict(center=(304, 347), diam=148, part="part_004"),  # Bottom-center front
    # 6 Outer Peripheral Slingshot Orbs (sat_0..5, ordered CCW around cluster_hub)
    "sat_0": dict(center=(158, 162), diam=152, part="part_007"),    # Upper-left (~156 deg CCW)
    "sat_1": dict(center=(122, 272), diam=144, part="part_008"),    # Far-left (~192 deg CCW)
    "sat_2": dict(center=(220, 353), diam=144, part="part_009"),    # Bottom-left (~232 deg CCW)
    "sat_3": dict(center=(436, 366), diam=148, part="part_010"),    # Bottom-right (~312 deg CCW)
    "sat_4": dict(center=(516, 308), diam=146, part="part_003"),    # Far-right (~339 deg CCW)
    "sat_5": dict(center=(466, 197), diam=152, part="part_011"),    # Upper-right (~13 deg CCW)
}

BROOD_NAMES = [f"brood_{i}" for i in range(6)]
SAT_NAMES = [f"sat_{i}" for i in range(6)]
ORB_NAMES = BROOD_NAMES + SAT_NAMES
NO_ROT_BONES = ["root", "shadow_b", "cluster_hub", "aura_b", "ring_e", "ring_w", "ring_n", "ring_s"] + ORB_NAMES

POS = {
    "root": ORIGIN,
    "shadow_b": ORIGIN,
    "cluster_hub": HUB_CANVAS,
    "aura_b": HUB_CANVAS,
    "ring_e": (HUB_CANVAS[0] + 200, HUB_CANVAS[1]),
    "ring_w": (HUB_CANVAS[0] - 200, HUB_CANVAS[1]),
    "ring_n": (HUB_CANVAS[0], HUB_CANVAS[1] - 200),
    "ring_s": (HUB_CANVAS[0], HUB_CANVAS[1] + 200),
}
for _oname, _spec in ORB_SPECS.items():
    POS[_oname] = _spec["center"]
for _k in range(6):
    POS[f"trail_{_k}_b"] = ORB_SPECS[f"sat_{_k}"]["center"]

BONES = [
    ("root", None, POS["root"]),
    ("shadow_b", "root", POS["shadow_b"]),
    ("cluster_hub", "root", POS["cluster_hub"]),
    ("aura_b", "cluster_hub", POS["aura_b"]),
    ("ring_e", "cluster_hub", POS["ring_e"]),
    ("ring_w", "cluster_hub", POS["ring_w"]),
    ("ring_n", "cluster_hub", POS["ring_n"]),
    ("ring_s", "cluster_hub", POS["ring_s"]),
    ("brood_0", "cluster_hub", POS["brood_0"]),
    ("brood_1", "cluster_hub", POS["brood_1"]),
    ("brood_2", "cluster_hub", POS["brood_2"]),
    ("brood_3", "cluster_hub", POS["brood_3"]),
    ("brood_4", "cluster_hub", POS["brood_4"]),
    ("brood_5", "cluster_hub", POS["brood_5"]),
    ("sat_0", "root", POS["sat_0"]),
    ("sat_1", "root", POS["sat_1"]),
    ("sat_2", "root", POS["sat_2"]),
    ("sat_3", "root", POS["sat_3"]),
    ("sat_4", "root", POS["sat_4"]),
    ("sat_5", "root", POS["sat_5"]),
    ("trail_0_b", "sat_0", POS["trail_0_b"]),
    ("trail_1_b", "sat_1", POS["trail_1_b"]),
    ("trail_2_b", "sat_2", POS["trail_2_b"]),
    ("trail_3_b", "sat_3", POS["trail_3_b"]),
    ("trail_4_b", "sat_4", POS["trail_4_b"]),
    ("trail_5_b", "sat_5", POS["trail_5_b"]),
]

# Exact painter's depth order matching soul_roes.png overlaps (with each trail immediately behind its satellite)
ORDER = [
    "shadow",
    "brood_1",
    "brood_0",
    "trail_1",
    "sat_1",
    "trail_2",
    "sat_2",
    "trail_0",
    "sat_0",
    "brood_2",
    "trail_5",
    "sat_5",
    "brood_4",
    "brood_3",
    "trail_4",
    "sat_4",
    "brood_5",
    "trail_3",
    "sat_3",
]

# Additive bioluminescent slots (each starts with setup color "ffffff00", blend="additive", GLOW_ALPHA=1.0)
GLOW_OF = {
    **{f"brood_glow_{i}": f"brood_{i}" for i in range(6)},
    **{f"sat_glow_{i}": f"sat_{i}" for i in range(6)},
    "beckon_aura": "brood_5",
    "beckon_ring": "brood_5",
    "intangible_aura": "sat_3",
    "intangible_shimmer": "sat_3",
}

GLOW_SKIN = {
    **{f"brood_glow_{i}": f"brood_{i}" for i in range(6)},
    **{f"sat_glow_{i}": f"sat_{i}" for i in range(6)},
    "beckon_aura": "beckon_aura",
    "beckon_ring": "beckon_ring",
    "intangible_aura": "intangible_aura",
    "intangible_shimmer": "intangible_shimmer",
}

GLOW_SCALE = 0.65
GLOW_ALPHA = 1.0  # Direct 1:1 alpha mapping so f.glow[g] == 1.0 produces slot.color.a == 1.0 (> 0.90!)

SLOT_DEFAULT_ALPHA = {
    "shadow": 0.42,
    **{f"trail_{i}": 0.0 for i in range(6)},
    **{f"sat_glow_{i}": 0.0 for i in range(6)},
    "beckon_aura": 0.0,
    "beckon_ring": 0.0,
    "intangible_aura": 0.0,
    "intangible_shimmer": 0.0,
}

SKIN = {
    "shadow": [("shadow_b", (120, 440), (520, 440))],
    **{f"brood_{i}": [(f"brood_{i}", (0, 0), (0, 0))] for i in range(6)},
    **{f"sat_{i}": [(f"sat_{i}", (0, 0), (0, 0))] for i in range(6)},
    **{f"trail_{i}": [(f"trail_{i}_b", (0, 0), (0, 0))] for i in range(6)},
    "beckon_aura": [("aura_b", (0, 0), (0, 0))],
    "intangible_aura": [("aura_b", (0, 0), (0, 0))],
    "beckon_ring": [
        ("ring_e", (0, 0), (0, 0)),
        ("ring_w", (0, 0), (0, 0)),
        ("ring_n", (0, 0), (0, 0)),
        ("ring_s", (0, 0), (0, 0)),
    ],
    "intangible_shimmer": [
        ("ring_e", (0, 0), (0, 0)),
        ("ring_w", (0, 0), (0, 0)),
        ("ring_n", (0, 0), (0, 0)),
        ("ring_s", (0, 0), (0, 0)),
    ],
}

SPACING = {
    "shadow": 120,
    **{f"brood_{i}": 34 for i in range(6)},
    **{f"sat_{i}": 34 for i in range(6)},
    **{f"brood_glow_{i}": 48 for i in range(6)},
    **{f"sat_glow_{i}": 48 for i in range(6)},
    **{f"trail_{i}": 38 for i in range(6)},
    "beckon_aura": 64,
    "beckon_ring": 44,
    "intangible_aura": 64,
    "intangible_shimmer": 46,
}

# Wide arena camera framing (0.38x) so -640px left slingshot volleys and +260px radial bursts fit in full!
VIEW = dict(scale=0.42, W=620, H=320, ox=425, oy=248)


def weights(slot_name: str, world: np.ndarray):
    """Exact rigid single-bone weights for orbs/trails + exact 4-bone Euclidean radial expansion for rings."""
    world = np.asarray(world, dtype=np.float64)
    if slot_name in ("beckon_ring", "intangible_shimmer"):
        # Canvas X right, Canvas Y down -> Spine X = x - cx, Spine Y = cy - y
        cx, cy = HUB_CANVAS
        sx = np.clip(world[:, 0] - cx, -RING_SPAN + 4.0, RING_SPAN - 4.0)
        sy = np.clip(cy - world[:, 1], -RING_SPAN + 4.0, RING_SPAN - 4.0)
        denom = 4.0 * RING_SPAN
        we = 0.25 + sx / denom
        ww = 0.25 - sx / denom
        wn = 0.25 + sy / denom
        ws = 0.25 - sy / denom
        out = []
        for i in range(len(world)):
            s = float(we[i] + ww[i] + wn[i] + ws[i])
            out.append([
                ("ring_e", float(we[i]) / s),
                ("ring_w", float(ww[i]) / s),
                ("ring_n", float(wn[i]) / s),
                ("ring_s", float(ws[i]) / s),
            ])
        return out

    segs = SKIN[slot_name]
    bname = segs[0][0]
    return [[(bname, 1.0)] for _ in range(len(world))]
