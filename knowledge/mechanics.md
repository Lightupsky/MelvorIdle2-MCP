# Melvor Idle 2 机制详解（全离线反编译 + PCK 数据提取）

> 数据来源：`Melvor2.exe` 内嵌 PCK 中的 `Data/Packages/MelvorBase.json` + 反编译 `GamesByMalcs.Melvor2.dll`。
> 游戏版本更新后无需启动游戏，直接运行 `python extract_offline.py && python generate.py` 即可刷新全部数值。

## 核心循环与并行机制

- **主动作（Active Action）**：同一时间最多运行 **1 个**主要主动动作（采集 / 工匠 / 支援 / 战斗）；切换主动动作会自动停止旧动作。
- **被动技能（Passive Skills）**：**Farming（农耕）**与**Ranching（牧场）**在后台持续运转，与主动作完全并行。农田可配置自动浇水/堆肥/收割/补种（需商店解锁 Farmhand / Sprinklers）；牧场动物需要定期维护/喂食/抚摸，否则会生病或死亡。
- **离线进度**：离线自动推进，基础上限 **24 小时**（`BaseMaxOfflineTime = 86,400,000 ms`），可被 `FlatOfflineTime` 修饰符延长。

## 经验与等级公式（StandardExperienceTable）

- 代码验证公式（`StandardExperienceTable.Default`）：
  - `XpSum(i) = XpSum(i-1) + floor(i + 300 * 2^(i/7))`
  - `XpForLevel(L) = floor(0.25 * XpSum(L-1))`
- 技能等级上限 **99**（共 25 项技能）。
- 参考节点：Lv.2 = 83，Lv.10 = 1,154，Lv.20 = 4,431，Lv.50 = 103,862，Lv.70 = 737,627，Lv.99 = 13,034,431。

## 采集技能的真实产出公式（重要）

- **Mining（采矿）**：每次挥镐耗时 `BaseInterval`（默认 1.5s），按 `BaseProductChance`（加上工具与修饰符加成）决定是否破岩产出矿石并给经验。
  - **期望单矿耗时** `≈ Interval / ProductChance`（如 Ruby 基础破岩率 5%，1.5s / 0.05 = 30s/个，绝不能用 `xp / interval` 误算！）。
  - 每次挥镐独立判定 `RandomProductsPerHit`（宝石/Geode）。
- **Woodcutting（伐木）**：每棵树拥有 `BaseMaxHealth`（血量），每次挥斧耗时 `BaseInterval` 并扣血，砍倒获得产物；`RandomProductsPerHit` 每次挥斧判定，`RandomProductsPerCut` 砍倒判定。
- **Fishing（钓鱼）**：间隔在 `[BaseMinInterval, BaseMaxInterval]` 随机；成功率为 `FishChance`（否则掉 Junk），从 `RandomProducts` 权重表抽取鱼种，独立判定 `SpecialProducts`。
- **Hunting（狩猎）**：两阶段（追踪 Tracking + 设陷 Trapping），受 `Tool` / `TrapType` / `PreferredBait` 与各自成功率影响；满足条件可解锁无陷阱（Trapless）。
- **Thieving（偷窃）**：存在 `Stages` 阶段与 `Perception`（警觉度）；失败会被击晕并受到 `Damage` 伤害（需配食物防暴毙）。

## 任务系统（Quests，二代核心新增）

- 共 **37** 个任务（详见 `quests.md`）：
  - **主线 Act 1（`MQ1`–`MQ8`）→ Act 2（`MQ9`–`MQ17`）**：严格按 `QuestComplete` 串行推进，解锁新战斗区域（如 Silted Oasis）、提供海量启动资金（数万 GP）、稀有装备、工具与大宗材料奖励。
  - **主线后续（`MQ18`–`MQ27`）**：EA 阶段占位/后续章节。
  - **支线任务（`SideQuests`，10 个）**：大多无前置任务限制，可直接与技能练级并行完成，奖励同样丰厚。
- 每个任务由若干 **Step** 顺序组成，每个 Step 下有按序执行的 **Task**，Task 包含三类目标：
  1. **Goals（事件计数型）**：如击杀指定敌人（可限定战斗风格如 Ranged）、采矿/伐木/制作/钓鱼/狩猎/窃取/通关地下城等；
  2. **ConditionGoals（状态条件型）**：如敏捷技巧等级、占星主节点解锁数；
  3. **Costs（材料/货币提交型）**：需要凑齐物品后调用 `quest_submit`（游戏内 `QuestsComponent.ExecuteTaskCosts`）提交。
- 所有步骤完成后需调用 `quest_claim`（`QuestsComponent.ClaimRewards`）领取奖励并正式标记完成。

## 战斗系统（Combat）

- **战斗三角（Combat Triangle）**：Melee / Ranged / Magic 之间存在克制加成；此外二代新增 **5 元素（Element）克制**与 **攻击姿态（AttackStance）**。
- **战斗掉落**：二代战利品由 `CombatLootComponent` **自动入库**（无需手动点击战利品箱），受 `ItemDoublingChance` 与 `BuriableDoublingChance` 加成。
- **食物与自动吃药（FoodComponent）**：需先用 `select_food` 装备食物；生命值低于自动吃血阈值会自动进食（可在商店购买 Auto-Eat 升级）。
- **装备套装（LoadoutComponent）**：支持多套 Loadout（装备 + 工具带 + 食物 + 祷告 + 法术 + 姿态一键切换 `swap_loadout`）。
- **地下城（Dungeon）与屠魔（Slayer）**：地下城按波次连续击杀并掉落通关宝箱；Slayer Master 发布击杀任务产出 Slayer Coins。

## 货币与商店

| 货币 | ID | 来源 / 用途 |
|---|---|---|
| 金币 GP | `MelvorBase:GoldPieces` | 卖出物品、任务奖励、偷窃 / 商店购买（工具、银行格、Auto-Eat、Farmhand） |
| 灵魂币 Slayer Coins | `MelvorBase:SlayerCoins` | 屠魔任务 / 刷新任务、屠魔商店 |
| 祈祷点 Prayer Points | `MelvorBase:PrayerPoints` | 埋葬骨头（`bank_bury`）恢复 / 战斗祷告消耗 |

## 桥接与自动化计划引擎

- **桥接 HTTP API**（`127.0.0.1:17817`，运行于游戏进程内）支持读取完整状态、执行全量游戏操作，并内置**自主计划队列调度器（Plan Scheduler）**。
- 调度器直接订阅 `GameEventBus.ActionPerformed`、`Enemy.Killed`、`CombatAreas.DungeonCompleted`，支持按 **时长 / 执行次数 / 击杀数 / 物品库存 / 物品增量 / 技能等级 / 任务目标完成 / 材料耗尽** 自动切换下一步，**全程无需 Agent 保持运行**。
