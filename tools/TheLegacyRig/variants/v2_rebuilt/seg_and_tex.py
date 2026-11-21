"""Stage 1: True 3D Depth-Sorted Seamless Macro-Layer Segmentation, Push-Pull Fill & 3-Zone Coronary Glow.

True 3D Painter's Depth Order (Back -> Front):
  1. BACK_WEEDS       : weed_left, weed_ground_left, weed_top_left, weed_back_a, weed_back_c,
                        weed_ground_right, weed_edge_right (42px push-pull green stem roots behind organs!)
  2. dorsal_purple    : Unified dark-purple dorsal aortic/venous complex (45px push-pull underlap)
  3. MID_PURPLE_WEEDS : weed_back_b, weed_back_d (y<275), weed_right (in front of dorsal_purple, behind venous_blue/myocardium)
  4. venous_blue      : Unified slate-blue right venous manifold (clean push-pull behind myocardium & front flora only)
  5. MID_BLUE_WEEDS   : weed_blue (on venous_blue, behind myocardium)
  6. myocardium       : ONE single unified cyan-teal heart mesh (zero internal cut seams, full push-pull fill)
  7. GLOW_ZONES       : glow_sa_root, glow_av_bulb, glow_purkinje_apex (exact myocardium mesh/UVs/weights, 0.000px drift)
  8. FRONT_FLORA      : coral_purple, coral_pink, weed_bottom_mid, weed_bottom_right, weed_ground_bl
"""
import json
import os
import cv2
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi
from skimage.segmentation import watershed


BACK_WEEDS = [
    'weed_left',
    'weed_ground_left',
    'weed_top_left',
    'weed_back_a',
    'weed_back_c',
    'weed_ground_right',
    'weed_edge_right',
]
MID_PURPLE_WEEDS = ['weed_back_b', 'weed_back_d', 'weed_right']
MID_BLUE_WEEDS = ['weed_blue']
FRONT_FLORA = ['coral_purple', 'coral_pink', 'weed_bottom_mid', 'weed_bottom_right', 'weed_ground_bl']


def pushpull(col: np.ndarray, wt: np.ndarray) -> np.ndarray:
    """Multi-scale pyramid push-pull color extrapolation where wt == 0."""
    levels = [(col * wt[..., None], wt.astype(np.float32))]
    c, w = levels[0]
    while min(w.shape) > 2:
        c = cv2.pyrDown(c)
        w = cv2.pyrDown(w)
        levels.append((c, w))
    cc, ww = levels[-1]
    est = cc / np.maximum(ww, 1e-6)[..., None]
    for c, w in reversed(levels[:-1]):
        up = cv2.resize(est, (w.shape[1], w.shape[0]), interpolation=cv2.INTER_LINEAR)
        here = c / np.maximum(w, 1e-6)[..., None]
        k = np.clip(w * 4.0, 0.0, 1.0)[..., None]
        est = here * k + up * (1.0 - k)
    return est


def rect(H, W, x0, y0, x1, y1):
    r = np.zeros((H, W), bool)
    r[y0:y1, x0:x1] = True
    return r


def disc(H, W, cx, cy, rr):
    yy, xx = np.ogrid[:H, :W]
    return (xx - cx) ** 2 + (yy - cy) ** 2 < rr * rr


def keep_largest_components(mask: np.ndarray, min_area: int = 250) -> np.ndarray:
    n, lab, st, _ = cv2.connectedComponentsWithStats(mask.astype(np.uint8), 8)
    out = np.zeros_like(mask, bool)
    for cid in range(1, n):
        if st[cid, cv2.CC_STAT_AREA] >= min_area:
            out |= (lab == cid)
    return out


