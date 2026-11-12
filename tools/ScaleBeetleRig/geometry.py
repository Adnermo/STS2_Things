"""Print the rest geometry and per-clip leg reach margins (authoring aid).

    python geometry.py            # rest pose summary
    python geometry.py reach      # min hip-to-foot slack per clip/leg from out/frames.pkl
"""
import math
import pickle
import sys

import numpy as np

import rigkit
from rigdef import LEGS


def rest_summary():
    _, sk = rigkit.load_rig()
    world = sk.world({})
    def p(name):
        m = world[name]
        return m[0, 2], m[1, 2]
    for name in ('body', 'thorax', 'abdomen', 'neck', 'head', 'jaw_near', 'jaw_far', 'eye',
                 'collar', 'neck_shield', 'shell_near', 'shell_far', 'antenna_near_0', 'antenna_near_7',
                 'antenna_far_0', 'antenna_far_7', 'ground_shadow'):
        x, y = p(name)
        print(f'{name:16s} {x:8.1f} {y:8.1f}')
    for leg in LEGS:
        h, k, a, t = (p(f'{leg}_{j}') for j in ('hip', 'knee', 'ankle', 'toe'))
        l1 = math.dist(h, k); l2 = math.dist(k, a); d = math.dist(h, a)
        print(f'{leg:10s} hip {h[0]:7.1f},{h[1]:6.1f} ankle {a[0]:7.1f},{a[1]:6.1f} '
              f'len {l1:6.1f}+{l2:6.1f} rest {d:6.1f} slack {l1 + l2 - d:6.1f} min {abs(l1 - l2):6.1f}')


def reach():
    _, sk = rigkit.load_rig()
    frames = pickle.load(open('out/frames.pkl', 'rb'))
    rest = sk.world({})
    for clip, rows in frames.items():
        worst = {}
        for row in rows:
            pose = dict(row['pose'])
            # Remove the leg solve so the hip is measured from the authored torso.
            world = sk.world(pose)
            for leg in LEGS:
                hip = world[f'{leg}_hip'][:2, 2]
                l1 = np.linalg.norm(sk.local[f'{leg}_knee']); l2 = np.linalg.norm(sk.local[f'{leg}_ankle'])
                target = rest[f'{leg}_ankle'][:2, 2] + np.array(row['feet'][leg][:2])
                d = np.linalg.norm(target - hip)
                slack = l1 + l2 - d
                if leg not in worst or slack < worst[leg][0]:
                    worst[leg] = (slack, row['t'])
        print(clip.ljust(10), ' '.join(f'{leg}:{v[0]:6.1f}@{v[1]:.2f}' for leg, v in worst.items()))


if __name__ == '__main__':
    reach() if sys.argv[1:] == ['reach'] else rest_summary()
