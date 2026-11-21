"""Package ThingsSoulRoes Spine 4.2 files into tools/SoulRoesClusterRig/pkg/soul_roes/."""
import json
import shutil
from pathlib import Path

from anims import EVENTS

HERE = Path(__file__).resolve().parent
KEY = "soul_roes"
DEST = HERE / "pkg" / KEY
DEST.mkdir(parents=True, exist_ok=True)
RES = f"res://STS2_Things/animations/monsters/{KEY}"

atlas = (HERE / "out" / f"{KEY}.atlas").read_text(encoding="utf-8")
shutil.copyfile(HERE / "out" / f"{KEY}.png", DEST / f"{KEY}.png")
skeleton = json.loads((HERE / "out" / f"{KEY}.json").read_text(encoding="utf-8"))

skeleton["events"] = {name: {} for events in EVENTS.values() for name, _ in events}
for animation, events in EVENTS.items():
    if events and animation in skeleton["animations"]:
        sorted_events = sorted(events, key=lambda item: item[1])
        skeleton["animations"][animation]["events"] = [
            {"time": round(time, 4), "name": event} for event, time in sorted_events
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

print("Packaged", DEST)
