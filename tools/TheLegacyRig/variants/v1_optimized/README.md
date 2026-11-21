# The Legacy (腐化之遗) Spine 4.2 Rig — Version A (`v1_optimized`)

沉稳厚重·腔室节律精修版（基于 29 分件架构深度优化，含完整 4 腔室冠状脉发光层，共 31 个渲染插槽 / 66 根骨骼）。

## Key Upgrades over Original `TheLegacyRig`

1. **Texture & Segmentation Fixes (`seg.py`, `build_parts.py`, `build_tex.py`)**
   - **Zero Cutout Holes & Zero Shadow Stickers**: `body_true` smoothly covers behind `coral_pink`, `coral_purple`, `weed_bottom_mid`, `weed_bottom_right` (inner body bridge), `weed_blue`, `weed_ground_bl`, and surface barnacles down to the anatomical lobe contact curve, filled cleanly via uncontaminated `pushpull` pyramid synthesis (`recomp.py` rest composite `mean abs diff = 0.129/255`). Coral/weed growth excludes deep dark cleft pockets (`v < 38`) beyond a 1px ink outline so foreground flora never stamp dark shadow squares over moving chambers.
   - **No Floating Surface Moss**: `weed_back_d` is restricted strictly to the tall dorsal sprig (`y < 275`, components `14, 32`) behind `purple_mass`, leaving `bulb` and `purple_mass` surface moss anchored directly to their host surfaces.
   - **Complete 4-Chamber Coronary Vein Glow**: Extracts additive crimson vein glow across `atrium_glow`, `bulb_glow`, `mid_lobe_glow`, and `bottom_lobe_glow` (excluding `coral_pink` `[155:410, 330:545]`, `coral_purple`, and dorsal rim barnacles), lighting up continuous coronary branches seamlessly across `bulb -> bottom_lobe` and `atrium -> mid_lobe`.

2. **Ground-Rooted Base & Cross-Seam Weight Continuity (`rigdef.py`, `build_rig.py`)**
   - **Ground Flora Parented to `'root'`**: `weed_ground_left`, `weed_ground_bl`, `weed_ground_right`, `weed_edge_right`, `coral_purple`, `weed_bottom_mid`, `weed_bottom_right`, and `weed_left` are parented to `'root'` with smooth `'root'` crown/floor pinning (`<= 0.92px` base drift across all 8 clips). `coral_pink` is parented to `'mid_lobe'`, and `weed_top_left` is parented to `'tube_a'`.
   - **Ground Contact Plane Pinning**: Lower body vertices blend smoothly into `'root'` (`g = smoothstep((y_img - 605.0) / 52.0) * 0.94`, reaching `>= 0.936` for `y_img >= 655`), keeping the bottom 5% of body vertices within `[-1.30, +0.88] px` across all 8 animations.
   - **Unified Cross-Chamber Weight Field**: Connected cyan chambers (`bulb`, `bottom_lobe`, `mid_lobe`, `right_lobe`, `atrium`) evaluate continuous Gaussian weights (`sig = 76.0`, zero Voronoi `argmin` step cliffs), guaranteeing `0.0px` seam slip and `0` triangle inversions across all overlaps (`3314` vertices, `5174` triangles).

3. **Grounded Cardiac Choreography (`anims.py`, `export.py`)**
   - `idle_loop` (3.00s loop), `attack` (1.25s), `cast` (1.40s), `hurt` (0.65s), `die` (2.60s), `summon` (1.30s), `power_up` (1.50s), `revive` (1.80s).

## Rebuild & Verify

```powershell
python seg.py
python build_parts.py
python build_tex.py
python recomp.py
python weed_axes.py
python build_rig.py
python export.py
python package.py
node verify.mjs
python render.py --review
```
