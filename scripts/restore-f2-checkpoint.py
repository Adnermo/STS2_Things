"""Validate a new F2 checkpoint and restore it into a stopped Workbench test profile."""
import argparse
import gzip
import hashlib
import io
import json
import shutil
import sys
from pathlib import Path

MCP = Path(__file__).resolve().parents[2] / "STS2-MCP"
sys.path.insert(0, str(MCP / "src"))
from sts2_mcp.config import Config
from sts2_mcp.instances import InstanceManager
from sts2_mcp.jobs import atomic_json


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("report", type=Path)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--instance")
    args = p.parse_args()
    report = json.loads(args.report.read_text(encoding="utf-8-sig"))
    run = report.get("report", report).get("payload", report).get("run", report)
    checkpoint = run.get("checkpoint")
    if not checkpoint or not checkpoint.get("available"):
        raise RuntimeError("This report has no native checkpoint; old snapshot data cannot reconstruct missing RNG")
    if (checkpoint.get("format") != "sts2-native-save" or checkpoint.get("encoding") != "gzip-base64"
            or checkpoint.get("schema") != 1 or not 1 <= checkpoint.get("player_count", 0) <= 8):
        raise RuntimeError("Unsupported checkpoint format")
    import base64

    blob = base64.b64decode(checkpoint["data"], validate=True)
    if len(blob) > 110_000:
        raise RuntimeError("Checkpoint exceeds compressed size limit")
    with gzip.GzipFile(fileobj=io.BytesIO(blob)) as compressed:
        data = compressed.read(2_000_001)
    if len(data) > 2_000_000 or len(data) != checkpoint["plain_bytes"]:
        raise RuntimeError("Invalid native checkpoint size")
    if hashlib.sha256(data).hexdigest() != checkpoint["sha256"]:
        raise RuntimeError("Checkpoint integrity check failed")
    save = json.loads(data)
    if [player["net_id"] for player in save["players"]] != list(range(1, checkpoint["player_count"] + 1)):
        raise RuntimeError("Checkpoint does not contain anonymous player identities")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(data)
    result = {"native_schema": save["schema_version"], "players": len(save["players"]),
              "seed": save["rng"]["seed"], "output": str(args.output)}
    if args.instance:
        c = Config.load()
        instance = InstanceManager(c).get(args.instance)
        if instance["running"]:
            raise RuntimeError("Close this exact isolated instance before restoring")
        if instance["release"]["version"] != run.get("game_version"):
            raise RuntimeError("The private runtime does not match the report game version")
        # Use only native anonymous IDs for the offline test connection. Real Steam
        # multiplayer ownership must be rebound deliberately in a separate workflow.
        target = Path(instance["user_data"]) / "default" / str(instance["client_id"]) / "modded/profile1/saves/current_run.save"
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists():
            backup = Path(instance["game"]).parent / "checkpoint-before-restore.save"
            shutil.copy2(target, backup)
        atomic_json(target, save)
        result["instance"] = args.instance
        result["scope"] = "offline native save, not the player's live Steam profile"
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
