import json, math, os, pickle, numpy as np
import anims as A
from fk import Skel
from rigdef import S, CHAIN_PARENT, CHAMBER_COUPLE, GLOW_OF

os.makedirs('out', exist_ok=True)
rig = json.load(open('out/rig_static.json'))
sk = Skel(rig)
chains = rig['chains']
GLOWS = [g for g in ('atrium_glow', 'bulb_glow', 'mid_lobe_glow', 'bottom_lobe_glow') if any(s['name'] == g for s in rig['slots'])]
FPS = 30

def chain_setup_dir(c):
    b = np.array(S(c['base']))
    t = np.array(S(c['tip']))
    d = t - b
    nrm = max(np.linalg.norm(d), 1e-6)
    return d / nrm, nrm

def base_pose(name, t):
    tint = (1.0, 1.0, 1.0)
    c = 0.0
    if name == 'idle_loop':
        P, g = A.idle_base(t)
    elif name in ('die', 'revive'):
        P, g, tint, c = getattr(A, name)(t)
    else:
        P, g = getattr(A, name)(t)
    return P, g, tint, c

def chain_targets(name, cn, i, t, c):
    ch = chains[cn]
    bx = ch['base'][0]
    is_ground = (CHAIN_PARENT[cn] == 'root')
    is_left_frond = cn in ('weed_left', 'weed_ground_left')
    coral = cn.startswith('coral')
    side = 1.0 if bx < 679.0 else -1.0  # + = lean outward away from cardiac center
    d, _ = chain_setup_dir(ch)
    horiz = abs(d[0]) > 0.7

    if name == 'idle_loop':
        return A.weed_target_idle(cn, i, t, bx, is_ground=is_ground) * (0.38 if coral else 1.0)

    # Ambient baseline anchored at idle(0) for clean seamless one-shot joins
    th = A.weed_target_idle(cn, i, 0.0, bx, is_ground=is_ground) * (0.38 if coral else 1.0)

    if name in ('die', 'revive'):
        if cn == 'weed_left':
            # Tall left seaweed loses support from tube_b/mid_lobe and slumps clockwise (- angle) onto the collapsing chambers!
            droop_signed = -(2.8 + 1.1 * i) * c
        elif coral:
            droop_signed = side * 3.2 * c
        elif horiz:
            droop_signed = side * (3.0 + 1.0 * i) * c
        elif is_ground:
            droop_signed = side * (4.2 + 1.4 * i) * c
        else:
            droop_signed = side * (8.5 if i == 0 else 5.5) * c

        if name == 'die':
            shiver = (1.5 if is_left_frond else (1.0 if is_ground else 1.5)) * math.sin(A.TAU * 11.0 * t + 0.5 * i) * (1.0 - A.ease(A.seg(t, 0.85, 1.65)))
            if coral:
                shiver *= 0.4
            return th * (1.0 - c) + droop_signed + shiver
        else:
            jolt = side * (2.6 if is_left_frond else 2.0) * A.pulse(t - 0.52 - 0.03 * i, 0.04, 0.22) * (0.45 if coral else 1.0)
            return th * (1.0 - c) + droop_signed + jolt

    if name == 'attack':
        coil = A.ease(A.seg(t, 0.0, 0.27)) * (1.0 - A.ease_out(A.seg(t, 0.27, 0.35)))
        if is_left_frond:
            # Left fronds draw slightly inward during coil, then get swept outward-left as arterial tubes lunge past!
            sweep = A.systole(t - (0.30 + 0.025 * i), rise=0.036, hold=0.045, relax=0.22)
            th += -(1.5 + 0.7 * i) * coil + (4.5 + 1.9 * i) * sweep
        else:
            shock = A.pulse(t - (0.34 + abs(bx - 420.0) / 3000.0 + 0.025 * i), 0.035, 0.20)
            amp = (1.1 if coral else (2.4 + 1.0 * i)) if is_ground else (1.6 if coral else (2.8 + 1.1 * i))
            th += side * (-0.35 * amp * coil + amp * shock)

    elif name == 'cast':
        hold = A.ease(A.seg(t, 0.0, 0.36)) * (1.0 - A.ease(A.seg(t, 0.54, 1.18)))
        blast = A.systole(t - (0.38 + 0.028 * i), rise=0.036, hold=0.055, relax=0.24)
        scale = 0.90 if is_left_frond else (0.72 if is_ground else 0.85)
        if coral:
            scale *= 0.42
        th += side * ((1.8 + 0.9 * i) * hold + (3.8 + 1.5 * i) * blast) * scale

    elif name == 'power_up':
        lock = A.ease(A.seg(t, 0.0, 0.30)) * (1.0 - A.ease_out(A.seg(t, 0.36, 0.46)))
        over = A.ease_out(A.seg(t, 0.38, 0.52)) * (1.0 - A.ease(A.seg(t, 0.82, 1.34)))
        trem = math.sin(A.TAU * 10.5 * t + 0.65 * i) * over
        scale = 0.90 if is_left_frond else (0.72 if is_ground else 0.85)
        if coral:
            scale *= 0.42
        th += side * (-(1.2 + 0.5 * i) * lock + (3.4 + 1.4 * i) * over + (1.0 + 0.35 * i) * trem) * scale

    elif name == 'summon':
        # Right-to-left wave delay matching peristaltic expulsion!
        delay = 0.07 + (1358.0 - bx) / 1358.0 * 0.42 + 0.030 * i
        wv = A.systole(t - delay, rise=0.038, hold=0.045, relax=0.20)
        scale = 0.95 if is_left_frond else (0.75 if is_ground else 0.88)
        if coral:
            scale *= 0.42
        th += side * (3.2 + 1.4 * i) * wv * scale

    elif name == 'hurt':
        imp = A.pulse(t - (0.015 + bx / 4200.0 + 0.022 * i), 0.022, 0.16)
        scale = 0.95 if is_left_frond else (0.75 if is_ground else 0.88)
        if coral:
            scale *= 0.42
        # Blunt hit from left whips all fronds rightward (- rotation)!
        th += -1.0 * (3.4 + 1.4 * i) * imp * scale

    return th

