# Changelog — Melvor2-MCP

## V0.0.1-0116a1 (2026-11-24)

> **MCP 插件版本**：`V0.0.1`  
> **适配游戏版本**：`Melvor Idle 2 v0.11.6-alpha.1`（`0116a1`，Godot 4.7.2 / SaveVersion `Alpha0_11_5_5`）  
> **内置桥接版本**：`Melvor2BridgeHook v2.1.0`

### 首个公开测试版本核心内容
- **全离线知识库提取引擎（`knowledge/extract_offline.py` + `generate.py`）**：
  - 支持直接从 `Melvor2.exe` 内嵌 Godot 4 PCK 提取 `MelvorBase.json` 及中英文语言包，自动生成 1,811 物品、37 任务、499 商店、192 敌人掉落、25 技能配方与 519 条跨系统流转文档。
  - 新增 Windows Steam 多库路径自动探测，解压到任意目录均可一键提取。
- **离线二进制存档全量解析（`save_parser.py` + `SaveDecompress.exe`）**：
  - 支持解析技能/XP、银行仓库、货币、主/支线任务进度、商店购买、装备、工具带、食物、战斗配置与屠魔任务。
- **游戏内自主计划队列调度器（`PlanScheduler`）**：
  - 支持 8 种条件切换（定时 / 次数 / 击杀 / 地下城 / 物品目标 / 增量 / 技能等级 / 任务步骤 / 材料耗尽自动切换）。
  - 支持离线提交计划与持久化断点续跑；内置手动操作礼让与低食物自动停战守卫。
- **全覆盖实时操控桥接（`Melvor2BridgeHook.dll` + `hostfxr_wrapper.dll`）**：
  - 纯反射零编译依赖，适配游戏内置 .NET 10 运行时；覆盖 30+ 项游戏操作与防采集误关保护。
  - 新增 `CompatibilityGuard` 反射自检、异常熔断与诊断日志。
- **双模式可视化面板（`dashboard/index.html`）**：
  - 内置 `http://127.0.0.1:17817/` 零额外进程访问 + 独立 `http://127.0.0.1:17818/` 离线回退模式。
- **对外发布便携化改进**：
  - 新增一键配置向导 `Setup.ps1` 与一键无痕卸载脚本 `bridge/UninstallBridge.ps1`。
  - `bridge/InstallBridge.ps1` 支持 Steam 自动定位与默认使用预编译 DLL（免安装 .NET SDK / GCC）。
