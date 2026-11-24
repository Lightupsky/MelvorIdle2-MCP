# Melvor Idle 2 MCP 连接器（V0.0.1-0116a1）— Agent 交接与开发文档

## 目标

为 Melvor Idle 2（Godot 4.7 + .NET 10 桌面游戏，Steam EA `v0.11.6-alpha.1`）提供完整的 MCP 接入、离线数据提取与**内置自主计划队列引擎**，实现：

1. **全离线知识库提取（无需再开游戏）**：直接解析 `Melvor2.exe` 内嵌 Godot PCK 中的 `Data/Packages/MelvorBase.json` + `Locale/zh_CN.po` 与反编译 `GamesByMalcs.Melvor2.dll`，1 秒内生成全物品、商店真实价格、37 个主/支线任务、全部战斗区域/地下城/敌人掉落表与 25 技能配方。
2. **离线存档全量解析**：即使游戏关闭，也能从 `.sav` 解析技能/XP、仓库、货币、**任务进度**、已购商店、装备、工具带、食物、战斗/法术/祷告与屠魔任务。
3. **全覆盖游戏操控**：桥接支持 30+ 项操作（全部采集/工匠/支援技能、战斗选怪/地下城/吃食物/法术/祷告/姿态/换装/工具带/Loadout/屠魔、商店购买、银行卖/开箱/埋骨/升级、任务提交与领奖、农耕与牧场一键维护、篝火、占星节点、敏捷捷径）。
4. **内置自主计划队列调度器（Plan Scheduler）**：Agent 一次编排好包含多个步骤（定时 / 定执行次数 / 定击杀数 / 定物品数量 / 定增量 / 定技能等级 / 定任务完成 / 材料耗尽自动切换）的 Plan JSON 下发给游戏内桥接后即可退出，由游戏内调度器自动按顺序执行数小时，玩家随时或事后再召唤 Agent 验收并规划下一轮。

## 架构总览

```
Melvor2-MCP-V0.0.1-0116a1/
├── README.md                        对外使用与安装文档
├── CHANGELOG.md                     版本日志 (V0.0.1-0116a1)
├── Setup.ps1                        一键配置向导（.venv + 离线提取 + 桥接安装 + MCP JSON）
├── StartDashboard.ps1               独立可视化面板启动脚本
├── melvor2_mcp/                     Python MCP 包 (FastMCP)
│   ├── save_parser.py               二进制存档解析（含 Quests/Shop/Equipment/Toolbelt/Food/Combat/Slayer）
│   ├── save_io.py                   存档定位 + SaveDecompress 调用
│   ├── plan_validator.py            离线 Plan 静态校验 + 知识库中英文检索
│   ├── dashboard.py                 独立可视化面板服务（http://127.0.0.1:17818，支持离线回退）
│   ├── server.py                    FastMCP 服务器（离线读 + 知识库 + Plan 调度 + 实时操控 + /health）
│   └── __main__.py
├── dashboard/
│   └── index.html                   单页可视化调度与状态面板（同时嵌入到桥接 DLL 的 GET /）
├── bridge/                          游戏内 .NET 桥接（纯反射 + System.Text.Json，net8.0 编译加载于游戏 .NET 10）
│   ├── Melvor2BridgeHook.csproj
│   ├── StartupHook.cs               Bootstrap 入口、HTTP :17817 路由、内嵌面板 GET /、RunOnMain 主线程封送
│   ├── CompatibilityGuard.cs        EA 版本反射自检、熔断器、优雅退场与 melvor2_compat_report.json 诊断日志
│   ├── StateQueries.cs              /state /bank /quests /actions /shop /combat /passive /probe
│   ├── GameOperations.cs            /op 统一执行器（30+ 游戏操作，采集防 toggle 误关）
│   ├── PlanScheduler.cs             /plan/* 自主计划队列调度器 + EventBus/Enemy.Killed 事件精准计数 + JSON 持久化
│   ├── hostfxr_wrapper.c / .S / .def  hostfxr 原生包装器（含预编译 hostfxr_wrapper.dll）
│   ├── InstallBridge.ps1            便携式安装脚本（自动探测 Steam 目录，默认使用预编译 DLL，-Rebuild 可重编译）
│   └── UninstallBridge.ps1          一键无痕卸载脚本
├── knowledge/                       全离线知识库
│   ├── extract_offline.py           自动探测 Melvor2.exe 并离线提取 MelvorBase.json + zh_CN.po + en_GB.po
│   ├── generate.py                  生成全部 markdown + data/items_index.json + data/quests_index.json
│   ├── quests.md / mechanics.md / planning.md / interlinks.md
│   └── skills/*.md                  全技能配方、战斗区域/地下城/敌人掉落、商店完整价格、物品升级/宠物
└── tools/SaveDecompress/            .NET BrotliStream 存档解压器（含预编译 exe）
```

