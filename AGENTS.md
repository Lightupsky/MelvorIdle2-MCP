# Melvor Idle 2 MCP 连接器（V0.0.2-0117a）— Agent 交接与开发文档

> 当前离线数据已同步至 **Melvor Idle 2 `v0.11.7-alpha`**（2026-11-25 提取自 `Melvor2.exe` PCK）。

## 目标

为 Melvor Idle 2（Godot 4.7 + .NET 10 桌面游戏，Steam 版 EA 阶段）提供完整的 MCP 接入、离线数据提取、**多维度规划支持**与**内置自主计划队列引擎**，实现：

1. **全离线知识库提取（无需开游戏）**：直接解析 `Melvor2.exe` 内嵌 Godot PCK 中的 `Data/Packages/MelvorBase.json` + `Locale/zh_CN.po`，1 秒内生成全物品（1,811）、商店真实价格（499）、37 个主/支线任务、**25 技能专精公会（3,251 个专精任务与 150 项段位加成）**、42 套装备套装、192 敌人掉落表与 25 技能配方。
2. **离线存档全量解析**：即使游戏关闭，也能从 `.sav` 解析技能/XP、仓库、货币、任务进度、**25 技能专精公会任务进度**、已购商店、装备、工具带、食物、战斗/法术/祷告、屠魔任务、宠物、生火篝火与敏捷技巧。
3. **全覆盖游戏操控**：桥接支持 30+ 项操作（全部采集/工匠/支援技能、战斗选怪/地下城/吃食物/法术/祷告/姿态/换装/工具带/Loadout/屠魔、商店购买、银行卖/开箱/埋骨/升级、任务提交与领奖、农耕与牧场一键维护、篝火、占星节点、敏捷捷径）。
4. **内置自主计划队列调度器（Plan Scheduler）**：Agent 综合六大维度（主线/支线任务、技能专精任务、工具带/装备套装、占星/篝火/图鉴、农耕/牧场被动、战斗生态）一次编排好 Plan JSON 下发给桥接后即可退出，由游戏内调度器自动按顺序执行数小时。

## 架构总览

```
Melvor2-MCP-V0.0.2-0117a/
├── README.md                        对外使用与安装文档
├── CHANGELOG.md                     版本日志 (V0.0.2-0117a)
├── Setup.ps1                        一键配置向导（.venv + 离线提取 + 桥接安装 + MCP JSON）
├── StartDashboard.ps1               独立可视化面板一键启动脚本（http://127.0.0.1:17818）
├── melvor2_mcp/                     Python MCP 包 (FastMCP，共 27 个工具)
│   ├── save_parser.py               二进制存档解析（含 Quests/MasteryGuild/Shop/Equipment/Toolbelt/Food/Combat/Slayer/Pets/Bonfire/Techniques）
│   ├── save_io.py                   存档定位 + SaveDecompress 调用
│   ├── plan_validator.py            离线 Plan 静态校验 + 专精公会离线进度计算 + 知识库中英文检索
│   ├── dashboard.py                 独立可视化面板代理服务（http://127.0.0.1:17818，支持离线回退与专精展示）
│   ├── server.py                    FastMCP 服务器（离线读 + 专精任务查询 + 知识库 + Plan 调度 + 实时操控 + /health）
│   └── __main__.py
├── dashboard/
│   └── index.html                   「羊皮手账 / 暗墨黄铜」双主题可视化调度面板（同步嵌入到桥接 DLL 的 GET /）
├── bridge/                          游戏内 .NET 桥接（v2.2.0，纯反射 + System.Text.Json，net8.0 加载于游戏 .NET 10）
│   ├── Melvor2BridgeHook.csproj
│   ├── StartupHook.cs               Bootstrap 入口、HTTP :17817 路由、内嵌面板 GET /、RunOnMain 主线程封送
│   ├── CompatibilityGuard.cs        EA 版本反射自检（含 MasteryGuild）、熔断器、优雅退场与 melvor2_compat_report.json
│   ├── StateQueries.cs              /state /bank /quests /mastery /actions /shop /combat /passive /probe
│   ├── GameOperations.cs            /op 统一执行器（30+ 游戏操作，含采集防 toggle 与同技能工匠配方切换修复）
│   ├── PlanScheduler.cs             /plan/* 自主计划队列调度器 + EventBus/Enemy.Killed 精准计数 + JSON 持久化
│   ├── hostfxr_wrapper.c / .S / .def  hostfxr 原生包装器（含预编译 hostfxr_wrapper.dll）
│   ├── InstallBridge.ps1            便携式安装脚本（自动探测 Steam 目录，默认使用预编译 DLL，-Rebuild 可重编译）
│   └── UninstallBridge.ps1          一键无痕卸载脚本
├── knowledge/                       全离线知识库
│   ├── extract_offline.py           自动探测 Melvor2.exe 并离线提取 MelvorBase.json + zh_CN.po + en_GB.po
│   ├── generate.py                  生成全部 markdown + data/items_index.json + quests_index.json + mastery_index.json
│   ├── quests.md                    全 37 个任务（Act 1/2 主线 + 支线：前置、步骤目标、提交材料、奖励）
│   ├── mastery.md                   全 25 技能专精公会段位加成速查表与 Novice/Easy 速刷任务示例
│   ├── mechanics.md                 经反编译源码验证的核心机制与公式
│   ├── planning.md                  六大维度规划框架（任务 + 专精公会 + 套装/工具带 + 永久加成 + 被动 + 战斗）与队列规范
│   ├── interlinks.md                519 条跨技能/战斗/任务/商店物品流转表
│   └── skills/*.md                  全技能配方（含农耕自动化消耗）、战斗掉落、商店完整价格、42 装备套装/升级/宠物/图鉴/劫案
└── tools/SaveDecompress/            .NET BrotliStream 存档解压器（含预编译 exe）
```