def simulate(name, dur, loop, init=None, end_lock_state=None, settle_start_lock=False):
    n = int(round(dur * FPS))
    sub = 8
    dt = 1.0 / (FPS * sub)
    warm = 5 if loop else 0
    total = (warm + 1) * n * sub if loop else n * sub

    state = {}
    for cn, ch in chains.items():
        for b in ch['bones']:
            state[b] = [0.0, 0.0] if init is None else list(init[b])

    prevpos = {}
    prevvel = {}
    out = []

    for k in range(total + 1):
        tt = (k * dt) % dur if loop else min(k * dt, dur)
        P, g, tint, c = base_pose(name, tt)
        W = sk.world(P)
        for cn, ch in chains.items():
            d, _ = chain_setup_dir(ch)
            par = sk.bones[sk.idx[ch['bones'][0]]]['parent']
            M = W[par]
            base = M @ np.array([*sk.local[ch['bones'][0]], 1.0])
            dw = M[:2, :2] @ d
            dw /= max(np.linalg.norm(dw), 1e-6)

            # Combine parent bone acceleration with adjacent heart-chamber hydrodynamic/contact impulse!
            probe_pt = base[:2].copy()
            if cn in CHAMBER_COUPLE:
                drv_bone, drv_gain = CHAMBER_COUPLE[cn]
                M_drv = W[drv_bone]
                drv_pt = (M_drv @ np.array([0.0, 0.0, 1.0]))[:2]
                probe_pt = probe_pt + drv_gain * drv_pt

            if cn in prevpos and k > 0:
                v = (probe_pt - prevpos[cn]) / dt
                a = (v - prevvel.get(cn, v)) / dt
                a = np.clip(a, -7500.0, 7500.0)
            else:
                v = np.zeros(2)
                a = np.zeros(2)
            prevpos[cn] = probe_pt
            prevvel[cn] = v

            cross = dw[0] * a[1] - dw[1] * a[0]
            coral = cn.startswith('coral')
            is_ground = (CHAIN_PARENT[cn] == 'root')
            # Higher fluid damping (z = 0.48..0.56) so seaweeds trail gracefully in damp dock air without rubbery ringing!
            w0 = 2.0 * math.pi * (2.35 if coral else (1.75 if is_ground else 1.60))
            z = 0.56 if coral else (0.50 if is_ground else 0.46)

            for i, b in enumerate(ch['bones']):
                tgt = chain_targets(name, cn, i, tt, c)
                th, om = state[b]
                gain = (0.00032 if coral else 0.00062) * (1.0 + 0.45 * i)
                acc = -w0 * w0 * (th - tgt) - 2.0 * z * w0 * om - gain * cross * 44.0
                om += acc * dt
                th += om * dt
                lim = 13.0 if is_ground else 17.0
                th = max(-lim, min(lim, th))
                state[b] = [th, om]

        if k % sub == 0 and (not loop or k >= warm * n * sub):
            t_rec = round(tt if not loop else (k - warm * n * sub) * dt, 5)
            fr = {'t': t_rec, 'pose': dict(P), 'glow': dict(g), 'tint': tint}
            for cn, ch in chains.items():
                for b in ch['bones']:
                    fr['pose'][b] = (state[b][0], 0.0, 0.0, 1.0, 1.0)
            out.append(fr)

    frames = out[:n + 1]

    if loop:
        for cn, ch in chains.items():
            for b in ch['bones']:
                th0 = frames[0]['pose'][b][0]
                th1 = frames[-1]['pose'][b][0]
                gap = th1 - th0
                for idx_f, fr in enumerate(frames):
                    u = idx_f / max(1, len(frames) - 1)
                    fr['pose'][b] = (fr['pose'][b][0] - gap * u, 0.0, 0.0, 1.0, 1.0)
        frames[-1]['pose'] = dict(frames[0]['pose'])
        frames[-1]['glow'] = dict(frames[0]['glow'])
        frames[-1]['tint'] = frames[0]['tint']

    if settle_start_lock and init is not None:
        for idx_f, fr in enumerate(frames):
            u = 1.0 - A.ease(A.seg(fr['t'], 0.0, 0.12))
            if u <= 0.0:
                break
            for cn, ch in chains.items():
                for b in ch['bones']:
                    cur = fr['pose'][b][0]
                    fr['pose'][b] = (cur * (1.0 - u) + init[b][0] * u, 0.0, 0.0, 1.0, 1.0)

    if end_lock_state is not None:
        tail = 0.24 if dur >= 1.0 else 0.16
        for idx_f, fr in enumerate(frames):
            u = A.ease(A.seg(fr['t'], dur - tail, dur))
            if u <= 0.0:
                continue
            for cn, ch in chains.items():
                for b in ch['bones']:
                    cur = fr['pose'][b][0]
                    fr['pose'][b] = (cur * (1.0 - u) + end_lock_state[b][0] * u, 0.0, 0.0, 1.0, 1.0)

    if name == 'die':
        steady = {b: frames[-1]['pose'][b][0] for cn, ch in chains.items() for b in ch['bones']}
        for fr in frames:
            u = A.ease(A.seg(fr['t'], dur - 0.28, dur - 0.04))
            if u > 0.0:
                for b, tgt_th in steady.items():
                    cur = fr['pose'][b][0]
                    fr['pose'][b] = (cur * (1.0 - u) + tgt_th * u, 0.0, 0.0, 1.0, 1.0)

    final = {b: (frames[-1]['pose'][b][0], 0.0) for cn, ch in chains.items() for b in ch['bones']}
    return frames, final

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
    return ''.join(f'{int(round(max(0.0, min(1.0, v)) * 255)):02x}' for v in (r, g, b, a))

