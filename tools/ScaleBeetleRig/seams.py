"""Carapace seam check for the Scale Beetle's rigid painted parts.

The body, shells, collar, neck shield, head, eye and jaws were cut out of one painting.
Wherever a part continues under a part drawn in front of it, its edge is an artificial
cut. Plates may slide over each other, but a cut edge must stay covered: once it shows,
the armour looks torn and underlayers or background appear.

    python seams.py overlay OUT.png                rest composite with part outlines, hidden cut edges in red
    python seams.py report BONES.json [BONES ...]   per clip: deepest exposed cut edge and visible-seam drift
    python seams.py check BONES.json [MAX_PX]       exit 1 when a clip exposes a cut edge deeper than MAX_PX (4)
                                                    or over more than 40 px2; 4 source px is ~2 screen px in battle

BONES.json comes from `node seam_dump.mjs SPJSON ATLAS BONES.json [HZ]` (official Spine 4.2 runtime).
"""
import json
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ORIGIN = np.array([563.5, 883.0])
PAD = 160
ORDER = json.loads((HERE / 'parts/order.json').read_text())['order']
META = json.loads((HERE / 'parts/meta.json').read_text())
RIGID = [n for n in ORDER if n in ('body', 'shell_far', 'head', 'neck_shield', 'collar', 'shell_near', 'eye', 'jaw_far', 'jaw_near')]
W = max(META[n]['x'] + META[n]['w'] for n in META) + 2 * PAD
H = max(META[n]['y'] + META[n]['h'] for n in META) + 2 * PAD


def load_mask(name):
    m = META[name]
    alpha = np.array(Image.open(HERE / f'parts/{name}.png').convert('RGBA'))[..., 3]
    full = np.zeros((H, W), bool)
    full[PAD + m['y']:PAD + m['y'] + m['h'], PAD + m['x']:PAD + m['x'] + m['w']] = alpha > 127
    return full


MASKS = {n: load_mask(n) for n in RIGID}
# Distance (source px) from any pixel to the nearest opaque pixel of a part.
REACH = {n: cv2.distanceTransform((~MASKS[n]).astype(np.uint8), cv2.DIST_L2, 5) for n in RIGID}


def cut_edges(name):
    """Boundary pixels of a part that lie under rigid parts drawn in front of it at rest (source px)."""
    mask = MASKS[name]
    edge = mask & ~cv2.erode(mask.astype(np.uint8), np.ones((3, 3), np.uint8)).astype(bool)
    front = np.zeros_like(mask)
    for other in RIGID[RIGID.index(name) + 1:]:
        front |= MASKS[other]
    ys, xs = np.nonzero(edge & front)
    return np.stack([xs - PAD + 0.5, ys - PAD + 0.5], 1)[::2]


def seam_pixels():
    """Visible borders between two rigid parts at rest: {(front, back): source px}."""
    top = np.full((H, W), -1, int)
    for i, name in enumerate(RIGID):
        top[MASKS[name]] = i
    pairs = {}
    for dy, dx in ((0, 1), (1, 0)):
        a, b = top[:H - dy, :W - dx], top[dy:, dx:]
        ys, xs = np.nonzero((a >= 0) & (b >= 0) & (a != b))
        for y, x in zip(ys, xs):
            i, j = a[y, x], b[y, x]
            pairs.setdefault((RIGID[max(i, j)], RIGID[min(i, j)]), []).append((x - PAD + 0.5 + dx / 2, y - PAD + 0.5 + dy / 2))
    return {key: np.array(points)[::2] for key, points in pairs.items()}


CUTS = {n: cut_edges(n) for n in RIGID}
SEAMS = seam_pixels()


def spine(p):
    return np.stack([p[:, 0] - ORIGIN[0], ORIGIN[1] - p[:, 1]], 1)


def source(s):
    return np.stack([s[:, 0] + ORIGIN[0], ORIGIN[1] - s[:, 1]], 1)


def forward(m, rest, p):
    a, b, c, d, wx, wy = m
    s = spine(p) - rest
    return source(np.stack([a * s[:, 0] + b * s[:, 1] + wx, c * s[:, 0] + d * s[:, 1] + wy], 1))


def inverse(m, rest, p):
    a, b, c, d, wx, wy = m
    s = spine(p) - (wx, wy)
    det = a * d - b * c
    return source(np.stack([(d * s[:, 0] - b * s[:, 1]) / det, (a * s[:, 1] - c * s[:, 0]) / det], 1) + rest)


def reach(name, p):
    xs = np.round(p[:, 0] + PAD - 0.5).astype(int)
    ys = np.round(p[:, 1] + PAD - 0.5).astype(int)
    inside = (xs >= 0) & (ys >= 0) & (xs < W) & (ys < H)
    out = np.full(len(p), 999.0)
    out[inside] = REACH[name][ys[inside], xs[inside]]
    return out


