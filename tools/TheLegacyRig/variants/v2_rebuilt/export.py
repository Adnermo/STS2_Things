"""Stage 3: 240 Hz Damped Flora Spring Simulation & Spine 4.2 JSON Exporter — v2_rebuilt."""
import json
import math
import pickle
import numpy as np
import anims as A
from rigdef import GLOW_ALPHA, GLOW_OF, NAME, S

FPS = 60
SUB = 4  # 60 * 4 = 240 Hz physics integration!
TAU = 2.0 * math.pi


class Skel:
    def __init__(self, bones):
        self.bones = bones
        self.idx = {b['name']: i for i, b in enumerate(bones)}
        self.setup = {b['name']: S(b['pos']) for b in bones}
        self.parent = {b['name']: b['parent'] for b in bones}
        self.local = {}
        for b in bones:
            p = b['parent']
            sp = self.setup[b['name']]
            self.local[b['name']] = sp if p is None else (sp[0] - self.setup[p][0], sp[1] - self.setup[p][1])

    def world(self, pose: dict) -> dict:
        W = {}
        for b in self.bones:
            n = b['name']
            r, dx, dy, sx, sy = pose.get(n, (0.0, 0.0, 0.0, 1.0, 1.0))
            lx, ly = self.local[n]
            c, s = math.cos(math.radians(r)), math.sin(math.radians(r))
            M = np.array([[c * sx, -s * sy, lx + dx], [s * sx, c * sy, ly + dy], [0.0, 0.0, 1.0]])
            W[n] = M if b['parent'] is None else W[b['parent']] @ M
        return W


def chain_axis_dir(ch: dict):
    b = np.array(S(ch['base']), dtype=np.float64)
    t = np.array(S(ch['tip']), dtype=np.float64)
    d = t - b
    L = float(np.linalg.norm(d))
    return d / max(L, 1e-6), L


def flora_target_angle(anim_name: str, cn: str, link_i: int, ch: dict, t: float, bias: dict) -> float:
    bx = ch['base'][0]
    is_coral = cn.startswith('coral')
    side = 1.0 if bx < 679.0 else -1.0  # +CCW leans left-side flora outward-left; -CW leans right-side outward-right
    d_vec, _ = chain_axis_dir(ch)
    horiz = abs(d_vec[0]) > 0.72

    th = 0.0
    if '__ocean_t' in bias:
        ot = bias['__ocean_t']
        ph = TAU * ot / 3.0 - (bx / 1358.0) * TAU * 0.55
        amp_c = 0.50 if is_coral else 1.0
        th += amp_c * ((2.4 + 1.3 * link_i) * math.sin(ph - 0.52 * link_i) + 0.85 * math.sin(2.0 * ph + 1.1 + 0.4 * link_i))

    if '__collapse' in bias:
        c = bias['__collapse']
        droop = 5.5 if is_coral else (4.5 if horiz else 10.5)
        th += side * droop * c * (0.85 if link_i == 0 else 0.75)

    if '__outward' in bias:
        out_v = bias['__outward']
        th += side * out_v * (0.45 if is_coral else 1.0) * (0.75 + 0.35 * link_i)

    if '__shock' in bias:
        sh = bias['__shock']
        th += sh * (0.45 if is_coral else 0.90) * (0.7 + 0.35 * link_i)

    if '__wave_t' in bias:
        wt = bias['__wave_t']
        # Right-to-left traveling wave across x = 1350 -> 50 over t = 0.08 -> 0.56s
        t_arr = 0.08 + 0.46 * (1.0 - bx / 1358.0)
        th += side * (5.0 if is_coral else 10.5) * A.bump(wt, t_arr - 0.08, t_arr + 0.04, t_arr + 0.26) * (0.8 + 0.3 * link_i)

    return th