def build_anim(name, frames):
    times = [f['t'] for f in frames]
    bones = {}
    for b in sk.idx:
        rot = [(f['pose'].get(b, (0, 0, 0, 1, 1))[0],) for f in frames]
        tr  = [tuple(f['pose'].get(b, (0, 0, 0, 1, 1))[1:3]) for f in frames]
        sc  = [tuple(f['pose'].get(b, (0, 0, 0, 1, 1))[3:5]) for f in frames]
        tl = {}
        if any(abs(v[0]) > 1e-4 for v in rot):
            ks = reduce_keys(times, rot, (0.03,))
            tl['rotate'] = [{'time': round(times[k], 4), 'value': round(rot[k][0], 3)} for k in ks]
        if any(abs(v[0]) > 1e-4 or abs(v[1]) > 1e-4 for v in tr):
            ks = reduce_keys(times, tr, (0.04, 0.04))
            tl['translate'] = [{'time': round(times[k], 4), 'x': round(tr[k][0], 2), 'y': round(tr[k][1], 2)} for k in ks]
        if any(abs(v[0] - 1.0) > 1e-4 or abs(v[1] - 1.0) > 1e-4 for v in sc):
            ks = reduce_keys(times, sc, (0.0005, 0.0005))
            tl['scale'] = [{'time': round(times[k], 4), 'x': round(sc[k][0], 4), 'y': round(sc[k][1], 4)} for k in ks]
        if tl:
            for arr in tl.values():
                if arr[0]['time'] == 0:
                    del arr[0]['time']
            bones[b] = tl

    slots = {}
    for gname in GLOWS:
        vals = [(0.76 * f['glow'].get(gname, 0.0),) for f in frames]
        ks = reduce_keys(times, vals, (0.008,))
        slots[gname] = {'rgba': [{'time': round(times[k], 4), 'color': hexcol(1.0, 1.0, 1.0, vals[k][0])} for k in ks]}

    if any(f['tint'] != (1.0, 1.0, 1.0) for f in frames):
        vals = [tuple(f['tint']) for f in frames]
        ks = reduce_keys(times, vals, (0.003, 0.003, 0.003))
        for s in rig['slots']:
            if s['name'] in GLOWS:
                continue
            slots[s['name']] = {'rgba': [{'time': round(times[k], 4), 'color': hexcol(*vals[k], 1.0)} for k in ks]}

    for sl in slots.values():
        for arr in sl.values():
            if arr and arr[0].get('time') == 0:
                del arr[0]['time']
    return {'bones': bones, 'slots': slots}

