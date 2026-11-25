"""Melvor Idle 2 save file parser.

Reverse-engineered from GamesByMalcs.Melvor2.dll (decompiled WriteTo/ReadFrom).

File layout:
    .sav  = BrotliStream(raw binary)           (use tools/SaveDecompress to decode)
    raw   = int32 version
            SaveHeader (flat, no region)
            Game       (entity/component tree, regions are int32-length-prefixed)

Decoded components (offline-readable even while game is closed):
    - Wallet       (Gold Pieces, Slayer Coins, Prayer Points, ...)
    - Inventory    (Bank items, quantities, locked/placeholder flags)
    - Experience   (All 25 skills XP & levels)
    - Quests       (All 37 quests: state, step states, task goal progress, costs_met)
    - Shop         (Purchased upgrades and counts)
    - Equipment    (Equipped combat gear per slot)
    - Toolbelt     (Equipped tools across all skills)
    - Food         (Selected food item)
    - Tasks        (Slayer tasks per master)
    - Areas        (Selected combat area / enemy / dungeon & progress)
    - MagicSpells  (Selected spell, imbuement, spellbook)
    - Prayer       (Selected & active prayers per slot)
    - MasteryGuild (All 25 skills' Mastery Guild task progress & completion)
    - Pets         (Unlocked pets & unlock goal progress)
    - Bonfire      (Firemaking active bonfire)
    - Techniques   (Agility technique XP & levels)
"""

from __future__ import annotations

import struct
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from typing import Any, Optional

EPOCH = datetime(1, 1, 1)  # .NET DateTime ticks epoch

QUEST_STATES = {0: "Unstarted", 1: "InProgress", 2: "Completed", 3: "Claimed"}
QUEST_STEP_STATES = {0: "Inactive", 1: "Active", 2: "Complete"}


class SaveParseError(Exception):
    pass


class Reader:
    """Mimics .NET System.IO.BinaryReader over bytes."""

    def __init__(self, data: bytes, pos: int = 0):
        self.data = data
        self.pos = pos

    def eof(self) -> bool:
        return self.pos >= len(self.data)

    def remaining(self) -> int:
        return len(self.data) - self.pos

    def _take(self, n: int) -> bytes:
        if self.pos + n > len(self.data):
            raise SaveParseError(f"Unexpected end of stream at {self.pos}, need {n} bytes")
        b = self.data[self.pos : self.pos + n]
        self.pos += n
        return b

    def int32(self) -> int:
        return struct.unpack("<i", self._take(4))[0]

    def uint32(self) -> int:
        return struct.unpack("<I", self._take(4))[0]

    def int64(self) -> int:
        return struct.unpack("<q", self._take(8))[0]

    def uint64(self) -> int:
        return struct.unpack("<Q", self._take(8))[0]

    def double(self) -> float:
        return struct.unpack("<d", self._take(8))[0]

    def boolean(self) -> bool:
        return self._take(1)[0] != 0

    def byte(self) -> int:
        return self._take(1)[0]

    def seven_bit_int(self) -> int:
        result = 0
        shift = 0
        while True:
            b = self.byte()
            result |= (b & 0x7F) << shift
            if not (b & 0x80):
                return result
            shift += 7
            if shift > 35:
                raise SaveParseError("Invalid 7-bit encoded int")

    def string(self) -> str:
        """.NET BinaryReader.ReadString: 7-bit length prefix + UTF-8."""
        n = self.seven_bit_int()
        return self._take(n).decode("utf-8", errors="replace")

    def guid(self) -> uuid.UUID:
        return uuid.UUID(bytes_le=self._take(16))

    def region(self) -> "Reader":
        """Read a length-prefixed region and return a sub-reader."""
        n = self.int32()
        if n < 0 or self.pos + n > len(self.data):
            raise SaveParseError(f"Bad region length {n} at {self.pos}")
        sub = Reader(self.data[self.pos : self.pos + n])
        self.pos += n
        return sub

    def skip_region(self) -> None:
        n = self.int32()
        if n < 0 or self.pos + n > len(self.data):
            raise SaveParseError(f"Bad region length {n} at {self.pos}")
        self.pos += n

    def datetime_binary(self) -> datetime:
        """Parse .NET DateTime.ToBinary, honouring the Kind flags like FromBinary."""
        raw = self.int64()
        kind = (raw >> 62) & 3  # 0=Unspecified 1=UTC 2=Local(as stored by ToBinary)
        ticks = raw & 0x3FFFFFFFFFFFFFFF
        dt = EPOCH + timedelta(microseconds=ticks / 10)
        if kind == 2:
            dt = dt.replace(tzinfo=timezone.utc).astimezone()
        return dt.replace(tzinfo=None)