def simulate(sk: Skel, chains: dict, name: str, dur: float, loop: bool, init_state=None, settle_to_state=None):
    n_frames = int(round(dur * FPS))
    dt = 1.0 / (FPS * SUB)
    warm_loops = 3 if loop else 0
    total_steps = (warm_loops + 1) * n_frames * SUB if loop else n_frames * SUB

    state = {}
    for cn, ch in chains.items():
        for b in ch['bones']:
            state[b] = [0.0, 0.0] if init_state is None else [float(init_state[b][0]), float(init_state[b][1])]

    prev_drv = {}
    out = []

    for k in range(total_steps + 1):
        tt = (k * dt) % dur if loop else min(k * dt, dur)
        f = A.FUNCS[name](tt)
        P = A.Pose(f.P)
        W = sk.world(P)

        for cn, ch in chains.items():
            d_setup, _ = chain_axis_dir(ch)
            drv_bone = ch['driver']
            M_drv = W[drv_bone]
            drv_pt = (M_drv @ np.array([0.0, 0.0, 1.0]))[:2]
            par_bone = sk.parent[ch['bones'][0]]
            M_par = W[par_bone]
            dw = M_par[:2, :2] @ d_setup
            dw_norm = float(np.linalg.norm(dw))
            if dw_norm > 1e-8:
                dw = dw / dw_norm

            if cn in prev_drv and k > 0:
                p_pos, p_vel = prev_drv[cn]
                vel = (drv_pt - p_pos) / dt
                acc = (vel - p_vel) / dt
                # Clamp numerical acceleration spikes so springs respond smoothly
                acc = np.clip(acc, -18000.0, 18000.0)
            else:
                vel = np.zeros(2)
                acc = np.zeros(2)
            prev_drv[cn] = (drv_pt, vel)

            cross = float(dw[0] * acc[1] - dw[1] * acc[0])
            # Extra necrotic rigor damping at end of `die` so corpse flatlines completely still by 2.45s!
            extra_zeta = 2.5 * A.ease(A.seg(tt, 1.95, 2.42)) if name == 'die' else 0.0

            for j, b in enumerate(ch['bones']):
                w0 = TAU * ch['freq'] * (1.0 - 0.06 * j)
                zeta = ch['zeta'] + extra_zeta
                tgt = flora_target_angle(name, cn, j, ch, tt, f.bias)
                th, om = state[b]
                ang_acc = -w0 * w0 * (th - tgt) - 2.0 * zeta * w0 * om - ch['gain'] * (1.0 + 0.55 * j) * cross
                om += ang_acc * dt
                th += om * dt
                lim = ch['clamp']
                if th > lim:
                    th = lim
                    om = min(0.0, om)
                elif th < -lim:
                    th = -lim
                    om = max(0.0, om)
                state[b] = [th, om]

        if k % SUB == 0 and (not loop or k >= warm_loops * n_frames * SUB):
            t_out = tt if not loop else (k - warm_loops * n_frames * SUB) * dt
            # Smoothly blend spring angles into settle_to_state over final window for 0.000px return-to-idle!
            settle_w = 0.0
            if settle_to_state is not None and not loop:
                win = 0.24 if name == 'revive' else max(0.14, min(0.22, 0.25 * dur))
                settle_w = A.ease(A.seg(t_out, dur - win, dur))
            if name == 'die' and t_out >= 2.42:
                # Guarantee absolute post-mortem stillness in final flatline hold
                for cn, ch in chains.items():
                    for j, b in enumerate(ch['bones']):
                        tgt_end = flora_target_angle('die', cn, j, ch, dur, {'__collapse': 1.0})
                        w_flat = A.ease(A.seg(t_out, 2.42, 2.54))
                        state[b][0] = state[b][0] * (1.0 - w_flat) + tgt_end * w_flat
                        state[b][1] *= (1.0 - w_flat)

            pose_out = dict(P)
            for cn, ch in chains.items():
                for b in ch['bones']:
                    th_val = state[b][0]
                    if settle_w > 1e-6:
                        th_val = th_val * (1.0 - settle_w) + settle_to_state[b][0] * settle_w
                    r0, x0, y0, sx0, sy0 = pose_out.get(b, (0.0, 0.0, 0.0, 1.0, 1.0))
                    pose_out[b] = (r0 + th_val, x0, y0, sx0, sy0)

            out.append({
                't': round(t_out, 5),
                'pose': pose_out,
                'glow': {g: A.clamp(f.glow.get(g, 0.0)) for g in GLOW_OF},
                'tint': tuple(f.tint),
            })

    out = out[:n_frames + 1]
    if loop:
        out[-1] = dict(out[0], t=round(dur, 5))
    final_state = {b: (float(state[b][0]), float(state[b][1])) for b in state}
    return out, final_state


def reduce_keys(times, vals, eps):
    keep = [0]
    i = 0
    N = len(times)
    while i < N - 1:
        j = i + 2
        while j < N:
            ok = True
            t0, t1 = times[i], times[j]
            for m in range(i + 1, j):
                u = (times[m] - t0) / (t1 - t0)
                for q in range(len(vals[0])):
                    if abs(vals[i][q] + (vals[j][q] - vals[i][q]) * u - vals[m][q]) > eps[q]:
                        ok = False
                        break
                if not ok:
                    break
            if not ok:
                break
            j += 1
        i = j - 1
        keep.append(i)
    return keep


def hexcol(r, g, b, a):
    return ''.join(f'{int(round(A.clamp(v) * 255)):02x}' for v in (r, g, b, a))


