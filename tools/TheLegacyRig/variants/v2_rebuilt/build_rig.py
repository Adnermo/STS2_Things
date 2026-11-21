"""Stage 2: Atlas Packing, Seamless Grid Meshes & Smooth Soft-Body Weight Skinning.

Generates:
  - out/the_legacy.png
  - out/the_legacy.atlas
  - out/rig_static.json
  - prev/rig_diag.jpg
"""
import json
import math
import os
import cv2
import numpy as np
from PIL import Image
from rigdef import (
    BONES,
    DEFAULT_FLORA_SPACING,
    GLOW_OF,
    GLOW_SCALE,
    GROUND_FLORA,
    NAME,
    ORGAN_FLORA,
    SPACING,
    S,
    smoothstep_np,
    weights_organ,
)


def seg_proj_t(points: np.ndarray, a, b) -> np.ndarray:
    a = np.asarray(a, dtype=np.float64)
    b = np.asarray(b, dtype=np.float64)
    ab = b - a
    denom = max(float(ab @ ab), 1e-6)
    return np.clip(((points - a) @ ab) / denom, 0.0, 1.0)


def chain_bone_weights(t_arr: np.ndarray, bone_names: list) -> list:
    """Smooth C1 piecewise partition of unity along chain bones."""
    nb = len(bone_names)
    res = []
    for t in t_arr:
        tt = float(t) * nb - 0.35
        ws = [max(0.0, 1.0 - abs(tt - k)) for k in range(nb)]
        if tt <= 0.0:
            ws = [1.0] + [0.0] * (nb - 1)
        elif tt >= nb - 1:
            ws = [0.0] * (nb - 1) + [1.0]
        # Smooth cubic shaping
        ws = [w * w * (3.0 - 2.0 * w) for w in ws]
        s = sum(ws)
        res.append([(bone_names[k], ws[k] / s) for k in range(nb) if ws[k] / s > 0.005])
    return res


def grid_mesh(part_name: str, meta: dict, spacing: int):
    al = np.array(Image.open(f'parts/{part_name}.png'))[..., 3]
    h, w = al.shape
    xs = sorted(set([int(v) for v in np.arange(0, w, spacing)] + [w]))
    ys = sorted(set([int(v) for v in np.arange(0, h, spacing)] + [h]))
    occ = cv2.dilate((al > 0).astype(np.uint8), np.ones((3, 3), np.uint8))
    vid = {}
    verts = []
    tris = []

    def V(i, j):
        if (i, j) not in vid:
            vid[(i, j)] = len(verts)
            verts.append((xs[i], ys[j]))
        return vid[(i, j)]

    for j in range(len(ys) - 1):
        for i in range(len(xs) - 1):
            if occ[ys[j]:ys[j + 1], xs[i]:xs[i + 1]].any():
                a_v, b_v, c_v, d_v = V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1)
                tris += [a_v, b_v, c_v, a_v, c_v, d_v]

    verts_np = np.array(verts, dtype=np.float64)
    uvs = np.stack([verts_np[:, 0] / w, verts_np[:, 1] / h], axis=1)
    world = verts_np + np.array([meta[part_name]['x'], meta[part_name]['y']], dtype=np.float64)
    return world, uvs, tris


def pack_atlas(imgs: dict, pad: int = 3):
    """Skyline / best-fit shelf packer into power-of-two friendly texture page."""
    order_keys = sorted(imgs.keys(), key=lambda k: -(imgs[k].height * 10000 + imgs[k].width))
    for PW, PH in [(2048, 1536), (2048, 2048), (4096, 2048)]:
        skyline = [(0, PW, 0)]  # list of (x0, x1, y)
        rects = {}
        ok = True
        for name in order_keys:
            rw = imgs[name].width + 2 * pad
            rh = imgs[name].height + 2 * pad
            best_y = 10**9
            best_x = None
            # Candidate x positions from skyline starts
            xs_cand = sorted(set(seg[0] for seg in skyline if seg[0] + rw <= PW))
            for cx in xs_cand:
                max_y = 0
                for sx0, sx1, sy in skyline:
                    if sx1 > cx and sx0 < cx + rw:
                        max_y = max(max_y, sy)
                if max_y + rh <= PH and max_y < best_y:
                    best_y = max_y
                    best_x = cx
            if best_x is None:
                ok = False
                break
            rects[name] = (best_x + pad, best_y + pad)
            # Update skyline over [best_x, best_x + rw]
            new_sky = []
            x_end = best_x + rw
            placed = False
            for sx0, sx1, sy in skyline:
                if sx1 <= best_x or sx0 >= x_end:
                    new_sky.append((sx0, sx1, sy))
                else:
                    if sx0 < best_x:
                        new_sky.append((sx0, best_x, sy))
                    if not placed:
                        new_sky.append((best_x, x_end, best_y + rh))
                        placed = True
                    if sx1 > x_end:
                        new_sky.append((x_end, sx1, sy))
            skyline = new_sky
        if ok:
            return PW, PH, rects
    raise RuntimeError('Failed to pack atlas!')


