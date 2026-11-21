import json
import os
import cv2
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
PARTS_DIR = os.path.abspath(
    os.path.join(HERE, '..', '..', '..', 'tools', 'ScalePeelerRig', 'parts')
)
os.makedirs(PARTS_DIR, exist_ok=True)

src = np.array(
    Image.open(os.path.join(HERE, 'character_final.png')).convert('RGBA')
)
H, W = src.shape[:2]


def poly_mask(pts, feather=0.0):
    m = np.zeros((H, W), np.float32)
    cv2.fillPoly(m, [np.array(pts, np.int32)], 1.0)
    if feather > 0:
        m = cv2.GaussianBlur(m, (0, 0), feather)
    return m


def cut_part(arr, mask, pad=6):
    out = arr.copy()
    out[..., 3] = np.clip(out[..., 3].astype(np.float32) * mask, 0, 255).astype(
        np.uint8
    )
    ys, xs = np.where(out[..., 3] > 2)
    if len(xs) == 0:
        return Image.fromarray(out[:10, :10]), {'x': 0, 'y': 0, 'w': 10, 'h': 10}
    x0 = max(0, int(xs.min()) - pad)
    y0 = max(0, int(ys.min()) - pad)
    x1 = min(W, int(xs.max()) + 1 + pad)
    y1 = min(H, int(ys.max()) + 1 + pad)
    crop = out[y0:y1, x0:x1]
    return Image.fromarray(crop, 'RGBA'), {
        'x': x0,
        'y': y0,
        'w': x1 - x0,
        'h': y1 - y0,
    }


r = src[..., 0].astype(float)
g = src[..., 1].astype(float)
b = src[..., 2].astype(float)
val = np.maximum(np.maximum(r, g), b)

# 1. Extract 4 dorsal crystal plates cleanly
PLATES = {
    'plate_0': [(390, 184), (382, 118), (434, 58), (478, 110), (470, 180), (434, 195), (396, 194)],
    'plate_1': [(522, 178), (534, 96), (602, 55), (624, 125), (598, 198), (558, 199), (528, 186)],
    'plate_2': [(646, 196), (674, 150), (722, 154), (748, 194), (728, 254), (692, 266), (660, 256)],
    'plate_3': [(712, 310), (748, 286), (806, 310), (784, 366), (742, 382), (714, 369)],
}
SLIVER_5 = [(716, 420), (776, 438), (770, 480), (718, 496)]

parts_out = {}
plate_all_poly = np.zeros((H, W), np.float32)

for pname, pts in PLATES.items():
    pm = poly_mask(pts, feather=0.0)
    plate_all_poly = np.maximum(plate_all_poly, pm)
    cyan = ((b + g * 0.6) - r * 1.35) > (-10.0 if pname == 'plate_2' else -6.0)
    cm = (pm > 0.5) & cyan & (src[..., 3] > 15)
    cm = cv2.dilate(cm.astype(np.uint8), np.ones((3, 3), np.uint8), iterations=1).astype(np.float32)
    cm = np.clip(cv2.GaussianBlur(cm, (0, 0), 0.6), 0, 1) * pm

    p_arr = src.copy()
    p_arr[..., 3] = np.clip(p_arr[..., 3].astype(float) * cm, 0, 255).astype(np.uint8)
    shift_x, shift_y = {
        'plate_0': (6, 14),
        'plate_1': (-4, 14),
        'plate_2': (-10, 10),
        'plate_3': (-16, 3),
    }[pname]
    M = np.float32([[1, 0, shift_x], [0, 1, shift_y]])
    shifted = cv2.warpAffine(p_arr, M, (W, H), flags=cv2.INTER_LINEAR)
    a_top = p_arr[..., 3:4].astype(float) / 255.0
    a_bot = shifted[..., 3:4].astype(float) / 255.0 * 0.9
    shifted_dark = shifted.copy()
    shifted_dark[..., :3] = np.clip(shifted_dark[..., :3].astype(float) * 0.80, 0, 255).astype(np.uint8)
    p_arr[..., :3] = np.clip(
        p_arr[..., :3] * a_top + shifted_dark[..., :3] * a_bot * (1.0 - a_top), 0, 255
    ).astype(np.uint8)
    p_arr[..., 3] = np.clip((a_top + a_bot * (1.0 - a_top))[..., 0] * 255.0, 0, 255).astype(np.uint8)
    parts_out[pname] = cut_part(p_arr, np.ones((H, W), np.float32))

plate_all_poly = np.maximum(plate_all_poly, poly_mask(SLIVER_5))

