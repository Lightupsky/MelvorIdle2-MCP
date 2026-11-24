# Melvor Idle 2 LLM 游玩与批处理计划规划指南

> **核心范式转变（从实时操控 → 批处理计划队列）**：
> Melvor Idle 2 是中长周期放置游戏，不需要 Agent 高频轮询。
> 正确工作流是：**玩家召唤 Agent → 读取最新存档/状态 → 一次规划好一整串多步骤行动计划（Action Plan）并提交给游戏内置调度器 → Agent 退出休息 → 游戏自动按定时/定次数/定目标顺序切换执行数小时 → 玩家稍后再次召唤 Agent 验收成果并规划下一轮！**

- **可视化监控**：游戏运行时可直接在浏览器打开 `http://127.0.0.1:17817/`（`v2.1+` 桥接内嵌）或运行 `python -m melvor2_mcp.dashboard --open`（`http://127.0.0.1:17818/`，支持离线回退），实时查看未来全部规划步骤、当前进度条、执行次数与暂停/跳过控制。
- **EA 兼容性检查**：若游戏刚更新，可先调用 `get_bridge_health()` 确认桥接处于 `healthy` 状态；若出现 `incompatible`，查看 `logs/melvor2_compat_report.json` 即可定位需要适配的接口。

---

## 一、标准规划工作流

1. **验收与盘点状态**：
   - 调用 `get_plan_status` 查看上一轮计划的执行日志、完成步数或中断原因。
   - 调用 `get_game_state` + `get_bank_items` + `get_quests`（或游戏运行时直接用 `get_live_state` / `get_live_quests`）掌握当前等级、GP、装备、工具、仓库与任务进度。
2. **确定本轮目标（主线任务优先 or 任务+练级并行）**：
   - 检查当前可推进的 **主线任务（Act 1 `MQ1`→`MQ8` → Act 2 `MQ9`→`MQ17`）** 和 **支线任务（`SideQuests`）**（查 `browse_knowledge("quests")`）。
   - 如果当前技能/战力已经满足下一步任务要求 → **主线优先**：直接把“备料 → 制作/击杀 → 提交任务 → 领奖”编排进同一个计划队列！
   - 如果当前等级或装备距离下一主线还有差距 → **并行推进**：把“挂机冲级/攒装备/买关键商店升级（如 Auto-Eat、银行格、高级工具）+ 顺手完成同等级段支线任务”编排进队列，同时穿插被动技能（Farming 收获补种、Ranching 维护）。
3. **查表校验**：
   - 用 `browse_knowledge("skills:<name>")` 或 `search_knowledge(query)` 确认准确配方 ID、等级要求、材料消耗与商店价格。
   - 查 `interlinks.md` 递归回溯上游原料，确保队列中“上游采集步”产出的数量足够“下游制作步”消耗。
4. **编排并验证计划队列**：
   - 用 `validate_plan(plan)` 离线静态校验所有 ID、等级门槛、前置材料与安全性。
   - 用 `set_action_plan(plan)` 下发到游戏内置调度器；若游戏当前未运行，也可保存为待执行计划（或启动游戏后自动恢复）。
5. **收尾建议**：
   - 队列最后一步通常安排一个**可持续长时间挂机的采集或训练动作**（如采矿/伐木/钓鱼/观星，不设过短上限或设为 8–24h），避免队列提前跑完导致角色发呆浪费时间！

---

## 二、任务优先 + 并行规划策略

### 1. 主线任务线（Act 1 → Act 2 速查）
- **Act 1**：`MQ1` (Ranged/石箭) → `MQ2` (Fishing 6, Smithing 6) → `MQ3` (WC 10, Mining 10, Fishing 23, Cooking 27) → `MQ4` (Smithing 12, Crafting 12) → `MQ5` (Mining 18, RC 20, Smithing 22, Crafting 25, 战斗 Lv25) → `MQ6` (WC 25, Smithing 25, 10× Silver Bar) → `MQ7` (Farming 21, Cooking 23, WC 30, FM 30, 战斗 Lv35) → `MQ8` (WC 39, FM 39, 战斗 Lv39 Pinnacle Boss)。
- **Act 2**：`MQ9` (Smithing 35, Hunting 35, Cooking 40) → `MQ10` (Thieving 32, FM 42, Crafting/WC/Hunting 45, Beastcrafting 20, 解锁 Silted Oasis) → `MQ11`–`MQ17`（45→65+ 全面技能与战斗）。
- **为什么优先推进主线？**
  - 单步奖励动辄 20,000–80,000 GP、数百个金属锭/原木/符文/食物，**收益远高于纯靠刷怪卖货**；
  - 部分关键区域与配方（如 `MQ5` 后的矿区、`MQ10` 解锁的 Silted Oasis）被主线锁住。

### 2. 并行协同技巧
- **被动技能穿插**：在队列开头或每隔 1–2 小时插入 `farming_harvest_all` / `farming_plant_all` / `ranching_maintain` 一键维护步骤（瞬间执行，不中断长期节奏）。
- **商店与装备自动升级**：在队列中当某技能达标或 GP 达标后，直接插入 `shop_buy` → `equip_tool` / `equip` → 切换更高阶配方，实现“练级 → 买工具/做新装备 → 换上 → 效率翻倍继续练”的全自动闭环！
- **任务自动闭环**：例如“采矿直到银矿 ≥ 10 → 锻造银锭 10 次 → `quest_submit` 提交 MQ6 Step 0 → `quest_claim` 领奖”，全程零人工介入。

---

## 三、批处理计划（Action Plan）编排规范

一个 Plan JSON 由 `name`、`pause_on_manual_override`（默认 `true`，防止和玩家手动操作打架）和有序的 `steps` 列表组成：

```json
{
  "name": "MQ6 备料 + 银锭冲级计划",
  "pause_on_manual_override": true,
  "steps": [
    {
      "label": "收割并重种农田",
      "op": "farming_harvest_all"
    },
    {
      "label": "购买升级工具",
      "op": "shop_buy",
      "params": { "purchase": "MelvorBase:IronPickaxe", "quantity": 1 }
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
      "label": "长期保底挂机：继续采矿",
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
- `kill_count`: 击杀敌人满 N 只（基于 `Enemy.Killed`）
- `dungeon_count`: 通关地下城满 N 次（基于 `CombatAreas.DungeonCompleted`）
- `item_qty`: `{ "item": "MelvorBase:...", "gte": 200 }` 或 `{ "item": "...", "lte": 0 }`
- `item_gained`: `{ "item": "MelvorBase:...", "amount": 50 }`（相对本步开始时的净增量）
- `skill_level`: `{ "skill": "Mining", "gte": 65 }`
- `quest_step_done`: `{ "quest": "MelvorBase:MQ6", "step": 0 }`（等待指定任务步骤完成）
- `until_exhausted`: `true`（当游戏自身停止动作时——如材料用完——自动平滑推进到下一步）

### 安全守卫（`safety`）
- 战斗 / 偷窃步强烈建议配置：`select_food` 确保食物充足 + `"safety": { "min_food_qty": 20, "stop_on_low_food": true }`，防止挂机暴毙！