## 常用维护命令

### 1. 部署更新后的桥接 DLL（游戏关闭时执行）
```powershell
.\bridge\InstallBridge.ps1            # 使用预编译 DLL
.\bridge\InstallBridge.ps1 -Rebuild   # 修改 C#/C 源码后重新编译并部署
```
> 若 EA 游戏更新或 Steam 校验覆盖了 `<data>\hostfxr.dll`，重新运行 `InstallBridge.ps1` 即可恢复。

### 2. EA 更新后离线刷新知识库（无需启动游戏）
```powershell
.\.venv\Scripts\python.exe knowledge\extract_offline.py
.\.venv\Scripts\python.exe knowledge\generate.py
```
如需同步重新反编译 `GamesByMalcs.Melvor2.dll`：`extract_offline.py --decompile`（需安装 `ilspycmd`）。

### 3. 启动可视化调度监控网页
- **内置模式（游戏运行时免额外进程）**：浏览器直接打开 `http://127.0.0.1:17817/`。
- **独立代理模式（支持游戏关闭时的离线存档回退）**：`.\StartDashboard.ps1 -Open`

## EA 更新兼容性保护与故障排查（`CompatibilityGuard`）

1. **原生层（`hostfxr_wrapper.c`）**：若 Steam 更新还原了 `hostfxr.dll` 或加载 `Melvor2BridgeHook.dll` 失败，包装器仅记录 `hostfxr_wrapper.log` 并继续放行 Godot 启动，绝不阻塞游戏。
2. **托管层启动与自检（`CompatibilityGuard.cs`）**：
   - `Bootstrap`/`Initialize` 全程异常兜底，不向原生边界抛异常。
   - 存档加载绑定 `Game` 时自动对 Core 与各子系统执行反射自检，并输出结构化报告至 `%APPDATA%\Games By Malcs\Melvor Idle 2\logs\melvor2_compat_report.json`（也可通过 `GET /health`、网页顶部徽章或 MCP `get_bridge_health()` 查看）。
   - **部分降级（`degraded`）**：记录缺失成员及反射探测到的**候选成员名列表（`candidate_members`）**，其余功能继续运行。
   - **致命不兼容或连续 5 次运行异常熔断（`incompatible`）**：自动停止 `PlanScheduler`、禁用所有写入操作接口，保留只读诊断接口，确保游戏本体 100% 稳定运行。

## 自主计划调度器（Plan Scheduler）说明

- 持久化路径：`%APPDATA%\Games By Malcs\Melvor Idle 2\melvor2_plan.json`
- **支持离线提交**：游戏关闭时调用 `set_action_plan(plan)` 会将计划以 `status: "pending_start"` 写入文件，待玩家启动游戏并进入存档后自动开始执行。
- **防冲突与安全守卫**：
  - `pause_on_manual_override: true`（默认）：若玩家在游戏里手动切换了其他技能/配方，调度器自动暂停而非和玩家抢操作；
  - `safety: { min_food_qty, stop_on_low_food: true }`：战斗/偷窃食物低于阈值时自动停战并暂停计划。
