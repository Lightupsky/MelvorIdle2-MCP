<p align="center">
  <img src="Branding/Melvor2-MCP-Poster-V0.0.1-0116a1.png" alt="Melvor2-MCP V0.0.1-0116a1 Banner" width="100%" />
</p>

# MelvorIdle2-MCP (`Melvor2-MCP`)

> **给 Melvor Idle 2 装上 AI 自主大脑** — 完整 MCP（Model Context Protocol）连接器、全离线知识库提取引擎、内置自主计划队列调度器与可视化监控面板。
>
> **Release Version**: `V0.0.1-0116a1` · **[前往 Releases 下载压缩包与宣传视频](../../releases/tag/V0.0.1-0116a1)**
> - **MCP 插件版本**：`V0.0.1`（内置游戏桥接 `v2.1.0`）
> - **适配游戏版本**：`Melvor Idle 2 v0.11.6-alpha.1`（代号 `0116a1`，Godot 4.7.2 + .NET 10，Steam Early Access Windows x64）

---

## 核心特性 Highlights

1. **全离线知识库与存档解析（无需打开游戏）**
   - 直接解析 `Melvor2.exe` 内嵌 Godot PCK 中的 `MelvorBase.json` 与 `zh_CN.po` / `en_GB.po`，1 秒生成全量游戏知识库：
     - **1,811 件物品**（售价、装备属性、食物回血、工具带、开箱/埋葬）
     - **37 个主线/支线任务**（Act 1/2 前置、步骤目标、提交材料与奖励）
     - **499 件商店商品**（真实价格、限购与解锁条件）
     - **64 战区 / 14 地下城 / 192 敌人掉落表 / 48 法术 / 41 祷告 / 10 屠魔大师**
     - **25 项全技能配方**与 **519 条跨系统物品流转关系**
   - 内置 `.NET BrotliStream` 解压器（`SaveDecompress.exe`），游戏关闭时也能完整解析 `.sav` 存档中的技能/XP、仓库、货币、任务进度、装备、工具带、食物与屠魔任务。
   - 支持中英文双语模糊检索（`search_knowledge`），防止 AI 编造游戏内部 ID。

2. **内置自主计划队列调度器（Autonomous Plan Scheduler）**
   - 通过自然语言让 AI 一次编排多步骤挂机计划，经 `validate_plan` 静态校验后调用 `set_action_plan` 下发给游戏内桥接，**AI 即可退出，游戏内调度器自动按顺序执行数小时**。
   - 支持 8 种精准切换条件：`duration_seconds`（定时）、`action_count`（执行次数）、`kill_count`（击杀数）、`dungeon_count`（地下城通关）、`item_qty`（仓库目标）、`item_gained`（增量目标）、`skill_level`（技能等级）、`quest_step_done`（任务步骤完成）与 `until_exhausted`（材料耗尽自动切换）。
   - **支持离线提交**：游戏关闭时提交计划会写入 `%APPDATA%\Games By Malcs\Melvor Idle 2\melvor2_plan.json`，下次启动进档自动开跑。
   - **多重安全护卫**：玩家手动切换技能时自动暂停礼让（`pause_on_manual_override`）；战斗/偷窃食物低于阈值自动安全停战（`safety.min_food_qty`）。

3. **30+ 项全游戏实时操控（Live Bridge）**
   - 覆盖全部采集/工匠/支援技能、战斗选怪/地下城/吃食/换装/工具带/Loadout/法术/祷告/姿态/屠魔、商店购买、仓库卖货/开箱/埋骨/升级、主支线提交领奖、农耕与牧场一键维护等。
   - 零模拟键鼠点击，原生主线程封送执行，且具备采集防重复 toggle 误关保护。

4. **双模式可视化调度监控面板**
   - **内置模式（免额外进程）**：游戏运行时直接浏览器打开 `http://127.0.0.1:17817/`。
   - **独立离线回退模式**：运行 `.\StartDashboard.ps1 -Open`，打开 `http://127.0.0.1:17818/`，游戏关闭时也可离线浏览存档状态与计划队列。

5. **专为 Early Access 设计的兼容性自检与熔断保护（`CompatibilityGuard`）**
   - 仅通过轻量 `hostfxr.dll` 包装器挂载，**不修改游戏核心资源**，随时可一键无痕卸载。
   - 进档自动反射自检，若游戏 EA 更新导致内部结构变动，自动输出诊断报告与候选成员名至 `%APPDATA%\Games By Malcs\Melvor Idle 2\logs\melvor2_compat_report.json`，异常熔断优雅退场，**100% 保障游戏本体稳定不崩溃**。

---

## 快速安装 Quick Start (Windows)

### 环境要求
- Windows 10/11 (x64)
- Steam 版 **Melvor Idle 2**（当前适配 `v0.11.6-alpha.1`）
- **Python 3.10+**（安装时勾选 *Add python.exe to PATH*）
- 预编译的桥接 DLL 与存档解压器已随包附带，**普通玩家无需安装 .NET SDK 或 MinGW GCC**。

