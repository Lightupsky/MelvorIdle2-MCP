# Melvor Idle 2 多维度规划与批处理行动队列指南

> **核心范式（从实时操控 → 多维度规划 + 批处理计划队列）**：
> Melvor Idle 2 是中长周期多系统联动放置游戏，不需要 Agent 高频轮询。
> 正确工作流是：**玩家召唤 Agent → 读取最新存档/状态（含任务、专精公会、装备/工具带、被动技能）→ 综合多维度 ROI 编排一串多步骤行动计划（Action Plan）并提交给游戏内置调度器 → Agent 退出休息 → 游戏自动按定时/定次数/定目标执行数小时 → 玩家稍后召唤 Agent 验收并规划下一轮！**

- **可视化调度手账**：游戏运行时直接打开 `http://127.0.0.1:17817/`（`v2.2+` 桥接内嵌），或运行 `StartDashboard.ps1` 打开 `http://127.0.0.1:17818/`（支持离线存档回退）。
- **EA 兼容性检查**：游戏更新后先调用 `get_bridge_health()` 确认桥接状态；若出现 `degraded`/`incompatible`，查看 `logs/melvor2_compat_report.json` 并运行 `extract_offline.py && generate.py` 刷新离线数据。

---

## 一、标准规划工作流

1. **全景盘点（先读存档/状态，切忌拍脑袋）**：
   - `get_plan_status`：查看上一轮计划完成情况、耗时、计数与中止原因。
   - `get_game_state` + `get_bank_items` + `get_quests(state_filter="active")` + `get_equipment_and_setup`：掌握技能等级、GP、仓库存货、当前装备/工具带、篝火、宠物、敏捷技巧与任务卡点。
   - **`get_mastery_tasks()`**：检查 25 项技能的 **专精公会（Mastery Guild）段位、距离下一段位所需点数与下一段位永久加成**！
2. **多维度联合决策（6 大维度并行考量）**：
   - 详见下方 **「二、六大规划维度与 ROI 决策框架」**，确保每一轮计划不只盯着单一技能升级，而是把**主线任务 + 专精任务速刷 + 装备/工具带/套装升级 + 被动技能维护 + 全局永久加成**拧成一股绳。
3. **查表校验**：
   - `browse_knowledge("quests")` / `browse_knowledge("mastery")` / `browse_knowledge("skills:<name>")` / `search_knowledge(query)` 核实配方 ID、材料链、套装属性与商店真实价格。
   - 查 `interlinks.md` 确认上游采集量足够覆盖下游消耗与任务提交。
4. **验证与下发**：
   - `validate_plan(plan)` 离线静态校验所有 ID、参数与战斗/偷窃安全守卫。
   - `set_action_plan(plan)` 下发（游戏关闭时自动写入 `melvor2_plan.json` 待启动）。
   - **保底尾步原则**：队列最后一步始终放置可持续数小时的长周期采集/训练/观星步，防止队列跑空发呆。

---

## 二、六大规划维度与 ROI 决策框架

制定每一轮 2–8 小时行动计划时，按以下六个维度逐一扫查：

### 维度 1：主线与支线任务（Quests — 最高单次收益与系统解锁）
- **Act 1 主线**：`MQ1` (Ranged/石箭) → `MQ2` (Fishing 6, Smithing 6) → `MQ3` (WC 10, Mining 10, Fishing 23, Cooking 27) → `MQ4` (Smithing 12, Crafting 12) → `MQ5` (Mining 18, RC 20, Smithing 22, Crafting 25, 战斗 Lv25) → `MQ6` (WC 25, Smithing 25, 10× Silver Bar) → `MQ7` (Farming 21, Cooking 23, WC 30, FM 30, 战斗 Lv35) → `MQ8` (WC 39, FM 39, 战斗 Lv39 Pinnacle Boss)。
- **Act 2 主线**：`MQ9` (Smithing 35, Hunting 35, Cooking 40) → `MQ10` (Thieving 32, FM 42, Crafting/WC/Hunting 45, Beastcrafting 20, 解锁 Silted Oasis) → `MQ11`–`MQ17`（45→65+）。
- **规划要点**：
  - 满足门槛时优先把「备料 → 制作/击杀 → `quest_submit` → `quest_claim`」串成闭环。
  - 注意检查任务专属前置机制（如 `MQ5` 采硫磺需先做 `SulfurProtectionGloves` 装备到工具带，且需要 `MQ5 Step 0` 处于活跃状态）。
  - 同等级段的 `SideQuests`（支线任务）可穿插进练级路线顺带领奖。

