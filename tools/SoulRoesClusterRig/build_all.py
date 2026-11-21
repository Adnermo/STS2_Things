"""End-to-end reproducible build, export, package, verification, and visual audit runner for ThingsSoulRoes."""
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def run(cmd: list[str]):
    print(">>", " ".join(cmd), flush=True)
    subprocess.run(cmd, cwd=HERE, check=True)


def main():
    py = sys.executable
    run([py, "extract_parts.py"])
    run([py, "soul_roes_rigkit.py", "build"])
    run([py, "soul_roes_rigkit.py", "export"])
    run([py, "package.py"])
    run(["node", "verify.mjs"])
    run([py, "render_audit.py"])


if __name__ == "__main__":
    main()
