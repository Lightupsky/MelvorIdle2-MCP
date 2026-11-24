#!/usr/bin/env python3
"""Generate the comprehensive Melvor Idle 2 knowledge base completely OFFLINE.

Input : data/MelvorBase.json + data/zh_CN.po (extracted from Melvor2.exe via
        extract_offline.py — no need to launch the game!)
Output:
  - README.md
  - quests.md                  (All 37 quests: Act 1/2, MainQuests, SideQuests,
                                prerequisites, steps, tasks, goals, hand-in costs, rewards)
  - interlinks.md              (Cross-skill + combat drops + quest demands item graph)
  - skills/*.md                (All skills, Combat areas/dungeons/enemies/spells/prayers/slayer,
                                Shop with exact prices & buy limits, Upgrades/Toolbelt/Pets)
  - data/items_index.json      (Fast lookup index for MCP search_knowledge & plan validation)
  - data/quests_index.json     (Structured quest guide for MCP & planning)

Run:
    python extract_offline.py && python generate.py
"""

from __future__ import annotations

import json
import pathlib
import re
import sys
from collections import defaultdict

HERE = pathlib.Path(__file__).resolve().parent
DATA_DIR = HERE / "data"
BASE_JSON = DATA_DIR / "MelvorBase.json"
ZH_PO = DATA_DIR / "zh_CN.po"
META_JSON = DATA_DIR / "extract_meta.json"


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def short_id(oid: str | None) -> str:
    if not oid:
        return ""
    return oid.split(":", 1)[1] if ":" in oid else oid


def full_id(oid: str | None, ns: str = "MelvorBase") -> str:
    if not oid:
        return ""
    return oid if ":" in oid else f"{ns}:{oid}"


def display_name(obj: dict | None) -> str:
    if not obj:
        return ""
    dn = obj.get("DisplayName")
    if isinstance(dn, dict):
        return dn.get("Singular") or dn.get("Plural") or obj.get("Id", "")
    if isinstance(dn, str) and dn:
        return dn
    return obj.get("Id", "")


def parse_po(path: pathlib.Path) -> dict[str, str]:
    """Minimal GNU gettext .po parser mapping English msgid -> translated msgstr."""
    if not path.is_file():
        return {}
    mapping: dict[str, str] = {}
    msgid_parts: list[str] | None = None
    msgstr_parts: list[str] | None = None
    mode = None

    def flush():
        nonlocal msgid_parts, msgstr_parts
        if msgid_parts is not None and msgstr_parts is not None:
            k = "".join(msgid_parts)
            v = "".join(msgstr_parts)
            if k and v:
                mapping[k] = v
        msgid_parts = None
        msgstr_parts = None

    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            if not line:
                flush()
                mode = None
            continue
        if line.startswith("msgid "):
            flush()
            mode = "id"
            msgid_parts = [json.loads(line[6:])]
            msgstr_parts = None
        elif line.startswith("msgid_plural "):
            mode = "ignore"
        elif line.startswith("msgstr "):
            mode = "str"
            msgstr_parts = [json.loads(line[7:])]
        elif line.startswith("msgstr[0] "):
            mode = "str"
            msgstr_parts = [json.loads(line[10:])]
        elif line.startswith('"') and mode in ("id", "str"):
            s = json.loads(line)
            if mode == "id" and msgid_parts is not None:
                msgid_parts.append(s)
            elif mode == "str" and msgstr_parts is not None:
                msgstr_parts.append(s)
    flush()
    return mapping


def strip_bbcode(s: str | None) -> str:
    if not s:
        return ""
    return re.sub(r"\[/?[a-zA-Z0-9_=#]+\]", "", s).replace("\n", " ").strip()


def md_escape(s: str) -> str:
    return s.replace("|", "\\|").replace("\n", " ").strip()


