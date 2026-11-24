"""Offline knowledge search and Action Plan static validator.

Uses the offline-extracted MelvorBase.json + items_index.json + quests_index.json
to validate plans before sending them to the in-game scheduler, catching typos,
missing parameters, unknown IDs, or unsafe combat/thieving steps.
"""

from __future__ import annotations

import json
from functools import lru_cache
from pathlib import Path
from typing import Any

KNOWLEDGE_DIR = Path(__file__).resolve().parent.parent / "knowledge"
DATA_DIR = KNOWLEDGE_DIR / "data"

KNOWN_OPS = {
    # Continuous
    "start_action", "combat_fight", "combat_dungeon", "wait",
    # Instant skill helpers
    "stop_action", "fishing_select_bait", "firemaking_bonfire_add",
    "firemaking_bonfire_upgrade", "astrology_unlock_node", "agility_create_shortcut",
    # Combat / Equipment / Loadouts / Slayer
    "combat_stop", "select_food", "eat_food", "equip", "unequip",
    "equip_tool", "unequip_tool", "swap_loadout", "select_spell",
    "select_imbuement", "toggle_prayer", "set_stance", "slayer_task",
    # Bank & Shop
    "shop_buy", "bank_sell", "bank_open", "bank_bury", "bank_upgrade",
    # Quests
    "quest_start", "quest_submit", "quest_confirm", "quest_claim",
    # Passive
    "farming_harvest_all", "farming_plant_all", "farming_water_all",
    "farming_purchase_plots", "farming_grain_mill",
    "ranching_maintain", "ranching_buy_animal", "ranching_toggle_treat",
}

CONTINUOUS_OPS = {"start_action", "combat_fight", "combat_dungeon", "wait"}


def _norm_id(val: str | None) -> str:
    if not val:
        return ""
    return val if ":" in val else f"MelvorBase:{val}"


@lru_cache(maxsize=1)
def load_items_index() -> list[dict]:
    p = DATA_DIR / "items_index.json"
    if not p.is_file():
        return []
    return json.loads(p.read_text(encoding="utf-8"))


@lru_cache(maxsize=1)
def load_quests_index() -> list[dict]:
    p = DATA_DIR / "quests_index.json"
    if not p.is_file():
        return []
    return json.loads(p.read_text(encoding="utf-8"))


@lru_cache(maxsize=1)
def load_id_sets() -> dict[str, set[str]]:
    """Load valid ID sets from MelvorBase.json for fast plan validation."""
    p = DATA_DIR / "MelvorBase.json"
    if not p.is_file():
        return {}
    raw = json.loads(p.read_text(encoding="utf-8"))
    objs = raw.get("Objects", {})
    out: dict[str, set[str]] = {}
    for k, arr in objs.items():
        if isinstance(arr, list):
            out[k] = {_norm_id(o["Id"]) for o in arr if isinstance(o, dict) and "Id" in o}
    out["Entity"] = set(raw.get("EntityConfig", {}).keys())
    # Union of all selectable skill recipes/options
    recipe_union: set[str] = set()
    for rk in (
        "ArtisanRecipe", "CookingRecipe", "HerbloreRecipe", "FiremakingRecipe",
        "MiningRecipe", "WoodcuttingRecipe", "FishingRecipe", "HuntingAnimal",
        "ThievingRecipe", "AgilityCourse", "AstrologyConstellation",
        "FarmingRecipe", "GrainMillRecipe",
    ):
        recipe_union |= out.get(rk, set())
    out["AnyRecipe"] = recipe_union
    return out


