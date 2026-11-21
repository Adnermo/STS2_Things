"""End-to-end single-body SoulRoeRig build, packaging, and visual audit generator (`prev/soul_roe_variants_audit.jpg`)."""
from __future__ import annotations

import pickle
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from extract_parts import VARIANT_SPECS, extract_all
from roe_rigkit import VariantRenderer, build_variant, export_and_package_variant


def render_variants_audit() -> Path:
    prev_dir = HERE / "prev"
    prev_dir.mkdir(parents=True, exist_ok=True)

    # 8 columns showcasing pure single-roe anatomy, full-body self-fling comet strike, dual bioluminescence, hurt & die:
    # 0: idle_loop (t=0.00s pure single-roe setup)
    # 1: attack coil (t=0.25s +76px right wind-up + pressurized bead + glowing core)
    # 2: attack mid-flight (t=0.35s aerodynamic teardrop bullet + blazing `body_trail`!)
    # 3: attack impact hit-stop (t=0.42s -498px pancake hit-stop + `body_trail` + `impact_flare`! Wide combat view!)
    # 4: attack rebound apex (t=0.68s +65px vaulted underwater return arc)
    # 5: cast (t=0.50s Beckon warm sun-gold lantern + expanding `beckon_halo_ring`)
    # 6: power_up (t=0.52s Intangible SoulFysh pearlescent aqua-white-gold + `intangible_shimmer` + ethereal cytoplasm)
    # 7: hurt (t=0.06s +42px hydro-shock + -16.5px yolk inertia lag) & die (t=0.32s radial rupture burst) -> let's show both hurt & die as 9 cols!
    columns = [
        ("idle_loop", 0.00, "idle t=0.00 (Single Roe)", dict(scale=0.96, W=240, H=255, ox=120, oy=196)),
        ("attack", 0.25, "attack t=0.25 (+76 Coil)", dict(scale=0.86, W=250, H=255, ox=82, oy=196)),
        ("attack", 0.35, "attack t=0.35 (Comet!)", dict(scale=0.68, W=330, H=255, ox=280, oy=194)),
        ("attack", 0.42, "attack t=0.42 (-498 Hit!)", dict(scale=0.58, W=440, H=255, ox=384, oy=192)),
        ("attack", 0.68, "attack t=0.68 (+65 Vault)", dict(scale=0.66, W=300, H=255, ox=268, oy=206)),
        ("cast", 0.50, "cast t=0.50 (Beckon Gold)", dict(scale=0.84, W=260, H=255, ox=130, oy=206)),
        ("power_up", 0.52, "power t=0.52 (Intangible)", dict(scale=0.84, W=260, H=255, ox=130, oy=208)),
        ("hurt", 0.06, "hurt t=0.06 (Yolk Lag)", dict(scale=0.86, W=245, H=255, ox=92, oy=196)),
        ("die", 0.42, "die t=0.42 (Dissolve)", dict(scale=0.76, W=260, H=255, ox=130, oy=210)),
    ]

    header_h = 46
    row_h = 255
    total_w = sum(cfg["W"] for _, _, _, cfg in columns)
    total_h = header_h + 3 * row_h
    sheet = np.full((total_h, total_w, 3), (18, 22, 30), dtype=np.uint8)

    x_cur = 0
    for _, _, label, cfg in columns:
        cw = cfg["W"]
        cv2.putText(sheet, label, (x_cur + 8, 29), cv2.FONT_HERSHEY_SIMPLEX, 0.52, (238, 226, 184), 1, lineType=cv2.LINE_AA)
        cv2.line(sheet, (x_cur, 0), (x_cur, total_h), (46, 54, 68), 1)
        x_cur += cw

    for r_idx, v in enumerate((1, 2, 3)):
        v_name = VARIANT_SPECS[v]["name"]
        rr = VariantRenderer(v)
        with open(HERE / "out" / v_name / "frames.pkl", "rb") as fp:
            all_frames = pickle.load(fp)

        y0 = header_h + r_idx * row_h
        cv2.line(sheet, (0, y0), (total_w, y0), (46, 54, 68), 1)

        x_cur = 0
        for c_idx, (anim_name, target_t, _, vcfg) in enumerate(columns):
            fr_list = all_frames[anim_name]
            best_f = min(fr_list, key=lambda f: abs(f["t"] - target_t))
            tile = rr.render(best_f, **vcfg)

            if anim_name == "attack" and abs(target_t - 0.42) < 0.02:
                ox_t, sc_t = vcfg["ox"], vcfg["scale"]
                cv2.line(tile, (ox_t, 24), (ox_t, row_h - 14), (70, 82, 102), 1)
                cv2.putText(tile, "x=0", (ox_t - 34, row_h - 18), cv2.FONT_HERSHEY_SIMPLEX, 0.42, (130, 145, 170), 1, lineType=cv2.LINE_AA)
                imp_x = int(round(ox_t - 498.0 * sc_t))
                cv2.line(tile, (imp_x, 24), (imp_x, row_h - 14), (120, 96, 52), 1)
                cv2.putText(tile, "-498px", (imp_x + 4, row_h - 18), cv2.FONT_HERSHEY_SIMPLEX, 0.44, (246, 212, 120), 1, lineType=cv2.LINE_AA)

            if c_idx == 0:
                cv2.putText(
                    tile,
                    v_name,
                    (10, 24),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.58,
                    (255, 236, 156),
                    1,
                    lineType=cv2.LINE_AA,
                )

            cw = vcfg["W"]
            sheet[y0 : y0 + row_h, x_cur : x_cur + cw] = tile
            cv2.line(sheet, (x_cur, y0), (x_cur, y0 + row_h), (46, 54, 68), 1)
            x_cur += cw

    out_path = prev_dir / "soul_roe_variants_audit.jpg"
    Image.fromarray(sheet).save(out_path, quality=92)
    print(f"Saved contact sheet -> {out_path}")
    return out_path


def main() -> None:
    extract_all()
    for v in (1, 2, 3):
        build_variant(v)
        export_and_package_variant(v)
    render_variants_audit()


if __name__ == "__main__":
    main()