## 常用维护命令

### 1. 部署更新后的桥接 DLL（游戏关闭时执行）
```powershell
.\bridge\InstallBridge.ps1            # 使用预编译 DLL
.\bridge\InstallBridge.ps1 -Rebuild   # 修改 C#/C 源码后重新编译并部署
```
> 若 EA 游戏更新或 Steam 校验覆盖了 `<data>\hostfxr.dll`，关闭游戏后重新运行 `InstallBridge.ps1` 即可恢复并同步最新桥接与内嵌面板。

### 2. EA 更新后离线刷新知识库（无需启动游戏）
```powershell
.\.venv\Scripts\python.exe knowledge\extract_offline.py
.\.venv\Scripts\python.exe knowledge\generate.py
```
如需同步重新反编译 `GamesByMalcs.Melvor2.dll`：`extract_offline.py --decompile`（需安装 `ilspycmd`）。

### 3. 启动可视化调度手账网页
- **内置模式（部署 `v2.2.0` 桥接后，游戏运行时免额外进程）**：浏览器直接打开 `http://127.0.0.1:17817/`。
- **独立代理模式（支持游戏关闭时的离线存档回退）**：`.\StartDashboard.ps1 -Port 17818 -Open`

## EA 更新兼容性保护与故障排查（`CompatibilityGuard`）

1. **原生层（`hostfxr_wrapper.c`）**：若 Steam 更新还原了 `hostfxr.dll` 或加载 `Melvor2BridgeHook.dll` 失败，包装器仅记录 `hostfxr_wrapper.log` 并继续放行 Godot 启动，绝不阻塞游戏。
2. **托管层启动与自检（`CompatibilityGuard.cs`）**：
   - `Bootstrap`/`Initialize` 全程异常兜底，不向原生边界抛异常。
   - 存档加载绑定 `Game` 时自动对 Core 与 7 大子系统（`Bank`/`Quests`/`Shop`/`Combat`/`EventBus`/`Passive`/`MasteryGuild`）执行反射自检，并输出结构化报告至 `%APPDATA%\Games By Malcs\Melvor Idle 2\logs\melvor2_compat_report.json`（可通过 `GET /health`、网页「EA 兼容诊断」页签或 MCP `get_bridge_health()` 查看）。
   - **部分降级（`degraded`）**：记录缺失成员及反射探测到的**候选成员名列表（`candidate_members`）**，其余功能继续运行。
   - **致命不兼容或连续 5 次运行异常熔断（`incompatible`）**：自动停止 `PlanScheduler`、禁用所有写入操作接口，保留只读诊断接口，确保游戏本体 100% 稳定运行。

## 自主计划调度器（Plan Scheduler）说明

- 持久化路径：`%APPDATA%\Games By Malcs\Melvor Idle 2\melvor2_plan.json`
- **支持离线提交**：游戏关闭时调用 `set_action_plan(plan)` 会将计划以 `status: "pending_start"` 写入文件，待玩家启动游戏并进入存档后自动开始执行。
- **防冲突与安全守卫**：
  - `pause_on_manual_override: true`（默认）：若玩家在游戏里手动切换了其他技能/配方，调度器自动暂停而非和玩家抢操作；
  - `safety: { min_food_qty, stop_on_low_food: true }`：战斗/偷窃食物低于阈值时自动停战并暂停计划。
