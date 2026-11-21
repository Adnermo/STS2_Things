"""Build a native pre-combat save from the available fields in the old F2 report.

Only writes into the named, stopped Workbench instance and an output directory.
RNG counters, hidden SavedProperties and transport state were not in the report.
"""
import argparse
import copy
import json
import sys
from pathlib import Path

MCP = Path(__file__).resolve().parents[2] / "STS2-MCP"
sys.path.insert(0, str(MCP / "src"))
from sts2_mcp.config import Config
from sts2_mcp.instances import InstanceManager
from sts2_mcp.jobs import atomic_json


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--instance", required=True)
    p.add_argument("--report", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--template", type=Path)
    args = p.parse_args()
    c = Config.load()
    instance = InstanceManager(c).get(args.instance)
    if instance["running"]:
        raise RuntimeError("Close this exact test instance before restoring")
    save = Path(instance["user_data"]) / "default" / str(instance["client_id"]) / "modded/profile1/saves/current_run.save"
    template = json.loads((args.template or save).read_text(encoding="utf-8"))
    original = json.loads(args.report.read_text(encoding="utf-8"))
    report = original["payload"]["run"]["state"]
    args.output.mkdir(parents=True, exist_ok=True)
    atomic_json(args.output / "template-before.restore.json", template)
    data = copy.deepcopy(template)
    data["schema_version"] = 20
    data["ascension"] = report["ascension"]
    data["current_act_index"] = report["current_act_index"]
    data["rng"] = {"seed": report["seed"], "rngs": {}}
    data["num_reloads"] = 0
    for index, info in enumerate(report["players"]):
        target = data["players"][index]
        target.update(character_id=info["character"], current_hp=info["hp"], max_hp=info["max_hp"],
                      gold=info["gold"], max_energy=3, max_potion_slot_count=len(info["potions"]), base_orb_slot_count=3)
        target["deck"] = [{"id": card["id"], "current_upgrade_level": card["upgrade"], "floor_added_to_deck": 1}
                          for card in info["deck"]]
        if any(card["enchantment"] or card["affliction"] for card in info["deck"]):
            raise RuntimeError("This reconstruction needs an explicit enchantment/affliction mapper")
        target["relics"] = [{"id": relic, "floor_added_to_deck": 1} for relic in info["relics"]]
        target.pop("potions", None)
        if any(info["potions"]):
            raise RuntimeError("This report contains potions that need a slot mapper")
        target["rng"] = {"seed": int(report["numeric_seed"]) + index, "rngs": {}}
    assert len(data["players"]) == len(report["players"]) == 2
    for act in data["acts"]:
        act.pop("saved_map", None)
    map_save = {"width": 7, "height": 16,
                "start": {"coord": {"col": 4, "row": 0}, "type": "ancient", "can_modify": True,
                          "children": [{"col": 4, "row": 1}]},
                "start_coords": [{"col": 4, "row": 1}],
                "boss": {"coord": {"col": 4, "row": 16}, "type": "boss", "can_modify": True},
                "points": [{"coord": {"col": 4, "row": row}, "type": "monster", "can_modify": True,
                            "children": [{"col": 4, "row": row + 1}]} for row in range(1, 16)]}
    for act, model_id in zip(data["acts"], report["acts"], strict=True):
        act["id"] = model_id
    data["acts"][report["current_act_index"]]["saved_map"] = map_save
    # Start from the map checkpoint; the test bootstrap enters the reported room.
    data["visited_map_coords"] = []
    stat_base = template["map_point_history"][0][0]["player_stats"]
    histories = []
    for route in report["route"]:
        points = []
        for point in route["points"]:
            stats = []
            for i, info in enumerate(point["players"]):
                stat = copy.deepcopy(stat_base[i])
                for key in ["ancient_choice", "relic_choices", "upgraded_cards"]:
                    stat.pop(key, None)
                stat.update(current_hp=info["hp"], max_hp=info["max_hp"], current_gold=info["gold"],
                            damage_taken=info["damage_taken"], hp_healed=info["healed"],
                            player_id=data["players"][i]["net_id"])
                stat["relic_choices"] = [{"choice": r["id"], "was_picked": r["picked"]} for r in info["relic_choices"]]
                stats.append(stat)
            points.append({"map_point_type": point["map_point_type"].lower(), "player_stats": stats,
                           "rooms": [{"model_id": room["model_id"], "room_type": room["type"].lower(),
                                      "turns_taken": room["turns"]} for room in point["rooms"]]})
        histories.append(points)
    data["map_point_history"] = histories
    data["map_point_history"][-1].pop()
    data["pre_finished_room"] = None
    atomic_json(args.output / "restored-precombat.save", data)
    atomic_json(save, data)
    settings_path = save.parents[3] / "settings.save"
    settings = json.loads(settings_path.read_text(encoding="utf-8"))
    order = [mod["id"] for mod in original["payload"]["run"]["active_mods"]]
    settings["mod_settings"] = {"mods_enabled": True, "mod_list": [
        {"id": mod, "source": "mods_directory", "is_enabled": True} for mod in
        [*order, "STS2_MCP_Bridge", "ThingsReportReplayDriver"]]}
    atomic_json(settings_path, settings)
    summary = {"instance": args.instance, "source_report": original["id"], "seed": report["seed"],
               "ascension": data["ascension"], "act_index": data["current_act_index"],
               "encounter": "SPONGE_LEECH_WEAK", "players": [
                   {"hp": p["current_hp"], "max_hp": p["max_hp"], "gold": p["gold"],
                    "deck_count": len(p["deck"]), "relic_count": len(p["relics"])} for p in data["players"]],
               "limits": ["single-player transport with two player models, not real network co-op",
                          "fresh RNG streams: original counters missing", "hidden relic/card properties missing",
                          "map graph is reconstructed; room bags use the earlier valid template"]}
    atomic_json(args.output / "restoration-summary.json", summary)
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