def search_knowledge_data(query: str, kind: str | None = None, limit: int = 25) -> dict:
    """Search offline items, quests, shop purchases, enemies, and recipes."""
    q = query.strip().lower()
    if not q:
        return {"error": "empty query"}
    results: dict[str, list[dict]] = {}

    if kind in (None, "item", "items"):
        hits = []
        for it in load_items_index():
            if (
                q in it["id"].lower()
                or q in it["name"].lower()
                or q in (it.get("name_zh") or "").lower()
            ):
                hits.append(it)
                if len(hits) >= limit:
                    break
        if hits:
            results["items"] = hits

    if kind in (None, "quest", "quests"):
        hits = []
        for qr in load_quests_index():
            hay = " ".join([
                qr["id"], qr["name"], qr.get("name_zh") or "",
                qr.get("category") or "", qr.get("rewards") or "",
            ]).lower()
            if q in hay:
                hits.append({
                    "id": qr["id"],
                    "name": qr["name"],
                    "name_zh": qr.get("name_zh"),
                    "category": qr.get("category"),
                    "prereq_quests": qr.get("prereq_quests"),
                    "rewards": qr.get("rewards"),
                    "step_count": len(qr.get("steps") or []),
                })
                if len(hits) >= limit:
                    break
        if hits:
            results["quests"] = hits

    p = DATA_DIR / "MelvorBase.json"
    if p.is_file() and kind in (None, "shop", "enemy", "combat", "recipe"):
        raw = json.loads(p.read_text(encoding="utf-8"))
        objs = raw.get("Objects", {})
        if kind in (None, "shop"):
            hits = []
            for sp in objs.get("ShopPurchase", []):
                sid = _norm_id(sp["Id"])
                nm = sp.get("DisplayName") or ""
                if q in sid.lower() or q in str(nm).lower():
                    hits.append({
                        "id": sid,
                        "name": nm,
                        "subcategory": sp.get("Subcategory"),
                        "costs": sp.get("Costs"),
                        "buy_limit": sp.get("DefaultBuyLimit"),
                    })
                    if len(hits) >= limit:
                        break
            if hits:
                results["shop"] = hits

        if kind in (None, "enemy", "combat"):
            hits = []
            for en in objs.get("Enemy", []):
                eid = _norm_id(en["Id"])
                nm = en.get("DisplayName") or ""
                if q in eid.lower() or q in str(nm).lower():
                    lvls = {x["Key"].split(":")[-1]: x["Value"] for x in (en.get("Levels") or [])}
                    hits.append({"id": eid, "name": nm, "levels": lvls})
                    if len(hits) >= limit:
                        break
            if hits:
                results["enemies"] = hits

    return {"query": query, "results": results}


