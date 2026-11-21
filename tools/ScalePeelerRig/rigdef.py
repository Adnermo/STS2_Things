"""Scale-Peeler Wyrm rig: continuous 24-bone spine, hinged scraper jaws, spring feelers, and 4 dorsal armor crystals."""
import numpy as np


def _seg_dist(p, a, b):
    a = np.asarray(a, dtype=np.float64)
    b = np.asarray(b, dtype=np.float64)
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(float(ab @ ab), 1e-6), 0.0, 1.0)
    return np.hypot(*(p - (a + t[:, None] * ab)).T), t

NAME = 'scale_peeler'
ORIGIN = (555, 898)
VIEW = dict(scale=0.52, W=900, H=620, ox=460, oy=565)

ORDER = [
    'burrow_back',
    'plate_0',
    'plate_1',
    'plate_3',
    'feeler_far',
    'mouth_interior',
    'jaw_lower',
    'jaw_upper',
    'body',
    'eye_far_lid',
    'eye_near_lid',
    'feeler_near',
    'plate_2',
    'burrow_front',
]

GLOW_OF = {'mouth_glow': 'mouth_interior'}
GLOW_SCALE = 1.0
NOSCALE = ('mouth_glow',)
GLOW_ALPHA = 0.90

SPACING = {
    'body': 18,
    'burrow_back': 36,
    'burrow_front': 36,
    'jaw_upper': 18,
    'jaw_lower': 18,
    'mouth_interior': 20,
    'feeler_far': 10,
    'feeler_near': 10,
    'plate_0': 24,
    'plate_1': 24,
    'plate_2': 24,
    'plate_3': 24,
    'eye_far_lid': 8,
    'eye_near_lid': 8,
}

BONES = [
    ('root', None, ORIGIN),
    ('burrow', 'root', (555, 845)),
    ('trunk_base', 'root', (515, 825)),
    ('seg_0', 'trunk_base', (500, 725)),
    ('seg_1', 'seg_0', (528, 600)),
    ('seg_2', 'seg_1', (598, 465)),
    ('seg_3', 'seg_2', (630, 335)),
    ('plate_3', 'seg_3', (718, 342)),
    ('seg_4', 'seg_3', (575, 222)),
    ('plate_2', 'seg_4', (670, 228)),
    ('plate_1', 'seg_4', (562, 178)),
    ('seg_5', 'seg_4', (458, 195)),
    ('plate_0', 'seg_5', (432, 182)),
    ('neck', 'seg_5', (368, 222)),
    ('head', 'neck', (286, 265)),
    ('mouth_core', 'head', (252, 365)),
    ('mouth_vfx', 'head', (192, 366)),
    ('jaw_upper', 'head', (228, 308)),
    ('jaw_lower', 'head', (305, 352)),
    ('eye_far_lid', 'head', (183, 248)),
    ('eye_near_lid', 'head', (300, 264)),
    ('feeler_far_0', 'head', (205, 196)),
    ('feeler_far_1', 'feeler_far_0', (180, 174)),
    ('feeler_near_0', 'head', (282, 188)),
    ('feeler_near_1', 'feeler_near_0', (268, 158)),
]

# Required by rigkit to populate slot default parent bone
SKIN = {
    'burrow_back': [('burrow', (0, 0), (0, 0))],
    'burrow_front': [('burrow', (0, 0), (0, 0))],
    'plate_0': [('plate_0', (0, 0), (0, 0))],
    'plate_1': [('plate_1', (0, 0), (0, 0))],
    'plate_2': [('plate_2', (0, 0), (0, 0))],
    'plate_3': [('plate_3', (0, 0), (0, 0))],
    'jaw_upper': [('jaw_upper', (0, 0), (0, 0))],
    'jaw_lower': [('jaw_lower', (0, 0), (0, 0))],
    'mouth_interior': [('mouth_core', (0, 0), (0, 0))],
    'eye_far_lid': [('eye_far_lid', (0, 0), (0, 0))],
    'eye_near_lid': [('eye_near_lid', (0, 0), (0, 0))],
    'feeler_far': [('feeler_far_0', (205, 196), (180, 174)), ('feeler_far_1', (180, 174), (155, 155))],
    'feeler_near': [('feeler_near_0', (282, 188), (268, 158)), ('feeler_near_1', (268, 158), (244, 134))],
    'body': [
        ('trunk_base', (515, 855), (508, 775)),
        ('seg_0', (508, 775), (512, 665)),
        ('seg_1', (512, 665), (562, 535)),
        ('seg_2', (562, 535), (618, 400)),
        ('seg_3', (618, 400), (610, 278)),
        ('seg_4', (610, 278), (522, 205)),
        ('seg_5', (522, 205), (412, 208)),
        ('neck', (412, 208), (325, 244)),
        ('head', (325, 244), (185, 290)),
    ],
}