### 维度 2：技能专精公会任务（Mastery Guild Tasks — 长期效率的隐形倍增器）
> 完整段位表见 `browse_knowledge("mastery")`，实时/离线进度用 `get_mastery_tasks(skill=...)` 查询。

- **机制**：25 项技能各自拥有独立专精公会（共 3,251 项激活任务），按难度给点：`Novice` (1pt) / `Easy` (2pt) / `Adept` (3pt) / `Intermediate` (5pt) / `Hard` (8pt) / `Expert` (12pt)。
- **关键段位质变阈值**：
  - **Rank 2（仅需总点数 5%！）**：几乎所有技能永久 **`+10% XP Gained`**！通常只需 10–15 分钟扫一轮 `Novice` / `Easy` 任务即可拿到。
  - **Rank 3（10%）**：核心效率质变，例如：
    - `Mining`：`+5% Doubling Chance`
    - `Woodcutting`：`+10% Double Damage Chance`
    - `Cooking`：**`+30 FlatProficiency`**（0.11.7alpha 实装！）
    - `Smithing` / `Crafting` / `Fishing` 等均提供高价值保留/双倍/速度加成。
  - **Rank 4（20%）**：关键商店/机制解锁（如 Cooking 解锁 Shop 购买 Recipe Bases，Woodcutting 每次挥斧额外 +1 产物）。
  - **Rank 5（40%）**：**额外解锁 +1 个技能工具带槽位（Toolbelt Slot）+ 再加 10% XP**！
- **黄金规划法则（“先扫专精，再长挂机”）**：
  - 每次准备对某个技能安排 ≥ 1 小时的冲级/刷料前，先调用 `get_mastery_tasks(skill="...", max_category="Easy")`：
  - 若距离 Rank 2 或 Rank 3 仅差 5–20 点，**优先在队列前段插入 2–5 个低成本 Novice/Easy 专精任务步**（比如顺手敲 50–200 次未刷过的低级矿石/树木、烧几组低级原木、做几件低级锻造/工艺品），把 Rank 2/3 拿到手后再进入主力挂机步！
  - 在多配方均可练级时，优先选择**同时推进未完成专精任务**的配方（一鱼两吃）。

### 维度 3：工具带、装备套装与敏捷奖章商店（Toolbelt & 42 Equipment Sets）
> 全部 42 套装备套装加成与 124 项物品升级见 `browse_knowledge("skills:upgrades")`。

- **工具带升级闭环**：技能升级或攒够 GP 后，在队列中直接串联 `shop_buy` → `equip_tool`（或锻造更高阶工具 → `equip_tool`），立即加速后续所有采集步。
- **技能套装（3 件套触发）**：
  - 敏捷奖章商店（0.11.7alpha 已按 11 个技能子分类整理）出售 Novice / Intermediate / Elite **Mining / Woodcutting / Fishing 套装**与工匠手套（I / II），3 件即触发套装效果（如 Intermediate Mining `+20% Geode`、Elite Mining `+5% Doubling`、Elite Fishing `+20% Special`、Graceful Agility `-6% 间隔`）。
  - 战斗套装如 Dawnsteel（+Light）、各元素 Wizard 套装（符文保留）在对应阶段收益极高。
- **商店核心优先级**：`Auto-Eat`（自动进食）→ `Bank Capacity`（银行格子，避免爆仓丢掉落）→ 工具升级 → 关键技能加成。

### 维度 4：全局与技能永久加成（Astrology / Bonfire / Journal / Heist / Pets）
- **Astrology（占星，12 星座 / 280 节点）**：中后期极高 ROI 的跨技能永久 Buff 来源；挂机碎片时间或睡前可安排观星并用 `astrology_unlock_node` 点出关键节点。
- **Firemaking（生火篝火，8 级篝火）**：升级与维持篝火可提供持续全局/技能增益；烧木练级同时清理 Firemaking 专精任务与篝火升级。
- **Hunting 图鉴 & 陷阱（0.11.7alpha 加强）**：基础陷阱现已内置失败保底 `+2% FlatTrappingSuccessChance`；刷齐不同猎物与稀有亚种可提升 `HuntingJournalBonus`（24 项永久加成）。
- **Thieving 劫案加成与分散事件**：推进不同地点偷窃可解锁 `ThievingHeistBonus`（16 项）与专属 Distractions。
- **Agility（敏捷）**：5 项技巧（`Balancing`/`Climbing`/`Jumping`/`Evading`/`Rappelling`）经验独立累计，同时产出 Agility Marks 购买全技能套装与手套。

