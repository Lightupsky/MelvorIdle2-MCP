"""Locates Melvor Idle 2 save files and decodes them via the SaveDecompress tool."""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

from .save_parser import ParsedSave, parse_raw

GAME_DIR_NAME = "Games By Malcs"
GAME_NAME = "Melvor Idle 2"
DECOMPRESS_EXE = (
    Path(__file__).resolve().parent.parent
    / "tools" / "SaveDecompress" / "bin" / "SaveDecompress.exe"
)


def saves_root() -> Path:
    """Directory that holds per-platform save folders."""
    appdata = Path(os.environ.get("APPDATA", Path.home() / "AppData" / "Roaming"))
    return appdata / GAME_DIR_NAME / GAME_NAME


def find_save_files() -> list[Path]:
    root = saves_root()
    if not root.exists():
        return []
    return sorted(root.glob("*/*/saves/*.sav"))


def active_save() -> Path | None:
    """Pick the most recently modified save file."""
    saves = find_save_files()
    if not saves:
        return None
    return max(saves, key=lambda p: p.stat().st_mtime)


def decompress(save_path: Path) -> bytes:
    if not DECOMPRESS_EXE.exists():
        raise FileNotFoundError(
            f"SaveDecompress tool not found at {DECOMPRESS_EXE}. "
            "Build it with: dotnet build tools/SaveDecompress -c Release -o bin"
        )
    with tempfile.NamedTemporaryFile(suffix=".raw", delete=False) as tmp:
        tmp_path = Path(tmp.name)
    try:
        proc = subprocess.run(
            [str(DECOMPRESS_EXE), str(save_path), str(tmp_path)],
            capture_output=True,
            text=True,
        )
        if proc.returncode != 0:
            raise RuntimeError(f"SaveDecompress failed: {proc.stderr or proc.stdout}")
        return tmp_path.read_bytes()
    finally:
        tmp_path.unlink(missing_ok=True)


def load_save(save_path: Path | None = None) -> ParsedSave:
    save_path = save_path or active_save()
    if save_path is None:
        raise FileNotFoundError(f"No save files found under {saves_root()}")
    raw = decompress(save_path)
    parsed = parse_raw(raw)
    return parsed


if __name__ == "__main__":
    save = sys.argv[1] if len(sys.argv) > 1 else None
    p = load_save(Path(save) if save else None)
    print("version:", p.version)
    h = p.header
    print("character:", h.character_name, "| gamemode:", h.gamemode)
    print("saved at:", h.timestamp)
    print("total level:", h.total_level, "| GP:", h.gp, "| Slayer coins:", h.slayer_coins)
    print("active:", h.active_entity_name, "->", h.active_action_name)
    print("entities:", len(p.entities))
    for eid, e in p.entities.items():
        comps = ",".join(e.components.keys())
        print(" ", eid, "=>", comps)
    print("active actions:", p.active_actions)
    print("currencies:", json.dumps(p.currencies(), indent=1))
    items = p.bank_items()
    print("bank items:", len(items))
    for it in items[:50]:
        print("  ", it["quantity"], it["item"])