CHAINS = {
    'feeler_far': dict(bones=['feeler_far_0', 'feeler_far_1'], freq=3.4, zeta=0.52, clamp=26.0, gain=0.0085, rgain=0.0032),
    'feeler_near': dict(bones=['feeler_near_0', 'feeler_near_1'], freq=3.6, zeta=0.48, clamp=28.0, gain=0.0092, rgain=0.0035),
}

# Arc-length spine polyline matching continuous worm centerline
_SPINE_NAMES = ['trunk_base', 'seg_0', 'seg_1', 'seg_2', 'seg_3', 'seg_4', 'seg_5', 'neck', 'head']
_SPINE_PTS = np.array([
    [515.0, 825.0],
    [500.0, 725.0],
    [528.0, 600.0],
    [598.0, 465.0],
    [630.0, 335.0],
    [575.0, 222.0],
    [458.0, 195.0],
    [368.0, 222.0],
    [286.0, 265.0],
    [165.0, 295.0],
], dtype=np.float64)
_SEG_LENS = np.linalg.norm(_SPINE_PTS[1:] - _SPINE_PTS[:-1], axis=1)
_ARC_STARTS = np.concatenate([[0.0], np.cumsum(_SEG_LENS[:-1])])
_BONE_S = np.array([
    0.0,
    _ARC_STARTS[1],
    _ARC_STARTS[2],
    _ARC_STARTS[3],
    _ARC_STARTS[4],
    _ARC_STARTS[5],
    _ARC_STARTS[6],
    _ARC_STARTS[7],
    _ARC_STARTS[8] + 28.0,
], dtype=np.float64)


def _feeler_weights(points, segs):
    d = np.stack([_seg_dist(points, a, b)[0] for _, a, b in segs], axis=1)
    w = np.exp(-(d / 18.0) ** 2) + 1e-6
    w /= w.sum(axis=1, keepdims=True)
    return [[(segs[k][0], float(w[i, k])) for k in range(len(segs)) if w[i, k] > 0.01] for i in range(len(points))]


def weights(name, points):
    points = np.asarray(points, dtype=np.float64)
    if name in SKIN and len(SKIN[name]) == 1:
        b = SKIN[name][0][0]
        return [[(b, 1.0)] for _ in range(len(points))]
    if name in ('feeler_far', 'feeler_near'):
        return _feeler_weights(points, SKIN[name])

    # Body continuous 1D arc-length projection along spine curve -> smooth Gaussian weights
    n = len(points)
    dists = np.empty((n, len(_SPINE_PTS) - 1), dtype=np.float64)
    ts = np.empty((n, len(_SPINE_PTS) - 1), dtype=np.float64)
    for k in range(len(_SPINE_PTS) - 1):
        dk, tk = _seg_dist(points, _SPINE_PTS[k], _SPINE_PTS[k + 1])
        dists[:, k] = dk
        ts[:, k] = tk
    best_seg = dists.argmin(axis=1)
    s_pt = _ARC_STARTS[best_seg] + ts[np.arange(n), best_seg] * _SEG_LENS[best_seg]

    # Gaussian blend radius (~124px along spine arc) prevents inner-elbow pinching on thick 190px body
    sigma = 124.0
    W = np.exp(-((s_pt[:, None] - _BONE_S[None, :]) / sigma) ** 2) + 1e-8
    W /= W.sum(axis=1, keepdims=True)

    # Firmly lock eyes + brow front shield to 'head' so eyes never smear horizontally during neck flex
    d_head = np.hypot(points[:, 0] - 252.0, points[:, 1] - 268.0)
    head_lock = np.clip((115.0 - d_head) / 48.0, 0.0, 1.0) ** 2
    head_idx = _SPINE_NAMES.index('head')
    W = W * (1.0 - head_lock[:, None])
    W[:, head_idx] += head_lock

    # Also firmly root bottom crater-submerged tail to trunk_base / seg_0 so base translates cleanly
    base_lock = np.clip((points[:, 1] - 795.0) / 55.0, 0.0, 1.0)
    W[:, 0] = W[:, 0] * (1.0 - base_lock) + base_lock
    W[:, 1:] = W[:, 1:] * (1.0 - base_lock[:, None])

    W /= W.sum(axis=1, keepdims=True)
    out = []
    for i in range(n):
        acc = {b: float(W[i, k]) for k, b in enumerate(_SPINE_NAMES) if W[i, k] > 0.004}
        tot = sum(acc.values())
        out.append([(b, w / tot) for b, w in acc.items()])
    return out