def exposed(frame, rest):
    """Per part: distance (px) by which each hidden cut-edge point sticks out of the parts in front."""
    out = {}
    for name, points in CUTS.items():
        if not len(points):
            continue
        world = forward(frame[name], rest[name], points)
        best = np.full(len(points), 999.0)
        for other in RIGID[RIGID.index(name) + 1:]:
            best = np.minimum(best, reach(other, inverse(frame[other], rest[other], world)))
        out[name] = (best, world)
    return out


def area(depths):
    """Approximate revealed area (px^2): cut points are sampled every second edge pixel."""
    return float(2.0 * depths[depths > 1.0].sum())


def analyse(frame, rest):
    """Per part: deepest exposure of its cut edge (px), where, and revealed area; per seam: largest drift (px)."""
    depth = {}
    for name, (best, world) in exposed(frame, rest).items():
        k = int(best.argmax())
        depth[name] = (float(best[k]), world[k].round(1).tolist(), area(best))
    drift = {}
    for (front, back), points in SEAMS.items():
        gap = np.linalg.norm(forward(frame[front], rest[front], points) - forward(frame[back], rest[back], points), axis=1)
        drift[f'{front}|{back}'] = float(gap.max())
    return depth, drift


def load_bones(path):
    raw = json.loads(Path(path).read_text())
    rest = {n: np.array(m[4:6]) for n, m in raw['rest'].items()}
    return rest, raw['clips']


def summarise(path):
    rest, clips = load_bones(path)
    result = {}
    for clip, frames in clips.items():
        worst, drift = {}, {}
        for frame in frames:
            d, s = analyse(frame['m'], rest)
            for part, (value, where, size) in d.items():
                if value > worst.get(part, (-1,))[0]:
                    worst[part] = (value, frame['t'], where, max(size, worst.get(part, (0, 0, 0, 0))[3]))
                elif size > worst[part][3]:
                    worst[part] = worst[part][:3] + (size,)
            for seam, value in s.items():
                if value > drift.get(seam, (-1,))[0]:
                    drift[seam] = (value, frame['t'])
        result[clip] = dict(exposed=worst, drift=drift)
    return result


def report(paths):
    results = {path: summarise(path) for path in paths}
    clips = list(next(iter(results.values())))
    print('exposed cut edge depth (px), deepest part@time; then largest visible-seam drift (px)')
    for clip in clips:
        for path, result in results.items():
            r = result[clip]
            parts = sorted(r['exposed'].items(), key=lambda kv: -kv[1][0])
            top = ', '.join(f'{p} {v:.1f}@{t:.2f}/{a:.0f}px2' for p, (v, t, _, a) in parts[:4] if v > 1.0) or 'none > 1px'
            seams = sorted(r['drift'].items(), key=lambda kv: -kv[1][0])
            drift = ', '.join(f'{s} {v:.0f}@{t:.2f}' for s, (v, t) in seams[:3])
            print(f'{clip:10s} {Path(path).parent.name[:12]:12s} cut: {top}\n{"":24s}drift: {drift}')
    return results


def check(path, limit, max_area=40.0):
    result = summarise(path)
    failures = []
    for clip, r in result.items():
        for part, (value, t, where, size) in r['exposed'].items():
            if value > limit or size > max_area:
                failures.append(f'{clip}: {part} cut edge exposed {value:.1f}px / {size:.0f}px2 at {t:.2f}s near {where}')
    print('\n'.join(failures) if failures else
          f'SCALE_BEETLE_SEAMS_PASS (hidden cut edges stay within {limit}px and {max_area:.0f}px2 in every frame)')
    return not failures


def overlay(out):
    base = Image.open(HERE / 'out/rest-composite.png').convert('RGBA')
    canvas = Image.new('RGBA', base.size, (32, 36, 44, 255))
    canvas.alpha_composite(base)
    draw = ImageDraw.Draw(canvas)
    colors = {'body': '#8cff8c', 'shell_far': '#ffd24a', 'shell_near': '#ff9d2e', 'collar': '#5ad1ff',
              'neck_shield': '#b98cff', 'head': '#ffffff', 'eye': '#7fffd4', 'jaw_far': '#ff66cc', 'jaw_near': '#ff66cc'}
    for name in RIGID:
        mask = MASKS[name].astype(np.uint8)
        edge = (mask - cv2.erode(mask, np.ones((3, 3), np.uint8))).astype(bool)
        ys, xs = np.nonzero(edge)
        for x, y in zip(xs[::3], ys[::3]):
            draw.point((x - PAD, y - PAD), fill=colors[name])
    for name, points in CUTS.items():
        for x, y in points:
            draw.point((x, y), fill='#ff2020')
    canvas.convert('RGB').save(out, quality=92)
    print(out, {n: len(p) for n, p in CUTS.items()})


if __name__ == '__main__':
    command = sys.argv[1]
    if command == 'overlay':
        overlay(sys.argv[2])
    elif command == 'report':
        report(sys.argv[2:])
    elif command == 'check':
        sys.exit(0 if check(sys.argv[2], float(sys.argv[3]) if len(sys.argv) > 3 else 4.0) else 1)