class GameDB:
    def __init__(self, data: dict, zh_map: dict[str, str]):
        self.raw = data
        self.ns = data.get("Namespace", "MelvorBase")
        self.objs = data["Objects"]
        self.ec = data["EntityConfig"]
        self.zh = zh_map

        # Build fast ID -> object lookup per registry
        self.by_type: dict[str, dict[str, dict]] = {}
        for rtype, arr in self.objs.items():
            if not isinstance(arr, list):
                continue
            tbl: dict[str, dict] = {}
            for o in arr:
                if isinstance(o, dict) and "Id" in o:
                    fid = full_id(o["Id"], self.ns)
                    tbl[fid] = o
                    tbl[o["Id"]] = o
            self.by_type[rtype] = tbl

        self.items = self.by_type.get("Item", {})
        self.currencies = self.by_type.get("Currency", {})
        self.enemies = self.by_type.get("Enemy", {})
        self.quests = self.by_type.get("Quest", {})
        self.shop_purchases = self.by_type.get("ShopPurchase", {})

    def zh_of(self, en: str) -> str:
        return self.zh.get(en, "")

    def label(self, en: str, include_zh: bool = False) -> str:
        if include_zh and en in self.zh and self.zh[en] != en:
            return f"{en} ({self.zh[en]})"
        return en

    def item_name(self, iid: str | None) -> str:
        if not iid:
            return ""
        obj = self.items.get(iid)
        return display_name(obj) if obj else short_id(iid)

    def item_sell(self, iid: str | None) -> int:
        obj = self.items.get(iid or "")
        if not obj:
            return 0
        sf = obj.get("SellsFor") or {}
        return int(sf.get("Quantity") or 0)

    def currency_name(self, cid: str | None) -> str:
        if not cid:
            return ""
        obj = self.currencies.get(cid)
        return display_name(obj) if obj else short_id(cid)

    def enemy_name(self, eid: str | None) -> str:
        if not eid:
            return ""
        obj = self.enemies.get(eid)
        return display_name(obj) if obj else short_id(eid)

    def quest_name(self, qid: str | None) -> str:
        if not qid:
            return ""
        obj = self.quests.get(qid)
        return display_name(obj) if obj else short_id(qid)

    def fmt_req(self, req: dict) -> str:
        rtype = short_id(req.get("Type"))
        d = req.get("Data") or {}
        if rtype == "Level":
            ent = short_id((d.get("Component") or {}).get("Entity"))
            return f"{ent} Lv.{d.get('Level')}"
        if rtype == "QuestComplete":
            qid = d.get("Quest")
            return f"Quest:{self.quest_name(qid)}({short_id(qid)})"
        if rtype == "ShopPurchase":
            sp = d.get("ShopPurchase")
            sp_obj = self.shop_purchases.get(sp or "")
            nm = display_name(sp_obj) if sp_obj else short_id(sp)
            amt = d.get("Amount")
            return f"Shop:{nm}" + (f"×{amt}" if amt and amt > 1 else "")
        if rtype == "EnemyStatistic":
            en = self.enemy_name(d.get("Enemy"))
            return f"Kill {d.get('Value')}× {en}"
        if rtype == "DungeonStatistic":
            return f"Clear {d.get('Value')}× {short_id(d.get('Dungeon'))}"
        if rtype == "ItemFound":
            return f"Found:{self.item_name(d.get('Item'))}"
        if rtype == "ItemOwned":
            return f"Own {d.get('Quantity', 1)}× {self.item_name(d.get('Item'))}"
        if rtype == "AgilityTechniqueLevel":
            return f"AgilityTech:{short_id(d.get('Technique'))} Lv.{d.get('Level')}"
        if rtype == "AstrologyMajorNodeUnlockCount":
            return f"AstrologyMajorNodes≥{d.get('Count') or d.get('Value')}"
        if rtype == "SlayerMasterStatistic":
            return f"Slayer({short_id(d.get('Master'))})≥{d.get('Value')}"
        if rtype == "Completion":
            return f"Completion:{d}"
        # Fallback compact format
        compact = {k: short_id(v) if isinstance(v, str) else v for k, v in d.items() if k not in ("IsOr", "Component")}
        return f"{rtype}({compact})" if compact else rtype

    def req_level(self, reqs: list | None, skill_hint: str | None = None) -> int | None:
        if not reqs:
            return None
        fallback = None
        for r in reqs:
            if short_id(r.get("Type")) == "Level":
                d = r.get("Data") or {}
                ent = short_id((d.get("Component") or {}).get("Entity"))
                lvl = d.get("Level")
                if lvl is None:
                    continue
                if skill_hint and ent.lower() == skill_hint.lower():
                    return int(lvl)
                if fallback is None:
                    fallback = int(lvl)
        return fallback

    def fmt_reqs(self, reqs: list | None, skip_skill_level: str | None = None) -> str:
        if not reqs:
            return ""
        parts = []
        for r in reqs:
            if skip_skill_level and short_id(r.get("Type")) == "Level":
                ent = short_id(((r.get("Data") or {}).get("Component") or {}).get("Entity"))
                if ent.lower() == skip_skill_level.lower():
                    continue
            parts.append(self.fmt_req(r))
        return ", ".join(parts)

    def fmt_costs(self, costs: dict | None) -> str:
        if not costs:
            return ""
        parts = []
        for c in costs.get("Currencies") or []:
            qty = c.get("Quantity", 0)
            parts.append(f"{qty:,} {self.currency_name(c.get('Currency'))}")
        for c in costs.get("Currency") or []:
            qty = c.get("Quantity", 0)
            parts.append(f"{qty:,} {self.currency_name(c.get('Currency'))}")
        for it in costs.get("Items") or []:
            qty = it.get("Quantity", 1)
            nm = self.item_name(it.get("Item"))
            parts.append(f"{qty}× {nm}" if qty != 1 else nm)
        return ", ".join(parts)

    def fmt_item_list(self, items: list | None) -> str:
        if not items:
            return ""
        parts = []
        for it in items:
            if not isinstance(it, dict):
                continue
            iid = it.get("Item")
            nm = self.item_name(iid)
            qty = it.get("Quantity")
            mn, mx = it.get("MinQuantity"), it.get("MaxQuantity")
            chance = it.get("Chance")
            s = nm
            if qty is not None and qty != 1:
                s = f"{qty}× {s}"
            elif mn is not None and mx is not None and (mn != 1 or mx != 1):
                s = f"{mn}-{mx}× {s}"
            if chance is not None and chance < 1:
                pct = round(float(chance) * 100, 2)
                s = f"{s} ({pct:g}%)"
            parts.append(s)
        return ", ".join(parts)

    def fmt_goal(self, g: dict) -> str:
        gtype = short_id(g.get("Type"))
        d = g.get("Data") or {}
        mx = d.get("MaxProgress", 1)
        if gtype == "CombatKillEnemy":
            style = short_id(d.get("PlayerCombatStyle"))
            extra = f" [{style}]" if style else ""
            return f"击杀 {mx}× {self.enemy_name(d.get('Enemy'))}{extra}"
        if gtype == "CompleteDungeon":
            return f"通关 {mx}× {short_id(d.get('Dungeon'))}"
        if gtype == "ActionGainItem":
            ent = short_id((d.get("Component") or {}).get("Entity"))
            return f"通过 {ent} 获取 {mx}× {self.item_name(d.get('Item'))}"
        if gtype == "ArtisanCreateRecipe":
            ent = short_id((d.get("Component") or {}).get("Entity"))
            return f"{ent} 制作 {mx}× {short_id(d.get('Recipe'))}"
        if gtype == "MiningMineRock":
            return f"采矿 {mx}× {short_id(d.get('Recipe') or d.get('Rock'))}"
        if gtype == "WoodcuttingCutTree":
            return f"伐木 {mx}× {short_id(d.get('Recipe') or d.get('Tree'))}"
        if gtype == "FiremakingBurnRecipe":
            return f"生火燃烧 {mx}× {short_id(d.get('Recipe'))}"
        if gtype == "FarmingPlantRecipe":
            return f"农耕种植 {mx}× {short_id(d.get('Recipe'))}"
        if gtype == "HuntingTrapAnimal":
            return f"狩猎捕获 {mx}× {short_id(d.get('Animal') or d.get('Recipe'))}"
        if gtype == "ThievingPickpocketTarget":
            target = short_id(d.get("Recipe") or d.get("Target") or d.get("Location") or "目标")
            return f"窃取 {mx}× {target}"
        if gtype == "AgilityCompleteCourse":
            return f"敏捷完成 {mx}× {short_id(d.get('Course'))}"
        if gtype == "AstrologyObserveEvent":
            return f"占星观测 {mx}× {short_id(d.get('Constellation') or d.get('Event') or '事件')}"
        return f"{gtype} (×{mx}) { {k: short_id(v) if isinstance(v, str) else v for k, v in d.items() if k not in ('Id', 'MaxProgress', 'Component', 'Conditions')} }"


# ---------------------------------------------------------------------------
# Generators
# ---------------------------------------------------------------------------

