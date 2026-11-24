"""Melvor Idle 2 MCP server (v2).

Provides:
  1. Offline save-file state tools (skills, bank, currencies, quests, equipment,
     toolbelts, shop purchases, combat/slayer setup) — works even when game is closed!
  2. Offline knowledge base & search tools (1,811 items, 37 quests, 499 shop prices,
     192 enemies & drops, all 25 skills) — extracted directly from Melvor2.exe PCK.
  3. Autonomous Action Plan / Queue tools (validate_plan, set_action_plan,
     get_plan_status, control_plan) — author a multi-step timed/counted/goal-driven
     plan once, let the in-game scheduler run autonomously for hours without the
     Agent staying active, then inspect & re-plan later!
  4. Full live in-game bridge tools (state, bank, quests, combat, shop, passive,
     and execute_game_op covering 30+ in-game operations).
"""

from __future__ import annotations

import json
import os
import time
import urllib.parse
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from fastmcp import FastMCP

from .plan_validator import (
    KNOWLEDGE_DIR,
    KNOWN_OPS,
    load_quests_index,
    search_knowledge_data,
    validate_plan_dict,
)
from .save_io import active_save, find_save_files, load_save
from .save_parser import ParsedSave

mcp = FastMCP("melvor-idle-2")

# Cache the last parsed save briefly (the game writes every ~10s while running)
_cache: dict[str, Any] = {"path": None, "mtime": 0.0, "parsed": None, "at": 0.0}

BRIDGE_PORT = int(os.environ.get("MELVOR2_BRIDGE_PORT", "17817"))


def _plan_file_path() -> Path:
    appdata = os.environ.get("APPDATA", "")
    return Path(appdata) / "Games By Malcs" / "Melvor Idle 2" / "melvor2_plan.json"


def _is_game_running() -> bool:
    import subprocess

    out = subprocess.run(
        ["tasklist", "/fi", "imagename eq Melvor2.exe", "/fo", "csv"],
        capture_output=True,
        text=True,
    ).stdout
    return "Melvor2.exe" in out


def _get_save() -> tuple[ParsedSave, Path, bool]:
    path = active_save()
    if path is None:
        raise FileNotFoundError(
            "No Melvor Idle 2 save files found. Start the game and create a save first."
        )
    mtime = path.stat().st_mtime
    if (
        _cache["parsed"] is not None
        and _cache["path"] == path
        and _cache["mtime"] == mtime
    ):
        return _cache["parsed"], path, True
    parsed = load_save(path)
    _cache.update(path=path, mtime=mtime, parsed=parsed, at=time.time())
    return parsed, path, False


def _freshness(path: Path) -> dict:
    age = time.time() - path.stat().st_mtime
    return {
        "save_file": str(path),
        "save_mtime_age_seconds": round(age, 1),
        "game_running": _is_game_running(),
        "bridge_reachable": _bridge_available(),
    }


def _bridge_available() -> bool:
    import socket

    with socket.socket() as s:
        s.settimeout(0.3)
        return s.connect_ex(("127.0.0.1", BRIDGE_PORT)) == 0


