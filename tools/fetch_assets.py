#!/usr/bin/env python3
"""Download the Kenney CC0 packs into tools/.cache/ and verify them by SHA-256.

The first run records each zip's hash in tools/assets.lock.json; every later run
(and every re-download) must match it. Delete a lock entry deliberately to accept
a changed pack. Fails loudly if a URL stops resolving or a hash changes.
"""
import hashlib
import json
import sys
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
CACHE = HERE / ".cache"
LOCK = HERE / "assets.lock.json"

K = "https://kenney.nl/media/pages/assets/"
PACKS = {
    "top-down-shooter": K + "top-down-shooter/230204340a-1677694684/kenney_top-down-shooter.zip",
    "ui-pack-sci-fi": K + "ui-pack-sci-fi/b67c2acd31-1724181109/kenney_ui-pack-space-expansion.zip",
    "kenney-fonts": K + "kenney-fonts/8d5435c213-1677661710/kenney_kenney-fonts.zip",
    "crosshair-pack": K + "crosshair-pack/5ef74bd405-1785950072/kenney_crosshair-pack.zip",
    "game-icons": K + "game-icons/1ebf9c14af-1677661579/kenney_game-icons.zip",
    "smoke-particles": K + "smoke-particles/23249a0d35-1677695171/kenney_smoke-particles.zip",
    "sci-fi-sounds": K + "sci-fi-sounds/6b296f9ecf-1677589334/kenney_sci-fi-sounds.zip",
    "impact-sounds": K + "impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip",
    "interface-sounds": K + "interface-sounds/fa43c1dd4d-1677589452/kenney_interface-sounds.zip",
}


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    CACHE.mkdir(exist_ok=True)
    lock = json.loads(LOCK.read_text()) if LOCK.exists() else {}
    changed = False
    for name, url in PACKS.items():
        dest = CACHE / f"{name}.zip"
        if dest.exists() and name in lock and sha256(dest) != lock[name]:
            print(f"{name}: cached zip does not match lock, re-downloading")
            dest.unlink()
        if not dest.exists():
            print(f"{name}: downloading {url}")
            try:
                urllib.request.urlretrieve(url, dest)
            except Exception as e:  # URL moved, network down, ...
                dest.unlink(missing_ok=True)
                print(f"FAILED to download {name} from {url}: {e}", file=sys.stderr)
                return 1
        digest = sha256(dest)
        if name not in lock:
            lock[name] = digest
            changed = True
            print(f"{name}: recorded sha256 {digest}")
        elif lock[name] != digest:
            print(f"{name}: SHA-256 mismatch\n  expected {lock[name]}\n  got      {digest}", file=sys.stderr)
            dest.unlink()
            return 1
        else:
            print(f"{name}: ok")
    if changed:
        LOCK.write_text(json.dumps(lock, indent=2, sort_keys=True) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