def main():
    os.makedirs('out', exist_ok=True)
    os.makedirs('prev', exist_ok=True)
    meta = json.load(open('parts/meta.json'))
    order = json.load(open('order.json'))

    # ---- 1. Build Full Bone List (Core Anatomical Bones + Flora Spring Chains)
    bones = [dict(name=n, parent=p, pos=[float(pos[0]), float(pos[1])]) for n, p, pos in BONES]
    chains = {}

    for fn in order['front']:
        if fn in GROUND_FLORA:
            cfg = GROUND_FLORA[fn]
            parent_root = 'root'
            kind = 'ground'
            driver = cfg['driver']
            host = None
        elif fn in ORGAN_FLORA:
            cfg = ORGAN_FLORA[fn]
            parent_root = cfg['parent']
            kind = 'organ'
            driver = cfg['parent']
            host = cfg['host']
        else:
            raise KeyError(f'Missing flora config for {fn}')

        base = cfg['base']
        tip = cfg['tip']
        nb = cfg['nb']
        L = math.dist(base, tip)
        b_names = []
        for i in range(nb):
            t_b = i / nb
            pos = [
                round(base[0] + (tip[0] - base[0]) * t_b, 2),
                round(base[1] + (tip[1] - base[1]) * t_b, 2),
            ]
            bn = f'{fn}_{i}'
            bones.append(dict(name=bn, parent=(parent_root if i == 0 else b_names[-1]), pos=pos))
            b_names.append(bn)
        chains[fn] = dict(
            base=list(base),
            tip=list(tip),
            bones=b_names,
            len=round(L, 2),
            kind=kind,
            driver=driver,
            host=host,
            freq=cfg['freq'],
            zeta=cfg['zeta'],
            gain=cfg['gain'],
            clamp=cfg['clamp'],
        )

    bidx = {b['name']: i for i, b in enumerate(bones)}
    bpos = {b['name']: b['pos'] for b in bones}

    # ---- 2. Pack Atlas Texture with 6-Pass Edge Color Bleed
    imgs = {}
    for n in meta:
        im = Image.open(f'parts/{n}.png').convert('RGBA')
        if n in GLOW_OF:
            im = im.resize(
                (max(1, round(im.width * GLOW_SCALE)), max(1, round(im.height * GLOW_SCALE))),
                Image.LANCZOS,
            )
        imgs[n] = im

    PW, PH, rects = pack_atlas(imgs, pad=3)
    page = Image.new('RGBA', (PW, PH), (0, 0, 0, 0))
    for n, (px, py) in rects.items():
        page.paste(imgs[n], (px, py))

    P_arr = np.array(page).astype(np.float32)
    known = P_arr[..., 3] > 0
    col = P_arr[..., :3].copy()
    for _ in range(6):
        k = cv2.dilate(known.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        s = cv2.blur(col * known[..., None], (3, 3))
        c = cv2.blur(known.astype(np.float32), (3, 3))
        new_px = k & ~known
        col[new_px] = s[new_px] / np.maximum(c[new_px, None], 1e-6)
        known = k
    P_arr[..., :3] = col
    Image.fromarray(np.clip(np.round(P_arr), 0, 255).astype(np.uint8)).save(f'out/{NAME}.png', optimize=True)

    atlas_lines = [
        f'{NAME}.png',
        f'size: {PW},{PH}',
        'format: RGBA8888',
        'filter: Linear,Linear',
        'repeat: none',
    ]
    for n, (px, py) in rects.items():
        w, h = imgs[n].size
        atlas_lines += [
            n,
            '  rotate: false',
            f'  xy: {px}, {py}',
            f'  size: {w}, {h}',
            f'  orig: {w}, {h}',
            '  offset: 0, 0',
            '  index: -1',
        ]
    with open(f'out/{NAME}.atlas', 'w', newline='\n') as f:
        f.write('\n'.join(atlas_lines) + '\n')

    # ---- 3. Build Meshes & Weights
    def weights_flora(fn: str, world: np.ndarray) -> list:
        ch = chains[fn]
        if fn == 'coral_pink':
            # Crucial: lower half (y_img > 455) uses EXACT myocardium weight field at (x,y);
            # upper branches (y_img <= 455) blend smoothly into coral_pink_0 / coral_pink_1!
            host_wts = weights_organ('myocardium', world)
            crown_blend = smoothstep_np(468.0, 412.0, world[:, 1])  # 0 at y>=468 -> 1 at y<=412
            tip_blend = smoothstep_np(448.0, 372.0, world[:, 1])
            b0, b1 = ch['bones'][0], ch['bones'][1]
            out = []
            for i in range(len(world)):
                cb = float(crown_blend[i])
                tb = float(tip_blend[i])
                acc = {}
                for hb, hw in host_wts[i]:
                    acc[hb] = acc.get(hb, 0.0) + hw * (1.0 - cb)
                if cb > 1e-4:
                    acc[b0] = acc.get(b0, 0.0) + cb * (1.0 - tb)
                    acc[b1] = acc.get(b1, 0.0) + cb * tb
                acc = {k: v for k, v in acc.items() if v > 0.003}
                tot = sum(acc.values())
                out.append([(k, v / tot) for k, v in sorted(acc.items(), key=lambda kv: -kv[1])])
            return out

        t_arr = seg_proj_t(world, ch['base'], ch['tip'])
        cw_list = chain_bone_weights(t_arr, ch['bones'])

        if ch['kind'] == 'ground':
            # Base 100% locked to 'root' on the wet stone dock (0.000px drift on base!),
            # with a wide, gentle C1 ramp (0.16 -> 0.68) so wide lateral branches never fold against the base!
            lock_t = 1.0 - smoothstep_np(0.16, 0.68, t_arr)
            lock_y = smoothstep_np(605.0, 654.0, world[:, 1])
            w_root = np.maximum(lock_t, lock_y)
            out = []
            for i in range(len(world)):
                wr = float(w_root[i])
                acc = {}
                if wr > 1e-4:
                    acc['root'] = wr
                rem = 1.0 - wr
                if rem > 1e-4:
                    for cb_n, cb_w in cw_list[i]:
                        acc[cb_n] = acc.get(cb_n, 0.0) + rem * cb_w
                acc = {k: v for k, v in acc.items() if v > 0.003}
                tot = sum(acc.values())
                out.append([(k, v / tot) for k, v in sorted(acc.items(), key=lambda kv: -kv[1])])
            return out

        # Organ-mounted dorsal/venous flora: weld base smoothly to host organ weight field over wide gentle span!
        host_wts = weights_organ(ch['host'], world)
        w_host = 1.0 - smoothstep_np(0.08, 0.62, t_arr)
        out = []
        for i in range(len(world)):
            wh = float(w_host[i])
            acc = {}
            if wh > 1e-4:
                for hb, hw in host_wts[i]:
                    acc[hb] = acc.get(hb, 0.0) + wh * hw
            rem = 1.0 - wh
            if rem > 1e-4:
                for cb_n, cb_w in cw_list[i]:
                    acc[cb_n] = acc.get(cb_n, 0.0) + rem * cb_w
            acc = {k: v for k, v in acc.items() if v > 0.003}
            tot = sum(acc.values())
            out.append([(k, v / tot) for k, v in sorted(acc.items(), key=lambda kv: -kv[1])])
        return out

    def spine_vertices(world: np.ndarray, wts: list) -> list:
        out = []
        for p, wl in zip(world, wts):
            s = sum(w for _, w in wl)
            out.append(len(wl))
            sp = S(p)
            for b, w in wl:
                bp = S(bpos[b])
                out += [bidx[b], round(sp[0] - bp[0], 2), round(sp[1] - bp[1], 2), round(w / s, 4)]
        return out

    slot_list = []
    for n in order['draw_stack']:
        slot_list.append(n)
        if n == 'myocardium':
            for g in order['glows']:
                slot_list.append(g)

    DOMINANT_SLOT_BONE = {
        'dorsal_purple': 'dorsal_arch',
        'venous_blue': 'venous_sinus',
        'myocardium': 'vent_dome',
    }

    mesh_cache = {}
    attachments = {}
    slots = []

    for n in slot_list:
        src = GLOW_OF.get(n, n)
        if src not in mesh_cache:
            spc = SPACING.get(src, DEFAULT_FLORA_SPACING)
            world, uvs, tris = grid_mesh(src, meta, spc)
            if src in ('dorsal_purple', 'venous_blue', 'myocardium'):
                wts = weights_organ(src, world)
            else:
                wts = weights_flora(src, world)
            verts_spine = spine_vertices(world, wts)
            mesh_cache[src] = (world, uvs, tris, wts, verts_spine)

        world, uvs, tris, wts, verts_spine = mesh_cache[src]
        attachments[n] = {
            n: {
                'type': 'mesh',
                'uvs': [round(float(v), 5) for v in uvs.flatten()],
                'triangles': tris,
                'vertices': verts_spine,
                'hull': 0,
                'width': meta[n]['w'],
                'height': meta[n]['h'],
            }
        }

        slot_bone = DOMINANT_SLOT_BONE.get(src, chains[src]['bones'][0] if src in chains else 'root')
        sl = {'name': n, 'bone': slot_bone, 'attachment': n}
        if n in GLOW_OF:
            sl['blend'] = 'additive'
            sl['color'] = 'ffffff00'
        slots.append(sl)

    rig_data = dict(
        bones=bones,
        chains=chains,
        slots=slots,
        attachments=attachments,
        page=[PW, PH],
        mesh={
            k: dict(world=v[0].tolist(), uvs=v[1].tolist(), tris=v[2], wts=v[3])
            for k, v in mesh_cache.items()
        },
    )
    with open('out/rig_static.json', 'w') as f:
        json.dump(rig_data, f)

    tot_verts = sum(len(mesh_cache[GLOW_OF.get(sl['name'], sl['name'])][0]) for sl in slots)
    uniq_verts = sum(len(v[0]) for v in mesh_cache.values())
    uniq_tris = sum(len(v[2]) // 3 for v in mesh_cache.values())
    print(
        f'Built static rig: atlas={PW}x{PH}, bones={len(bones)}, slots={len(slots)}, '
        f'unique_verts={uniq_verts} (total_slot_verts={tot_verts}), unique_tris={uniq_tris}'
    )

    # ---- 4. Diagnostic Rig Overlay Image (prev/rig_diag.jpg)
    src_im = np.array(Image.open('src.png').convert('RGBA'))
    bg = np.full((src_im.shape[0], src_im.shape[1], 3), (30, 34, 44), np.uint8)
    al_f = src_im[..., 3:4].astype(float) / 255.0
    vis = (bg * (1.0 - al_f) + src_im[..., :3] * al_f * 0.78).astype(np.uint8)

    # Draw myocardium wireframe lightly
    myo_w, _, myo_t, myo_wt, _ = mesh_cache['myocardium']
    for t_i in range(0, len(myo_t), 3):
        ia, ib, ic = myo_t[t_i], myo_t[t_i + 1], myo_t[t_i + 2]
        pa = tuple(np.round(myo_w[ia]).astype(int))
        pb = tuple(np.round(myo_w[ib]).astype(int))
        pc = tuple(np.round(myo_w[ic]).astype(int))
        cv2.line(vis, pa, pb, (65, 110, 125), 1)
        cv2.line(vis, pb, pc, (65, 110, 125), 1)

    # Highlight Ground Contact Pad vertices in green
    for p_v, wl in zip(myo_w, myo_wt):
        rw = dict(wl).get('root', 0.0)
        if rw > 0.25:
            col_g = (int(40 * (1 - rw)), int(255 * rw), int(120 * rw))
            cv2.circle(vis, (int(round(p_v[0])), int(round(p_v[1]))), 2, col_g, -1)

    # Ground line y=660
    cv2.line(vis, (0, 660), (src_im.shape[1], 660), (80, 255, 120), 2)

    # Draw bone links & pivots
    for b in bones:
        bx, by = int(round(b['pos'][0])), int(round(b['pos'][1]))
        if b['parent'] and b['parent'] != 'root':
            px, py = int(round(bpos[b['parent']][0])), int(round(bpos[b['parent']][1]))
            cv2.line(vis, (px, py), (bx, by), (255, 200, 90), 2, cv2.LINE_AA)
        is_orf = b['name'].startswith('orifice_')
        is_flr = any(b['name'].startswith(fn + '_') for fn in chains)
        col_b = (80, 255, 255) if is_orf else ((140, 235, 130) if is_flr else (255, 90, 110))
        rad = 6 if is_orf else (3 if is_flr else 5)
        cv2.circle(vis, (bx, by), rad, col_b, -1, cv2.LINE_AA)
        if not is_flr:
            cv2.putText(vis, b['name'], (bx + 6, by + 4), cv2.FONT_HERSHEY_SIMPLEX, 0.38, (255, 248, 210), 1, cv2.LINE_AA)

    Image.fromarray(vis).save('prev/rig_diag.jpg', quality=90)
    print('Saved prev/rig_diag.jpg')


if __name__ == '__main__':
    main()