def build_anim_json(sk: Skel, rig: dict, frames: list):
    times = [f['t'] for f in frames]
    bones_tl = {}
    for b in sk.idx:
        rot = [(f['pose'].get(b, (0.0, 0.0, 0.0, 1.0, 1.0))[0],) for f in frames]
        tr = [tuple(f['pose'].get(b, (0.0, 0.0, 0.0, 1.0, 1.0))[1:3]) for f in frames]
        sc = [tuple(f['pose'].get(b, (0.0, 0.0, 0.0, 1.0, 1.0))[3:5]) for f in frames]
        tl = {}
        if any(abs(v[0]) > 1e-3 for v in rot):
            ks = reduce_keys(times, rot, (0.035,))
            tl['rotate'] = [{'time': round(times[k], 4), 'value': round(rot[k][0], 3)} for k in ks]
        if any(abs(v[0]) > 1e-3 or abs(v[1]) > 1e-3 for v in tr):
            ks = reduce_keys(times, tr, (0.045, 0.045))
            tl['translate'] = [{'time': round(times[k], 4), 'x': round(tr[k][0], 2), 'y': round(tr[k][1], 2)} for k in ks]
        if any(abs(v[0] - 1.0) > 1e-4 or abs(v[1] - 1.0) > 1e-4 for v in sc):
            ks = reduce_keys(times, sc, (0.0005, 0.0005))
            tl['scale'] = [{'time': round(times[k], 4), 'x': round(sc[k][0], 4), 'y': round(sc[k][1], 4)} for k in ks]
        if tl:
            for arr in tl.values():
                if arr[0]['time'] == 0:
                    del arr[0]['time']
            bones_tl[b] = tl

    slots_tl = {}
    for gname in GLOW_OF:
        vals = [(GLOW_ALPHA * f['glow'].get(gname, 0.0),) for f in frames]
        ks = reduce_keys(times, vals, (0.008,))
        slots_tl[gname] = {'rgba': [{'time': round(times[k], 4), 'color': hexcol(1.0, 1.0, 1.0, vals[k][0])} for k in ks]}

    if any(tuple(f['tint']) != (1.0, 1.0, 1.0, 1.0) for f in frames):
        vals = [tuple(f['tint']) for f in frames]
        ks = reduce_keys(times, vals, (0.003, 0.003, 0.003, 0.003))
        for s in rig['slots']:
            if s['name'] in GLOW_OF:
                continue
            slots_tl[s['name']] = {'rgba': [{'time': round(times[k], 4), 'color': hexcol(*vals[k])} for k in ks]}

    for sl in slots_tl.values():
        for arr in sl.values():
            if arr and arr[0].get('time') == 0:
                del arr[0]['time']

    return {'bones': bones_tl, 'slots': slots_tl}


def main():
    rig = json.load(open('out/rig_static.json'))
    sk = Skel(rig['bones'])
    chains = rig['chains']

    anim_json = {}
    allframes = {}

    # 1. Simulate idle_loop (periodic steady state)
    idle_frames, idle_state_t0 = simulate(sk, chains, 'idle_loop', *A.ANIMS['idle_loop'])
    allframes['idle_loop'] = idle_frames
    anim_json['idle_loop'] = build_anim_json(sk, rig, idle_frames)

    # 2. Simulate die first so revive can start at exact die_end_state (0.000px join!)
    die_frames, die_end_state = simulate(sk, chains, 'die', *A.ANIMS['die'], init_state=idle_state_t0, settle_to_state=None)
    allframes['die'] = die_frames
    anim_json['die'] = build_anim_json(sk, rig, die_frames)

    # 3. Simulate revive starting from exact die_end_state -> settling to exact idle_state_t0!
    rev_frames, _ = simulate(sk, chains, 'revive', *A.ANIMS['revive'], init_state=die_end_state, settle_to_state=idle_state_t0)
    allframes['revive'] = rev_frames
    anim_json['revive'] = build_anim_json(sk, rig, rev_frames)

    # 4. Simulate all other one-shots starting & ending cleanly at idle_state_t0!
    for name, (dur, loop) in A.ANIMS.items():
        if name in ('idle_loop', 'die', 'revive'):
            continue
        fr, _ = simulate(sk, chains, name, dur, loop, init_state=idle_state_t0, settle_to_state=idle_state_t0)
        allframes[name] = fr
        anim_json[name] = build_anim_json(sk, rig, fr)

    bones_json = []
    for b in rig['bones']:
        e = {'name': b['name']}
        if b['parent']:
            e['parent'] = b['parent']
        lx, ly = sk.local[b['name']]
        if abs(lx) > 1e-6:
            e['x'] = round(lx, 2)
        if abs(ly) > 1e-6:
            e['y'] = round(ly, 2)
        bones_json.append(e)

    skel = {
        'skeleton': {
            'hash': 'thelegacyv2rebuilt',
            'spine': '4.2.0',
            'x': -679.0,
            'y': -29.0,
            'width': 1358.0,
            'height': 689.0,
            'images': './',
            'audio': '',
        },
        'bones': bones_json,
        'slots': rig['slots'],
        'skins': [{'name': 'default', 'attachments': rig['attachments']}],
        'animations': anim_json,
    }
    with open(f'out/{NAME}.json', 'w') as f:
        json.dump(skel, f, separators=(',', ':'))
    with open('out/frames.pkl', 'wb') as f:
        pickle.dump(allframes, f)

    print('Exported Spine 4.2 JSON:', {k: len(v) for k, v in allframes.items()})


if __name__ == '__main__':
    main()
