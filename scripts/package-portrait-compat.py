"""Verify and package the isolated two-mod portrait compatibility build."""
from pathlib import Path
import hashlib
import json
import re
import zipfile

ROOT = Path(__file__).resolve().parents[1]
TASK = ROOT / "build/regent-portrait-compat-20261012"
BASE = ROOT / "build/f2-complete-20261011/source"
FILES = ("STS2_Things.json", "STS2_Things.dll", "STS2_Things.pck", "STS2_Things.BaseLibBridge.dll")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main():
    package = TASK / "package"
    version = json.loads((package / FILES[0]).read_text("utf-8"))["version"]
    assert version == "1.25.20" and sorted(p.name for p in package.iterdir()) == sorted(FILES)
    receipt = json.loads((TASK / "install-receipt.json").read_text("utf-8-sig"))
    assert not receipt["is_error"]
    installed = Path(receipt["result"]["installed"])
    for item in receipt["result"]["files"]:
        assert digest(package / item["name"]) == item["sha256"] == digest(installed / item["name"])
    previous = ROOT / "build/bettercallspire-compat-20261012/package"
    for name in ("STS2_Things.pck", "STS2_Things.BaseLibBridge.dll"):
        assert digest(package / name) == digest(previous / name)

    changed, unchanged = [], []
    for folder in ("STS2_Things", "bootstrap"):
        for source in (BASE / folder).rglob("*.cs"):
            relative = source.relative_to(BASE)
            if any(p in {"obj", "bin", ".godot"} for p in relative.parts):
                continue
            candidate = TASK / "source" / relative
            assert candidate.is_file(), relative
            (unchanged if source.read_bytes() == candidate.read_bytes() else changed).append(relative.as_posix())
    assert set(changed) == {"STS2_Things/STS2_ThingsInit.cs", "bootstrap/UnifiedBootstrap.cs"}
    new_file = "STS2_Things/Compatibility/PortraitCacheCompatibility.cs"
    assert (TASK / "source" / new_file).read_bytes() == (ROOT / new_file).read_bytes()
    assert not (TASK / "source/STS2_Things/Compatibility/BetterCallSpireCompatibility.cs").exists()
    source_record = {
        "baseline_commit": "63a02e4846d75057742526ef6cd845143a13d52c",
        "baseline_version": "1.25.18", "version": version,
        "unchanged_csharp_files": len(unchanged), "changed_csharp_files": changed,
        "new_csharp_file": new_file, "excluded_other_working_changes": True,
    }
    write_json(TASK / "source-freeze.json", source_record)

    checks = []
    for target in ("v107.1", "v111"):
        for case in ("regent", "better", "both-regent", "both-better"):
            for order in ("before", "after"):
                logs = TASK / f"final-{case}-{target}-{order}"
                output = (logs / "stdout.log").read_text("utf-8")
                errors = (logs / "stderr.log").read_text("utf-8")
                marker = re.search(r"PORTRAIT_COMPAT_PASS checks=(\d+)", output)
                assert marker and "ERROR:" not in errors
                assert "native-pooled-parasite" in output and "native-retained-debris" in output
                assert ("independent-mod-caches" if case.startswith("both") else "native-replacement-preserved") in output
                checks.append({"target": target, "case": case, "foreign_load_order": order,
                               "assertions": int(marker[1]), "result": "PASS"})
        startup = (TASK / f"startup-final-{target}.log").read_text("utf-8")
        assert "Unified startup probe: PASS" in startup and "ERROR:" not in startup
    baseline = (TASK / "baseline-native-v107/stdout.log").read_text("utf-8")
    assert "System.ObjectDisposedException" in baseline and "regentCardsAnimeRework.PortraitPatch.RestoreDefaultState" in baseline
    assert "NCard.UpdateVisuals_Patch" in baseline

    match = json.loads((ROOT / "build/report-6a50d0fc-20261012/package-match.json").read_text("utf-8"))
    assert match["downloaded_player_package_exact_hash_match"]
    destination = ROOT / "dist" / f"v{version}"
    destination.mkdir(parents=True, exist_ok=True)
    archive = destination / f"STS2_Things-{version}-unified.zip"
    if archive.exists():
        raise RuntimeError(f"Preserve existing archive before packaging: {archive}")
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as zipped:
        for name in FILES:
            zipped.write(package / name, name)
    with zipfile.ZipFile(archive) as zipped:
        assert zipped.testzip() is None and sorted(zipped.namelist()) == sorted(FILES)
        for name in FILES:
            assert hashlib.sha256(zipped.read(name)).hexdigest() == digest(package / name)
    sums = [f"{digest(archive)}  {archive.name}"] + [f"{digest(package / name)}  {name}" for name in FILES]
    (destination / "SHA256SUMS.txt").write_text("\n".join(sums) + "\n", encoding="utf-8")
    delivery = {
        "version": version, "files": receipt["result"]["files"],
        "fingerprint": receipt["result"]["fingerprint"],
        "zip": str(archive), "zip_sha256": digest(archive), "zip_crc_and_hashes": "PASS",
        "foreign_mods": ["BetterCallSpire 1.0.0", "regentCardsAnimeRework 1.0.0.0"],
        "regent_player_package_exact_match": match, "baseline_native_fault_reproduced": True,
        "checks": checks, "assertions": sum(c["assertions"] for c in checks),
        "startup_without_foreign_mods": {"v107.1": "PASS", "v111": "PASS"},
        "resources_and_bridge_identical_to_1_25_19": True,
        "installed": str(installed), "installation_hashes": "PASS", "backup": receipt["result"]["backup"],
        "full_game_client_started": False, "network_multiplayer_tested": False, "published": False,
        "source": source_record,
    }
    write_json(TASK / "delivery.json", delivery)
    print(json.dumps({"version": version, "zip": str(archive), "installed_hashes": "PASS",
                      "native_assertions": delivery["assertions"], "matrix_cases": len(checks),
                      "startup": "v107.1/v111 PASS", "unchanged_cs": len(unchanged)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