# ---------------------------------------------------------------------------
# Decoded structures
# ---------------------------------------------------------------------------


@dataclass
class SaveHeader:
    uuid: uuid.UUID
    character_name: str
    gamemode: str
    timestamp: datetime
    active_entity_name: str
    active_entity_icon: str
    active_action_name: str
    active_action_icon: str
    total_level: int
    gp: int
    slayer_coins: int
    is_demo: bool


@dataclass
class ComponentData:
    component_id: str
    raw: bytes
    decoded: Optional[dict] = None


@dataclass
class EntityData:
    entity_id: str
    components: dict[str, ComponentData] = field(default_factory=dict)


@dataclass
class ParsedSave:
    version: int
    header: SaveHeader
    entities: dict[str, EntityData]
    active_actions: list[tuple[str, str]]
    raw_size: int

    # ----- convenience accessors -----

    def currencies(self) -> dict[str, int]:
        bank = self.entities.get("MelvorBase:Bank")
        if not bank or "Wallet" not in bank.components:
            return {}
        d = bank.components["Wallet"].decoded
        return dict(d["stacks"]) if d else {}

    def bank_items(self) -> list[dict]:
        bank = self.entities.get("MelvorBase:Bank")
        if not bank or "Inventory" not in bank.components:
            return []
        d = bank.components["Inventory"].decoded
        return d["items"] if d else []

    def skills(self) -> list[dict]:
        """All entities that have an Experience component (i.e. skills)."""
        out = []
        for eid, entity in self.entities.items():
            if "Experience" in entity.components:
                xp = entity.components["Experience"].decoded
                xp_val = xp["xp"] if xp else 0.0
                level = level_from_xp(xp_val) if xp_val else 1
                out.append(
                    {
                        "id": eid,
                        "level": level,
                        "xp": xp_val,
                        "xp_to_next_level": xp_for_level(level + 1),
                    }
                )
        return out

    def quests(self) -> dict:
        ent = self.entities.get("MelvorBase:Quests")
        if not ent or "Quests" not in ent.components:
            return {"quests": {}}
        d = ent.components["Quests"].decoded
        return d if d else {"quests": {}}

    def shop_purchases(self) -> dict[str, int]:
        ent = self.entities.get("MelvorBase:Shop")
        if not ent or "Shop" not in ent.components:
            return {}
        d = ent.components["Shop"].decoded
        return dict(d.get("purchased", {})) if d else {}

    def equipment(self) -> dict[str, dict]:
        ent = self.entities.get("MelvorBase:Combat")
        if not ent or "Equipment" not in ent.components:
            return {}
        d = ent.components["Equipment"].decoded
        return dict(d.get("slots", {})) if d else {}

    def toolbelts(self) -> dict[str, dict[str, dict]]:
        out: dict[str, dict[str, dict]] = {}
        for eid, ent in self.entities.items():
            if "Toolbelt" in ent.components and ent.components["Toolbelt"].decoded:
                slots = ent.components["Toolbelt"].decoded.get("slots", {})
                equipped = {k: v for k, v in slots.items() if v.get("item")}
                if equipped:
                    out[eid] = equipped
        return out

    def slayer_tasks(self) -> dict:
        ent = self.entities.get("MelvorBase:Slayer")
        if not ent or "Tasks" not in ent.components:
            return {}
        d = ent.components["Tasks"].decoded
        return d if d else {}

    def combat_setup(self) -> dict:
        ent = self.entities.get("MelvorBase:Combat")
        if not ent:
            return {}
        out: dict[str, Any] = {}
        if "Equipment" in ent.components and ent.components["Equipment"].decoded:
            out["equipment"] = {
                k: v for k, v in ent.components["Equipment"].decoded.get("slots", {}).items()
                if v.get("item")
            }
        if "Food" in ent.components and ent.components["Food"].decoded:
            out["food"] = ent.components["Food"].decoded.get("selected_food")
        if "Areas" in ent.components and ent.components["Areas"].decoded:
            out["area"] = ent.components["Areas"].decoded
        if "MagicSpells" in ent.components and ent.components["MagicSpells"].decoded:
            out["magic"] = ent.components["MagicSpells"].decoded
        if "Prayer" in ent.components and ent.components["Prayer"].decoded:
            out["prayers"] = ent.components["Prayer"].decoded
        return out

    def mastery_guild(self) -> dict[str, dict]:
        """Per-skill MasteryGuild raw decoded tasks (keyed by entity id)."""
        out: dict[str, dict] = {}
        for eid, ent in self.entities.items():
            if "MasteryGuild" in ent.components and ent.components["MasteryGuild"].decoded:
                out[eid] = ent.components["MasteryGuild"].decoded
        return out

    def pets(self) -> dict[str, dict]:
        out: dict[str, dict] = {}
        for ent in self.entities.values():
            if "Pets" in ent.components and ent.components["Pets"].decoded:
                out.update(ent.components["Pets"].decoded.get("pets", {}))
        return out

    def bonfire(self) -> dict:
        ent = self.entities.get("MelvorBase:Firemaking")
        if not ent or "Bonfire" not in ent.components:
            return {}
        d = ent.components["Bonfire"].decoded
        return dict(d) if d else {}

    def agility_techniques(self) -> dict[str, dict]:
        ent = self.entities.get("MelvorBase:Agility")
        if not ent or "Techniques" not in ent.components:
            return {}
        d = ent.components["Techniques"].decoded
        return dict(d.get("techniques", {})) if d else {}


