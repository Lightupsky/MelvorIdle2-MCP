#!/usr/bin/env python3
"""Offline extractor for Melvor Idle 2 game data and localization.

Reads the embedded Godot 4 PCK directly from Melvor2.exe (no need to launch
the game or load a save!) and extracts:
  - knowledge/data/MelvorBase.json   (full 15MB game database: items, recipes,
                                      shop prices, quests, enemies, drops, etc.)
  - knowledge/data/en_GB.po          (English localization)
  - knowledge/data/zh_CN.po          (Simplified Chinese localization)
  - knowledge/data/extract_meta.json (extraction metadata: Godot version, game
                                      exe timestamp, package stats)

Optionally also decompiles GamesByMalcs.Melvor2.dll with ilspycmd (--decompile)
and extracts SaveVersion / game version metadata from the assembly.

Usage:
    python extract_offline.py [--exe PATH] [--out DIR] [--decompile]
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import struct
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import BinaryIO

HERE = Path(__file__).resolve().parent
DEFAULT_OUT = HERE / "data"
# Tools/melvor2-mcp/knowledge -> game root is 3 levels up
DEFAULT_GAME_ROOT = HERE.parent.parent.parent


def find_pe_pck_section(f: BinaryIO) -> tuple[int, int]:
    """Locate the 'pck' section in a Windows PE executable (pure stdlib)."""
    f.seek(0)
    dos = f.read(64)
    if dos[:2] != b"MZ":
        raise ValueError("Not a Windows PE executable (missing MZ header)")
    pe_off = struct.unpack_from("<I", dos, 60)[0]
    f.seek(pe_off)
    sig = f.read(4)
    if sig != b"PE\x00\x00":
        raise ValueError("Invalid PE signature")
    coff = f.read(20)
    num_sections, opt_hdr_size = struct.unpack_from("<HH", coff, 2)[0], struct.unpack_from("<H", coff, 16)[0]
    f.seek(pe_off + 24 + opt_hdr_size)
    for _ in range(num_sections):
        sec = f.read(40)
        name = sec[:8].rstrip(b"\x00").decode("ascii", errors="replace")
        raw_size, raw_ptr = struct.unpack_from("<II", sec, 16)
        if name == "pck":
            return raw_ptr, raw_size
    raise ValueError("No embedded 'pck' section found in executable")


def read_pck_index(f: BinaryIO, pck_off: int) -> tuple[dict, list[tuple[str, int, int, int]]]:
    """Parse Godot 4 PCK header (v2/v3/v4) and return (header_info, entries)."""
    f.seek(pck_off)
    magic = f.read(4)
    if magic != b"GDPC":
        raise ValueError(f"Expected GDPC magic at {pck_off:#x}, got {magic!r}")
    ver, maj, min_, pat, flags = struct.unpack("<IIIII", f.read(20))
    fbase = struct.unpack("<Q", f.read(8))[0]
    dir_base = None
    if ver >= 3:
        dir_base = struct.unpack("<Q", f.read(8))[0]
        f.seek(pck_off + dir_base)
    else:
        # PCK v2: 16 reserved uint32s (64 bytes) follow fbase
        f.seek(pck_off + 4 + 20 + 8 + 64)

    count = struct.unpack("<I", f.read(4))[0]
    entries: list[tuple[str, int, int, int]] = []
    for _ in range(count):
        plen = struct.unpack("<I", f.read(4))[0]
        path = f.read(plen).rstrip(b"\x00").decode("utf-8", errors="replace")
        ofs, sz = struct.unpack("<QQ", f.read(16))
        _md5 = f.read(16)
        fl = struct.unpack("<I", f.read(4))[0]
        entries.append((path, pck_off + fbase + ofs, sz, fl))

    header = {
        "pck_version": ver,
        "godot_version": f"{maj}.{min_}.{pat}",
        "flags": flags,
        "file_count": count,
    }
    return header, entries


def extract_from_exe(exe_path: Path, out_dir: Path) -> dict:
    """Extract game data JSON packages and PO locales from Melvor2.exe."""
    out_dir.mkdir(parents=True, exist_ok=True)
    extracted: list[dict] = []
    with open(exe_path, "rb") as f:
        pck_off, pck_size = find_pe_pck_section(f)
        header, entries = read_pck_index(f, pck_off)
        for path, abs_off, sz, fl in entries:
            is_pkg = path.startswith("Data/Packages/") and path.endswith(".json")
            is_po = path.startswith("Locale/") and path.endswith(".po")
            if not (is_pkg or is_po):
                continue
            if fl & 1:
                print(f"  [WARN] skipping encrypted entry {path}", file=sys.stderr)
                continue
            fname = Path(path).name
            dest = out_dir / fname
            f.seek(abs_off)
            blob = f.read(sz)
            dest.write_bytes(blob)
            extracted.append({"pck_path": path, "dest": str(dest), "size": sz})
            print(f"  Extracted {path} -> {dest.name} ({sz:,} bytes)")

    meta = {
        "extracted_at": datetime.now(timezone.utc).isoformat(),
        "exe_path": str(exe_path),
        "exe_size": exe_path.stat().st_size,
        "exe_mtime": datetime.fromtimestamp(exe_path.stat().st_mtime, tz=timezone.utc).isoformat(),
        "pck": header,
        "files": extracted,
    }

    # Try to read game version & SaveVersion from decompiled source if available
    meta["code_info"] = probe_code_metadata(exe_path.parent)
    (out_dir / "extract_meta.json").write_text(
        json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    return meta


def probe_code_metadata(game_root: Path) -> dict:
    """Extract SaveVersion & version string from existing decompiled source if present."""
    info: dict = {}
    decompiled = Path(os.environ.get("TEMP", "")) / "opencode" / "melvor2-mcp" / "decompiled" / "GamesByMalcs.Melvor2.decompiled.cs"
    if decompiled.is_file():
        text = decompiled.read_text(encoding="utf-8", errors="replace")
        m_ver = re.search(r'enum\s+SaveVersion\s*\{([^}]+)\}', text)
        if m_ver:
            vals = [line.strip().rstrip(",") for line in m_ver.group(1).splitlines() if line.strip()]
            info["save_versions"] = vals
            info["latest_save_version"] = vals[-1] if vals else None
        m_off = re.search(r'BaseMaxOfflineTime\s*=\s*([0-9_]+)', text)
        if m_off:
            info["base_max_offline_ms"] = int(m_off.group(1).replace("_", ""))
    data_dirs = list(game_root.glob("data_*"))
    if data_dirs:
        dll = data_dirs[0] / "GamesByMalcs.Melvor2.dll"
        if dll.is_file():
            info["game_dll"] = str(dll)
            info["game_dll_size"] = dll.stat().st_size
    return info


def run_decompile(game_root: Path, out_dir: Path) -> None:
    """Decompile GamesByMalcs.Melvor2.dll with ilspycmd (if installed)."""
    data_dirs = list(game_root.glob("data_*"))
    if not data_dirs:
        raise FileNotFoundError(f"No data_* directory found under {game_root}")
    dll = data_dirs[0] / "GamesByMalcs.Melvor2.dll"
    if not dll.is_file():
        raise FileNotFoundError(f"Game DLL not found: {dll}")
    ilspy = shutil.which("ilspycmd")
    if not ilspy:
        raise RuntimeError(
            "ilspycmd not found on PATH. Install with: "
            "dotnet tool install -g ilspycmd"
        )
    decomp_dir = out_dir / "decompiled"
    decomp_dir.mkdir(parents=True, exist_ok=True)
    print(f"Decompiling {dll} -> {decomp_dir} ...")
    subprocess.run([ilspy, str(dll), "-o", str(decomp_dir)], check=True)


def detect_game_exe() -> Path | None:
    """Auto-detect Melvor2.exe from relative path or Windows Steam libraries."""
    rel = DEFAULT_GAME_ROOT / "Melvor2.exe"
    if rel.is_file():
        return rel

    steam_roots: list[Path] = []
    if sys.platform == "win32":
        try:
            import winreg
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as k:
                sp, _ = winreg.QueryValueEx(k, "SteamPath")
                if sp:
                    steam_roots.append(Path(sp))
        except Exception:
            pass
    steam_roots.extend([
        Path(r"C:\Program Files (x86)\Steam"),
        Path(r"C:\Program Files\Steam"),
    ])

    libraries: list[Path] = []
    seen: set[str] = set()
    for sr in steam_roots:
        if not sr.is_dir():
            continue
        for cand_lib in [sr]:
            k = str(cand_lib).lower()
            if k not in seen:
                seen.add(k)
                libraries.append(cand_lib)
        vdf = sr / "steamapps" / "libraryfolders.vdf"
        if vdf.is_file():
            text = vdf.read_text(encoding="utf-8", errors="replace")
            for m in re.finditer(r'"path"\s+"([^"]+)"', text):
                lp = Path(m.group(1).replace("\\\\", "\\"))
                k = str(lp).lower()
                if lp.is_dir() and k not in seen:
                    seen.add(k)
                    libraries.append(lp)

    for lib in libraries:
        exe = lib / "steamapps" / "common" / "Melvor Idle 2" / "Melvor2.exe"
        if exe.is_file():
            return exe
    for drv in ("C", "D", "E", "F", "G"):
        for sub in ("SteamLibrary", "Steam", r"Program Files (x86)\Steam"):
            exe = Path(f"{drv}:\\{sub}\\steamapps\\common\\Melvor Idle 2\\Melvor2.exe")
            if exe.is_file():
                return exe
    return None


def main() -> int:
    ap = argparse.ArgumentParser(description="Offline Melvor Idle 2 data extractor")
    ap.add_argument("--exe", type=Path, default=None,
                    help="Path to Melvor2.exe (auto-detected via Steam if omitted)")
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT,
                    help="Output directory (default: knowledge/data)")
    ap.add_argument("--decompile", action="store_true",
                    help="Also run ilspycmd on GamesByMalcs.Melvor2.dll")
    args = ap.parse_args()

    exe_path = args.exe or detect_game_exe()
    if exe_path is None or not exe_path.is_file():
        print(
            f"ERROR: Melvor2.exe not found (tried {args.exe or 'Steam auto-detection'}). "
            "Pass --exe \"<path\\to\\Melvor2.exe>\"",
            file=sys.stderr,
        )
        return 1

    print(f"Extracting game data offline from {exe_path} ...")
    meta = extract_from_exe(exe_path, args.out)
    print(f"Done! Godot {meta['pck']['godot_version']}, {len(meta['files'])} files extracted.")

    if args.decompile:
        run_decompile(exe_path.parent, args.out)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