def main():
    anim_json = {}
    allframes = {}

    idle, idle0_state = simulate('idle_loop', *A.ANIMS['idle_loop'])
    allframes['idle_loop'] = idle
    anim_json['idle_loop'] = build_anim('idle_loop', idle)

    die_fr, die_end_state = simulate('die', *A.ANIMS['die'], init=idle0_state, end_lock_state=None, settle_start_lock=True)
    allframes['die'] = die_fr
    anim_json['die'] = build_anim('die', die_fr)

    rev_fr, _ = simulate('revive', *A.ANIMS['revive'], init=die_end_state, end_lock_state=idle0_state, settle_start_lock=True)
    rev_fr[0]['pose'] = dict(die_fr[-1]['pose'])
    rev_fr[0]['glow'] = dict(die_fr[-1]['glow'])
    rev_fr[0]['tint'] = die_fr[-1]['tint']
    allframes['revive'] = rev_fr
    anim_json['revive'] = build_anim('revive', rev_fr)

    for name, (dur, loop) in A.ANIMS.items():
        if name in ('idle_loop', 'die', 'revive'):
            continue
        fr, _ = simulate(name, dur, loop, init=idle0_state, end_lock_state=idle0_state, settle_start_lock=True)
        fr[0]['pose'] = dict(idle[0]['pose'])
        fr[0]['glow'] = dict(idle[0]['glow'])
        fr[-1]['pose'] = dict(idle[0]['pose'])
        fr[-1]['glow'] = dict(idle[0]['glow'])
        allframes[name] = fr
        anim_json[name] = build_anim(name, fr)

    bones = []
    for b in rig['bones']:
        e = {'name': b['name']}
        if b['parent']:
            e['parent'] = b['parent']
        lx, ly = sk.local[b['name']]
        if abs(lx) > 1e-6:
            e['x'] = round(lx, 2)
        if abs(ly) > 1e-6:
            e['y'] = round(ly, 2)
        bones.append(e)

    skel = {
        'skeleton': {
            'hash': 'thelegacy_v1_opt_p2',
            'spine': '4.2.0',
            'x': -679.0,
            'y': -29.0,
            'width': 1358,
            'height': 689,
            'images': './',
            'audio': '',
        },
        'bones': bones,
        'slots': rig['slots'],
        'skins': [{'name': 'default', 'attachments': rig['attachments']}],
        'animations': anim_json,
    }
    json.dump(skel, open('out/the_legacy.json', 'w'), separators=(',', ':'))
    pickle.dump(allframes, open('out/frames.pkl', 'wb'))
    print('exported ok:', {k: len(v) for k, v in allframes.items()})

if __name__ == '__main__':
    main()