def main():
    os.makedirs('parts', exist_ok=True)
    os.makedirs('prev', exist_ok=True)

    im_u8 = np.array(Image.open('src.png').convert('RGBA'))
    im = im_u8.astype(np.float32)
    rgb = im[..., :3]
    a = im[..., 3]
    H, W = a.shape

    hsv = cv2.cvtColor(im_u8[..., :3], cv2.COLOR_RGB2HSV)
    h = hsv[..., 0].astype(int) * 2
    s = hsv[..., 1].astype(int)
    v = hsv[..., 2].astype(int)

    m_any = a > 0
    m = a > 8

    green = (h >= 45) & (h <= 110) & m & (s > 50)
    cyan = (h > 170) & (h <= 205) & m & (s > 40)
    blue = (h > 205) & (h <= 235) & m & (s > 40)
    purple = (h > 235) & (h <= 300) & m & (s > 30)
    pink = ((h > 300) | (h < 20)) & m & (s > 40)
    strongbody = (cyan | blue | purple) & (v > 80)
    bodyish = (cyan | blue | purple) & (v > 35) & (s > 35)

    # ---- 1. Flora extraction
    g_cc = cv2.morphologyEx(green.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
    _, lab_g, _, _ = cv2.connectedComponentsWithStats(g_cc, 8)

    SPRIGS = {
        'weed_top_left': [1],
        'weed_left': [22, 97],
        'weed_bottom_mid': [209],
        'weed_bottom_right': [214, 265, 295],
        'weed_right': [12],
        'weed_ground_left': [264, 248],
        'weed_back_a': [7],
        'weed_back_b': [3],
        'weed_back_c': [2],
        'weed_back_d': [14, 32],  # Strictly dorsal stalk above bulb (y < 275)
        'weed_blue': [120],
        'weed_ground_right': [282],
        'weed_edge_right': [244],
        'weed_ground_bl': [328, 355],
    }

    cand = (~strongbody) & m & (~pink)

    def grow(core, it=4, allow_mask=None):
        c_allow = (cand | core) if allow_mask is None else allow_mask
        gm = core.copy()
        ker3 = np.ones((3, 3), np.uint8)
        for _ in range(it):
            gm = ((cv2.dilate(gm.astype(np.uint8), ker3) > 0) & c_allow) | gm
        return gm

    box_cp = rect(H, W, 160, 330, 400, 520)
    coral_pink = pink & box_cp
    coral_pink = cv2.morphologyEx(coral_pink.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8)) > 0
    cp_allow = (
        pink
        | (((h > 285) | (h < 28)) & (s > 32) & (v > 72) & m)
        | (cand & (v > 78) & rect(H, W, 175, 465, 360, 520))
    ) & box_cp
    coral_pink = grow(coral_pink & box_cp, 2, cp_allow)
    coral_pink = keep_largest_components(coral_pink, min_area=300)

    box_cpu = rect(H, W, 740, 455, 1000, 689)
    coral_purple = purple & box_cpu & (h < 285)
    coral_purple = keep_largest_components(coral_purple, min_area=200)
    coral_purple = grow(coral_purple, 3, (cand | purple) & box_cpu)
    coral_purple = keep_largest_components(coral_purple, min_area=500)

    corals = coral_pink | coral_purple

    bt = (strongbody | (pink & (v > 60))) & ~corals
    bt = cv2.morphologyEx(bt.astype(np.uint8), cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    bt = cv2.morphologyEx(bt, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (13, 13))) > 0
    body_true = ndi.binary_fill_holes(bt) & m

    outside = m & ~body_true & ~corals
    sprig_masks = {}
    taken = corals.copy()
    ker3 = np.ones((3, 3), np.uint8)
    cores = {name: np.isin(lab_g, ids) for name, ids in SPRIGS.items()}
    grown = {k: val.copy() for k, val in cores.items()}
    yy_grid, xx_grid = np.ogrid[:H, :W]

    for it in range(14):
        for k in grown:
            d = cv2.dilate(grown[k].astype(np.uint8), ker3) > 0
            if k in BACK_WEEDS:
                # Background perimeter weeds stay strictly outside body_true so organ rims keep their own moss!
                allow = outside & ~bodyish
            elif k in MID_PURPLE_WEEDS:
                allow = (outside & ~bodyish & (it < 14)) | (cand & ~bodyish & (green | (v > 65)) & (it < 3))
            else:
                allow = (outside & ~bodyish & (it < 14)) | (cand & ~bodyish & (it < 7))
            if k == 'weed_back_d':
                allow = allow & (yy_grid < 275)
            grown[k] |= d & allow

    urch = disc(H, W, 296, 656, 29)
    for k in grown:
        g2 = grown[k] & ~taken & ~urch
        if k == 'weed_back_d':
            g2 &= (yy_grid < 275)
        sprig_masks[k] = g2
        taken |= g2

    all_flora_names = ['coral_purple', 'coral_pink'] + list(SPRIGS.keys())
    flora_masks = {'coral_purple': coral_purple, 'coral_pink': coral_pink, **sprig_masks}

    # ---- 2. Watershed Body into 3 Unified Macro-Organs:
    #         1 = dorsal_purple, 2 = venous_blue, 3 = myocardium
    body_rem = m & ~taken
    markers = np.zeros((H, W), np.int32)
    MACRO_PARTS = ['dorsal_purple', 'venous_blue', 'myocardium']

    seeds = {
        'dorsal_purple': (
            purple | (green & (xx_grid > 848) & (xx_grid < 925) & (yy_grid < 340)),
            [
                rect(H, W, 640, 60, 690, 110),
                rect(H, W, 700, 25, 760, 80),
                rect(H, W, 760, 120, 800, 180),
                rect(H, W, 830, 140, 1000, 230),
                rect(H, W, 700, 215, 800, 245),
                rect(H, W, 850, 245, 922, 338),
                rect(H, W, 1000, 250, 1080, 300),
                rect(H, W, 1260, 390, 1330, 440),
                rect(H, W, 1170, 560, 1270, 600),
                rect(H, W, 1100, 470, 1150, 520),
            ],
        ),
        'venous_blue': (
            blue,
            [
                rect(H, W, 920, 340, 1080, 380),
                rect(H, W, 1190, 290, 1240, 340),
                rect(H, W, 1110, 360, 1160, 390),
                rect(H, W, 1180, 440, 1280, 480),
                rect(H, W, 1010, 520, 1070, 560),
                rect(H, W, 1000, 470, 1040, 500),
            ],
        ),
        'myocardium': (
            cyan | pink,
            [
                rect(H, W, 180, 240, 330, 285),
                rect(H, W, 140, 350, 300, 385),
                rect(H, W, 380, 190, 570, 240),
                rect(H, W, 100, 440, 160, 510),
                rect(H, W, 420, 420, 600, 470),
                rect(H, W, 180, 570, 400, 640),
                rect(H, W, 450, 590, 640, 640),
                disc(H, W, 765, 365, 78),
                disc(H, W, 945, 470, 35),
                rect(H, W, 900, 540, 980, 600),
            ],
        ),
    }

    for idx_m, p_name in enumerate(MACRO_PARTS, 1):
        cls_mask, regs = seeds[p_name]
        for r in regs:
            markers[r & cls_mask & body_rem] = idx_m

    lum = cv2.GaussianBlur(v.astype(np.float32), (0, 0), 2.0)
    elev = 255.0 - lum
    hue_cls = np.zeros((H, W), np.uint8)
    hue_cls[cyan] = 1
    hue_cls[blue] = 2
    hue_cls[purple] = 3
    edge = np.zeros((H, W), np.float32)
    for k_c in (1, 2, 3):
        mk_c = (hue_cls == k_c).astype(np.float32)
        mk_c = cv2.GaussianBlur(mk_c, (0, 0), 1.5)
        edge += np.hypot(cv2.Sobel(mk_c, cv2.CV_32F, 1, 0), cv2.Sobel(mk_c, cv2.CV_32F, 0, 1))
    elev = elev + edge * 120.0
    labels = watershed(elev, markers, mask=body_rem)

    # Keep pink veins on bulb intact (~pink!) while handing dorsal moss/purple shadow behind bulb to dorsal_purple
    dorsal_behind_bulb = body_rem & (xx_grid > 848) & (xx_grid < 925) & (yy_grid < 340) & ~cyan & ~blue & ~pink
    labels[dorsal_behind_bulb] = 1

    known0 = labels > 0
    stray = (known0 & ~body_true) | ((labels > 0) & (a <= 24) & ~body_true)
    for k in list(SPRIGS.keys()):
        mk = flora_masks[k]
        near = cv2.dilate(mk.astype(np.uint8), np.ones((15, 15), np.uint8)) > 0
        if k == 'weed_back_d':
            near &= (yy_grid < 275)
        add = stray & near
        mk |= add
        stray &= ~add
        labels[add] = 0
        flora_masks[k] = mk

    for idx_m in (1, 2, 3):
        part_m = (labels == idx_m)
        clean_m = keep_largest_components(part_m, min_area=350)
        labels[part_m & ~clean_m] = 0

    fk = list(flora_masks.keys())
    U = np.zeros((H, W), np.int32)
    kn = labels > 0
    ii = ndi.distance_transform_edt(~kn, return_distances=False, return_indices=True)
    U[m_any] = labels[ii[0], ii[1]][m_any]
    for j, k in enumerate(fk):
        U[flora_masks[k]] = 100 + j
    faint = m_any & (a <= 24) & ~body_true
    solid = (U > 0) & ~faint
    ii = ndi.distance_transform_edt(~solid, return_distances=False, return_indices=True)
    U[faint] = U[ii[0], ii[1]][faint]
    for j, k in enumerate(fk):
        flora_masks[k] = (U == 100 + j)
    labels = np.where(U < 100, U, 0)

    flora_any = np.zeros((H, W), bool)
    for k in flora_masks:
        flora_any |= flora_masks[k]

    known = labels > 0
    idx_edt = ndi.distance_transform_edt(~known, return_distances=False, return_indices=True)
    full_under = labels[idx_edt[0], idx_edt[1]]
    full_under[~m_any] = 0
    body_lab = full_under.copy()
    body_lab[flora_any] = 0

    # ---- 3. Solid Opaque Interior Mask
    # Exclude ONLY border-connected exterior sky/fringe (a < 238) + the two genuine pipe sky holes,
    # while keeping enclosed shadow crevices behind coral_pink 100% solid!
    low_a = (a < 238)
    n_la, lab_la, st_la, cen_la = cv2.connectedComponentsWithStats(low_a.astype(np.uint8), 8)
    sky_or_hole = np.zeros((H, W), bool)
    for cid in range(1, n_la):
        x0_c, y0_c, w_c, h_c, area_c = st_la[cid]
        cx_c, cy_c = cen_la[cid]
        touches_border = (x0_c == 0) or (y0_c == 0) or (x0_c + w_c >= W) or (y0_c + h_c >= H)
        is_dorsal_arch_hole = (720 < cx_c < 920) and (145 < cy_c < 255) and (area_c >= 60)
        is_right_pipe_hole = (1020 < cx_c < 1120) and (515 < cy_c < 585) and (area_c >= 60)
        if touches_border or is_dorsal_arch_hole or is_right_pipe_hole:
            sky_or_hole |= (lab_la == cid)
    interior_solid = ~(cv2.dilate(sky_or_hole.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0)
    dist_from_sky = ndi.distance_transform_edt(~sky_or_hole)

    organ_core = cv2.morphologyEx(body_true.astype(np.uint8), cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15))) > 0
    organ_hull = cv2.morphologyEx(organ_core.astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (95, 95))) > 0
    organ_hull |= cv2.dilate((body_lab > 0).astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (29, 29))) > 0
    organ_hull |= rect(H, W, 780, 560, 955, 654)
    organ_hull |= rect(H, W, 185, 325, 360, 515)  # Solid cyan wall behind coral_pink & lower cannon crease!
    organ_hull = ndi.binary_fill_holes(organ_hull)
    interior_organ = interior_solid & organ_hull

    # Explicit Depth Stack (back -> front)
    DRAW_STACK = (
        BACK_WEEDS
        + ['dorsal_purple']
        + MID_PURPLE_WEEDS
        + ['venous_blue']
        + MID_BLUE_WEEDS
        + ['myocardium']
        + FRONT_FLORA
    )
    DRAW_STACK = [n for n in DRAW_STACK if (n in MACRO_PARTS or flora_masks[n].sum() >= 50)]
    pid = {p: i + 1 for i, p in enumerate(MACRO_PARTS)}
    vis_of = {}
    for n in DRAW_STACK:
        vis_of[n] = (body_lab == pid[n]) if n in MACRO_PARTS else flora_masks[n]

    EXT = 45
    ell_ext = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * EXT + 1, 2 * EXT + 1))
    ell_back_weed = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (85, 85))
    ell_close = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (121, 121))

    meta = {}

    def save_part(name, rgb_arr, alpha_arr, bbox_mask):
        ys, xs = np.nonzero(bbox_mask)
        x0, x1, y0, y1 = int(xs.min()), int(xs.max()) + 1, int(ys.min()), int(ys.max()) + 1
        out = np.dstack([rgb_arr, alpha_arr])[y0:y1, x0:x1]
        Image.fromarray(np.clip(np.round(out), 0, 255).astype(np.uint8)).save(f'parts/{name}.png')
        meta[name] = dict(x=x0, y=y0, w=int(x1 - x0), h=int(y1 - y0))
        return x0, y0, x1, y1

    layers = {}
    for rank_i, name in enumerate(DRAW_STACK):
        vis = vis_of[name]
        # Pixels covered by layers drawn LATER (in front of `name`), strictly inside opaque boss interior!
        infront = np.zeros((H, W), bool)
        for q in DRAW_STACK[rank_i + 1:]:
            infront |= vis_of[q]
        allowed_hidden = infront & interior_organ

        if name in MACRO_PARTS:
            if name == 'dorsal_purple':
                # Keep dorsal_purple's hidden underlap >= 18px inside top sky rim & x >= 655 so no purple fin peeks above conus_atrium!
                allowed_hidden &= (xx_grid >= 655) & ~((xx_grid < 740) & (dist_from_sky < 18.0))
            elif name == 'venous_blue':
                # Prevent venous_blue hidden extension from creeping onto dorsal_purple above y=342 or over purple_lr!
                purple_lr_zone = (xx_grid > 1095) & (yy_grid > 505)
                allowed_hidden &= (yy_grid >= 342) & ~purple_lr_zone & (dist_from_sky >= 10.0)
            under = (full_under == pid[name]) & allowed_hidden

            own = vis | under
            grow_m = cv2.dilate(own.astype(np.uint8), ell_ext) > 0
            big_m = cv2.morphologyEx(own.astype(np.uint8), cv2.MORPH_CLOSE, ell_close) > 0
            big_m = ndi.binary_fill_holes(big_m)

            if name == 'dorsal_purple':
                bridge = np.zeros((H, W), np.uint8)
                cv2.fillPoly(
                    bridge,
                    [np.array([[930, 270], [1160, 310], [1310, 420], [1235, 575], [1070, 505], [900, 360]], np.int32)],
                    1,
                )
                big_m |= (bridge > 0)

            ext = (grow_m | big_m) & allowed_hidden
            full = ndi.binary_fill_holes(vis | under | ext)
            full = (full & allowed_hidden) | vis
            hidden_only = full & ~vis
            hidden_smooth = cv2.morphologyEx(hidden_only.astype(np.uint8), cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9))) > 0
            full = vis | (hidden_smooth & allowed_hidden) | under

            n_c, lab_c, _, _ = cv2.connectedComponentsWithStats(full.astype(np.uint8), 8)
            keep_c = np.zeros((H, W), bool)
            for cid in (set(np.unique(lab_c[vis])) - {0}):
                keep_c |= (lab_c == cid)
            layers[name] = dict(vis=vis, full=keep_c)

            other = ~vis
            band = cv2.dilate(other.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0
            sky_band = cv2.dilate((a == 0).astype(np.uint8), np.ones((5, 5), np.uint8)) > 0
            src_wt = vis & ~(band & ~sky_band) & (a >= 245)
            if name == 'myocardium':
                near_flora = cv2.dilate(flora_any.astype(np.uint8), np.ones((15, 15), np.uint8)) > 0
                src_wt &= ~(near_flora & (green | pink | purple))
            else:
                near_flora = cv2.dilate(flora_any.astype(np.uint8), np.ones((11, 11), np.uint8)) > 0
                src_wt &= ~(near_flora & green)

            fill = pushpull(rgb, src_wt.astype(np.float32))
            fill = cv2.GaussianBlur(fill, (0, 0), 1.6)
            col = np.where(vis[..., None], rgb, fill)
            dist_in_full = ndi.distance_transform_edt(keep_c)
            ext_alpha = np.clip((dist_in_full - 0.5) / 2.5, 0.0, 1.0) * 254.0
            alpha_out = np.where(vis, a, np.where(keep_c, ext_alpha, 0.0)).astype(np.float32)
            save_part(name, col, alpha_out, keep_c)

        elif name in ('weed_top_left', 'weed_back_a', 'weed_back_b', 'weed_back_c', 'weed_right'):
            # Compact 18px dorsal stem root tucked strictly >= 14px inside the occluding organ rim!
            ell_stem = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (37, 37))
            grow_w = cv2.dilate(vis.astype(np.uint8), ell_stem) > 0
            ys_v, xs_v = np.nonzero(vis)
            cx_v, cy_v = float(xs_v.mean()), float(ys_v.max())
            rad_box = ((xx_grid - cx_v) ** 2 + (yy_grid - cy_v) ** 2) < (42.0 ** 2)
            ext_w = grow_w & rad_box & allowed_hidden & (dist_from_sky >= 14.0)
            full_w = vis | ext_w

            src_wt = vis & (a >= 220)
            if src_wt.sum() < 10:
                src_wt = vis
            fill_w = pushpull(rgb, src_wt.astype(np.float32))
            fill_w = cv2.GaussianBlur(fill_w, (0, 0), 1.2)
            col_w = np.where(vis[..., None], rgb, fill_w)
            dist_w = ndi.distance_transform_edt(full_w)
            ext_al = np.clip((dist_w - 0.5) / 2.5, 0.0, 1.0) * 254.0
            alpha_w = np.where(vis, a, np.where(full_w, ext_al, 0.0)).astype(np.float32)
            save_part(name, col_w, alpha_w, full_w)

        else:
            alpha_f = np.where(vis, a, 0.0).astype(np.float32)
            save_part(name, rgb, alpha_f, vis)

    # ---- 4. 3-Zone Traveling Coronary Impulse Glow on Myocardium
    vis_myo = layers['myocardium']['vis']
    full_myo = layers['myocardium']['full']

    excl_coral_vein = (
        (cv2.dilate(coral_pink.astype(np.uint8), np.ones((11, 11), np.uint8)) > 0)
        | (cv2.dilate(coral_purple.astype(np.uint8), np.ones((11, 11), np.uint8)) > 0)
        | rect(H, W, 140, 480, 260, 555)
    )

    r_ch, g_ch = rgb[..., 0], rgb[..., 1]
    vein_raw = (
        vis_myo
        & ~excl_coral_vein
        & ((h > 295) | (h < 26))
        & (s > 28)
        & ((r_ch - g_ch) > 7.0)
        & (v > 75)
    )
    vein_clean = keep_largest_components(
        cv2.morphologyEx(vein_raw.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8)) > 0,
        min_area=14,
    )
    vm = (vein_clean & ~excl_coral_vein).astype(np.float32)
    g_core = cv2.GaussianBlur(vm, (0, 0), 1.2)
    g_mid = cv2.GaussianBlur(vm, (0, 0), 4.5)
    g_wide = cv2.GaussianBlur(vm, (0, 0), 11.0)
    coral_soft_fade = 1.0 - cv2.GaussianBlur((coral_pink | coral_purple).astype(np.float32), (0, 0), 6.0)
    ga_total = np.clip(g_core * 1.45 + g_mid * 1.55 + g_wide * 0.80, 0.0, 1.0) * 255.0 * np.clip(coral_soft_fade, 0.0, 1.0)
    ga_total = np.where(full_myo, ga_total, 0.0)

    yy_f, xx_f = np.mgrid[0:H, 0:W].astype(np.float32)
    pts_cond = np.stack([xx_f, yy_f], axis=-1)

    p0 = np.array([790.0, 310.0], np.float32)
    p1 = np.array([580.0, 430.0], np.float32)
    p2 = np.array([355.0, 555.0], np.float32)

    v01 = p1 - p0
    L01 = float(np.linalg.norm(v01))
    t01 = np.clip(((pts_cond - p0) @ v01) / (L01 * L01), 0.0, 1.0)
    d01 = np.linalg.norm(pts_cond - (p0 + t01[..., None] * v01), axis=-1)

    v12 = p2 - p1
    L12 = float(np.linalg.norm(v12))
    t12 = np.clip(((pts_cond - p1) @ v12) / (L12 * L12), 0.0, 1.0)
    d12 = np.linalg.norm(pts_cond - (p1 + t12[..., None] * v12), axis=-1)

    L_tot = L01 + L12
    u_seg1 = (t01 * L01) / L_tot
    u_seg2 = (L01 + t12 * L12) / L_tot
    w_s1 = np.exp(-((d01 / 95.0) ** 2)) + 1e-6
    w_s2 = np.exp(-((d12 / 95.0) ** 2)) + 1e-6
    u_cond = (w_s1 * u_seg1 + w_s2 * u_seg2) / (w_s1 + w_s2)
    u_cond = cv2.GaussianBlur(u_cond.astype(np.float32), (0, 0), 14.0)

    def smoothstep(edge0, edge1, x):
        t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
        return t * t * (3.0 - 2.0 * t)

    s12 = smoothstep(0.16, 0.34, u_cond)
    s23 = smoothstep(0.42, 0.62, u_cond)
    w_sa = 1.0 - s12
    w_av = s12 * (1.0 - s23)
    w_pk = s23

    glow_rgb = np.zeros_like(rgb)
    glow_rgb[...] = (255.0, 68.0, 112.0)

    GLOW_NAMES = ['glow_sa_root', 'glow_av_bulb', 'glow_purkinje_apex']
    for g_name, w_zone in zip(GLOW_NAMES, [w_sa, w_av, w_pk]):
        save_part(g_name, glow_rgb, ga_total * w_zone, full_myo)
        print(f'  {g_name}: active_glow_sum={float((ga_total * w_zone).sum() / 255.0):.1f}')

    with open('parts/meta.json', 'w') as f:
        json.dump(meta, f, indent=2)

    kept_flora = [n for n in DRAW_STACK if n not in MACRO_PARTS]
    order_data = {
        'draw_stack': DRAW_STACK,
        'order': MACRO_PARTS,
        'glows': GLOW_NAMES,
        'front': kept_flora,
    }
    with open('order.json', 'w') as f:
        json.dump(order_data, f, indent=2)

    # ---- 5. Verify Rest Recomposition Accuracy vs src.png
    src_pil = Image.open('src.png').convert('RGBA')
    comp = Image.new('RGBA', src_pil.size, (0, 0, 0, 0))
    for n in DRAW_STACK:
        p_im = Image.open(f'parts/{n}.png')
        comp.alpha_composite(p_im, (meta[n]['x'], meta[n]['y']))
    arr_c = np.array(comp).astype(np.float32)
    arr_s = np.array(src_pil).astype(np.float32)
    premul_c = arr_c[..., :3] * (arr_c[..., 3:] / 255.0)
    premul_s = arr_s[..., :3] * (arr_s[..., 3:] / 255.0)
    diff = np.abs(premul_c - premul_s)
    mean_diff = float(diff.mean())
    max_diff = float(diff.max())
    px_gt20 = int((diff.max(axis=-1) > 20.0).sum())
    print(f'Rest recomposition: mean_abs_diff={mean_diff:.5f}/255, max={max_diff:.2f}, px>20={px_gt20}')
    assert mean_diff < 0.6, f'Recomposition diff too high: {mean_diff}'

    # ---- 6. Save Parts Contact Sheet
    tiles = []
    for n in MACRO_PARTS + GLOW_NAMES + kept_flora:
        p_im = Image.open(f'parts/{n}.png').convert('RGBA')
        p_im.thumbnail((420, 260), Image.LANCZOS)
        tile = Image.new('RGBA', (440, 290), (42, 46, 58, 255))
        tile.alpha_composite(p_im, ((440 - p_im.width) // 2, (265 - p_im.height) // 2))
        d = ImageDraw.Draw(tile)
        d.text((8, 270), f"{n} ({meta[n]['w']}x{meta[n]['h']})", fill=(240, 240, 255))
        tiles.append(tile.convert('RGB'))

    cols = 4
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new('RGB', (440 * cols, 290 * rows), (30, 32, 40))
    for idx_t, t_im in enumerate(tiles):
        sheet.paste(t_im, ((idx_t % cols) * 440, (idx_t // cols) * 290))
    sheet.save('prev/parts_sheet.jpg', quality=90)
    print(f'Saved {len(meta)} parts and prev/parts_sheet.jpg')


if __name__ == '__main__':
    main()
