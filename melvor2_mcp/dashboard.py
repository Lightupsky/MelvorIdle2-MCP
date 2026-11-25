"""Standalone visual web dashboard for Melvor Idle 2 Plan Scheduler & State.

Serves dashboard/index.html on http://127.0.0.1:17818 (configurable):
  - When the game bridge (127.0.0.1:17817) is reachable, proxies live API calls
    (/ping, /state, /plan/*, /health, /quests, ...) transparently.
  - When the game is closed (or bridge doesn't have /health yet), falls back
    gracefully to reading the latest .sav file, melvor2_plan.json, and
    logs/melvor2_compat_report.json on disk.

Run:
    python -m melvor2_mcp.dashboard [--port 17818] [--open]
"""

from __future__ import annotations

import argparse
import json
import os
import socket
import urllib.error
import urllib.request
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from .plan_validator import compute_offline_mastery
from .save_io import active_save, load_save

HERE = Path(__file__).resolve().parent
HTML_PATH = HERE.parent / "dashboard" / "index.html"
BRIDGE_PORT = int(os.environ.get("MELVOR2_BRIDGE_PORT", "17817"))


def _data_dir() -> Path:
    return Path(os.environ.get("APPDATA", "")) / "Games By Malcs" / "Melvor Idle 2"


def _bridge_up() -> bool:
    try:
        with socket.socket() as s:
            s.settimeout(0.25)
            return s.connect_ex(("127.0.0.1", BRIDGE_PORT)) == 0
    except Exception:
        return False


def _proxy(method: str, path: str, body: bytes | None) -> tuple[int, bytes, str] | None:
    if not _bridge_up():
        return None
    url = f"http://127.0.0.1:{BRIDGE_PORT}{path}"
    req = urllib.request.Request(
        url,
        data=body if method == "POST" else None,
        method=method,
        headers={"Content-Type": "application/json; charset=utf-8"} if body else {},
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as resp:
            raw = resp.read()
            ctype = resp.headers.get("Content-Type", "application/json; charset=utf-8")
            return resp.status, raw, ctype
    except urllib.error.HTTPError as e:
        return e.code, e.read(), "application/json; charset=utf-8"
    except Exception:
        return None


def _offline_state() -> dict:
    p = active_save()
    if p is None:
        return {"error": "未找到本地存档文件"}
    s = load_save(p)
    h = s.header
    q = s.quests()
    mg = compute_offline_mastery(s).get("skills", {})
    q_map = q.get("quests", {})
    active_ids = [qid for qid, qv in q_map.items() if qv.get("state") == "InProgress"]
    claim_ids = [qid for qid, qv in q_map.items() if qv.get("state") == "Completed"]
    q_summary = dict(q.get("summary", {}))
    q_summary["active_ids"] = active_ids
    q_summary["claimable_ids"] = claim_ids
    skills_dict = {}
    for sk in s.skills():
        sname = sk["id"].split(":")[-1]
        m = mg.get(sname) or {}
        skills_dict[sname] = {
            "level": sk["level"],
            "xp": int(sk["xp"]),
            "mastery_rank": m.get("rank_number", 1),
            "mastery_rank_name": m.get("rank_name"),
            "mastery_points": m.get("points", 0),
            "mastery_total": m.get("total_points", 0),
            "mastery_pct": m.get("percent", 0.0),
            "to_next_rank": m.get("points_to_next_rank", 0),
            "next_rank_bonus": m.get("next_rank_bonus"),
        }
    return {
        "character": h.character_name,
        "total_level": h.total_level,
        "gold_pieces": h.gp,
        "currencies": s.currencies(),
        "active": {"entity": h.active_entity_name, "action": h.active_action_name},
        "skills": skills_dict,
        "quests_summary": q_summary,
        "offline_fallback": True,
    }


def _offline_plan() -> dict:
    p = _data_dir() / "melvor2_plan.json"
    if p.is_file():
        try:
            return json.loads(p.read_text(encoding="utf-8"))
        except Exception as e:
            return {"status": "error", "status_reason": str(e)}
    return {"status": "idle"}


def _offline_health() -> dict:
    p = _data_dir() / "logs" / "melvor2_compat_report.json"
    if p.is_file():
        try:
            return json.loads(p.read_text(encoding="utf-8"))
        except Exception:
            pass
    return {
        "status": "healthy" if _bridge_up() else "offline",
        "compatible": True,
        "report_file": str(p),
        "note": "部署 v2.1 桥接后将提供完整实时 EA 反射自检报告",
    }


class DashboardHandler(BaseHTTPRequestHandler):
    def log_message(self, fmt: str, *args) -> None:
        pass

    def _send(self, code: int, body: bytes, ctype: str = "application/json; charset=utf-8") -> None:
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _send_json(self, obj: dict, code: int = 200) -> None:
        self._send(code, json.dumps(obj, ensure_ascii=False).encode("utf-8"))

    def do_GET(self) -> None:
        path = self.path.split("?", 1)[0].rstrip("/") or "/"
        if path in ("/", "/dashboard", "/index.html"):
            self._send(200, HTML_PATH.read_bytes(), "text/html; charset=utf-8")
            return

        proxied = _proxy("GET", self.path, None)
        if proxied is not None:
            code, raw, ctype = proxied
            # If running bridge is v2.0.0 without /health yet, serve graceful fallback
            if path == "/health" and b"unknown path" in raw:
                self._send_json(_offline_health())
                return
            self._send(code, raw, ctype)
            return

        # Offline disk fallbacks
        if path == "/ping":
            self._send_json({"ok": True, "offline_fallback": True, "game_found": False})
        elif path == "/state":
            self._send_json(_offline_state())
        elif path == "/plan/status":
            self._send_json(_offline_plan())
        elif path == "/health":
            self._send_json(_offline_health())
        else:
            self._send_json({"ok": False, "error": "bridge offline"}, code=503)

    def do_POST(self) -> None:
        length = int(self.headers.get("Content-Length", "0") or 0)
        body = self.rfile.read(length) if length > 0 else b"{}"
        proxied = _proxy("POST", self.path, body)
        if proxied is not None:
            code, raw, ctype = proxied
            self._send(code, raw, ctype)
            return
        self._send_json({"ok": False, "error": "游戏桥接未运行，无法执行实时控制"}, code=503)


def main() -> int:
    ap = argparse.ArgumentParser(description="Melvor Idle 2 Plan & Status Web Dashboard")
    ap.add_argument("--port", type=int, default=17818, help="HTTP port (default: 17818)")
    ap.add_argument("--open", action="store_true", help="Open browser automatically")
    args = ap.parse_args()

    url = f"http://127.0.0.1:{args.port}/"
    srv = ThreadingHTTPServer(("127.0.0.1", args.port), DashboardHandler)
    print(f"Melvor Idle 2 Dashboard running at {url} (Ctrl+C to stop)")
    if args.open:
        webbrowser.open(url)
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