# ---------------------------------------------------------------------------
# Component decoders
# ---------------------------------------------------------------------------


def _decode_wallet(r: Reader, _version: int = 0) -> dict:
    count = r.int32()
    stacks = {}
    for _ in range(count):
        qty = r.int64()
        cur = r.string()
        stacks[cur] = qty
    return {"stacks": stacks}


def _decode_inventory(r: Reader, _version: int = 0) -> dict:
    default_new_item_tab = r.int32()
    tab_count = r.int32()
    items: list[dict] = []
    for _ in range(tab_count):
        _decode_tab_inventory(r, items)
    locked = [r.string() for _ in range(r.int32())]
    default_tabs_count = r.int32()
    default_tabs: dict[str, int] = {}
    for _ in range(default_tabs_count):
        tab_idx = r.int32()
        item_id = r.string()
        default_tabs[item_id] = tab_idx
    sort_count = r.int32()
    for _ in range(sort_count):
        r.int32()
        r.string()
    placeholder_count = r.int32()
    placeholders = [r.string() for _ in range(placeholder_count)]
    _is_config_locked = r.boolean()
    _auto_sort = r.boolean()
    _auto_placeholder = r.boolean()
    _sort_type = r.int32()
    return {
        "default_new_item_tab": default_new_item_tab,
        "tab_count": tab_count,
        "items": items,
        "locked_items": locked,
        "default_item_tabs": default_tabs,
        "placeholder_items": placeholders,
    }


def _decode_tab_inventory(r: Reader, items_out: list[dict]) -> None:
    """StackableInventory + BankTabInventory label/tags."""
    _max_stacks = r.int32()
    _min_stack_qty = r.int32()
    _max_stack_qty = r.int32()
    _allow_combining = r.boolean()
    _allow_same = r.boolean()
    count = r.int32()
    for _ in range(count):
        qty = r.int32()
        placeholder = r.boolean()
        locked = r.boolean()
        item_id = r.string()
        items_out.append(
            {
                "item": item_id,
                "quantity": qty,
                "placeholder": placeholder,
                "locked": locked,
            }
        )
    _label = r.string()
    tag_count = r.int32()
    for _ in range(tag_count):
        r.boolean()
        r.boolean()
        r.string()