def generate_quests(db: GameDB) -> tuple[str, list[dict]]:
    cats = {full_id(c["Id"]): display_name(c) for c in db.objs.get("QuestCategory", [])}
    cat_order = ["MelvorBase:Act1", "MelvorBase:Act2", "MelvorBase:Act3", "MelvorBase:Act4", "MelvorBase:MainQuests", "MelvorBase:SideQuests", "MelvorBase:OtherQuests"]

    grouped: dict[str, list[dict]] = defaultdict(list)
    for q in db.objs.get("Quest", []):
        grouped[q.get("Category", "MelvorBase:OtherQuests")].append(q)

    lines = [
        "# Melvor Idle 2 任务系统（Quests）全览",
        "",
        f"> 离线提取自 `MelvorBase.json`，共 **{len(db.objs.get('Quest', []))}** 个任务（含 Act 1 / Act 2 主线、后续主线占位、支线任务）。",
        "> 主线任务按 Act 1 (`MQ1`→`MQ8`) → Act 2 (`MQ9`→`MQ17`) 串行推进；支线任务 (`SideQuests`) 多无前置任务，可与技能练级并行推进。",
        "",
    ]

    index_list: list[dict] = []

    for cid in cat_order:
        qs = grouped.get(cid, [])
        if not qs:
            continue
        cname = cats.get(cid, short_id(cid))
        lines.append(f"## {cname}（{len(qs)} 个）")
        lines.append("")

        for q in qs:
            qid = full_id(q["Id"])
            name = display_name(q)
            zh = db.zh_of(name)
            title = f"{name} / {zh}" if zh and zh != name else name
            reqs = db.fmt_reqs(q.get("Requirements"))
            vis_reqs = []
            for vr_key in ("VisualOnlyRequiredRequirements", "VisualOnlyRecommendedRequirements"):
                vr = q.get(vr_key) or {}
                for cc in vr.get("CustomConditions") or []:
                    vis_reqs.append(strip_bbcode(cc))
                for c in vr.get("Conditions") or []:
                    vis_reqs.append(db.fmt_req(c))
            rewards_str = db.fmt_costs(q.get("Rewards"))
            custom_rew = (q.get("Rewards") or {}).get("CustomRewards")
            if custom_rew:
                if isinstance(custom_rew, str):
                    cr_txt = strip_bbcode(custom_rew)
                else:
                    cr_txt = ", ".join(strip_bbcode(str(x)) for x in custom_rew)
                rewards_str = f"{rewards_str}; {cr_txt}" if rewards_str else cr_txt

            lines.append(f"### `{qid}` — {title}")
            if reqs:
                lines.append(f"- **前置条件**：{reqs}")
            if vis_reqs:
                lines.append(f"- **提示/推荐要求**：{'; '.join(vis_reqs)}")
            if rewards_str:
                lines.append(f"- **奖励**：{rewards_str}")

            step_records = []
            for idx, step in enumerate(q.get("Steps") or []):
                s_desc = strip_bbcode(step.get("Description"))
                tasks_out = []
                for t in step.get("TasksInOrder") or []:
                    t_parts = []
                    for g in t.get("Goals") or []:
                        t_parts.append(db.fmt_goal(g))
                    for cg in t.get("ConditionGoals") or []:
                        t_parts.append("条件:" + db.fmt_req(cg))
                    cost_s = db.fmt_costs(t.get("Costs"))
                    if cost_s:
                        t_parts.append(f"提交材料/费用: {cost_s}")
                    tasks_out.append(" | ".join(t_parts) if t_parts else "对话/自动完成")
                lines.append(f"  - **Step {idx}** (`{step.get('Id')}`): {' → '.join(tasks_out)}" + (f" *(说明: {s_desc})*" if s_desc else ""))
                step_records.append({
                    "id": step.get("Id"),
                    "description": s_desc,
                    "tasks": [
                        {
                            "id": t.get("Id"),
                            "goals": [db.fmt_goal(g) for g in (t.get("Goals") or [])],
                            "condition_goals": [db.fmt_req(cg) for cg in (t.get("ConditionGoals") or [])],
                            "costs_items": [{"item": it.get("Item"), "name": db.item_name(it.get("Item")), "qty": it.get("Quantity", 1)} for it in ((t.get("Costs") or {}).get("Items") or [])],
                            "costs_currencies": [{"currency": c.get("Currency"), "qty": c.get("Quantity", 0)} for c in ((t.get("Costs") or {}).get("Currencies") or [])],
                        }
                        for t in (step.get("TasksInOrder") or [])
                    ],
                })
            lines.append("")

            index_list.append({
                "id": qid,
                "name": name,
                "name_zh": zh,
                "category": cid,
                "requirements": [db.fmt_req(r) for r in (q.get("Requirements") or [])],
                "prereq_quests": [
                    (r.get("Data") or {}).get("Quest")
                    for r in (q.get("Requirements") or [])
                    if short_id(r.get("Type")) == "QuestComplete"
                ],
                "rewards": rewards_str,
                "steps": step_records,
            })

    return "\n".join(lines), index_list


def generate_artisan_skill(db: GameDB, entity_id: str, skill_name: str, registry_key: str) -> str:
    cfg = db.ec.get(entity_id, {})
    recipe_refs = [r["Id"] if isinstance(r, dict) else r for r in cfg.get("Recipes", [])]
    reg = db.by_type.get(registry_key, {})
    recipes = [reg[rid] for rid in recipe_refs if rid in reg]

    lines = [
        f"# {skill_name}",
        "",
        f"> 实体 ID：`{entity_id}` | 组件：`Action` | 配方数：{len(recipes)}",
        "",
    ]

    groups: dict[str, list[dict]] = defaultdict(list)
    for r in recipes:
        key = r.get("Subcategory") or r.get("Category") or "Other"
        groups[key].append(r)

    for key in sorted(groups):
        rows = groups[key]
        rows.sort(key=lambda x: (db.req_level(x.get("Requirements"), skill_name) or 999, display_name(x)))
        lines.append(f"## {short_id(key)}")
        lines.append("")
        lines.append("| ID | 名称 | 等级 | 经验 | 间隔(s) | 步骤 | 材料 | 产物 | 需求 |")
        lines.append("|---|---|---|---|---|---|---|---|---|")
        for r in rows:
            rid = full_id(r["Id"])
            nm = display_name(r)
            lvl = db.req_level(r.get("Requirements"), skill_name)
            xp = r.get("BaseExperience", "")
            iv = r.get("BaseInterval")
            if iv is None and r.get("BaseBrewingInterval"):
                iv = (r.get("BaseBrewingInterval") or 0) + (r.get("BaseStirringInterval") or 0) + (r.get("BaseBottlingInterval") or 0)
            iv_s = round(iv / 1000, 2) if iv else ""
            steps = r.get("Steps") or ""
            ing = db.fmt_costs(r.get("Ingredients"))
            prods = db.fmt_item_list(r.get("Products") or ([r["Product"]] if r.get("Product") else []))
            reqs = db.fmt_reqs(r.get("Requirements"), skip_skill_level=skill_name)
            lines.append(f"| `{rid}` | {md_escape(nm)} | {lvl or ''} | {xp} | {iv_s} | {steps} | {md_escape(ing)} | {md_escape(prods)} | {md_escape(reqs)} |")
        lines.append("")

    # Firemaking bonfires
    if entity_id == "MelvorBase:Firemaking" and db.objs.get("FiremakingBonfire"):
        lines.append("## 篝火（Bonfires）")
        lines.append("")
        lines.append("| ID | 名称 | 需求 | 成本 |")
        lines.append("|---|---|---|---|")
        for bf in db.objs["FiremakingBonfire"]:
            lines.append(f"| `{full_id(bf['Id'])}` | {md_escape(display_name(bf))} | {md_escape(db.fmt_reqs(bf.get('Requirements')))} | {md_escape(db.fmt_costs(bf.get('PurchaseCosts') or bf.get('Costs')))} |")
        lines.append("")

    return "\n".join(lines)


def generate_mining(db: GameDB) -> str:
    recs = list(db.objs.get("MiningRecipe", []))
    recs.sort(key=lambda r: (db.req_level(r.get("Requirements"), "Mining") or 999, display_name(r)))
    lines = [
        "# Mining（采矿）",
        "",
        f"> 实体 ID：`MelvorBase:Mining` | 组件：`Action` | 矿点：{len(recs)}",
        "> **重要机制**：采矿每次挥镐耗时 `BaseInterval`，但**只有 `BaseProductChance` 的概率敲出矿石**（期望单矿耗时 ≈ `interval / chance`，实际受工具与全局加成提升）。",
        "",
        "| ID | 名称 | 分类 | 等级 | 经验 | 挥镐间隔(s) | 破岩率 | 期望单矿(s) | 产物 | 额外/稀有掉落 |",
        "|---|---|---|---|---|---|---|---|---|---|",
    ]
    for r in recs:
        rid = full_id(r["Id"])
        lvl = db.req_level(r.get("Requirements"), "Mining") or ""
        iv_s = round((r.get("BaseInterval") or 0) / 1000, 2)
        ch = float(r.get("BaseProductChance") or 1.0)
        exp_s = round(iv_s / ch, 1) if ch > 0 else ""
        prod = db.fmt_item_list([r["Product"]] if r.get("Product") else [])
        rare = db.fmt_item_list(r.get("RandomProductsPerHit"))
        lines.append(f"| `{rid}` | {md_escape(display_name(r))} | {short_id(r.get('Category'))} | {lvl} | {r.get('BaseExperience','')} | {iv_s} | {round(ch*100,1)}% | {exp_s} | {md_escape(prod)} | {md_escape(rare)} |")
    lines.append("")
    return "\n".join(lines)