### 维度 5：被动零占位技能（Farming & Ranching）
- **完全不占主动作槽**：在队列开头及每 1.5–2 小时穿插瞬间步骤：`farming_harvest_all` → `farming_plant_all` → `farming_water_all` → `ranching_maintain`。
- **0.11.7alpha 利好**：27 种农作物与堆肥的 **自动化解锁消耗（`AutomationCosts`）大幅下调约 30–50%**（如 Onion/Compost/Penril 仅需 1 个、Wheat 3 个），更早即可开启农田全自动循环！
- 牧场产出的皮革/毛料/食材直接反哺 Crafting / Cooking / 任务提交。

### 维度 6：战斗生态与安全守卫（Combat / Slayer / Prayer / Food）
- **多目标协同**：打怪尽量同时满足「主线/支线击杀目标 + Slayer 屠魔任务 + 敌人掉落材料 + 战斗技能专精任务（注意部分专精任务限定近战/远程/魔法流派）」。
- **强制安全守卫**：凡 `combat_fight` / `combat_dungeon` / `MelvorBase:Thieving` 步骤，必须先 `select_food` 确认食物充足，并配置 `"safety": { "min_food_qty": 20, "stop_on_low_food": true }`，避免挂机暴毙！

---

## 三、批处理计划（Action Plan）编排规范

一个 Plan JSON 由 `name`、`pause_on_manual_override`（默认 `true`）和有序的 `steps` 列表组成：

```json
{
  "name": "农田维护 + 采矿专精冲 Rank 3 + MQ6 银锭闭环",
  "pause_on_manual_override": true,
  "steps": [
    {
      "label": "收割并维护农田与牧场",
      "op": "farming_harvest_all"
    },
    {
      "label": "牧场一键照料",
      "op": "ranching_maintain"
    },
    {
      "label": "专精速刷：采锡矿补齐 Novice 任务冲 Rank 3",
      "op": "start_action",
      "params": { "entity": "MelvorBase:Mining", "component": "Action", "recipe": "MelvorBase:TinOre" },
      "until": { "item_gained": { "item": "MelvorBase:TinOre", "amount": 100 }, "duration_seconds": 900 }
    },
    {
      "label": "采银矿直到仓库有 200 个",
      "op": "start_action",
      "params": { "entity": "MelvorBase:Mining", "component": "Action", "recipe": "MelvorBase:SilverOre" },
      "until": { "item_qty": { "item": "MelvorBase:SilverOre", "gte": 200 }, "duration_seconds": 3600 }
    },
    {
      "label": "锻造 100 次银锭",
      "op": "start_action",
      "params": { "entity": "MelvorBase:Smithing", "component": "Action", "recipe": "MelvorBase:SilverBar" },
      "until": { "action_count": 100, "until_exhausted": true }
    },
    {
      "label": "提交 MQ6 任务材料",
      "op": "quest_submit",
      "params": { "quest": "MelvorBase:MQ6" }
    },
    {
      "label": "长期保底挂机：继续采红宝石",
      "op": "start_action",
      "params": { "entity": "MelvorBase:Mining", "component": "Action", "recipe": "MelvorBase:Ruby" },
      "until": { "duration_seconds": 28800 }
    }
  ]
}
```

### 支持的停止条件（`until`，默认满足**任一**条件即进入下一步；设置 `"until_mode": "all"` 则需全部满足）
- `duration_seconds`: 运行满 N 秒
- `action_count`: 主动作完成满 N 次（基于 `GameEventBus.ActionPerformed` 精确计数，忽略子挥击）
- `sub_action_count`: 子动作（如采矿每次挥镐/伐木每次挥斧）满 N 次
- `kill_count`: 击杀敌人满 N 只（基于 `Enemy.Killed`）
- `dungeon_count`: 通关地下城满 N 次（基于 `CombatAreas.DungeonCompleted`）
- `item_qty`: `{ "item": "MelvorBase:...", "gte": 200 }` 或 `{ "item": "...", "lte": 0 }`
- `item_gained`: `{ "item": "MelvorBase:...", "amount": 50 }`（相对本步开始时的净增量）
- `skill_level`: `{ "skill": "Mining", "gte": 65 }`
- `quest_step_done`: `{ "quest": "MelvorBase:MQ6", "step": 0 }`（等待指定任务步骤完成）
- `until_exhausted`: `true`（当游戏自身停止动作时——如材料用完——自动平滑推进到下一步）