def _decode_experience(r: Reader, _version: int = 0) -> dict:
    return {"xp": r.double()}


def _decode_quests(r: Reader, version: int = 0) -> dict:
    count = r.int32()
    quests: dict[str, dict] = {}
    for _ in range(count):
        qid = r.string()
        qr = r.region()
        qstate_raw = qr.int32()
        step_count = qr.int32()
        steps = []
        for _ in range(step_count):
            sid = qr.string()
            sr = qr.region()
            task_count = sr.int32()
            tasks = []
            for _ in range(task_count):
                tid = sr.string()
                tr = sr.region()
                goal_count = tr.int32()
                goals = []
                for _ in range(goal_count):
                    gid = tr.string()
                    gr = tr.region()
                    prog = gr.double() if gr.remaining() >= 8 else 0.0
                    complete = gr.boolean() if gr.remaining() >= 1 else False
                    active = gr.boolean() if gr.remaining() >= 1 else False
                    goals.append({
                        "id": gid,
                        "progress": prog,
                        "is_complete": complete,
                        "is_active": active,
                    })
                task_visible = tr.boolean()
                costs_met = tr.boolean()
                # Alpha0_11_5 added _conditionGoalsConfirmed before _state
                cond_confirmed = False
                if tr.remaining() >= 5:
                    cond_confirmed = tr.boolean()
                tstate_raw = tr.int32()
                tasks.append({
                    "id": tid,
                    "visible": task_visible,
                    "costs_met": costs_met,
                    "condition_goals_confirmed": cond_confirmed,
                    "state": QUEST_STEP_STATES.get(tstate_raw, str(tstate_raw)),
                    "goals": goals,
                })
            step_visible = sr.boolean()
            sstate_raw = sr.int32()
            steps.append({
                "id": sid,
                "visible": step_visible,
                "state": QUEST_STEP_STATES.get(sstate_raw, str(sstate_raw)),
                "tasks": tasks,
            })
        quests[qid] = {
            "state": QUEST_STATES.get(qstate_raw, str(qstate_raw)),
            "steps": steps,
        }
    show_completed = r.boolean() if not r.eof() else False
    show_locked = r.boolean() if not r.eof() else False
    selected_quest = r.string() if (not r.eof() and r.boolean()) else None
    return {
        "selected_quest": selected_quest,
        "quests": quests,
        "summary": {
            st: sum(1 for q in quests.values() if q["state"] == st)
            for st in ("Claimed", "Completed", "InProgress", "Unstarted")
        },
    }


def _decode_shop(r: Reader, _version: int = 0) -> dict:
    _show_purchased = r.boolean()
    _show_locked = r.boolean()
    _hold_to_buy = r.boolean()
    _custom_qty = r.int32()
    _purchase_mode = r.int32()
    count = r.int32()
    purchased: dict[str, int] = {}
    for _ in range(count):
        qty = r.int32()
        pid = r.string()
        purchased[pid] = qty
    return {"purchased": purchased}


def _decode_equipment(r: Reader, _version: int = 0) -> dict:
    count = r.int32()
    slots: dict[str, dict] = {}
    for _ in range(count):
        slot_id = r.string()
        sr = r.region()
        qty = sr.int32()
        occupied = sr.boolean()
        has_eq = sr.boolean()
        item_id = sr.string() if has_eq else None
        slots[slot_id] = {"item": item_id, "quantity": qty, "occupied": occupied}
    return {"slots": slots}


def _decode_toolbelt(r: Reader, _version: int = 0) -> dict:
    count = r.int32()
    slots: dict[str, dict] = {}
    for _ in range(count):
        slot_id = r.string()
        sr = r.region()
        qty = sr.int32()
        has_eq = sr.boolean()
        item_id = sr.string() if has_eq else None
        slots[slot_id] = {"item": item_id, "quantity": qty}
    return {"slots": slots}


def _decode_food(r: Reader, _version: int = 0) -> dict:
    selected = r.string() if r.boolean() else None
    return {"selected_food": selected}


