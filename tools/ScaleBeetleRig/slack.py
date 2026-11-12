"""How far each rigid carapace group can move before a hidden cut edge shows (authoring aid).

    python slack.py          best hinge pivot per direction, safe range, and the part that limits it
"""
import math
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import rigdef  # noqa: E402
import seams  # noqa: E402

DEPTH, AREA = 3.0, 40.0
REST = {name: rigdef.s(pos) for name, _, pos in rigdef.BONES if name in seams.RIGID}
GROUPS = {
    'head (+eye, jaws)': ['head', 'eye', 'jaw_far', 'jaw_near'],
    'front (head + neck shield + collar)': ['head', 'eye', 'jaw_far', 'jaw_near', 'neck_shield', 'collar'],
    'shells (both)': ['shell_far', 'shell_near'],
    'shell_far': ['shell_far'],
    'jaw_near': ['jaw_near'],
    'jaw_far': ['jaw_far'],
}


def bone_point(name):
    return tuple(np.round([rigdef.ORIGIN[0] + REST[name][0], rigdef.ORIGIN[1] - REST[name][1]], 1))


PIVOTS = {
    'head (+eye, jaws)': [(x, y) for x in range(200, 441, 10) for y in range(340, 741, 20)],
    'front (head + neck shield + collar)': [(x, y) for x in range(360, 621, 20) for y in range(280, 741, 20)],
    'shells (both)': [(x, y) for x in range(460, 1101, 40) for y in range(330, 811, 40)],
    'shell_far': [(x, y) for x in range(480, 1101, 40) for y in range(280, 621, 30)],
    'jaw_near': [bone_point('jaw_near')] + [(x, y) for x in range(110, 231, 10) for y in range(630, 741, 10)],
    'jaw_far': [bone_point('jaw_far')] + [(x, y) for x in range(20, 111, 10) for y in range(600, 701, 10)],
}


def frame(moved=(), angle=0.0, pivot=(0, 0), dx=0.0, dy=0.0):
    out = {}
    c, s = math.cos(math.radians(angle)), math.sin(math.radians(angle))
    p = rigdef.s(pivot)
    for name, rest in REST.items():
        if name in moved:
            w = np.array([c * (rest[0] - p[0]) - s * (rest[1] - p[1]), s * (rest[0] - p[0]) + c * (rest[1] - p[1])]) + p + (dx, -dy)
            out[name] = [c, -s, s, c, w[0], w[1]]
        else:
            out[name] = [1.0, 0.0, 0.0, 1.0, rest[0], rest[1]]
    return out


def worst(f):
    """(depth, area, limiting part) of the largest exposure in a pose."""
    depth, _ = seams.analyse(f, REST)
    part = max(depth, key=lambda n: (depth[n][2], depth[n][0]))
    return max(v for v, _, _ in depth.values()), sum(a for _, _, a in depth.values()), part


def ok(result):
    return result[0] <= DEPTH and result[1] <= AREA


def reach(test, step, limit):
    value, last = 0.0, None
    while value + step <= limit + 1e-9:
        result = test(value + step)
        if not ok(result):
            return value, result[2]
        value += step
    return value, '-'


if __name__ == '__main__':
    print(f'rest {worst(frame())}; limits depth {DEPTH}px, area {AREA}px2')
    for group, bones in GROUPS.items():
        jaw = group.startswith('jaw')
        limit, step = (60.0, 1.0) if jaw else (16.0, 0.5)
        for sign, label in ((1, 'nose-down/CCW +'), (-1, 'nose-up/CW -')):
            best = max(((reach(lambda a: worst(frame(bones, sign * a, pivot)), step, limit), pivot) for pivot in PIVOTS[group]),
                       key=lambda item: item[0][0])
            (value, limiter), pivot = best
            print(f'{group:38s} {label:16s} best pivot {pivot}: {value:5.1f} deg, then {limiter} shows')
        shifts = []
        for name, k in (('left', lambda d: (-d, 0)), ('right', lambda d: (d, 0)), ('up', lambda d: (0, -d)), ('down', lambda d: (0, d))):
            value, limiter = reach(lambda d, k=k: worst(frame(bones, 0, (0, 0), *k(d))), 1.0, 60.0)
            shifts.append(f'{name} {value:.0f} ({limiter})')
        print(f'{"":38s} shift px: ' + ', '.join(shifts))