# 2. Feelers
FEELERS = {
    'feeler_far': [(145, 146), (186, 158), (218, 186), (206, 208), (170, 185), (144, 170)],
    'feeler_near': [(234, 126), (272, 128), (294, 166), (292, 202), (270, 204), (256, 166), (235, 148)],
}
feelers_union = np.zeros((H, W), np.float32)
for fname, fpts in FEELERS.items():
    parts_out[fname] = cut_part(src.copy(), poly_mask(fpts))
    rm_fpts = (
        [(144, 145), (186, 156), (208, 178), (195, 192), (168, 182), (143, 170)]
        if fname == 'feeler_far'
        else [(233, 125), (274, 127), (290, 158), (272, 172), (255, 156), (234, 148)]
    )
    feelers_union = np.maximum(feelers_union, poly_mask(rm_fpts))

# 3. jaw_upper (tucks neatly behind brown brow hood!)
JAW_UPPER_GEOM = [
    (112, 336), (138, 286), (166, 278), (206, 290), (228, 304), (229, 332),
    (212, 368), (188, 376), (174, 344), (156, 412), (120, 416), (105, 388)
]
ju_geom_m = poly_mask(JAW_UPPER_GEOM)
throat_exclude_ju = poly_mask([(168, 365), (196, 364), (222, 340), (232, 326), (240, 385), (165, 390)])
ju_keep = (ju_geom_m > 0.5) & (throat_exclude_ju < 0.5) & (src[..., 3] > 20)
ju_arr = src.copy()
ju_arr[..., 3] = np.clip(ju_arr[..., 3].astype(float) * ju_keep.astype(float), 0, 255).astype(np.uint8)
bone_col = np.array([192, 170, 142], np.uint8)
ju_hinge = np.zeros((H, W), np.uint8)
cv2.ellipse(ju_hinge, (218, 314), (38, 23), -8, 0, 360, 255, -1)
ju_ha = cv2.GaussianBlur(ju_hinge.astype(np.float32) / 255.0, (0, 0), 0.7)
cur_a = ju_arr[..., 3].astype(float) / 255.0
for c_i in range(3):
    ju_arr[..., c_i] = np.where(cur_a > 0.5, ju_arr[..., c_i], bone_col[c_i])
ju_arr[..., 3] = np.clip(np.maximum(cur_a, ju_ha) * 255.0, 0, 255).astype(np.uint8)
parts_out['jaw_upper'] = cut_part(ju_arr, np.ones((H, W), np.float32))

# 4. jaw_lower (tucks behind terracotta cheek flap smoothly!)
JAW_LOWER_GEOM = [
    (142, 434), (176, 428), (218, 428), (254, 386), (274, 348), (294, 336),
    (326, 339), (324, 382), (300, 422), (276, 474), (225, 488), (170, 474), (140, 446)
]
jl_geom_m = poly_mask(JAW_LOWER_GEOM)
mouth_pocket_clip = poly_mask([(142, 426), (210, 424), (248, 382), (274, 340), (245, 335), (140, 400)])
jl_keep = (jl_geom_m > 0.5) & (mouth_pocket_clip < 0.5) & (src[..., 3] > 20)
jl_arr = src.copy()
jl_arr[..., 3] = np.clip(jl_arr[..., 3].astype(float) * jl_keep.astype(float), 0, 255).astype(np.uint8)
jl_hinge = np.zeros((H, W), np.uint8)
cv2.ellipse(jl_hinge, (305, 352), (40, 28), -15, 0, 360, 255, -1)
jl_ha = cv2.GaussianBlur(jl_hinge.astype(np.float32) / 255.0, (0, 0), 0.7)
cheek_col = np.array([154, 124, 100], np.uint8)
cur_a = jl_arr[..., 3].astype(float) / 255.0
for c_i in range(3):
    jl_arr[..., c_i] = np.where(cur_a > 0.5, jl_arr[..., c_i], cheek_col[c_i])
jl_arr[..., 3] = np.clip(np.maximum(cur_a, jl_ha) * 255.0, 0, 255).astype(np.uint8)
parts_out['jaw_lower'] = cut_part(jl_arr, np.ones((H, W), np.float32))

# 5. mouth_interior: cleanly behind upper inner tooth edge + extends slightly deeper under lower jaw so biting never leaves a gap!
mi_arr = np.zeros((H, W, 4), np.uint8)
mi_poly = poly_mask(
    [(208, 332), (235, 314), (298, 330), (312, 378), (274, 434), (205, 444), (195, 405)],
    feather=1.2,
)
ys_t, xs_t = np.where(mi_poly > 0.05)
for y, x in zip(ys_t, xs_t):
    d = ((x - 248) / 56.0) ** 2 + ((y - 366) / 48.0) ** 2
    f = max(0.0, 1.0 - d)
    mi_arr[y, x] = [int(38 + 46 * (1 - f)), int(14 + 20 * (1 - f)), int(18 + 20 * (1 - f)), int(mi_poly[y, x] * 255)]