def _decode_slayer_tasks(r: Reader, _version: int = 0) -> dict:
    count = r.int32()
    masters: dict[str, dict] = {}
    for _ in range(count):
        mid = r.string()
        mr = r.region()
        has_task = mr.boolean()
        enemy = mr.string() if has_task else None
        length = mr.int32()
        progress = mr.int32()
        extended = mr.boolean()
        auto_slayer = mr.boolean()
        if enemy or progress > 0:
            masters[mid] = {
                "enemy": enemy,
                "length": length,
                "progress": progress,
                "remaining": max(0, length - progress),
                "extended": extended,
                "auto_slayer": auto_slayer,
            }
    recent = r.string() if (not r.eof() and r.boolean()) else None
    return {"recent_master": recent, "masters": masters}


def _decode_combat_areas(r: Reader, _version: int = 0) -> dict:
    cur_prog = r.int32()
    max_prog = r.int32()
    area = r.string() if r.boolean() else None
    enemy = r.string() if r.boolean() else None
    dungeon = r.string() if r.boolean() else None
    return {
        "area": area,
        "enemy": enemy,
        "dungeon": dungeon,
        "progress": cur_prog,
        "max_progress": max_prog,
    }


def _decode_magic_spells(r: Reader, _version: int = 0) -> dict:
    spell = r.string() if r.boolean() else None
    imbuement = r.string() if r.boolean() else None
    spellbook = r.string() if r.boolean() else None
    activated_imb = r.string() if r.boolean() else None
    return {
        "selected_spell": spell,
        "selected_imbuement": imbuement,
        "selected_spellbook": spellbook,
        "activated_imbuement": activated_imb,
    }


def _decode_prayer(r: Reader, _version: int = 0) -> dict:
    count = r.int32()
    slots: dict[str, dict] = {}
    for _ in range(count):
        sid = r.string()
        sr = r.region()
        sel = sr.string() if sr.boolean() else None
        act = None
        if sr.boolean():
            _state = sr.int32()
            act = sr.string()
        if sel or act:
            slots[sid] = {"selected": sel, "active": act}
    return {"slots": slots}


def _decode_mastery_guild(r: Reader, _version: int = 0) -> dict:
    selected_cat = r.string() if r.boolean() else None
    count = r.int32()
    tasks: dict[str, dict] = {}
    for _ in range(count):
        tid = r.string()
        tr = r.region()
        goal_id = None
        prog = 0.0
        goal_complete = False
        goal_active = False
        if not tr.eof() and tr.boolean():
            goal_id = tr.string()
            gr = tr.region()
            if gr.remaining() >= 8:
                prog = gr.double()
            if gr.remaining() >= 1:
                goal_complete = gr.boolean()
            if gr.remaining() >= 1:
                goal_active = gr.boolean()
        trigger_met = tr.boolean() if not tr.eof() else False
        tasks[tid] = {
            "goal_id": goal_id,
            "progress": prog,
            "goal_complete": goal_complete,
            "goal_active": goal_active,
            "trigger_met": trigger_met,
        }
    return {"selected_category": selected_cat, "tasks": tasks}


def _decode_pets(r: Reader, _version: int = 0) -> dict:
    count = r.int32()
    pets: dict[str, dict] = {}
    for _ in range(count):
        pid = r.string()
        pr = r.region()
        unlocked = pr.boolean() if not pr.eof() else False
        prog = 0.0
        if not pr.eof() and pr.boolean():
            _gid = pr.string()
            gr = pr.region()
            if gr.remaining() >= 8:
                prog = gr.double()
        pets[pid] = {"unlocked": unlocked, "progress": prog}
    return {"pets": pets}


def _decode_bonfire(r: Reader, _version: int = 0) -> dict:
    # Timer.WriteTo: double remaining_ms + bool is_running (9 bytes)
    remaining_ms = r.double()
    timer_running = r.boolean()
    bonfire = r.string() if r.boolean() else None
    is_active = r.boolean() if not r.eof() else False
    return {
        "bonfire": bonfire,
        "is_active": is_active,
        "timer_running": timer_running,
        "remaining_ms": remaining_ms,
    }