def generate_woodcutting(db: GameDB) -> str:
    recs = list(db.objs.get("WoodcuttingRecipe", []))
    recs.sort(key=lambda r: (db.req_level(r.get("Requirements"), "Woodcutting") or r.get("Level") or 999, display_name(r)))
    lines = [
        "# Woodcutting（伐木）",
        "",
        f"> 实体 ID：`MelvorBase:Woodcutting` | 组件：`Action` | 树木：{len(recs)}",
        "> 树木拥有 `BaseMaxHealth`（需挥斧次数）；每次挥斧耗时 `BaseInterval`。",
        "",
        "| ID | 名称 | 分类 | 等级 | 经验 | 挥斧(s) | 血量 | 产物 | 每次挥斧/砍倒掉落 |",
        "|---|---|---|---|---|---|---|---|---|",
    ]
    for r in recs:
        rid = full_id(r["Id"])
        lvl = db.req_level(r.get("Requirements"), "Woodcutting") or r.get("Level") or ""
        iv_s = round((r.get("BaseInterval") or 0) / 1000, 2)
        hp = r.get("BaseMaxHealth", "")
        prod = db.fmt_item_list([r["Product"]] if r.get("Product") else [])
        rares = db.fmt_item_list((r.get("RandomProductsPerHit") or []) + (r.get("RandomProductsPerCut") or []))
        lines.append(f"| `{rid}` | {md_escape(display_name(r))} | {short_id(r.get('Category'))} | {lvl} | {r.get('BaseExperience','')} | {iv_s} | {hp} | {md_escape(prod)} | {md_escape(rares)} |")
    lines.append("")
    return "\n".join(lines)


def generate_fishing(db: GameDB) -> str:
    recs = list(db.objs.get("FishingRecipe", []))
    recs.sort(key=lambda r: (db.req_level(r.get("Requirements"), "Fishing") or 999, display_name(r)))
    lines = [
        "# Fishing（钓鱼）",
        "",
        f"> 实体 ID：`MelvorBase:Fishing` | 组件：`Action` | 钓点：{len(recs)}",
        "",
        "| ID | 名称 | 区域 | 等级 | 经验 | 间隔(s) | 鱼率 | 鱼获池 | 特殊掉落 |",
        "|---|---|---|---|---|---|---|---|---|",
    ]
    for r in recs:
        rid = full_id(r["Id"])
        lvl = db.req_level(r.get("Requirements"), "Fishing") or ""
        mn = round((r.get("BaseMinInterval") or 0) / 1000, 1)
        mx = round((r.get("BaseMaxInterval") or 0) / 1000, 1)
        fc = round(float(r.get("FishChance") or 0) * 100)
        fish = ", ".join(f"{db.item_name(p.get('Item'))}(w={p.get('Weight',1)})" for p in (r.get("RandomProducts") or []))
        spec = db.fmt_item_list(r.get("SpecialProducts"))
        lines.append(f"| `{rid}` | {md_escape(display_name(r))} | {short_id(r.get('Location'))} | {lvl} | {r.get('BaseExperience','')} | {mn}-{mx} | {fc}% | {md_escape(fish)} | {md_escape(spec)} |")
    lines.append("")
    return "\n".join(lines)