parts_out['mouth_interior'] = cut_part(mi_arr, np.ones((H, W), np.float32))

# 6. Burrow split cleanly: NO 1px sliver lines!
front_line_poly = poly_mask([
    (215, 864), (345, 804), (406, 818), (465, 836), (535, 826), (598, 790),
    (655, 752), (735, 744), (860, 765), (960, 840), (960, 980), (215, 980)
])
bf_m = front_line_poly * (src[..., 3] > 20).astype(np.float32)
bf_arr = src.copy()
bf_arr[..., 3] = np.clip(bf_arr[..., 3].astype(float) * bf_m, 0, 255).astype(np.uint8)
parts_out['burrow_front'] = cut_part(bf_arr, np.ones((H, W), np.float32))

# Only pick actual dark rocks far from trunk edges (left rocks x<=370 or right rocks x>=642)!
# Overlap burrow_back rocks 10px DOWN below front_line_poly so zero 1px seam line occurs at rock divide!
front_line_back_overlap = poly_mask([
    (215, 874), (345, 814), (406, 828), (465, 846), (535, 836), (598, 800),
    (655, 762), (735, 754), (860, 775), (960, 850), (960, 980), (215, 980)
])
rubble_left = (np.arange(H)[:, None] >= 755) & (np.arange(W)[None, :] <= 405) & (val < 118)
rubble_right = (np.arange(H)[:, None] >= 668) & (np.arange(W)[None, :] >= 618) & (val < 125) & ((r - b) < 26)
bb_rocks = ((rubble_left | rubble_right) & (front_line_back_overlap < 0.5) & (src[..., 3] > 20)).astype(np.float32)
bb_arr = src.copy()
bb_arr[..., 3] = np.clip(bb_arr[..., 3].astype(float) * bb_rocks, 0, 255).astype(np.uint8)
pit_mask = np.zeros((H, W), np.uint8)
cv2.ellipse(pit_mask, (520, 810), (148, 46), -5, 0, 360, 255, -1)
pit_m = (pit_mask.astype(np.float32) / 255.0) * (np.arange(H)[:, None] >= 755).astype(np.float32)
cur_a = bb_arr[..., 3].astype(np.float32) / 255.0
for c_i, val_c in enumerate([34, 31, 30]):
    bb_arr[..., c_i] = np.where(cur_a > 0.3, bb_arr[..., c_i], val_c)
bb_arr[..., 3] = np.clip(np.maximum(cur_a, pit_m) * 255.0, 0, 255).astype(np.uint8)
parts_out['burrow_back'] = cut_part(bb_arr, np.ones((H, W), np.float32))

# 7. Clean BODY (keep trunk completely solid down to y=785, zero crevice cutouts!)
body_arr = src.copy()
is_warm_body_global = ((r > b + 6.0) & (r > 52.0)) | (np.arange(H)[:, None] < 745)
trunk_envelope = poly_mask([
    (0, 0), (1024, 0), (1024, 668), (636, 668), (642, 718), (636, 785),
    (595, 825), (505, 845), (392, 835), (368, 795), (368, 668), (0, 668)
])
remove_mask = ((trunk_envelope < 0.5) | ((np.arange(H)[:, None] >= 745) & (~is_warm_body_global))).astype(np.float32)
remove_mask = np.maximum(remove_mask, feelers_union)

brow_keep_band = (
    ((r - b) > 38) & (g < 155) & (val < 195) & (np.arange(H)[:, None] <= 345)
) | (poly_mask([(295, 310), (355, 310), (355, 368), (318, 368), (295, 336)]) > 0.5)
jaw_throat_rm = poly_mask([
    (95, 282), (140, 284), (175, 278), (222, 298), (242, 320), (294, 334),
    (326, 342), (332, 385), (302, 422), (280, 488), (110, 492), (95, 350)
])
jaw_kill = ((jaw_throat_rm > 0.5) & (~brow_keep_band)).astype(np.float32)
remove_mask = np.maximum(remove_mask, jaw_kill)