def _decode_agility_techniques(r: Reader, _version: int = 0) -> dict:
    count = r.int32()
    techs: dict[str, dict] = {}
    for _ in range(count):
        tid = r.string()
        tr = r.region()
        xp = tr.double() if tr.remaining() >= 8 else 0.0
        techs[tid] = {"xp": xp, "level": level_from_xp(xp)}
    return {"techniques": techs}


COMPONENT_DECODERS = {
    "Wallet": _decode_wallet,
    "Inventory": _decode_inventory,
    "Experience": _decode_experience,
    "Quests": _decode_quests,
    "Shop": _decode_shop,
    "Equipment": _decode_equipment,
    "Toolbelt": _decode_toolbelt,
    "Food": _decode_food,
    "Tasks": _decode_slayer_tasks,
    "Areas": _decode_combat_areas,
    "MagicSpells": _decode_magic_spells,
    "Prayer": _decode_prayer,
    "MasteryGuild": _decode_mastery_guild,
    "Pets": _decode_pets,
    "Bonfire": _decode_bonfire,
    "Techniques": _decode_agility_techniques,
}


# ---------------------------------------------------------------------------
# Experience table (StandardExperienceTable.Default from the game DLL:
# scaling=0.25, exponentScaling=300, base=2^(1/7))
# ---------------------------------------------------------------------------

_XP_CACHE: list[float] = [0.0]
_XP_SUM = 0.0


def xp_for_level(level: int) -> float:
    global _XP_SUM
    if level < 1:
        return 0.0
    while len(_XP_CACHE) < level:
        i = len(_XP_CACHE)
        _XP_SUM += i + 300.0 * (2.0 ** (i / 7.0))
        _XP_SUM = float(int(_XP_SUM))  # Math.Floor on a positive double
        _XP_CACHE.append(float(int(0.25 * _XP_SUM)))
    return _XP_CACHE[level - 1]


def level_from_xp(xp: float, level_cap: int = 99) -> int:
    if xp <= 0:
        return 1
    level = 1
    while level < level_cap and xp >= xp_for_level(level + 1):
        level += 1
    return level


# ---------------------------------------------------------------------------
# Top-level parse
# ---------------------------------------------------------------------------


def parse_raw(data: bytes) -> ParsedSave:
    r = Reader(data)
    version = r.int32()

    # --- SaveHeader (flat) ---
    header = SaveHeader(
        uuid=r.guid(),
        character_name=r.string(),
        gamemode=r.string(),
        timestamp=r.datetime_binary(),
        active_entity_name=r.string(),
        active_entity_icon=r.string(),
        active_action_name=r.string(),
        active_action_icon=r.string(),
        total_level=r.int32(),
        gp=r.int64(),
        slayer_coins=r.int64(),
        is_demo=r.boolean(),
    )

    # --- Game ---
    entity_count = r.int32()
    entities: dict[str, EntityData] = {}
    for _ in range(entity_count):
        eid = r.string()
        region = r.region()
        entity = EntityData(entity_id=eid)
        comp_count = region.int32()
        for _ in range(comp_count):
            cid = region.string()
            cregion = region.region()
            raw = cregion.data[:]
            decoder = COMPONENT_DECODERS.get(cid)
            decoded = None
            if decoder:
                try:
                    decoded = decoder(Reader(raw), version)
                except SaveParseError:
                    decoded = None
            entity.components[cid] = ComponentData(
                component_id=cid, raw=raw, decoded=decoded
            )
        entities[eid] = entity

    # --- tail ---
    active_actions: list[tuple[str, str]] = []
    am_count = r.int32()
    for _ in range(am_count):
        eid = r.string()
        cid = r.string()
        active_actions.append((eid, cid))

    # RNG: 4 x uint64
    _rng = [r.uint64() for _ in range(4)]

    # EventLog
    el_count = r.int32()
    _el_cats = [r.int32() for _ in range(el_count)]

    # PassiveActionManager: empty
    _onboarding_skipped = r.boolean() if not r.eof() else False

    return ParsedSave(
        version=version,
        header=header,
        entities=entities,
        active_actions=active_actions,
        raw_size=len(data),
    )
