# Changelog — Melvor2-MCP

## V0.0.2-0117a (2026-11-25)

> **MCP 插件版本**：`V0.0.2`  
> **适配游戏版本**：`Melvor Idle 2 v0.11.7-alpha`（`0117a`，Godot 4.7.2 / SaveVersion `Alpha0_11_5_5`）  
> **内置桥接版本**：`Melvor2BridgeHook v2.2.0`

### 核心更新内容
- **同步 `0.11.7alpha` 游戏版本数据**：
  - 离线知识库全量同步至 `0.11.7alpha`（涵盖深渊锭/深渊钥匙 XP 调整、27 种农耕自动化消耗下调、烹饪 Rank3 +30 熟练度实装、8 种基础狩猎陷阱保底加成、敏捷奖章商店 11 子类细分与献祭砧规则更新等）。
- **新增技能专精公会（Mastery Guild）全链路支持**：
  - **知识库**：新增 `knowledge/mastery.md` 与 `knowledge/data/mastery_index.json`（覆盖全 25 技能共 **3,251 个专精任务**与 **150 项段位加成**，以及 42 套装备套装与图鉴/劫案信息）。
  - **离线解析与 MCP 工具**：`save_parser.py` 新增专精公会进度、宠物（Pets）、篝火（Bonfire）与敏捷技巧（Techniques）离线解析；MCP 扩展至 **27 个工具**，新增 `get_mastery_tasks` 查询各技能专精任务进度与推荐任务。
  - **游戏内桥接（`v2.2.0`）**：新增 `GET /mastery` 接口、`/state` 专精摘要字段，并将 `MasteryGuild` 纳入 `CompatibilityGuard` 7 大子系统反射自检。
- **六大维度规划框架升级**：
  - `knowledge/planning.md` 升级为六大维度规划规范（主/支线任务、技能专精公会、工具带/装备套装、永久加成、被动技能、战斗生态）。
- **可视化面板全面改版（`dashboard/index.html`）**：
  - 全新设计「羊皮手账 / 暗墨黄铜」双主题编辑排版界面，集成专精段位视图与 EA 兼容诊断矩阵（同步内嵌于 `Melvor2BridgeHook.dll` 及独立面板 `dashboard.py`）。

---

## V0.0.1-0116a1 (2026-11-24)

> **MCP 插件版本**：`V0.0.1`  
> **适配游戏版本**：`Melvor Idle 2 v0.11.6-alpha.1`（`0116a1`，Godot 4.7.2 / SaveVersion `Alpha0_11_5_5`）  
> **内置桥接版本**：`Melvor2BridgeHook v2.1.0`

### 首个公开测试版本核心内容
- **全离线知识库提取引擎（`knowledge/extract_offline.py` + `generate.py`）**：直接从 `Melvor2.exe` 内嵌 Godot 4 PCK 提取 `MelvorBase.json` 与中英文语言包，自动生成 1,811 物品、37 任务、499 商店、192 敌人掉落、25 技能配方与 519 条跨系统流转文档。
- **离线二进制存档全量解析（`save_parser.py` + `SaveDecompress.exe`）**：支持离线解析技能/XP、银行仓库、货币、主/支线任务进度、商店购买、装备、工具带、食物、战斗配置与屠魔任务。
- **游戏内自主计划队列调度器（`PlanScheduler`）**：支持 8 种条件切换、离线提交与断点续跑、手动操作礼让与低食物自动停战守卫。
- **全覆盖实时操控桥接（`Melvor2BridgeHook.dll` + `hostfxr_wrapper.dll`）**：纯反射零编译依赖，覆盖 30+ 项操作与 `CompatibilityGuard` 异常熔断保护。
- **双模式可视化面板与一键便携部署脚本**：提供 `Setup.ps1`、`InstallBridge.ps1`、`UninstallBridge.ps1` 与 `StartDashboard.ps1`。
