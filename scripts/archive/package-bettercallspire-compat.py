"""Package the isolated BetterCallSpire compatibility build and verify its identity."""
from pathlib import Path
import hashlib
import json
import zipfile

ROOT = Path(__file__).resolve().parents[1]
TASK = ROOT / "build/bettercallspire-compat-20261012"
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
    assert version == "1.25.19"
    assert sorted(p.name for p in package.iterdir()) == sorted(FILES)
    receipt = json.loads((TASK / "install-receipt.json").read_text("utf-8-sig"))
    assert not receipt["is_error"]
    installed = Path(receipt["result"]["installed"])
    for item in receipt["result"]["files"]:
        assert digest(package / item["name"]) == item["sha256"]
        assert digest(installed / item["name"]) == item["sha256"]

    expected_changes = {"STS2_Things/STS2_ThingsInit.cs", "bootstrap/UnifiedBootstrap.cs"}
    changed, unchanged = [], []
    for folder in ("STS2_Things", "bootstrap"):
        for source in (BASE / folder).rglob("*.cs"):
            relative = source.relative_to(BASE)
            if any(p in {"obj", "bin", ".godot"} for p in relative.parts):
                continue
            candidate = TASK / "source" / relative
            assert candidate.is_file(), relative
            (unchanged if source.read_bytes() == candidate.read_bytes() else changed).append(relative.as_posix())
    assert set(changed) == expected_changes, changed
    new_file = "STS2_Things/Compatibility/BetterCallSpireCompatibility.cs"
    assert (TASK / "source" / new_file).read_bytes() == (ROOT / new_file).read_bytes()
    source_record = {
        "baseline_commit": "63a02e4846d75057742526ef6cd845143a13d52c",
        "baseline_version": "1.25.18", "version": version,
        "unchanged_csharp_files": len(unchanged), "changed_csharp_files": changed,
        "new_csharp_file": new_file, "excluded_other_working_changes": True,
    }
    write_json(TASK / "source-freeze.json", source_record)

    pack = json.loads((TASK / "pack-report.json").read_text("utf-8"))
    assert pack["output_sha256"] == digest(package / "STS2_Things.pck")
    assert len(pack["changes"]) == 1 and pack["changes"][0]["path"] == "res://STS2_Things/Diagnostics/BugEngineLogger.cs"
    checks = []
    for target in ("v107.1", "v111"):
        for order in ("before", "after"):
            logs = TASK / f"final-{target}-{order}"
            output = (logs / "stdout.log").read_text("utf-8")
            errors = (logs / "stderr.log").read_text("utf-8")
            assert "PORTRAIT_COMPAT_PASS checks=26" in output
            assert "native-pooled-parasite,native-replacement-preserved" in output
            assert "ERROR:" not in errors
            checks.append({"target": target, "foreign_load_order": order, "assertions": 26, "result": "PASS"})
        startup = (TASK / f"startup-final-{target}.log").read_text("utf-8")
        assert "Unified startup probe: PASS" in startup and "ERROR:" not in startup
    baseline = (TASK / "baseline-native-v107/stdout.log").read_text("utf-8")
    assert "System.ObjectDisposedException" in baseline and "BetterCallSpire.PortraitPatch.RestoreDefaultState" in baseline
    assert "NCard.UpdateVisuals_Patch" in baseline

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
        "foreign_mod": {"id": "BetterCallSpire", "version": "1.0.0", "workshop_id": "3747671229",
                        "mvid": "1abc6b205f5a426794a66475c15a974d",
                        "dll_sha256": "37d945be0a097f21dd15734f968b45743c49601c46a4126e99b4a0307deb97dd"},
        "baseline_native_fault_reproduced": True, "checks": checks,
        "startup_without_foreign_mod": {"v107.1": "PASS", "v111": "PASS"},
        "native_resource_hashes_verified": pack["resources_verified"],
        "installed": str(installed), "installation_hashes": "PASS", "backup": receipt["result"]["backup"],
        "full_game_client_started": False, "network_multiplayer_tested": False, "published": False,
        "source": source_record,
    }
    write_json(TASK / "delivery.json", delivery)
    print(json.dumps({"version": version, "zip": str(archive), "installed_hashes": "PASS",
                      "native_checks": 104, "startup": "v107.1/v111 PASS", "unchanged_cs": len(unchanged)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