def validate_plan_dict(plan: dict[str, Any]) -> dict[str, Any]:
    """Statically validate an Action Plan against MelvorBase.json schemas."""
    errors: list[str] = []
    warnings: list[str] = []

    if not isinstance(plan, dict):
        return {"valid": False, "errors": ["plan must be a JSON object"], "warnings": []}

    steps = plan.get("steps")
    if not isinstance(steps, list) or len(steps) == 0:
        return {"valid": False, "errors": ["plan.steps must be a non-empty list"], "warnings": []}

    ids = load_id_sets()
    items_set = ids.get("Item", set())
    entities_set = ids.get("Entity", set())
    recipes_set = ids.get("AnyRecipe", set())
    quests_set = ids.get("Quest", set())
    shop_set = ids.get("ShopPurchase", set())
    enemies_set = ids.get("Enemy", set())
    areas_set = ids.get("CombatArea", set())
    dungeons_set = ids.get("Dungeon", set())

    for idx, step in enumerate(steps):
        prefix = f"step[{idx}]"
        if not isinstance(step, dict):
            errors.append(f"{prefix}: must be an object")
            continue
        op = str(step.get("op") or "").strip().lower()
        if not op:
            errors.append(f"{prefix}: missing 'op'")
            continue
        if op not in KNOWN_OPS:
            errors.append(f"{prefix}: unknown op {op!r}")
            continue

        params = step.get("params") or {}
        if not isinstance(params, dict):
            errors.append(f"{prefix}: 'params' must be an object")
            continue

        # Op-specific validation
        if op == "start_action":
            ent = _norm_id(params.get("entity"))
            if not ent:
                errors.append(f"{prefix}: start_action requires params.entity")
            elif entities_set and ent not in entities_set:
                errors.append(f"{prefix}: unknown entity {ent!r}")
            rec = _norm_id(params.get("recipe"))
            if rec and recipes_set and rec not in recipes_set:
                warnings.append(f"{prefix}: recipe {rec!r} not found in standard recipe registries (double-check ID)")
            if ent == "MelvorBase:Thieving" and not step.get("safety"):
                warnings.append(f"{prefix}: Thieving step has no safety.min_food_qty guard")

        elif op == "combat_fight":
            area = _norm_id(params.get("area"))
            enemy = _norm_id(params.get("enemy"))
            if not area or not enemy:
                errors.append(f"{prefix}: combat_fight requires params.area and params.enemy")
            else:
                if areas_set and area not in areas_set:
                    errors.append(f"{prefix}: unknown CombatArea {area!r}")
                if enemies_set and enemy not in enemies_set:
                    errors.append(f"{prefix}: unknown Enemy {enemy!r}")
            if not step.get("safety"):
                warnings.append(f"{prefix}: combat_fight has no safety.min_food_qty guard")

        elif op == "combat_dungeon":
            dung = _norm_id(params.get("dungeon"))
            if not dung:
                errors.append(f"{prefix}: combat_dungeon requires params.dungeon")
            elif dungeons_set and dung not in dungeons_set:
                errors.append(f"{prefix}: unknown Dungeon {dung!r}")
            if not step.get("safety"):
                warnings.append(f"{prefix}: combat_dungeon has no safety.min_food_qty guard")

        elif op == "shop_buy":
            sp = _norm_id(params.get("purchase") or params.get("id"))
            if not sp:
                errors.append(f"{prefix}: shop_buy requires params.purchase")
            elif shop_set and sp not in shop_set:
                errors.append(f"{prefix}: unknown ShopPurchase {sp!r}")

        elif op in ("bank_sell", "bank_open", "bank_bury", "equip", "equip_tool", "select_food"):
            it = _norm_id(params.get("item"))
            if op != "select_food" and not it:
                errors.append(f"{prefix}: {op} requires params.item")
            elif it and items_set and it not in items_set:
                errors.append(f"{prefix}: unknown Item {it!r}")

        elif op in ("quest_start", "quest_submit", "quest_confirm", "quest_claim"):
            qid = _norm_id(params.get("quest"))
            if not qid:
                errors.append(f"{prefix}: {op} requires params.quest")
            elif quests_set and qid not in quests_set:
                errors.append(f"{prefix}: unknown Quest {qid!r}")

        # Validate `until` on continuous steps
        until = step.get("until")
        if op in CONTINUOUS_OPS:
            if not until and idx < len(steps) - 1:
                warnings.append(
                    f"{prefix} ({op}): no 'until' condition; step will execute as fire-and-forget and immediately move to step[{idx+1}]!"
                )
            elif isinstance(until, dict):
                iq = until.get("item_qty")
                if isinstance(iq, dict) and iq.get("item") and items_set and _norm_id(iq["item"]) not in items_set:
                    errors.append(f"{prefix}: until.item_qty unknown item {iq['item']!r}")
                ig = until.get("item_gained")
                if isinstance(ig, dict) and ig.get("item") and items_set and _norm_id(ig["item"]) not in items_set:
                    errors.append(f"{prefix}: until.item_gained unknown item {ig['item']!r}")
                qs = until.get("quest_step_done")
                if isinstance(qs, dict) and qs.get("quest") and quests_set and _norm_id(qs["quest"]) not in quests_set:
                    errors.append(f"{prefix}: until.quest_step_done unknown quest {qs['quest']!r}")

    return {
        "valid": len(errors) == 0,
        "step_count": len(steps),
        "errors": errors,
        "warnings": warnings,
    }
