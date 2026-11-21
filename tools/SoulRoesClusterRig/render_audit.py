"""Render comprehensive visual contact sheet prev/soul_roes_cluster_audit.jpg for ThingsSoulRoes."""
import pickle
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

from soul_roes_rigkit import Renderer

HERE = Path(__file__).resolve().parent


def nearest_frame(frames, t_target: float):
    return min(frames, key=lambda f: abs(f["t"] - t_target))


def render_ref_comparison(rr: Renderer, idle0) -> np.ndarray:
    """Side-by-side comparison of source_assets/monsters/soul_roes.png vs reconstructed 12-egg setup pose."""
    W, H = 560, 260
    panel = np.zeros((H, W, 3), np.uint8)
    panel[...] = (20, 26, 34)

    # Left half: reference soul_roes.png
    ref = Image.open(HERE.parents[1] / "source_assets" / "monsters" / "soul_roes.png").convert("RGBA")
    rw = int(round(ref.width * 0.56))
    rh = int(round(ref.height * 0.56))
    ref_arr = np.array(ref.resize((rw, rh), Image.LANCZOS), dtype=np.float32) / 255.0
    x0, y0 = 140 - rw // 2, 146 - rh // 2
    sub = panel[y0 : y0 + rh, x0 : x0 + rw].astype(np.float32) / 255.0
    a = ref_arr[..., 3:4]
    panel[y0 : y0 + rh, x0 : x0 + rw] = (np.clip(sub * (1.0 - a) + ref_arr[..., :3] * a, 0.0, 1.0) * 255.0).astype(np.uint8)
    cv2.putText(panel, "REF: soul_roes.png", (24, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.52, (210, 220, 232), 1, cv2.LINE_AA)

    # Right half: reconstructed 12-egg rig setup pose
    rig_im = rr.render(idle0, scale=0.38, W=280, H=260, ox=140, oy=225)
    panel[:, 280:] = rig_im
    cv2.putText(panel, "RIG SETUP (12 High-Res Orbs)", (292, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.50, (255, 232, 150), 1, cv2.LINE_AA)
    cv2.line(panel, (280, 0), (280, H), (58, 68, 82), 1)
    return panel


def main():
    prev_dir = HERE / "prev"
    prev_dir.mkdir(exist_ok=True)
    with open(HERE / "out" / "frames.pkl", "rb") as fp:
        allframes = pickle.load(fp)

    rr = Renderer()
    PW, PH = 560, 260

    def shot(anim_name: str, t: float, subtitle: str, wide_left: bool = False) -> np.ndarray:
        fr = nearest_frame(allframes[anim_name], t)
        if wide_left:
            # Wide arena framing showing both the cluster recoil on right AND -620px hurled roes + comet trails on left!
            im = rr.render(fr, scale=0.40, W=PW, H=PH, ox=405, oy=212)
        else:
            im = rr.render(fr, scale=0.42, W=PW, H=PH, ox=284, oy=216)
        cv2.rectangle(im, (0, 0), (PW - 1, PH - 1), (46, 56, 68), 1)
        cv2.putText(
            im,
            f"{anim_name}  t={fr['t']:.2f}s  [{subtitle}]",
            (12, 24),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.50,
            (242, 246, 252),
            1,
            cv2.LINE_AA,
        )
        return im

    rows = [
        # Row 0: Reference match + idle_loop buoyancy & golden yolk wave
        [
            render_ref_comparison(rr, allframes["idle_loop"][0]),
            shot("idle_loop", 0.90, "Buoyant Rise + Core Yolk Wave"),
            shot("idle_loop", 2.35, "Buoyant Dip + Rim Yolk Wave"),
        ],
        # Row 1: multi_bite_6 (centrifugal spin-up + sequential hurled roes with golden comet trails!)
        [
            shot("multi_bite_6", 0.20, "CCW Centrifugal Vortex Spin-Up", wide_left=True),
            shot("multi_bite_6", 0.34, "Hit #1 bite6_contact (sat_0 -575px)", wide_left=True),
            shot("multi_bite_6", 0.74, "Hit #3 Barrage + Rebound Arc", wide_left=True),
        ],
        # Row 2: multi_bite_6 Hit #6 + multi_bite_4_intangible (fling -> blinding pearl-gold Intangible flare!)
        [
            shot("multi_bite_6", 1.34, "Hit #6 bite6_contact (sat_5 -615px)", wide_left=True),
            shot("multi_bite_4_intangible", 0.58, "Hit #2 bite4_contact (sat_1 -625px)", wide_left=True),
            shot("multi_bite_4_intangible", 1.38, "intangible_peak Pearl-Aqua Radiance!"),
        ],
        # Row 3: cast (12-egg warm sun-gold brightening + expanding halo ring) & power_up
        [
            shot("cast", 0.26, "0.84x Inward Brood Gather"),
            shot("cast", 0.58, "cast_release 12-Egg Sun-Gold + Ring!"),
            shot("power_up", 0.52, "power_peak Moonstone Intangible Glow"),
        ],
        # Row 4: summon (mitotic budding ±148px) + die (over-pressurization -> 6-way radial starburst!)
        [
            shot("summon", 0.64, "summon_release Mitotic Budding +-148px"),
            shot("die", 0.38, "Over-Pressurization Core Flare"),
            shot("die", 0.70, "rupture_burst 6-Way Starburst!"),
        ],
    ]

    header_h = 46
    sheet = np.zeros((header_h + len(rows) * PH, 3 * PW, 3), np.uint8)
    sheet[:header_h] = (16, 20, 26)
    cv2.putText(
        sheet,
        "ThingsSoulRoes (soul_roes) — 12-Orb Brood Cluster + 6-Slingshot-Satellite Spine 4.2 Audit",
        (18, 30),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.72,
        (255, 228, 136),
        2,
        cv2.LINE_AA,
    )

    for r_i, row in enumerate(rows):
        for c_i, cell in enumerate(row):
            y0 = header_h + r_i * PH
            x0 = c_i * PW
            sheet[y0 : y0 + PH, x0 : x0 + PW] = cell

    out_path = prev_dir / "soul_roes_cluster_audit.jpg"
    Image.fromarray(sheet).save(out_path, quality=92)
    print("Saved", out_path)


if __name__ == "__main__":
    main()