# Erase ALL cyan AND cool sky bounce inside plate polygons so bare back has clean warm terracotta only
is_pure_brown = (r > b + 36.0) & (r > 96.0)
crystal_pixels = (plate_all_poly > 0.5) & (~is_pure_brown) & (body_arr[..., 3] > 5)
ridge_cool = (poly_mask([(385, 130), (635, 130), (750, 260), (755, 385), (680, 385), (560, 225), (385, 198)]) > 0.5) & (b > r * 0.65)
crystal_pixels = crystal_pixels | ridge_cool
crystal_pixels = cv2.dilate(crystal_pixels.astype(np.uint8), np.ones((11, 11), np.uint8), iterations=1) > 0

keep_body = (1.0 - np.clip(remove_mask, 0, 1)) * (~crystal_pixels).astype(np.float32)
body_arr[..., 3] = np.clip(body_arr[..., 3].astype(float) * keep_body, 0, 255).astype(np.uint8)

solid_body = (body_arr[..., 3] > 60).astype(np.uint8)
nb, lbls, st, _ = cv2.connectedComponentsWithStats(solid_body, 8)
best = 1 + int(np.argmax(st[1:, cv2.CC_STAT_AREA]))
main_body = (lbls == best).astype(np.uint8)

# Reconstruct ONLY along the outer dorsal spine curve (never bridge inside concave belly arch!)
SPINE_ARCH_RECON = [
    (360, 185), (415, 148), (485, 144), (558, 158), (625, 184), (678, 224),
    (718, 280), (740, 342), (747, 405), (744, 475), (728, 530),
    (660, 450), (610, 310), (500, 230), (390, 220)
]
arch_fill = (poly_mask(SPINE_ARCH_RECON) > 0.5).astype(np.uint8)
dist_out = cv2.distanceTransform(1 - main_body, cv2.DIST_L2, 5)
smooth_body = np.maximum(main_body, (arch_fill & (dist_out < 38)).astype(np.uint8))

inpaint_m = ((smooth_body > 0) & (main_body == 0)).astype(np.uint8) * 255
body_arr[~(smooth_body > 0), 3] = 0
body_arr[..., :3] = cv2.inpaint(body_arr[..., :3], inpaint_m, 11, cv2.INPAINT_TELEA)
# Neutralize any faint cool cyan reflection lingering along dorsal carapace sockets in bare state -> true terracotta ratio
rb2, gb2, bb2 = body_arr[..., 0].astype(float), body_arr[..., 1].astype(float), body_arr[..., 2].astype(float)
cool_sheen = (smooth_body > 0) & (plate_all_poly > 0.5) & (bb2 > rb2 * 0.62)
lum = np.clip((rb2 * 0.4 + gb2 * 0.4 + bb2 * 0.2), 115, 195)
body_arr[cool_sheen, 0] = np.clip(lum[cool_sheen] * 1.02, 0, 215).astype(np.uint8)
body_arr[cool_sheen, 1] = np.clip(lum[cool_sheen] * 0.67, 0, 155).astype(np.uint8)
body_arr[cool_sheen, 2] = np.clip(lum[cool_sheen] * 0.46, 0, 115).astype(np.uint8)
ero_s = cv2.erode(smooth_body, np.ones((5, 5), np.uint8)) > 0
rim_recon = (smooth_body > 0) & (~ero_s) & (inpaint_m > 0)
body_arr[rim_recon, :3] = np.clip(body_arr[rim_recon, :3].astype(float) * 0.72, 0, 255).astype(np.uint8)
body_arr[smooth_body > 0, 3] = 255