def generate_hunting(db: GameDB) -> str:
    recs = list(db.objs.get("HuntingAnimal", []))
    recs.sort(key=lambda r: (db.req_level(r.get("Requirements"), "Hunting") or 999, display_name(r)))
    lines = [
        "# Hunting（狩猎）",
        "",
        f"> 实体 ID：`MelvorBase:Hunting` | 组件：`Action` | 猎物：{len(recs)}",
        "",
        "| ID | 名称 | 区域 | 等级 | 经验 | 工具 / 陷阱 / 诱饵 | 追踪/捕陷阱率 | 产物 |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for r in recs:
        rid = full_id(r["Id"])
        lvl = db.req_level(r.get("Requirements"), "Hunting") or ""
        gear = f"{short_id(r.get('Tool'))} / {short_id(r.get('TrapType'))} / {db.item_name(r.get('PreferredBait'))}"
        ch = f"{round(float(r.get('BaseTrackSuccessChance') or 0)*100)}% / {round(float(r.get('BaseTrapSuccessChance') or 0)*100)}%"
        prods = db.fmt_item_list(r.get("Products"))
        lines.append(f"| `{rid}` | {md_escape(display_name(r))} | {short_id(r.get('Location'))} | {lvl} | {r.get('BaseExperience','')} | {md_escape(gear)} | {ch} | {md_escape(prods)} |")
    lines.append("")
    return "\n".join(lines)


def generate_thieving(db: GameDB) -> str:
    recs = list(db.objs.get("ThievingRecipe", []))
    recs.sort(key=lambda r: (db.req_level(r.get("Requirements"), "Thieving") or 999, display_name(r)))
    lines = [
        "# Thieving（偷窃）",
        "",
        f"> 实体 ID：`MelvorBase:Thieving` | 组件：`Action` | 目标：{len(recs)}",
        "",
        "| ID | 名称 | 地点 | 等级 | 经验 | 间隔(s) | 阶段 | 警觉/伤害 | 金币 | 战利品 |",
        "|---|---|---|---|---|---|---|---|---|---|",
    ]
    for r in recs:
        rid = full_id(r["Id"])
        lvl = db.req_level(r.get("Requirements"), "Thieving") or ""
        iv_s = round((r.get("BaseInterval") or 0) / 1000, 2)
        cr = r.get("CurrencyReward") or {}
        gp = f"{cr.get('MinQuantity',0)}-{cr.get('MaxQuantity',0)}" if cr else ""
        loot_parts = []
        for lk in ("CommonLoot", "RareLoot", "UniqueLoot"):
            tbl = r.get(lk) or []
            if isinstance(tbl, list) and tbl:
                names = [db.item_name(x.get("Item")) for x in tbl[:4] if isinstance(x, dict)]
                if names:
                    loot_parts.append(f"{lk[:1]}:{','.join(names)}" + ("…" if len(tbl) > 4 else ""))
        lines.append(f"| `{rid}` | {md_escape(display_name(r))} | {short_id(r.get('Category'))} | {lvl} | {r.get('BaseExperience','')} | {iv_s} | {r.get('Stages','')} | {r.get('Perception','')}/{r.get('Damage','')} | {gp} | {md_escape(' '.join(loot_parts))} |")
    lines.append("")
    return "\n".join(lines)


def generate_farming(db: GameDB) -> str:
    recs = list(db.objs.get("FarmingRecipe", []))
    recs.sort(key=lambda r: (db.req_level(r.get("Requirements"), "Farming") or 999, display_name(r)))
    lines = [
        "# Farming（农耕，被动技能）",
        "",
        f"> 实体 ID：`MelvorBase:Farming` | 作物：{len(recs)} | 地块：{len(db.objs.get('FarmingPlot', []))} | 磨坊配方：{len(db.objs.get('GrainMillRecipe', []))}",
        "",
        "## 作物",
        "",
        "| ID | 名称 | 分类 | 等级 | 经验 | 生长(min) | 复收 | 种子 | 产物 |",
        "|---|---|---|---|---|---|---|---|---|",
    ]
    for r in recs:
        rid = full_id(r["Id"])
        lvl = db.req_level(r.get("Requirements"), "Farming") or ""
        mins = round((r.get("BaseInterval") or 0) / 60000, 1)
        ing = db.fmt_costs(r.get("Ingredients"))
        prod = db.fmt_item_list([r["Product"]] if r.get("Product") else [])
        lines.append(f"| `{rid}` | {md_escape(display_name(r))} | {short_id(r.get('Category'))} | {lvl} | {r.get('BaseExperience','')} | {mins} | {r.get('Regrows',0)} | {md_escape(ing)} | {md_escape(prod)} |")
    lines.append("")

    if db.objs.get("GrainMillRecipe"):
        lines.append("## 磨坊（Grain Mill）")
        lines.append("")
        lines.append("| ID | 名称 | 间隔(s) | 材料 | 产物 |")
        lines.append("|---|---|---|---|---|")
        for g in db.objs["GrainMillRecipe"]:
            iv = round((g.get("BaseInterval") or 0) / 1000, 1)
            lines.append(f"| `{full_id(g['Id'])}` | {md_escape(display_name(g))} | {iv} | {md_escape(db.fmt_costs(g.get('Ingredients')))} | {md_escape(db.fmt_item_list(g.get('Products') or ([g['Product']] if g.get('Product') else [])))} |")
        lines.append("")
    return "\n".join(lines)


def generate_ranching(db: GameDB) -> str:
    recs = list(db.objs.get("RanchingAnimal", []))
    recs.sort(key=lambda r: (db.req_level(r.get("Requirements"), "Ranching") or 999, display_name(r)))
    lines = [
        "# Ranching（牧场，被动技能）",
        "",
        f"> 实体 ID：`MelvorBase:Ranching` | 动物：{len(recs)} | 牧地：{len(db.objs.get('RanchingLand', []))} | 围栏：{len(db.objs.get('RanchingEnclosure', []))}",
        "",
        "| ID | 名称 | 等级 | 经验 | 购入费用 | 产出周期(min) | 产物 |",
        "|---|---|---|---|---|---|---|",
    ]
    for r in recs:
        rid = full_id(r["Id"])
        lvl = db.req_level(r.get("Requirements"), "Ranching") or ""
        mins = round((r.get("ProductInterval") or 0) / 60000, 1)
        cost = db.fmt_costs(r.get("PurchaseCosts"))
        prods = db.fmt_item_list(r.get("Products") or ([r["Product"]] if r.get("Product") else []))
        lines.append(f"| `{rid}` | {md_escape(display_name(r))} | {lvl} | {r.get('BaseExperience','')} | {md_escape(cost)} | {mins} | {md_escape(prods)} |")
    lines.append("")
    return "\n".join(lines)


def generate_agility(db: GameDB) -> str:
    courses = list(db.objs.get("AgilityCourse", []))
    courses.sort(key=lambda r: (db.req_level(r.get("Requirements"), "Agility") or 999, display_name(r)))
    lines = [
        "# Agility（敏捷）",
        "",
        f"> 实体 ID：`MelvorBase:Agility` | 赛道：{len(courses)} | 障碍：{len(db.objs.get('AgilityObstacle', []))} | 技巧：{len(db.objs.get('AgilityTechnique', []))}",
        "",
        "## 技巧（Techniques）",
        "",
        ", ".join(f"`{full_id(t['Id'])}` ({display_name(t)})" for t in db.objs.get("AgilityTechnique", [])),
        "",
        "## 赛道（Courses）",
        "",
        "| ID | 名称 | 地点 | 等级 | 路线数 | 需求 | 入场费 |",
        "|---|---|---|---|---|---|---|",
    ]
    for c in courses:
        lvl = db.req_level(c.get("Requirements"), "Agility") or ""
        lines.append(f"| `{full_id(c['Id'])}` | {md_escape(display_name(c))} | {short_id(c.get('Location'))} | {lvl} | {len(c.get('Paths') or [])} | {md_escape(db.fmt_reqs(c.get('Requirements'), 'Agility'))} | {md_escape(db.fmt_costs(c.get('EntryCost')))} |")
    lines.append("")
    return "\n".join(lines)


def generate_astrology(db: GameDB) -> str:
    cons = list(db.objs.get("AstrologyConstellation", []))
    cons.sort(key=lambda r: (db.req_level(r.get("Requirements"), "Astrology") or 999, display_name(r)))
    lines = [
        "# Astrology（占星）",
        "",
        f"> 实体 ID：`MelvorBase:Astrology` | 星座：{len(cons)} | 节点：{len(db.objs.get('AstrologyNode', []))}",
        "",
        "| ID | 名称 | 占星等级 | 关联技能要求 | 经验 | 间隔(s) | 知识 |",
        "|---|---|---|---|---|---|---|",
    ]
    for c in cons:
        lvl = db.req_level(c.get("Requirements"), "Astrology") or ""
        sec = db.fmt_reqs(c.get("SecondaryRequirements"))
        iv = round((c.get("BaseInterval") or 0) / 1000, 2)
        lines.append(f"| `{full_id(c['Id'])}` | {md_escape(display_name(c))} | {lvl} | {md_escape(sec)} | {c.get('BaseExperience','')} | {iv} | {c.get('BaseKnowledge','')} |")
    lines.append("")
    return "\n".join(lines)


def generate_combat(db: GameDB) -> str:
    areas = db.objs.get("CombatArea", [])
    dungeons = db.objs.get("Dungeon", [])
    enemies = db.objs.get("Enemy", [])
    spells = db.objs.get("MagicSpell", [])
    prayers = db.objs.get("Prayer", [])
    slayers = db.objs.get("SlayerMaster", [])

    lines = [
        "# Combat / Magic / Prayer / Slayer（战斗体系）",
        "",
        f"> 实体 ID：`MelvorBase:Combat` | 区域：{len(areas)} | 地下城：{len(dungeons)} | 敌人：{len(enemies)} | 法术：{len(spells)} | 祷告：{len(prayers)} | 屠魔大师：{len(slayers)}",
        "",
        "## 战斗区域（Combat Areas）",
        "",
        "| ID | 名称 | 分类 | 难度 | 敌人列表 | 解锁要求 |",
        "|---|---|---|---|---|---|",
    ]
    for a in areas:
        en_names = ", ".join(f"{db.enemy_name(e)} (`{short_id(e)}`)" for e in (a.get("Enemies") or []))
        lines.append(f"| `{full_id(a['Id'])}` | {md_escape(display_name(a))} | {short_id(a.get('Category'))} | {short_id(a.get('Difficulty'))} | {md_escape(en_names)} | {md_escape(db.fmt_reqs(a.get('UnlockRequirements')))} |")
    lines.append("")

    lines.append("## 地下城（Dungeons）")
    lines.append("")
    lines.append("| ID | 名称 | 难度 | 解锁要求 | 通关奖励 |")
    lines.append("|---|---|---|---|---|")
    for d in dungeons:
        rew = d.get("Rewards") or {}
        fixed = db.fmt_item_list(rew.get("FixedItems"))
        cur = db.fmt_costs({"Currencies": rew.get("FixedCurrencies") or []})
        rew_s = ", ".join(filter(None, [fixed, cur]))
        lines.append(f"| `{full_id(d['Id'])}` | {md_escape(display_name(d))} | {short_id(d.get('Difficulty'))} | {md_escape(db.fmt_reqs(d.get('UnlockRequirements')))} | {md_escape(rew_s)} |")
    lines.append("")

    lines.append("## 敌人与掉落表（Enemies，按 HP 等级排序）")
    lines.append("")
    lines.append("| ID | 名称 | HP/Atk/Str/Def/Rng/Mag | 攻速(s) | 必掉 | 概率掉落表 |")
    lines.append("|---|---|---|---|---|---|")

    def enemy_lvls(e: dict) -> dict[str, int]:
        return {short_id(x.get("Key")): int(x.get("Value", 1)) for x in (e.get("Levels") or [])}

    for e in sorted(enemies, key=lambda x: (enemy_lvls(x).get("Hitpoints", 1), display_name(x))):
        lv = enemy_lvls(e)
        lv_s = f"{lv.get('Hitpoints',1)}/{lv.get('Attack',1)}/{lv.get('Strength',1)}/{lv.get('Defence',1)}/{lv.get('Ranged',1)}/{lv.get('Magic',1)}"
        atk_iv = ""
        for st in ((e.get("Stats") or {}).get("Stats") or []):
            if short_id(st.get("Stat")) == "AttackInterval":
                atk_iv = str(round(float(st.get("Value", 0)) / 1000, 2))
        drops = e.get("Drops") or {}
        fixed = db.fmt_item_list(drops.get("FixedItems"))
        tbls = []
        for t in drops.get("ItemTables") or []:
            ch = round(float(t.get("Chance", 0)) * 100, 1)
            its = ", ".join(db.item_name(x.get("Item")) for x in (t.get("Table") or [])[:5])
            if len(t.get("Table") or []) > 5:
                its += "…"
            tbls.append(f"{ch:g}%:[{its}]")
        lines.append(f"| `{full_id(e['Id'])}` | {md_escape(display_name(e))} | {lv_s} | {atk_iv} | {md_escape(fixed)} | {md_escape('; '.join(tbls))} |")
    lines.append("")

    lines.append("## 法术（Magic Spells）")
    lines.append("")
    lines.append("| ID | 名称 | 法术书 | 元素 | 等级 | 符文消耗 |")
    lines.append("|---|---|---|---|---|---|")
    for sp in sorted(spells, key=lambda x: (db.req_level(x.get("UnlockRequirements"), "Magic") or 999, display_name(x))):
        lvl = db.req_level(sp.get("UnlockRequirements"), "Magic") or ""
        runes = db.fmt_item_list(sp.get("RuneCosts"))
        lines.append(f"| `{full_id(sp['Id'])}` | {md_escape(display_name(sp))} | {short_id(sp.get('Book'))} | {short_id(sp.get('Element'))} | {lvl} | {md_escape(runes)} |")
    lines.append("")

    lines.append("## 祷告（Prayers）")
    lines.append("")
    lines.append("| ID | 名称 | 槽位 | 需求 |")
    lines.append("|---|---|---|---|")
    for pr in sorted(prayers, key=lambda x: (db.req_level(x.get("UnlockRequirements") or x.get("Requirements"), "Prayer") or 999, display_name(x))):
        lines.append(f"| `{full_id(pr['Id'])}` | {md_escape(display_name(pr))} | {short_id(pr.get('FitsInSlot'))} | {md_escape(db.fmt_reqs(pr.get('UnlockRequirements') or pr.get('Requirements')))} |")
    lines.append("")

    lines.append("## 屠魔大师（Slayer Masters）")
    lines.append("")
    lines.append("| ID | 名称 | 基础任务数 | 刷新费用 | 区域 | 解锁需求 |")
    lines.append("|---|---|---|---|---|---|")
    for sm in slayers:
        rc = sm.get("RerollCost") or {}
        rc_s = f"{rc.get('Quantity',0)} {db.currency_name(rc.get('Currency'))}" if rc else ""
        ars = ", ".join(short_id(a) for a in (sm.get("Areas") or []))
        lines.append(f"| `{full_id(sm['Id'])}` | {md_escape(display_name(sm))} | {sm.get('BaseTaskLength','')} | {md_escape(rc_s)} | {md_escape(ars)} | {md_escape(db.fmt_reqs(sm.get('UnlockRequirements')))} |")
    lines.append("")
    return "\n".join(lines)


def generate_shop(db: GameDB) -> str:
    subcats = {full_id(s["Id"]): s for s in db.objs.get("ShopSubcategory", [])}
    purchases = db.objs.get("ShopPurchase", [])

    groups: dict[str, list[dict]] = defaultdict(list)
    for sp in purchases:
        sc = sp.get("Subcategory") or "Other"
        groups[sc].append(sp)

    lines = [
        "# Shop（商店，含完整价格与购买条件）",
        "",
        f"> 实体 ID：`MelvorBase:Shop` | 组件：`Shop` | 共 **{len(purchases)}** 件商品（全部价格离线提取自 `MelvorBase.json`）。",
        "",
    ]

    for sc_id in sorted(groups):
        sc_obj = subcats.get(sc_id)
        sc_label = display_name(sc_obj) if sc_obj else short_id(sc_id)
        rows = groups[sc_id]
        rows.sort(key=lambda x: (db.req_level(x.get("Requirements")) or 0, display_name(x)))
        lines.append(f"## {sc_label} (`{short_id(sc_id)}`)")
        lines.append("")
        lines.append("| ID | 名称 | 价格 | 限购 | 获得物品/加成 | 购买条件 |")
        lines.append("|---|---|---|---|---|---|")
        for sp in rows:
            sid = full_id(sp["Id"])
            nm = display_name(sp)
            price = db.fmt_costs(sp.get("Costs"))
            lim = sp.get("DefaultBuyLimit")
            lim_s = "∞" if (lim is None or lim <= 0) else str(lim)
            prov = sp.get("Provides") or {}
            prov_parts = []
            if prov.get("Items"):
                prov_parts.append(db.fmt_item_list(prov["Items"]))
            bonuses = prov.get("Bonuses") or {}
            for b in (bonuses.get("Component") or [])[:3]:
                st = (b.get("Stat") or {}).get("Name", "")
                val = b.get("Value")
                prov_parts.append(f"{st}:{val}")
            for b in (bonuses.get("Global") or [])[:2]:
                st = (b.get("Stat") or {}).get("Name", "")
                val = b.get("Value")
                prov_parts.append(f"Global.{st}:{val}")
            reqs = db.fmt_reqs(sp.get("Requirements"))
            lines.append(f"| `{sid}` | {md_escape(nm)} | {md_escape(price)} | {lim_s} | {md_escape('; '.join(filter(None, prov_parts)))} | {md_escape(reqs)} |")
        lines.append("")
    return "\n".join(lines)


def generate_upgrades_and_misc(db: GameDB) -> str:
    ups = db.objs.get("ItemUpgrade", [])
    pets = db.objs.get("Pet", [])
    lines = [
        "# 物品升级 / 宠物 / 工具带总览",
        "",
        f"> 物品升级：{len(ups)} | 宠物：{len(pets)} | 工具带槽位：{len(db.objs.get('ToolbeltSlot', []))} | Mastery Guild 加成：{len(db.objs.get('MasteryGuildBonus', []))}",
        "",
        "## 物品升级（Item Upgrades）",
        "",
        "| ID | 成品 | 消耗材料/费用 | 需求 |",
        "|---|---|---|---|",
    ]
    for u in sorted(ups, key=lambda x: display_name(db.items.get(x.get("UpgradedItem") or "")) or x["Id"]):
        uid = full_id(u["Id"])
        out_item = db.item_name(u.get("UpgradedItem"))
        qty = u.get("QuantityPerUpgrade", 1)
        out_s = f"{qty}× {out_item}" if qty != 1 else out_item
        lines.append(f"| `{uid}` | {md_escape(out_s)} | {md_escape(db.fmt_costs(u.get('Costs')))} | {md_escape(db.fmt_reqs(u.get('Requirements')))} |")
    lines.append("")

    lines.append("## 宠物（Pets）")
    lines.append("")
    lines.append("| ID | 名称 | 说明 |")
    lines.append("|---|---|---|")
    for p in pets:
        lines.append(f"| `{full_id(p['Id'])}` | {md_escape(display_name(p))} | {md_escape(strip_bbcode(p.get('Description')))} |")
    lines.append("")
    return "\n".join(lines)


def generate_interlinks(db: GameDB) -> tuple[str, int]:
    """Cross-skill + Combat + Quest + Shop item producer/consumer graph."""
    producers: dict[str, set[str]] = defaultdict(set)
    consumers: dict[str, set[str]] = defaultdict(set)

    # Gathering & Artisan recipes
    skill_regs = [
        ("Mining", "MiningRecipe"),
        ("Woodcutting", "WoodcuttingRecipe"),
        ("Fishing", "FishingRecipe"),
        ("Hunting", "HuntingAnimal"),
        ("Thieving", "ThievingRecipe"),
        ("Cooking", "CookingRecipe"),
        ("Herblore", "HerbloreRecipe"),
        ("Firemaking", "FiremakingRecipe"),
        ("Farming", "FarmingRecipe"),
        ("Farming(Mill)", "GrainMillRecipe"),
        ("Ranching", "RanchingAnimal"),
    ]
    for sname, rkey in skill_regs:
        for r in db.objs.get(rkey, []):
            for pk in ("Products", "RandomProductsPerHit", "RandomProductsPerCut", "RandomProducts", "SpecialProducts"):
                for p in r.get(pk) or []:
                    if isinstance(p, dict) and p.get("Item"):
                        producers[p["Item"]].add(sname)
            if isinstance(r.get("Product"), dict) and r["Product"].get("Item"):
                producers[r["Product"]["Item"]].add(sname)
            for lk in ("CommonLoot", "RareLoot", "UniqueLoot"):
                for p in r.get(lk) or []:
                    if isinstance(p, dict) and p.get("Item"):
                        producers[p["Item"]].add(sname)
            ing = r.get("Ingredients") or {}
            for it in ing.get("Items") or []:
                if it.get("Item"):
                    consumers[it["Item"]].add(sname)

    # Artisan skills via EntityConfig
    for eid, sname in [
        ("MelvorBase:Smithing", "Smithing"),
        ("MelvorBase:Crafting", "Crafting"),
        ("MelvorBase:Fletching", "Fletching"),
        ("MelvorBase:Runecrafting", "Runecrafting"),
        ("MelvorBase:Beastcrafting", "Beastcrafting"),
    ]:
        reg = db.by_type.get("ArtisanRecipe", {})
        for ref in db.ec.get(eid, {}).get("Recipes", []):
            rid = ref["Id"] if isinstance(ref, dict) else ref
            r = reg.get(rid)
            if not r:
                continue
            for p in r.get("Products") or []:
                if p.get("Item"):
                    producers[p["Item"]].add(sname)
            for it in (r.get("Ingredients") or {}).get("Items") or []:
                if it.get("Item"):
                    consumers[it["Item"]].add(sname)

    # Combat enemy drops
    for e in db.objs.get("Enemy", []):
        drops = e.get("Drops") or {}
        for p in drops.get("FixedItems") or []:
            if p.get("Item"):
                producers[p["Item"]].add("Combat")
        for tbl in drops.get("ItemTables") or []:
            for p in tbl.get("Table") or []:
                if p.get("Item"):
                    producers[p["Item"]].add("Combat")

    # Shop items & costs
    for sp in db.objs.get("ShopPurchase", []):
        for p in (sp.get("Provides") or {}).get("Items") or []:
            if p.get("Item"):
                producers[p["Item"]].add("Shop")
        for it in (sp.get("Costs") or {}).get("Items") or []:
            if it.get("Item"):
                consumers[it["Item"]].add("Shop")

    # Quest hand-ins & rewards
    for q in db.objs.get("Quest", []):
        for s in q.get("Steps") or []:
            for t in s.get("TasksInOrder") or []:
                for it in (t.get("Costs") or {}).get("Items") or []:
                    if it.get("Item"):
                        consumers[it["Item"]].add(f"Quest({q['Id']})")

    # Item Upgrades
    for u in db.objs.get("ItemUpgrade", []):
        if u.get("UpgradedItem"):
            producers[u["UpgradedItem"]].add("ItemUpgrade")
        for it in (u.get("Costs") or {}).get("Items") or []:
            if it.get("Item"):
                consumers[it["Item"]].add("ItemUpgrade")

    edges = []
    all_items = set(producers.keys()) | set(consumers.keys())
    for iid in all_items:
        p = producers.get(iid, set())
        c = consumers.get(iid, set())
        if len(p | c) >= 2 and (p and c):
            edges.append((iid, sorted(p), sorted(c)))
    edges.sort(key=lambda x: db.item_name(x[0]))

    lines = [
        "# 跨技能 / 战斗 / 任务 / 商店物品联动",
        "",
        f"> 由 `MelvorBase.json` 自动推导，共 **{len(edges)}** 个跨系统流转物品。",
        "",
        "| 物品 ID | 名称 | 售价(GP) | 产出方 | 消耗方 |",
        "|---|---|---|---|---|",
    ]
    for iid, p, c in edges:
        lines.append(f"| `{iid}` | {md_escape(db.item_name(iid))} | {db.item_sell(iid)} | {md_escape(', '.join(p))} | {md_escape(', '.join(c))} |")
    lines.append("")
    return "\n".join(lines), len(edges)


def build_items_index(db: GameDB) -> list[dict]:
    out = []
    for it in db.objs.get("Item", []):
        fid = full_id(it["Id"])
        nm = display_name(it)
        caps = it.get("Capabilities") or {}
        cap_names = [short_id(k) for k in caps.keys()]
        rec = {
            "id": fid,
            "name": nm,
            "name_zh": db.zh_of(nm),
            "sells_for": int((it.get("SellsFor") or {}).get("Quantity") or 0),
            "capabilities": cap_names,
        }
        if "MelvorBase:Equipment" in caps:
            eq = caps["MelvorBase:Equipment"]
            rec["equip_type"] = short_id(eq.get("Type"))
            rec["equip_reqs"] = [db.fmt_req(r) for r in (eq.get("Requirements") or [])]
        if "MelvorBase:Food" in caps:
            rec["heal"] = caps["MelvorBase:Food"].get("HealValue") or caps["MelvorBase:Food"].get("HealsFor")
        if "MelvorBase:Toolbelt" in caps:
            rec["toolbelt_slot"] = short_id(caps["MelvorBase:Toolbelt"].get("SlotType") or caps["MelvorBase:Toolbelt"].get("Type"))
        out.append(rec)
    return out


def write_readme(db: GameDB, skill_files: list[str], n_edges: int) -> str:
    meta = {}
    if META_JSON.is_file():
        meta = json.loads(META_JSON.read_text(encoding="utf-8"))
    godot_ver = (meta.get("pck") or {}).get("godot_version", "4.7+")
    extracted_at = meta.get("extracted_at", "offline")

    lines = [
        "# Melvor Idle 2 知识库（全离线自动提取）",
        "",
        f"> 数据来源：`Melvor2.exe` 内嵌 PCK 中的 `Data/Packages/MelvorBase.json` + `Locale/zh_CN.po`（Godot {godot_ver}，提取时间 `{extracted_at}`）。",
        "> **无需启动游戏**：游戏 EA 版本更新后，只需运行：",
        "> ```bash",
        "> cd Tools/melvor2-mcp/knowledge && python extract_offline.py && python generate.py",
        "> ```",
        "",
        "## 数据覆盖范围",
        "",
        f"- **物品**：{len(db.objs.get('Item', []))} 件（含售价、装备属性、食物回血、工具带、开箱/埋葬属性，索引见 `data/items_index.json`）",
        f"- **任务（Quests）**：{len(db.objs.get('Quest', []))} 个（Act 1/2 主线 + 支线 + 步骤/材料/奖励，见 `quests.md`）",
        f"- **商店（Shop）**：{len(db.objs.get('ShopPurchase', []))} 件（含**真实价格**、限购、解锁条件与加成，见 `skills/shop.md`）",
        f"- **战斗（Combat）**：{len(db.objs.get('CombatArea', []))} 区域 / {len(db.objs.get('Dungeon', []))} 地下城 / {len(db.objs.get('Enemy', []))} 敌人与掉落表 / {len(db.objs.get('MagicSpell', []))} 法术 / {len(db.objs.get('Prayer', []))} 祷告 / {len(db.objs.get('SlayerMaster', []))} 屠魔大师（见 `skills/combat.md`）",
        f"- **跨系统物品联动**：{n_edges} 条（见 `interlinks.md`）",
        "",
        "## 文档导航（MCP `browse_knowledge(topic)`）",
        "",
        "| Topic | 说明 |",
        "|---|---|",
        "| `readme` | 本索引 |",
        "| `quests` | **主线/支线任务全攻略（步骤、提交材料、目标、奖励）** |",
        "| `mechanics` | 核心机制、公式（XP、破岩率、伐木血量、战斗三角/元素、离线、被动技能） |",
        "| `planning` | **任务优先 + 并行规划 + 队列批处理计划编排指南** |",
        "| `interlinks` | 跨技能 / 战斗 / 任务 / 商店物品流转表 |",
    ]
    for sf in sorted(skill_files):
        lines.append(f"| `skills:{sf}` | `{sf}.md` |")
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    if not BASE_JSON.is_file():
        print(f"ERROR: {BASE_JSON} not found. Run `python extract_offline.py` first.", file=sys.stderr)
        return 1

    print(f"Loading {BASE_JSON} ...")
    data = json.loads(BASE_JSON.read_text(encoding="utf-8"))
    zh_map = parse_po(ZH_PO)
    print(f"Loaded {len(zh_map):,} zh_CN translations.")
    db = GameDB(data, zh_map)

    skills_dir = HERE / "skills"
    skills_dir.mkdir(exist_ok=True)

    # 1. Quests
    quests_md, quests_idx = generate_quests(db)
    (HERE / "quests.md").write_text(quests_md, encoding="utf-8")
    (DATA_DIR / "quests_index.json").write_text(json.dumps(quests_idx, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"quests.md ({len(quests_idx)} quests)")

    # 2. Skills
    generated_skills: dict[str, str] = {
        "mining": generate_mining(db),
        "woodcutting": generate_woodcutting(db),
        "fishing": generate_fishing(db),
        "hunting": generate_hunting(db),
        "thieving": generate_thieving(db),
        "smithing": generate_artisan_skill(db, "MelvorBase:Smithing", "Smithing", "ArtisanRecipe"),
        "crafting": generate_artisan_skill(db, "MelvorBase:Crafting", "Crafting", "ArtisanRecipe"),
        "fletching": generate_artisan_skill(db, "MelvorBase:Fletching", "Fletching", "ArtisanRecipe"),
        "runecrafting": generate_artisan_skill(db, "MelvorBase:Runecrafting", "Runecrafting", "ArtisanRecipe"),
        "beastcrafting": generate_artisan_skill(db, "MelvorBase:Beastcrafting", "Beastcrafting", "ArtisanRecipe"),
        "cooking": generate_artisan_skill(db, "MelvorBase:Cooking", "Cooking", "CookingRecipe"),
        "herblore": generate_artisan_skill(db, "MelvorBase:Herblore", "Herblore", "HerbloreRecipe"),
        "firemaking": generate_artisan_skill(db, "MelvorBase:Firemaking", "Firemaking", "FiremakingRecipe"),
        "farming": generate_farming(db),
        "ranching": generate_ranching(db),
        "agility": generate_agility(db),
        "astrology": generate_astrology(db),
        "combat": generate_combat(db),
        "shop": generate_shop(db),
        "upgrades": generate_upgrades_and_misc(db),
    }

    for name, content in generated_skills.items():
        p = skills_dir / f"{name}.md"
        p.write_text(content, encoding="utf-8")
        print(f"skills/{name}.md ({len(content):,} chars)")

    # 3. Interlinks
    inter_md, n_edges = generate_interlinks(db)
    (HERE / "interlinks.md").write_text(inter_md, encoding="utf-8")
    print(f"interlinks.md ({n_edges} cross-system items)")

    # 4. Items index
    items_idx = build_items_index(db)
    (DATA_DIR / "items_index.json").write_text(json.dumps(items_idx, ensure_ascii=False), encoding="utf-8")
    print(f"data/items_index.json ({len(items_idx)} items)")

    # 5. README
    readme_md = write_readme(db, list(generated_skills.keys()), n_edges)
    (HERE / "README.md").write_text(readme_md, encoding="utf-8")
    print("README.md updated.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
