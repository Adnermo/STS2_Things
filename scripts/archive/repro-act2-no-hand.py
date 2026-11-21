"""Drive an owned single-player v111 client and check the reported combat-start symptom.

Uses the same bound Workbench bridge and native commands as MCP. Does not act on
multiplayer, change the Steam installation, or submit reports. Multiplayer coverage
must be provided separately; this loop isolates the card-generation interaction.
"""
import argparse
import asyncio
import json
from pathlib import Path
import sys
import time
import uuid

MCP_ROOT = Path(__file__).resolve().parents[2] / "STS2-MCP"
sys.path.insert(0, str(MCP_ROOT / "src"))
from sts2_mcp.config import Config
from sts2_mcp.instances import InstanceManager
from sts2_mcp.live import Bridge


async def main(options):
    bridge = Bridge(InstanceManager(Config.load()).bridge(options.instance))
    out = Path(options.output)
    out.mkdir(parents=True, exist_ok=True)

    async def observe():
        result = await bridge.call("observe")
        network = result.get("state", {}).get("network_type")
        if network not in (None, "Singleplayer", "singleplayer"):
            raise RuntimeError("This driver is single-player only")
        return result

    async def action(kind, **args):
        obs = await observe()
        rid = "act2-repro-" + uuid.uuid4().hex
        result = await bridge.call("action", {
            "kind": kind, "session_id": obs["session_id"],
            "state_token": obs["state_token"], "ui_token": obs["ui_token"],
            "input_mode": "native", **args}, rid)
        if result.get("status") == "failed":
            raise RuntimeError(result)
        return result

    async def click(suffix):
        deadline = time.monotonic() + 8
        acknowledged = set()
        while True:
            obs = await observe()
            ftues = [x for x in obs["ui"]["controls"]
                     if x.get("enabled") and x.get("type") == "NFtueConfirmButton"
                     and x["node_path"] not in acknowledged]
            if ftues:
                acknowledged.add(ftues[0]["node_path"])
                await action("click", node_path=ftues[0]["node_path"])
                await asyncio.sleep(.2)
                continue
            choices = [x for x in obs["ui"]["controls"]
                       if x["node_path"].endswith(suffix) and x.get("enabled")]
            if len(choices) == 1:
                break
            if time.monotonic() >= deadline:
                raise RuntimeError("Expected enabled native control " + suffix)
            await asyncio.sleep(.2)
        return await action("click", node_path=choices[0]["node_path"])

    if options.resume:
        obs = await observe()
        if (obs["ui"].get("modal") or {}).get("node_path", "").endswith("/ThingsStartupNotice"):
            await click("/ThingsStartupNotice/VerticalPopup/YesButton")
        await click("/ContinueButton")
        await asyncio.sleep(2)

    if options.setup:
        obs = await observe()
        modal = obs["ui"].get("modal")
        if modal and modal["node_path"].endswith("/ThingsStartupNotice"):
            await click("/ThingsStartupNotice/VerticalPopup/YesButton")
        obs = await observe()
        if any(c["node_path"].endswith("/SingleplayerButton") for c in obs["ui"]["controls"]):
            await click("/SingleplayerButton")
            await asyncio.sleep(.5)
        obs = await observe()
        if any(c["node_path"].endswith("/StandardButton") for c in obs["ui"]["controls"]):
            await click("/StandardButton")
        await click("/DEFECT_button")
        await asyncio.sleep(.4)
        await click("/CharacterSelectScreen/ConfirmButton")
        deadline = time.monotonic() + 25
        while time.monotonic() < deadline:
            obs = await observe()
            if obs["conditions"]["map_visible"]:
                break
            controls = [c for c in obs["ui"]["controls"] if c.get("enabled")]
            modal = obs["ui"].get("modal")
            if modal and any(c["node_path"].endswith("/NoButton") for c in controls):
                await click("/NoButton")
            elif any(c["type"] == "NEventOptionButton" for c in controls):
                c = next(c for c in controls if c["type"] == "NEventOptionButton")
                await action("click", node_path=c["node_path"])
            elif any(c["type"] == "NProceedButton" for c in controls):
                c = next(c for c in controls if c["type"] == "NProceedButton")
                await action("click", node_path=c["node_path"])
            await asyncio.sleep(.5)
        else:
            (out / "setup-state.json").write_text(json.dumps(obs, indent=2), encoding="utf-8")
            raise RuntimeError("New-run setup did not reach the map")

    report = json.loads(Path(options.report).read_text(encoding="utf-8"))
    player = report["payload"]["run"]["state"]["players"][options.player]
    if not options.no_relics:
        for relic in player["relics"]:
            obs = await observe()
            existing = json.dumps(obs.get("state", {}).get("run", {}))
            if relic in existing:
                continue
            await action("console", command="relic", arguments=["add", relic.removeprefix("RELIC.")])
            await asyncio.sleep(.2)
    await action("console", command="act", arguments=["2"])
    await asyncio.sleep(.8)
    await action("console", command="act", arguments=["DEPTHS"])
    await asyncio.sleep(.8)
    started = time.monotonic()
    dispatched = await action("console", command="fight", arguments=[options.encounter])
    samples = []
    while time.monotonic() - started < options.timeout:
        obs = await observe()
        samples.append(obs)
        if obs["conditions"]["player_ready"]:
            break
        await asyncio.sleep(.4)
    scene = await bridge.call("scene", {
        "path": "/root/Game/RootSceneContainer/Run/RoomContainer/CombatRoom/CombatUi",
        "depth": 3, "limit": 90})
    end = [n for n in scene.get("nodes", []) if "EndTurn" in n["path"]
           and n.get("properties", {}).get("visible")]
    ready = samples[-1]["conditions"]["player_ready"]
    verdict = bool(ready and end)
    (out / "samples.json").write_text(json.dumps(samples, ensure_ascii=False, indent=2), encoding="utf-8")
    (out / "ui.json").write_text(json.dumps(scene, ensure_ascii=False, indent=2), encoding="utf-8")
    result = {"pass": verdict, "scope": "single-player native graphical combat start",
              "player_ready": ready, "end_turn_visible": bool(end), "dispatch": dispatched,
              "elapsed": round(time.monotonic()-started, 2)}
    (out / "result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))
    return 0 if verdict else 2


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--instance", required=True)
    p.add_argument("--report", default="build/act2-no-hand-report-20261010/report.json")
    p.add_argument("--output", default="build/act2-no-hand-report-20261010/solo-p0")
    p.add_argument("--setup", action="store_true")
    p.add_argument("--resume", action="store_true")
    p.add_argument("--no-relics", action="store_true")
    p.add_argument("--player", type=int, choices=[0, 1], default=0)
    p.add_argument("--encounter", default="SPONGE_LEECH_WEAK")
    p.add_argument("--timeout", type=float, default=12)
    sys.exit(asyncio.run(main(p.parse_args())))