def _bridge_call(path: str, payload: dict | None = None) -> str:
    import urllib.request

    url = f"http://127.0.0.1:{BRIDGE_PORT}{path}"
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8") if payload is not None else None
    req = urllib.request.Request(
        url,
        data=data,
        method="POST" if data is not None else "GET",
        headers={"Content-Type": "application/json; charset=utf-8"} if data is not None else {},
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            return resp.read().decode("utf-8")
    except Exception as e:
        return json.dumps(
            {
                "ok": False,
                "error": f"Bridge unavailable: {e}. "
                "Launch the game via Steam with bridge/InstallBridge.ps1 run to enable live actions.",
            },
            ensure_ascii=False,
        )


def _read_persisted_plan() -> dict | None:
    p = _plan_file_path()
    try:
        if p.is_file():
            return json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        pass
    return None


# ===========================================================================
# 1. Offline Save-Based Read Tools (work even when game is closed!)
# ===========================================================================


@mcp.tool()
def get_game_state() -> str:
    """Get an overview of the current Melvor Idle 2 save: character name,
    gamemode, total level, GP, slayer coins, currently running action,
    skills, bank summary, quests summary, and active autonomous plan status."""
    save, path, _ = _get_save()
    h = save.header
    skills = save.skills()
    items = save.bank_items()
    qdata = save.quests()
    plan_disk = _read_persisted_plan()
    state = {
        "character": h.character_name,
        "gamemode": h.gamemode,
        "save_timestamp": h.timestamp.isoformat(),
        "total_level": h.total_level,
        "gold_pieces": h.gp,
        "slayer_coins": h.slayer_coins,
        "active": {
            "entity": h.active_entity_name,
            "action": h.active_action_name,
            "entity_id": save.active_actions[0][0] if save.active_actions else None,
        },
        "skills": {s["id"].split(":")[-1]: s["level"] for s in skills},
        "bank": {
            "unique_items": len(items),
            "total_quantity": sum(i["quantity"] for i in items),
        },
        "quests_summary": qdata.get("summary", {}),
        "plan_summary": {
            "status": plan_disk.get("status"),
            "name": (plan_disk.get("plan") or {}).get("name"),
            "current_index": plan_disk.get("current_index"),
            "total_steps": len((plan_disk.get("plan") or {}).get("steps") or []),
            "reason": plan_disk.get("status_reason"),
        } if plan_disk else None,
        **_freshness(path),
    }
    return json.dumps(state, ensure_ascii=False, indent=2)


@mcp.tool()
def get_skills() -> str:
    """Get all Melvor Idle 2 skill levels, XP and XP needed for the next level."""
    save, _, _ = _get_save()
    return json.dumps(save.skills(), ensure_ascii=False, indent=2)


@mcp.tool()
def get_bank_items(filter_text: str | None = None) -> str:
    """Get bank contents (item id, quantity). Optionally filter by a
    case-insensitive substring of the item id."""
    save, _, _ = _get_save()
    items = save.bank_items()
    if filter_text:
        f = filter_text.lower()
        items = [i for i in items if f in i["item"].lower()]
    return json.dumps(items, ensure_ascii=False, indent=2)


@mcp.tool()
def get_currencies() -> str:
    """Get all currency wallet balances (Gold Pieces, Slayer Coins, Prayer Points, ...)."""
    save, _, _ = _get_save()
    return json.dumps(save.currencies(), ensure_ascii=False, indent=2)


@mcp.tool()
def get_active_actions() -> str:
    """Get currently running actions as (entity, action component) pairs,
    plus human-readable names from the save header."""
    save, _, _ = _get_save()
    h = save.header
    return json.dumps(
        {
            "active_actions": save.active_actions,
            "header_active_entity": h.active_entity_name,
            "header_active_action": h.active_action_name,
        },
        ensure_ascii=False,
        indent=2,
    )


@mcp.tool()
def get_quests(state_filter: str | None = None) -> str:
    """Get Quest progress directly from the save file (works offline!), enriched
    with quest names, categories, prerequisites, step requirements, and rewards
    from the offline knowledge base.
    state_filter: optional 'InProgress', 'Completed', 'Claimed', 'Unstarted', or 'active'."""
    save, _, _ = _get_save()
    qsave = save.quests()
    saved_map = qsave.get("quests", {})
    kb_map = {q["id"]: q for q in load_quests_index()}

    out = []
    for qid, qmeta in kb_map.items():
        st_obj = saved_map.get(qid, {"state": "Unstarted", "steps": []})
        st = st_obj.get("state", "Unstarted")
        if state_filter:
            sf = state_filter.strip().lower()
            if sf == "active":
                if st not in ("InProgress", "Completed"):
                    continue
            elif st.lower() != sf:
                continue
        out.append({
            "id": qid,
            "name": qmeta["name"],
            "name_zh": qmeta.get("name_zh"),
            "category": qmeta.get("category"),
            "state": st,
            "prereq_quests": qmeta.get("prereq_quests"),
            "rewards": qmeta.get("rewards"),
            "save_steps": st_obj.get("steps", []),
            "guide_steps": qmeta.get("steps", []) if st in ("InProgress", "Completed", "Unstarted") else [],
        })

    return json.dumps(
        {
            "summary": qsave.get("summary", {}),
            "selected_quest": qsave.get("selected_quest"),
            "count": len(out),
            "quests": out,
        },
        ensure_ascii=False,
        indent=2,
    )


@mcp.tool()
def get_equipment_and_setup() -> str:
    """Get currently equipped combat gear, toolbelts across all skills, selected
    food, combat area/enemy/dungeon, spells, prayers, active slayer tasks, and
    purchased shop upgrade count from the save file (works offline!)."""
    save, _, _ = _get_save()
    shop = save.shop_purchases()
    return json.dumps(
        {
            "combat": save.combat_setup(),
            "toolbelts": save.toolbelts(),
            "slayer": save.slayer_tasks(),
            "shop_purchased_count": len(shop),
            "shop_purchased": shop,
        },
        ensure_ascii=False,
        indent=2,
    )


@mcp.tool()
def list_saves() -> str:
    """List all local save files with their character and last-modified time."""
    out = []
    for p in find_save_files():
        try:
            s = load_save(p)
            out.append(
                {
                    "file": str(p),
                    "character": s.header.character_name,
                    "gamemode": s.header.gamemode,
                    "total_level": s.header.total_level,
                    "gp": s.header.gp,
                    "modified": time.ctime(p.stat().st_mtime),
                }
            )
        except Exception as e:
            out.append({"file": str(p), "error": str(e)})
    return json.dumps(out, ensure_ascii=False, indent=2)


# ===========================================================================
# 2. Autonomous Action Plan / Queue Scheduler Tools (Point 1 Core Workflow)
# ===========================================================================


def _parse_plan_arg(plan: dict | str) -> dict:
    if isinstance(plan, str):
        return json.loads(plan)
    return plan


@mcp.tool()
def validate_plan(plan: dict | str) -> str:
    """Statically validate an Action Plan (JSON object or string) offline against
    MelvorBase.json (checks op names, required parameters, valid item/recipe/
    quest/shop/enemy/area/dungeon IDs, and safety guards on combat/thieving)."""
    try:
        plan_obj = _parse_plan_arg(plan)
    except Exception as e:
        return json.dumps({"valid": False, "errors": [f"invalid JSON: {e}"]})
    return json.dumps(validate_plan_dict(plan_obj), ensure_ascii=False, indent=2)


@mcp.tool()
def set_action_plan(plan: dict | str, validate_first: bool = True) -> str:
    """Submit an ordered multi-step Action Plan to the in-game autonomous
    PlanScheduler. Once submitted, the Agent does NOT need to keep running —
    the game will automatically switch steps by duration, action/kill/dungeon
    count, bank item target, skill level, quest step completion, or resource
    exhaustion!

    If the game is currently closed, saves the plan to disk with
    status='pending_start' so it starts automatically as soon as you open the
    game and load your save.

    Example plan:
    {
      "name": "Mine silver then smelt bars and submit MQ6",
      "pause_on_manual_override": true,
      "steps": [
        {"label": "Harvest farm", "op": "farming_harvest_all"},
        {"label": "Mine 200 Silver Ore", "op": "start_action",
         "params": {"entity": "MelvorBase:Mining", "recipe": "MelvorBase:SilverOre"},
         "until": {"item_qty": {"item": "MelvorBase:SilverOre", "gte": 200}, "duration_seconds": 3600}},
        {"label": "Smelt 100 Silver Bars", "op": "start_action",
         "params": {"entity": "MelvorBase:Smithing", "recipe": "MelvorBase:SilverBar"},
         "until": {"action_count": 100, "until_exhausted": true}},
        {"label": "Submit MQ6 Step 0", "op": "quest_submit", "params": {"quest": "MelvorBase:MQ6"}}
      ]
    }"""
    try:
        plan_obj = _parse_plan_arg(plan)
    except Exception as e:
        return json.dumps({"ok": False, "error": f"invalid JSON: {e}"})

    validation = validate_plan_dict(plan_obj)
    if validate_first and not validation["valid"]:
        return json.dumps(
            {"ok": False, "error": "plan validation failed", "validation": validation},
            ensure_ascii=False,
            indent=2,
        )

    if _bridge_available():
        raw = _bridge_call("/plan/set", plan_obj)
        try:
            res = json.loads(raw)
            if isinstance(res, dict) and validation["warnings"]:
                res["validation_warnings"] = validation["warnings"]
            return json.dumps(res, ensure_ascii=False, indent=2)
        except Exception:
            return raw

    # Offline fallback: write plan to melvor2_plan.json so bridge picks it up on game start
    p = _plan_file_path()
    try:
        p.parent.mkdir(parents=True, exist_ok=True)
        now = datetime.now(timezone.utc).isoformat()
        state_obj = {
            "status": "pending_start" if plan_obj.get("auto_start", True) else "paused",
            "status_reason": "queued_offline_waiting_for_game",
            "current_index": 0,
            "created_at_utc": now,
            "updated_at_utc": now,
            "current_step_progress": None,
            "history": [],
            "plan": plan_obj,
        }
        p.write_text(json.dumps(state_obj, ensure_ascii=False, indent=2), encoding="utf-8")
        return json.dumps(
            {
                "ok": True,
                "mode": "offline_queued",
                "plan_file": str(p),
                "note": "Game is not running; plan saved and will auto-start when you launch Melvor Idle 2 and load your save.",
                "validation_warnings": validation["warnings"],
                "state": state_obj,
            },
            ensure_ascii=False,
            indent=2,
        )
    except Exception as e:
        return json.dumps({"ok": False, "error": f"Failed to write offline plan file {p}: {e}"})


@mcp.tool()
def get_plan_status() -> str:
    """Get the current autonomous Action Plan status, current step progress
    (elapsed seconds, action/kill/dungeon counts), and full execution history.
    Reads live from the bridge when running, or from the persisted plan file
    when the game is closed."""
    if _bridge_available():
        return _bridge_call("/plan/status")
    persisted = _read_persisted_plan()
    if persisted is None:
        return json.dumps({"status": "idle", "note": "No plan found (game not running)."})
    persisted["_source"] = "persisted_disk_file"
    return json.dumps(persisted, ensure_ascii=False, indent=2)


@mcp.tool()
def control_plan(action: str, stop_action: bool = False) -> str:
    """Control the autonomous Action Plan scheduler.
    action: 'pause', 'resume', 'skip' (advance to next step), or 'cancel'.
    stop_action: if True with action='cancel', also stops the active in-game action."""
    act = action.strip().lower()
    if act not in ("pause", "resume", "skip", "cancel"):
        return json.dumps({"ok": False, "error": "action must be one of: pause, resume, skip, cancel"})
    if not _bridge_available():
        return json.dumps({"ok": False, "error": "Bridge not reachable (game not running)."})
    if act == "cancel":
        return _bridge_call("/plan/cancel", {"stop_action": stop_action})
    return _bridge_call(f"/plan/{act}", {})


# ===========================================================================
# 3. Live In-Game Bridge Tools (Full Game Coverage)
# ===========================================================================


@mcp.tool()
def get_bridge_health() -> str:
    """Get Early-Access compatibility & health diagnostic report from the
    bridge (/health), or read the persisted logs/melvor2_compat_report.json
    when the game is closed. Use this to diagnose breakage after EA updates."""
    if _bridge_available():
        raw = _bridge_call("/health")
        if "unknown path" not in raw:
            return raw
    report_path = _plan_file_path().parent / "logs" / "melvor2_compat_report.json"
    if report_path.is_file():
        try:
            return report_path.read_text(encoding="utf-8")
        except Exception as e:
            return json.dumps({"error": f"Failed reading {report_path}: {e}"})
    return json.dumps({
        "status": "unknown",
        "bridge_reachable": _bridge_available(),
        "report_file": str(report_path),
        "note": "No compatibility report generated yet (deploy v2.1+ bridge and enter a save).",
    }, ensure_ascii=False, indent=2)


@mcp.tool()
def get_live_state() -> str:
    """Get live in-game state from the bridge hook (skills, currencies, active
    actions with timers, HP, selected food, quests summary, and plan status)."""
    if not _bridge_available():
        return json.dumps({"error": "Bridge not reachable. Launch the game via Steam with InstallBridge.ps1 installed."})
    return _bridge_call("/state")


@mcp.tool()
def get_live_bank(filter_text: str | None = None) -> str:
    """Get live bank inventory directly from the running game."""
    if not _bridge_available():
        return json.dumps({"error": "Bridge not reachable."})
    qs = f"?q={urllib.parse.quote(filter_text)}" if filter_text else ""
    return _bridge_call("/bank" + qs)


@mcp.tool()
def get_live_quests(filter_state: str | None = None) -> str:
    """Get live quest progress from the running game, including exact goal
    counts, whether step hand-in costs are affordable, and claimable rewards.
    filter_state: optional 'active', 'InProgress', 'Completed', 'Claimed', 'Unstarted'."""
    if not _bridge_available():
        return json.dumps({"error": "Bridge not reachable."})
    qs = f"?filter={urllib.parse.quote(filter_state)}" if filter_state else ""
    return _bridge_call("/quests" + qs)


@mcp.tool()
def get_live_combat() -> str:
    """Get detailed live combat state: player HP, current enemy & HP, selected
    area/dungeon, equipped gear, food & quantity, selected spell/imbuement,
    prayers, and active slayer tasks."""
    if not _bridge_available():
        return json.dumps({"error": "Bridge not reachable."})
    return _bridge_call("/combat")


@mcp.tool()
def get_live_shop(category: str | None = None, available_only: bool = False) -> str:
    """Get live shop status (purchased counts, buy limits, requirements_met,
    can_afford). Filter by category substring or available_only=True."""
    if not _bridge_available():
        return json.dumps({"error": "Bridge not reachable."})
    params = []
    if category:
        params.append(f"category={urllib.parse.quote(category)}")
    if available_only:
        params.append("available=true")
    qs = ("?" + "&".join(params)) if params else ""
    return _bridge_call("/shop" + qs)


@mcp.tool()
def get_live_passive() -> str:
    """Get live Farming plots (can_harvest/can_plant/can_water/planted crop)
    and Ranching lands/enclosures/animals attention status."""
    if not _bridge_available():
        return json.dumps({"error": "Bridge not reachable."})
    return _bridge_call("/passive")


@mcp.tool()
def list_available_actions(skill_id: str | None = None) -> str:
    """List actions the player can start (optionally for one skill), with
    unlocked status for each recipe/option."""
    if not _bridge_available():
        return json.dumps({"error": "Bridge not reachable."})
    qs = f"?skill={urllib.parse.quote(skill_id)}" if skill_id else ""
    return _bridge_call("/actions" + qs)


@mcp.tool()
def execute_game_op(op: str, params: dict | None = None) -> str:
    """Execute ANY supported in-game operation live via the bridge.

    Supported ops:
      - Skills: start_action, stop_action, fishing_select_bait,
        firemaking_bonfire_add, firemaking_bonfire_upgrade,
        astrology_unlock_node, agility_create_shortcut
      - Combat & Gear: combat_fight, combat_dungeon, combat_stop, select_food,
        eat_food, equip, unequip, equip_tool, unequip_tool, swap_loadout,
        select_spell, select_imbuement, toggle_prayer, set_stance, slayer_task
      - Bank & Shop: shop_buy, bank_sell, bank_open, bank_bury, bank_upgrade
      - Quests: quest_start, quest_submit, quest_confirm, quest_claim
      - Passive: farming_harvest_all, farming_plant_all, farming_water_all,
        farming_purchase_plots, farming_grain_mill, ranching_maintain,
        ranching_buy_animal, ranching_toggle_treat
    """
    if op.strip().lower() not in KNOWN_OPS:
        return json.dumps({"ok": False, "error": f"Unknown op {op!r}", "known_ops": sorted(KNOWN_OPS)})
    if not _bridge_available():
        return json.dumps({"ok": False, "error": "Bridge not reachable (game not running)."})
    return _bridge_call("/op", {"op": op.strip().lower(), "params": params or {}})


@mcp.tool()
def start_action(entity_id: str, action_component_id: str = "Action", recipe: str | None = None) -> str:
    """Start an action in the running game (backwards-compatible shortcut for
    execute_game_op('start_action', ...)). Safe against toggle-off if already
    running the same rock/tree/target."""
    if not _bridge_available():
        return json.dumps({"ok": False, "error": "Bridge not reachable."})
    payload: dict[str, Any] = {"entity": entity_id, "component": action_component_id}
    if recipe:
        payload["recipe"] = recipe
    return _bridge_call("/start", payload)


@mcp.tool()
def stop_action(entity_id: str | None = None) -> str:
    """Stop the running action of an entity (or whatever active action is
    currently running if entity_id is omitted)."""
    if not _bridge_available():
        return json.dumps({"ok": False, "error": "Bridge not reachable."})
    return _bridge_call("/stop", {"entity": entity_id} if entity_id else {})


# ===========================================================================
# 4. Offline Knowledge Base & Search Tools
# ===========================================================================

_KNOWLEDGE_DOCS = {
    "readme": "README.md",
    "index": "README.md",
    "quests": "quests.md",
    "mechanics": "mechanics.md",
    "planning": "planning.md",
    "interlinks": "interlinks.md",
}


@mcp.tool()
def browse_knowledge(topic: str) -> str:
    """Browse the offline-extracted Melvor Idle 2 knowledge base.
    Topics: 'readme', 'quests' (all 37 quests), 'mechanics', 'planning',
    'interlinks', or 'skills:<name>' (e.g. 'skills:mining', 'skills:smithing',
    'skills:shop', 'skills:combat', 'skills:upgrades', 'skills:farming')."""
    t = topic.strip().lower()
    if t in _KNOWLEDGE_DOCS:
        path = KNOWLEDGE_DIR / _KNOWLEDGE_DOCS[t]
    elif t.startswith("skills:"):
        path = KNOWLEDGE_DIR / "skills" / (t[7:].strip() + ".md")
    else:
        skills = sorted(
            p.stem for p in (KNOWLEDGE_DIR / "skills").glob("*.md")
        ) if (KNOWLEDGE_DIR / "skills").is_dir() else []
        return json.dumps(
            {
                "error": f"unknown topic {topic!r}",
                "known": list(_KNOWLEDGE_DOCS) + [f"skills:{s}" for s in skills],
            }
        )
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as e:
        return json.dumps({"error": f"cannot read {path.name}: {e}"})
    if len(text) > 60000:
        text = text[:60000] + "\n\n…(truncated — use search_knowledge(query) for specific entries)"
    return text


@mcp.tool()
def search_knowledge(query: str, kind: str | None = None, limit: int = 20) -> str:
    """Search offline Melvor Idle 2 data (English or Chinese!) for exact IDs,
    prices, sell values, quest steps, or enemy stats.
    kind: optional 'item', 'quest', 'shop', or 'enemy'."""
    res = search_knowledge_data(query, kind=kind, limit=limit)
    return json.dumps(res, ensure_ascii=False, indent=2)


@mcp.tool()
def get_quest_guide(quest_id: str | None = None, category: str | None = None) -> str:
    """Get structured quest requirements, step goals, hand-in costs, and rewards
    from the offline knowledge base.
    Pass quest_id (e.g. 'MelvorBase:MQ6' or 'MQ6') or category ('Act1', 'Act2',
    'SideQuests', 'MainQuests')."""
    quests = load_quests_index()
    if quest_id:
        needle = quest_id.strip().lower()
        hits = [
            q for q in quests
            if q["id"].lower() == needle
            or q["id"].split(":")[-1].lower() == needle
            or needle in q["name"].lower()
            or needle in (q.get("name_zh") or "").lower()
        ]
        return json.dumps(hits, ensure_ascii=False, indent=2)
    if category:
        cat = category.strip().lower()
        hits = [q for q in quests if cat in (q.get("category") or "").lower()]
        return json.dumps(hits, ensure_ascii=False, indent=2)
    summary = [
        {
            "id": q["id"],
            "name": q["name"],
            "name_zh": q.get("name_zh"),
            "category": (q.get("category") or "").split(":")[-1],
            "prereq_quests": q.get("prereq_quests"),
            "rewards": q.get("rewards"),
        }
        for q in quests
    ]
    return json.dumps(summary, ensure_ascii=False, indent=2)


def main() -> None:
    mcp.run()


if __name__ == "__main__":
    main()
