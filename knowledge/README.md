# Melvor Idle 2 知识库（全离线自动提取）

> 数据来源：`Melvor2.exe` 内嵌 PCK 中的 `Data/Packages/MelvorBase.json` + `Locale/zh_CN.po`（Godot 4.7.2，提取时间 `2026-11-25T09:50:47.339017+00:00`）。
> **无需启动游戏**：游戏 EA 版本更新后，只需运行：
> ```bash
> cd Tools/melvor2-mcp/knowledge && python extract_offline.py && python generate.py
> ```

## 数据覆盖范围

- **物品**：1811 件（含售价、装备属性、食物回血、工具带、开箱/埋葬属性，索引见 `data/items_index.json`）
- **任务（Quests）**：37 个（Act 1/2 主线 + 支线 + 步骤/材料/奖励，见 `quests.md`）
- **技能专精公会（Mastery Guild）**：25 技能 / 3,403 个专精任务 / 175 段位 / 150 永久加成（见 `mastery.md` 与 `data/mastery_index.json`）
- **商店（Shop）**：499 件（含**真实价格**、限购、解锁条件与加成，见 `skills/shop.md`）
- **战斗（Combat）**：64 区域 / 14 地下城 / 192 敌人与掉落表 / 48 法术 / 41 祷告 / 10 屠魔大师（见 `skills/combat.md`）
- **装备套装 / 升级 / 宠物**：42 套装 / 124 升级 / 36 宠物（见 `skills/upgrades.md`）
- **跨系统物品联动**：519 条（见 `interlinks.md`）

## 文档导航（MCP `browse_knowledge(topic)`）

| Topic | 说明 |
|---|---|
| `readme` | 本索引 |
| `quests` | **主线/支线任务全攻略（步骤、提交材料、目标、奖励）** |
| `mastery` | **25 技能专精公会段位加成与速刷专精任务总览** |
| `mechanics` | 核心机制、公式（XP、破岩率、伐木血量、战斗三角/元素、离线、被动技能） |
| `planning` | **多维度规划（主线 + 专精公会 + 套装/工具带 + 被动技能）与批处理队列规范** |
| `interlinks` | 跨技能 / 战斗 / 任务 / 商店物品流转表 |
| `skills:agility` | `agility.md` |
| `skills:astrology` | `astrology.md` |
| `skills:beastcrafting` | `beastcrafting.md` |
| `skills:combat` | `combat.md` |
| `skills:cooking` | `cooking.md` |
| `skills:crafting` | `crafting.md` |
| `skills:farming` | `farming.md` |
| `skills:firemaking` | `firemaking.md` |
| `skills:fishing` | `fishing.md` |
| `skills:fletching` | `fletching.md` |
| `skills:herblore` | `herblore.md` |
| `skills:hunting` | `hunting.md` |
| `skills:mining` | `mining.md` |
| `skills:ranching` | `ranching.md` |
| `skills:runecrafting` | `runecrafting.md` |
| `skills:shop` | `shop.md` |
| `skills:smithing` | `smithing.md` |
| `skills:thieving` | `thieving.md` |
| `skills:upgrades` | `upgrades.md` |
| `skills:woodcutting` | `woodcutting.md` |