ba = (body_arr[..., 3] > 128).astype(np.float32)
ba_blur = cv2.GaussianBlur(ba, (0, 0), 0.7)
ero = cv2.erode((ba > 0.5).astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
body_arr[~ero, 3] = np.clip(ba_blur[~ero] * 255.0, 0, 255).astype(np.uint8)

# Extend trunk bottom 50px down behind burrow_front (deep enough for slam rear-up y+26 while staying >45px above y=945 in die)
for dy in range(1, 51):
    sy = 795 - min(24, dy // 2)
    ty = 795 + dy
    if ty >= H:
        break
    row = body_arr[sy : sy + 1].copy()
    row[..., :3] = np.clip(row[..., :3].astype(float) * (1.0 - dy * 0.008), 0, 255).astype(np.uint8)
    xs_r = np.where(row[0, :, 3] > 20)[0]
    if len(xs_r) > 0:
        row[0, : xs_r[0] + int(dy * 0.25), 3] = 0
        row[0, xs_r[-1] - int(dy * 0.25) :, 3] = 0
    empty = body_arr[ty, :, 3] < 200
    body_arr[ty, empty] = row[0, empty]

parts_out['body'] = cut_part(body_arr, np.ones((H, W), np.float32))

# 8. Natural resting upper eyelid strips (at scale=1 sits seamlessly along upper brow fold; scales sy->3.5 downward to cover pupils)
brow_patch = src[240:256, 240:262, :3].mean(axis=(0, 1)).astype(np.uint8)
brow_dark = (brow_patch * 0.72).astype(np.uint8)
for ename, (cx, cy, rx, ry, ang) in [
    ('eye_far_lid', (183, 248, 16, 6, 6)),
    ('eye_near_lid', (300, 264, 24, 7, -6)),
]:
    m_lid = np.zeros((H, W), np.uint8)
    cv2.ellipse(m_lid, (cx, cy), (rx, ry), ang, 0, 360, 255, -1)
    lid_rgb = np.zeros((H, W, 3), np.uint8)
    lid_rgb[m_lid > 0] = brow_patch
    cv2.ellipse(lid_rgb, (cx, cy + 1), (max(2, rx - 2), max(1, ry - 1)), ang, 10, 170, brow_dark.tolist(), 2)
    lid = np.zeros((H, W, 4), np.uint8)
    lid[..., :3] = lid_rgb
    la = cv2.GaussianBlur(m_lid.astype(np.float32) / 255.0, (0, 0), 0.5)
    lid[..., 3] = np.clip(la * 255, 0, 255).astype(np.uint8)
    parts_out[ename] = cut_part(lid, np.ones((H, W), np.float32))

# 9. Additive icy-cyan gullet siphon glow (EXACT same bbox as mouth_interior so GLOW_OF shares its mesh cleanly!)
mi_im, mi_box = parts_out['mouth_interior']
mg = np.zeros((H, W, 4), np.uint8)
yy, xx = np.ogrid[:H, :W]
dist = np.sqrt(((xx - 246.0) / 44.0) ** 2 + ((yy - 366.0) / 42.0) ** 2)
glow_a = np.clip(np.exp(-dist * dist * 2.1), 0, 1)
mg[..., 0] = 135
mg[..., 1] = 235
mg[..., 2] = 255
mg[..., 3] = np.clip(glow_a * 255, 0, 255).astype(np.uint8)
mg[glow_a < 0.02, 3] = 0
mg_crop = mg[mi_box['y'] : mi_box['y'] + mi_box['h'], mi_box['x'] : mi_box['x'] + mi_box['w']]
parts_out['mouth_glow'] = (Image.fromarray(mg_crop, 'RGBA'), dict(mi_box))

meta = {}
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
for n, (im, box) in parts_out.items():
    im.save(os.path.join(PARTS_DIR, f'{n}.png'))
    meta[n] = box
with open(os.path.join(PARTS_DIR, 'meta.json'), 'w') as f:
    json.dump(meta, f, indent=2)

re = Image.new('RGBA', (W, H), (29, 40, 51, 255))
for n in ORDER:
    if n in ('eye_far_lid', 'eye_near_lid'):
        continue
    im, box = parts_out[n]
    re.paste(im, (box['x'], box['y']), im)
re.save(os.path.join(HERE, 'parts_reassembled.png'))

bare = Image.new('RGBA', (W, H), (29, 40, 51, 255))
for n in ORDER:
    if n.startswith('plate_') or n in ('eye_far_lid', 'eye_near_lid'):
        continue
    im, box = parts_out[n]
    bare.paste(im, (box['x'], box['y']), im)
bare.save(os.path.join(HERE, 'parts_bare_armor.png'))

cols = 4
cell = 280
rows = (len(parts_out) + cols - 1) // cols
ov = Image.new('RGBA', (cols * cell, rows * cell), (35, 43, 52, 255))
draw = ImageDraw.Draw(ov)
for idx, (n, (im, box)) in enumerate(parts_out.items()):
    r_i, c_i = idx // cols, idx % cols
    cx, cy = c_i * cell, r_i * cell
    draw.rectangle([cx + 4, cy + 4, cx + cell - 5, cy + cell - 5], outline=(70, 90, 110, 255), width=2)
    scale = min((cell - 36) / max(1, im.width), (cell - 46) / max(1, im.height))
    nw, nh = max(1, int(im.width * scale)), max(1, int(im.height * scale))
    rim = im.resize((nw, nh), Image.Resampling.LANCZOS)
    ov.paste(rim, (cx + (cell - nw) // 2, cy + 28 + (cell - 36 - nh) // 2), rim)
    draw.text((cx + 10, cy + 8), f'{n} ({im.width}x{im.height})', fill=(235, 240, 245, 255))
ov.save(os.path.join(HERE, 'parts_overview.png'))
print('Re-extracted cleanly v3!')
