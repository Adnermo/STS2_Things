"""Package Chromatic Slime (Medium) v4 3-strain multi-skin Spine 4.2 files; deployment is explicit with --deploy."""
import json
import shutil
import sys
from pathlib import Path

from anims import EVENTS

HERE = Path(__file__).resolve().parent
KEY = "chromatic_slime_medium"
DEST = HERE / "pkg"
DEST.mkdir(exist_ok=True)
RES = f"res://STS2_Things/animations/monsters/{KEY}"

COMMON_SLOTS = {
    "shadow",
    "body",
    "aura",
    "inner_blob",
    "eye_clip_main",
    "iris_main",
    "lid_main",
    "eye_clip_sec",
    "iris_sec",
    "lid_sec",
    "proj_1",
    "proj_2",
}

atlas = (HERE / "out" / f"{KEY}.atlas").read_text(encoding="utf-8")
shutil.copyfile(HERE / "out" / f"{KEY}.png", DEST / f"{KEY}.png")
skeleton = json.loads((HERE / "out" / f"{KEY}.json").read_text(encoding="utf-8"))

# Keep ONLY universal slots on Spine's 'default' fallback skin so Crimson's c_shard_* never leak into Amber/Azure!
for skin_entry in skeleton.get("skins", []):
    if skin_entry.get("name") == "default":
        skin_entry["attachments"] = {
            k: v for k, v in skin_entry.get("attachments", {}).items() if k in COMMON_SLOTS
        }

skeleton["events"] = {name: {} for events in EVENTS.values() for name, _ in events}
for animation, events in EVENTS.items():
    if events and animation in skeleton["animations"]:
        skeleton["animations"][animation]["events"] = [
            {"time": time, "name": event} for event, time in events
        ]

(DEST / f"{KEY}.spjson").write_text(json.dumps(skeleton, separators=(",", ":")), encoding="utf-8", newline="\n")
(DEST / f"{KEY}.atlas").write_text(atlas, encoding="utf-8", newline="\n")
(DEST / f"{KEY}.spatlas").write_text(
    json.dumps(
        dict(
            source_path=f"{RES}/{KEY}.atlas",
            atlas_data=atlas,
            normal_texture_prefix="n",
            specular_texture_prefix="s",
        ),
        separators=(",", ":"),
    ),
    encoding="utf-8",
    newline="\n",
)
(DEST / f"{KEY}_skel_data.tres").write_text(
    f"""[gd_resource type="SpineSkeletonDataResource" load_steps=3 format=3]

[ext_resource type="SpineAtlasResource" path="{RES}/{KEY}.spatlas" id="1_atlas"]
[ext_resource type="SpineSkeletonFileResource" path="{RES}/{KEY}.spjson" id="2_skeleton"]

[resource]
atlas_res = ExtResource("1_atlas")
skeleton_file_res = ExtResource("2_skeleton")
default_mix = 0.12
""",
    encoding="utf-8",
    newline="\n",
)

if "--deploy" in sys.argv:
    target = HERE.parents[2] / "STS2_Things" / "animations" / "monsters" / KEY
    target.mkdir(parents=True, exist_ok=True)
    for file in DEST.iterdir():
        shutil.copyfile(file, target / file.name)
    print("Deployed", target)
else:
    print("Packaged", DEST)
