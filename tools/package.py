#!/usr/bin/env python3
"""Build the BetterNCM plugin package without external dependencies."""
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parent.parent
DIST = ROOT / "dist"
OUT = DIST / "ANLyrics-1.0.0.plugin"

FILES = [
    "manifest.json",
    "README.md",
    "config.js",
    "config.css",
    "src/model.js",
    "src/io.js",
    "src/lyrics.js",
    "src/player.js",
    "src/app.js",
    "overlay/Overlay.cs",
    "overlay/OverlayController.cs",
    "overlay/launch.ps1",
]


def main() -> None:
    missing = [f for f in FILES if not (ROOT / f).is_file()]
    if missing:
        raise SystemExit("missing files: " + ", ".join(missing))

    DIST.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as zf:
        for name in FILES:
            data = (ROOT / name).read_bytes()
            if b"\xef\xbb\xbf" in data[:3]:
                raise SystemExit(f"UTF-8 BOM not allowed: {name}")
            zf.writestr(name, data)
    print(f"{OUT} ({OUT.stat().st_size} bytes, {len(FILES)} files)")


if __name__ == "__main__":
    main()
