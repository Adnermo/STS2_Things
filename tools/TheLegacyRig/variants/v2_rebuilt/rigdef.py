"""Anatomical Soft-Body Field Rig Definition for The Legacy (腐化之遗) — v2_rebuilt.

All setup coordinates in source-image pixels (1358 x 689).
ORIGIN = (679.0, 660.0) maps to Spine (0.0, 0.0) so rest setup pose matches src.png 1:1.
"""
import cv2
import numpy as np

NAME = 'the_legacy'
H_IMG, W_IMG = 689, 1358
ORIGIN = (679.0, 660.0)


def S(p):
    return (round(float(p[0]) - ORIGIN[0], 2), round(ORIGIN[1] - float(p[1]), 2))


def smoothstep_np(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def poly_field(poly, sigma):
    arr = np.zeros((H_IMG, W_IMG), np.float32)
    cv2.fillPoly(arr, [np.array(poly, np.int32)], 1.0)
    return cv2.GaussianBlur(arr, (0, 0), sigma)


# ---------------- Anatomical Bone Hierarchy (parent always precedes child)
BONES = [
    # Immovable wet stone dock ground anchor
    ('root', None, (679.0, 660.0)),

    # 1. Myocardium chambers & dual arterial cannons
    ('conus_atrium', 'root', (540.0, 285.0)),
    ('cannon_upper', 'conus_atrium', (315.0, 258.0)),
    ('orifice_ta', 'cannon_upper', (169.0, 257.0)),
    ('cannon_lower', 'conus_atrium', (290.0, 368.0)),
    ('orifice_tb', 'cannon_lower', (121.0, 374.0)),

    ('vent_dome', 'root', (745.0, 395.0)),
    ('vent_mid', 'root', (485.0, 470.0)),
    ('apex_upper', 'vent_mid', (215.0, 472.0)),
    ('vent_low', 'root', (585.0, 585.0)),
    ('apex_lower', 'vent_low', (275.0, 595.0)),
    ('vent_right', 'root', (935.0, 515.0)),

    # 2. Dorsal purple aortic arch & drainage pipes
    ('dorsal_arch', 'root', (885.0, 255.0)),
    ('aorta_crown', 'dorsal_arch', (785.0, 195.0)),
    ('pipe_pl', 'aorta_crown', (720.0, 118.0)),
    ('orifice_pl', 'pipe_pl', (651.0, 83.0)),
    ('pipe_pr', 'aorta_crown', (755.0, 96.0)),
    ('orifice_pr', 'pipe_pr', (726.0, 38.0)),

    ('dorsal_drain', 'root', (1120.0, 470.0)),
    ('drain_a', 'dorsal_drain', (1220.0, 428.0)),
    ('orifice_pa', 'drain_a', (1325.0, 432.0)),
    ('drain_b', 'dorsal_drain', (1178.0, 535.0)),
    ('orifice_pb', 'drain_b', (1223.0, 590.0)),

    # 3. Venous blue right manifold & intake pipes
    ('venous_sinus', 'root', (985.0, 395.0)),
    ('vena_up', 'venous_sinus', (1165.0, 355.0)),
    ('orifice_bu', 'vena_up', (1202.0, 281.0)),
    ('vena_down', 'venous_sinus', (1195.0, 442.0)),
    ('orifice_bd', 'vena_down', (1281.0, 502.0)),
    ('orifice_bb', 'venous_sinus', (1059.0, 549.0)),
    ('orifice_bm', 'venous_sinus', (1114.0, 403.0)),
]

BONE_POS = {n: pos for n, _, pos in BONES}

# Mesh grid spacing (px)
SPACING = {
    'dorsal_purple': 14,
    'venous_blue': 14,
    'myocardium': 13,
}
DEFAULT_FLORA_SPACING = 14

# 3-zone traveling coronary glow slots reuse EXACT myocardium mesh & weights (0.000px drift!)
GLOW_OF = {
    'glow_sa_root': 'myocardium',
    'glow_av_bulb': 'myocardium',
    'glow_purkinje_apex': 'myocardium',
}
GLOW_SCALE = 0.5
GLOW_ALPHA = 0.88

# ---------------- Flora Spring Chains Configuration
# Ground-rooted flora: parent='root' (100% locked dock base), shock_driver=adjacent heart bone for shockwave coupling
GROUND_FLORA = {
    'coral_purple': dict(base=(873.0, 668.0), tip=(865.0, 470.0), nb=2, driver='vent_right', freq=1.95, zeta=0.36, gain=0.038, clamp=13.0),
    'weed_bottom_mid': dict(base=(574.0, 668.0), tip=(532.0, 420.0), nb=3, driver='vent_low', freq=1.35, zeta=0.28, gain=0.045, clamp=12.5),
    'weed_bottom_right': dict(base=(1098.0, 622.0), tip=(1180.0, 425.0), nb=3, driver='dorsal_drain', freq=1.40, zeta=0.27, gain=0.050, clamp=15.0),
    'weed_left': dict(base=(82.0, 462.0), tip=(75.0, 165.0), nb=3, driver='apex_upper', freq=1.28, zeta=0.25, gain=0.055, clamp=16.0),
    'weed_ground_left': dict(base=(158.0, 582.0), tip=(12.0, 535.0), nb=2, driver='apex_upper', freq=1.55, zeta=0.30, gain=0.044, clamp=14.0),
    'weed_ground_bl': dict(base=(308.0, 665.0), tip=(435.0, 608.0), nb=2, driver='apex_lower', freq=1.65, zeta=0.32, gain=0.040, clamp=12.0),
    'weed_ground_right': dict(base=(1252.0, 545.0), tip=(1346.0, 532.0), nb=2, driver='orifice_bd', freq=1.65, zeta=0.30, gain=0.042, clamp=14.0),
    'weed_edge_right': dict(base=(1304.0, 496.0), tip=(1355.0, 472.0), nb=2, driver='orifice_pa', freq=1.75, zeta=0.32, gain=0.040, clamp=13.0),
}

# Organ-mounted flora: base welded directly to host organ weight field, crown driven by 240 Hz spring
ORGAN_FLORA = {
    'coral_pink': dict(base=(288.0, 455.0), tip=(288.0, 352.0), nb=2, parent='vent_mid', host='myocardium', freq=2.05, zeta=0.36, gain=0.038, clamp=12.5),
    'weed_top_left': dict(base=(292.0, 255.0), tip=(316.0, 10.0), nb=3, parent='conus_atrium', host='myocardium', freq=1.25, zeta=0.26, gain=0.048, clamp=14.0),
    'weed_back_a': dict(base=(670.0, 240.0), tip=(616.0, 124.0), nb=2, parent='dorsal_arch', host='dorsal_purple', freq=1.45, zeta=0.27, gain=0.050, clamp=15.0),
    'weed_back_b': dict(base=(752.0, 190.0), tip=(806.0, 58.0), nb=2, parent='aorta_crown', host='dorsal_purple', freq=1.45, zeta=0.27, gain=0.050, clamp=15.0),
    'weed_back_c': dict(base=(918.0, 136.0), tip=(938.0, 42.0), nb=2, parent='dorsal_arch', host='dorsal_purple', freq=1.55, zeta=0.28, gain=0.048, clamp=15.0),
    'weed_back_d': dict(base=(925.0, 250.0), tip=(948.0, 146.0), nb=2, parent='dorsal_arch', host='dorsal_purple', freq=1.50, zeta=0.28, gain=0.048, clamp=15.0),
    'weed_right': dict(base=(1144.0, 315.0), tip=(1128.0, 148.0), nb=3, parent='dorsal_arch', host='dorsal_purple', freq=1.35, zeta=0.27, gain=0.046, clamp=13.5),
    'weed_blue': dict(base=(1043.0, 380.0), tip=(1012.0, 284.0), nb=2, parent='venous_sinus', host='venous_blue', freq=1.55, zeta=0.30, gain=0.042, clamp=12.0),
}

# ---------------- Precomputed Smooth Anatomical Weight Fields
MYO_CHAMBERS = {
    'conus_atrium': poly_field([(345, 185), (668, 190), (665, 355), (495, 382), (350, 355)], 46.0),
    'cannon_upper': poly_field([(140, 210), (360, 218), (360, 308), (140, 308)], 38.0),
    'cannon_lower': poly_field([(90, 310), (352, 310), (352, 412), (90, 415)], 42.0),
    'vent_dome': poly_field([(610, 250), (868, 250), (878, 505), (602, 535)], 50.0),
    'vent_mid': poly_field([(315, 385), (622, 365), (612, 525), (315, 525)], 48.0),
    'apex_upper': poly_field([(65, 412), (325, 412), (325, 548), (65, 555)], 45.0),
    'vent_low': poly_field([(415, 518), (778, 500), (795, 665), (415, 665)], 50.0),
    'apex_lower': poly_field([(130, 545), (425, 530), (425, 668), (130, 668)], 44.0),
    'vent_right': poly_field([(780, 415), (1045, 415), (1045, 665), (775, 665)], 48.0),
}

PURPLE_CHAMBERS = {
    'dorsal_arch': poly_field([(645, 195), (1090, 175), (1125, 405), (875, 415), (645, 345)], 48.0),
    'aorta_crown': poly_field([(680, 125), (865, 125), (865, 245), (695, 245)], 38.0),
    'pipe_pl': poly_field([(600, 50), (735, 85), (750, 175), (640, 175)], 32.0),
    'pipe_pr': poly_field([(685, 8), (808, 20), (815, 155), (725, 145)], 32.0),
    'dorsal_drain': poly_field([(960, 345), (1215, 360), (1205, 545), (960, 535)], 48.0),
    'drain_a': poly_field([(1195, 378), (1356, 388), (1356, 478), (1195, 478)], 34.0),
    'drain_b': poly_field([(1120, 495), (1305, 515), (1305, 630), (1120, 630)], 36.0),
}

BLUE_CHAMBERS = {
    'venous_sinus': poly_field([(850, 300), (1155, 330), (1155, 590), (850, 590)], 46.0),
    'vena_up': poly_field([(1125, 240), (1265, 240), (1265, 395), (1125, 385)], 35.0),
    'vena_down': poly_field([(1145, 390), (1340, 435), (1340, 555), (1155, 505)], 36.0),
}

# Localized Gaussian orifice rim fields: (bone, (cx, cy), sigma_px, max_blend)
# Note: max_blend <= 0.78 & sigma >= 34px mathematically guarantees det(J) > 0.85 even at +-30% flare/pinch!
ORIFICE_FIELDS = {
    'myocardium': [
        ('orifice_ta', (169.0, 257.0), 40.0, 0.78),
        ('orifice_tb', (121.0, 374.0), 40.0, 0.78),
    ],
    'dorsal_purple': [
        ('orifice_pl', (651.0, 83.0), 38.0, 0.78),
        ('orifice_pr', (726.0, 38.0), 38.0, 0.78),
        ('orifice_pa', (1325.0, 432.0), 40.0, 0.78),
        ('orifice_pb', (1223.0, 590.0), 42.0, 0.76),
    ],
    'venous_blue': [
        ('orifice_bu', (1202.0, 281.0), 38.0, 0.78),
        ('orifice_bd', (1281.0, 502.0), 40.0, 0.78),
        ('orifice_bb', (1059.0, 549.0), 36.0, 0.75),
        ('orifice_bm', (1114.0, 403.0), 34.0, 0.72),
    ],
}

ORGAN_CHAMBER_MAP = {
    'myocardium': MYO_CHAMBERS,
    'dorsal_purple': PURPLE_CHAMBERS,
    'venous_blue': BLUE_CHAMBERS,
}


def weights_organ(organ_name: str, points) -> list:
    """Continuous smooth field weights for any organ macro-layer + localized orifices + Ground Contact Pad."""
    pts = np.asarray(points, dtype=np.float32)
    xi = np.clip(np.round(pts[:, 0]).astype(int), 0, W_IMG - 1)
    yi = np.clip(np.round(pts[:, 1]).astype(int), 0, H_IMG - 1)

    chambers = ORGAN_CHAMBER_MAP[organ_name]
    b_names = list(chambers.keys())
    raw = np.zeros((len(pts), len(b_names)), dtype=np.float64)
    for k, b in enumerate(b_names):
        center = np.array(BONE_POS[b], dtype=np.float32)
        d2 = np.sum((pts - center) ** 2, axis=1)
        fallback = 1e-3 * np.exp(-d2 / (190.0 ** 2))
        raw[:, k] = chambers[b][yi, xi] + fallback

    raw /= np.maximum(raw.sum(axis=1, keepdims=True), 1e-12)

    out = []
    orifices = ORIFICE_FIELDS[organ_name]
    orf_vals = []
    for ob, (cx, cy), sig, strength in orifices:
        d2 = (pts[:, 0] - cx) ** 2 + (pts[:, 1] - cy) ** 2
        orf_vals.append((ob, strength * np.exp(-d2 / (sig * sig))))

    # Ground Contact Pad weight field: smoothstep from y_img=588..646 up to 0.955 locked to 'root'
    # Keeps bottom 5% strictly anchored on the wet stone floor with zero levitation & zero sinking!
    g_pad = 0.955 * smoothstep_np(588.0, 646.0, pts[:, 1])

    for i in range(len(pts)):
        acc = {b_names[k]: float(raw[i, k]) for k in range(len(b_names)) if raw[i, k] > 1e-5}
        # Blend localized vessel orifice fields smoothly
        for ob, vals in orf_vals:
            w_o = float(vals[i])
            if w_o > 0.002:
                acc = {k: v * (1.0 - w_o) for k, v in acc.items()}
                acc[ob] = acc.get(ob, 0.0) + w_o
        # Blend Ground Contact Pad to 'root'
        gp = float(g_pad[i])
        if gp > 0.002:
            acc = {k: v * (1.0 - gp) for k, v in acc.items()}
            acc['root'] = acc.get('root', 0.0) + gp

        acc = {k: v for k, v in acc.items() if v > 0.003}
        tot = sum(acc.values())
        out.append([(k, v / tot) for k, v in sorted(acc.items(), key=lambda kv: -kv[1])])
    return out