### 第一步：解压并运行一键配置脚本
将压缩包解压到任意目录（例如 `D:\Tools\Melvor2-MCP-V0.0.1-0116a1`），**先关闭游戏**，在该目录打开 PowerShell 运行：

```powershell
.\Setup.ps1
```

> 若脚本执行策略受限，请使用：
> ```powershell
> powershell -ExecutionPolicy Bypass -File .\Setup.ps1
> ```
> 若你的游戏不在默认 Steam 库路径，可显式指定目录：
> ```powershell
> .\Setup.ps1 -GameDir "E:\SteamLibrary\steamapps\common\Melvor Idle 2"
> ```

`Setup.ps1` 会自动完成：
1. 创建 `.venv` 虚拟环境并安装 `fastmcp`；
2. 自动定位本机 `Melvor2.exe`，1 秒离线提取 `MelvorBase.json` 与中文语言包并刷新知识库；
3. 将预编译桥接 DLL 安装到游戏目录；
4. 在当前目录生成即用型配置文件 `mcp_config.local.json` 并打印到控制台。

### 第二步：接入你的 AI 客户端（MCP）
将 `Setup.ps1` 输出的 JSON 粘贴到任意支持 MCP 的客户端配置中（如 **Claude Desktop**、**Cursor**、**Antigravity**、**Cline**、**Windsurf** 等）：

```json
{
  "mcpServers": {
    "melvor-idle-2": {
      "command": "D:\\Tools\\Melvor2-MCP-V0.0.1-0116a1\\.venv\\Scripts\\python.exe",
      "args": ["-m", "melvor2_mcp"],
      "cwd": "D:\\Tools\\Melvor2-MCP-V0.0.1-0116a1",
      "env": {
        "MELVOR2_BRIDGE_PORT": "17817"
      }
    }
  }
}
```

### 第三步：通过 Steam 启动游戏并开始对话！
- 启动 Melvor Idle 2 并进入存档，浏览器访问 `http://127.0.0.1:17817/` 即可看到实时调度面板。
- 在 AI 客户端中试试说：
  - *"读取我当前 Melvor Idle 2 存档状态，看看哪些主线任务可以立刻推进？"*
  - *"帮我编排一个 4 小时的挂机队列：先一键收菜、挖 300 银矿、熔炼银锭，然后自动提交任务，并做好低食物保护。"*

---

## 常用维护命令

| 场景 | 命令 |
|---|---|
| 启动独立离线面板（游戏关闭可用） | `.\StartDashboard.ps1 -Open` |
| 游戏 EA 更新后重新挂载桥接（游戏关闭时） | `.\bridge\InstallBridge.ps1` |
| 游戏 EA 更新后 1 秒刷新离线知识库 | `.\.venv\Scripts\python.exe knowledge\extract_offline.py && .\.venv\Scripts\python.exe knowledge\generate.py` |
| 一键无痕卸载游戏内桥接（游戏关闭时） | `.\bridge\UninstallBridge.ps1` |
| 开发者从源码重新编译 C#/C 桥接 | `.\bridge\InstallBridge.ps1 -Rebuild` |

---

## 提供的 MCP 工具一览

- **离线读取（游戏关闭亦可用）**：`get_game_state`、`get_skills`、`get_bank_items`、`get_currencies`、`get_active_actions`、`get_quests`、`get_equipment_and_setup`、`list_saves`
- **自主计划队列**：`validate_plan`、`set_action_plan`、`get_plan_status`、`control_plan`
- **实时桥接与操控**：`get_bridge_health`、`get_live_state`、`get_live_bank`、`get_live_quests`、`get_live_combat`、`get_live_shop`、`get_live_passive`、`list_available_actions`、`execute_game_op`、`start_action`、`stop_action`
- **知识库检索**：`browse_knowledge`、`search_knowledge`、`get_quest_guide`

---

## 卸载与免责声明

- **无痕卸载**：关闭游戏后运行 `.\bridge\UninstallBridge.ps1`（或在 Steam 中对 Melvor Idle 2 执行「验证游戏文件的完整性」），即可完全恢复原始 `hostfxr.dll` 并移除桥接文件。
- **兼容性提示**：Melvor Idle 2 处于 Early Access 快速迭代期，本发布包版本 `V0.0.1-0116a1` 针对游戏 `v0.11.6-alpha.1` 验证通过。游戏大版本更新后，如面板顶部「EA 兼容检查」提示降级，请查看 `%APPDATA%\Games By Malcs\Melvor Idle 2\logs\melvor2_compat_report.json` 并关注插件后续适配版本。
- 本工具仅供单机学习研究与个人辅助体验使用，不附带任何游戏受版权保护的原始资源文件（`MelvorBase.json` 与语言包由脚本从玩家本机合法拥有的 `Melvor2.exe` 本地提取）。
