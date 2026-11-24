using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Melvor2Bridge
{
    /// <summary>
    /// Early-Access compatibility self-check, circuit breaker, and graceful
    /// shutdown manager.
    ///
    /// If a Melvor Idle 2 EA update renames or changes core engine/game types,
    /// CompatibilityGuard detects the mismatch via reflection, writes a
    /// structured diagnostic report to:
    ///   %APPDATA%\Games By Malcs\Melvor Idle 2\logs\melvor2_compat_report.json
    /// disables mutating endpoints & the autonomous PlanScheduler cleanly, and
    /// NEVER crashes or freezes the game process.
    /// </summary>
    internal static class CompatibilityGuard
    {
        private static readonly object LockObj = new object();
        private static CompatReport _report = new CompatReport
        {
            BridgeVersion = Bridge.BridgeVersion,
            RuntimeVersion = Environment.Version.ToString(),
            Status = "waiting_for_save",
            Compatible = true
        };
        private static WeakReference _checkedGameRef;
        private static int _consecutiveErrors;
        private const int CircuitBreakerThreshold = 5;

        public static bool IsDisabled { get; private set; }
        public static string DisableReason { get; private set; }
        public static string ReportFilePath => Path.Combine(Bridge.DataFolder, "logs", "melvor2_compat_report.json");

        public static CompatReport GetReport()
        {
            lock (LockObj)
            {
                _report.ReportFile = ReportFilePath;
                return _report;
            }
        }

        public static void ResetCircuitBreaker()
        {
            lock (LockObj) { _consecutiveErrors = 0; }
        }

        public static void RecordRuntimeError(string context, Exception ex)
        {
            var inner = Bridge.UnwrapException(ex);
            Bridge.Log($"[ERROR] {context}: {inner.GetType().Name}: {inner.Message}");
            lock (LockObj)
            {
                _consecutiveErrors++;
                _report.RecentErrors.Add(new ErrorRecord
                {
                    TimestampUtc = DateTime.UtcNow.ToString("o"),
                    Context = context,
                    ExceptionType = inner.GetType().FullName,
                    Message = inner.Message,
                    StackTrace = Truncate(inner.StackTrace, 600)
                });
                if (_report.RecentErrors.Count > 20)
                    _report.RecentErrors.RemoveAt(0);

                if (_consecutiveErrors >= CircuitBreakerThreshold && !IsDisabled)
                {
                    TripFatalLocked(
                        $"Circuit breaker tripped after {_consecutiveErrors} consecutive errors (last: {context} -> {inner.GetType().Name}: {inner.Message})");
                }
            }
        }

        public static void TripFatal(string reason, Exception ex = null)
        {
            lock (LockObj)
            {
                if (ex != null)
                {
                    var inner = Bridge.UnwrapException(ex);
                    _report.RecentErrors.Add(new ErrorRecord
                    {
                        TimestampUtc = DateTime.UtcNow.ToString("o"),
                        Context = "fatal",
                        ExceptionType = inner.GetType().FullName,
                        Message = inner.Message,
                        StackTrace = Truncate(inner.StackTrace, 800)
                    });
                }
                TripFatalLocked(reason);
            }
        }

        /// <summary>
        /// Runs once per attached Game instance. Checks core & subsystem
        /// reflection contracts, writes melvor2_compat_report.json on any issue
        /// (and always writes an initial healthy snapshot), and gracefully
        /// disables mutating/scheduling features if a fatal EA break is found.
        /// </summary>
        public static void RunSelfCheck(object game)
        {
            if (game == null) return;
            lock (LockObj)
            {
                if (_checkedGameRef != null && _checkedGameRef.IsAlive && ReferenceEquals(_checkedGameRef.Target, game))
                    return;
                _checkedGameRef = new WeakReference(game);
            }

            var report = new CompatReport
            {
                CheckedAtUtc = DateTime.UtcNow.ToString("o"),
                BridgeVersion = Bridge.BridgeVersion,
                RuntimeVersion = Environment.Version.ToString(),
                ReportFile = ReportFilePath
            };

            try
            {
                var asm = game.GetType().Assembly;
                report.GameAssemblyName = asm.GetName().Name;
                report.GameAssemblyVersion = asm.GetName().Version?.ToString();
                try
                {
                    if (!string.IsNullOrEmpty(asm.Location) && File.Exists(asm.Location))
                        report.GameDllLastWriteUtc = File.GetLastWriteTimeUtc(asm.Location).ToString("o");
                }
                catch { }

                // 1. Core checks (FATAL if missing — cannot operate safely)
                CheckProp(report, "Core.Game.Entities", game, "Entities", fatal: true);
                CheckProp(report, "Core.Game.ActionManager", game, "ActionManager", fatal: true);
                CheckProp(report, "Core.Game.Data", game, "Data", fatal: true);

                var entities = Bridge.Prop(game, "Entities");
                CheckMethod(report, "Core.Entities.GetAllObjects", entities, "GetAllObjects", fatal: true);

                var am = Bridge.Prop(game, "ActionManager");
                CheckProp(report, "Core.ActionManager.ActiveActions", am, "ActiveActions", fatal: true);
                CheckMethod(report, "Core.ActionManager.TryStopActive", am, "TryStopActive", fatal: true);

                var data = Bridge.Prop(game, "Data");
                var db = Bridge.Prop(data, "Database");
                CheckProp(report, "Core.Data.Database", data, "Database", fatal: true);
                CheckMethod(report, "Core.Database.TryGetObject", db, "TryGetObject", fatal: true);

                // 2. Subsystem checks (DEGRADED if changed — log candidates for fast EA patching)
                CheckSubsystem(report, "Bank", () =>
                {
                    var inv = Bridge.FindComponent(game, "MelvorBase:Bank", "Inventory");
                    var wal = Bridge.FindComponent(game, "MelvorBase:Bank", "Wallet");
                    var sale = Bridge.FindComponent(game, "MelvorBase:Bank", "ItemSales");
                    CheckProp(report, "Bank.Inventory.Stacks", inv, "Stacks", fatal: false);
                    CheckMethod(report, "Bank.Inventory.GetQuantity", inv, "GetQuantity", fatal: false);
                    CheckProp(report, "Bank.Wallet.Stacks", wal, "Stacks", fatal: false);
                    CheckMethod(report, "Bank.ItemSales.SellItem", sale, "SellItem", fatal: false);
                });

                CheckSubsystem(report, "Quests", () =>
                {
                    var qc = Bridge.FindComponent(game, "MelvorBase:Quests", "Quests");
                    CheckProp(report, "Quests.ActiveQuests", qc, "ActiveQuests", fatal: false);
                    CheckMethod(report, "Quests.ExecuteTaskCosts", qc, "ExecuteTaskCosts", fatal: false);
                    CheckMethod(report, "Quests.ClaimRewards", qc, "ClaimRewards", fatal: false);
                });

                CheckSubsystem(report, "Shop", () =>
                {
                    var sc = Bridge.FindComponent(game, "MelvorBase:Shop", "Shop");
                    CheckProp(report, "Shop.Purchases", sc, "Purchases", fatal: false);
                    CheckMethod(report, "Shop.ProcessPurchase", sc, "ProcessPurchase", fatal: false);
                });

                CheckSubsystem(report, "Combat", () =>
                {
                    var ca = Bridge.FindComponent(game, "MelvorBase:Combat", "Action");
                    var areas = Bridge.FindComponent(game, "MelvorBase:Combat", "Areas");
                    var player = Bridge.FindComponent(game, "MelvorBase:Combat", "Player");
                    var enemy = Bridge.FindComponent(game, "MelvorBase:Combat", "Enemy");
                    var food = Bridge.FindComponent(game, "MelvorBase:Combat", "Food");
                    CheckMethod(report, "Combat.Areas.SelectEnemy", areas, "SelectEnemy", fatal: false);
                    CheckMethod(report, "Combat.Areas.SelectDungeon", areas, "SelectDungeon", fatal: false);
                    CheckProp(report, "Combat.Player.GlobalHealth", player, "GlobalHealth", fatal: false);
                    CheckEvent(report, "Combat.Enemy.Killed", enemy, "Killed", fatal: false);
                    CheckMethod(report, "Combat.Food.SelectFood", food, "SelectFood", fatal: false);
                    CheckProp(report, "Combat.Action.IsStarted", ca, "IsStarted", fatal: false);
                });

                CheckSubsystem(report, "EventBus", () =>
                {
                    var busComp = Bridge.FindComponent(game, "MelvorBase:EventBus", "Bus");
                    var eb = Bridge.Prop(busComp, "EventBus");
                    var ap = Bridge.Prop(eb, "ActionPerformed");
                    CheckMethod(report, "EventBus.ActionPerformed.Subscribe", ap, "Subscribe", fatal: false);
                });

                CheckSubsystem(report, "Passive", () =>
                {
                    var farm = Bridge.FindComponent(game, "MelvorBase:Farming", "Passive");
                    var ranch = Bridge.FindComponent(game, "MelvorBase:Ranching", "Passive");
                    CheckProp(report, "Farming.ActivePlots", farm, "ActivePlots", fatal: false);
                    CheckProp(report, "Ranching.ActiveLand", ranch, "ActiveLand", fatal: false);
                });
            }
            catch (Exception e)
            {
                var inner = Bridge.UnwrapException(e);
                report.FatalErrors.Add(new CheckFailure
                {
                    Check = "SelfCheck.Exception",
                    Reason = inner.GetType().Name + ": " + inner.Message
                });
            }

            lock (LockObj)
            {
                report.RecentErrors = _report.RecentErrors;
                if (report.FatalErrors.Count > 0)
                {
                    report.Compatible = false;
                    report.Status = "incompatible";
                    _report = report;
                    TripFatalLocked($"EA compatibility self-check found {report.FatalErrors.Count} fatal break(s): "
                        + string.Join("; ", report.FatalErrors.Select(f => f.Check)));
                }
                else if (report.Warnings.Count > 0)
                {
                    report.Compatible = true;
                    report.Status = "degraded";
                    _report = report;
                    WriteReportToDiskLocked();
                    Bridge.Log($"[COMPAT DEGRADED] {report.Warnings.Count} non-fatal warning(s); details in {ReportFilePath}");
                }
                else
                {
                    report.Compatible = true;
                    report.Status = "healthy";
                    _report = report;
                    WriteReportToDiskLocked();
                    Bridge.Log($"[COMPAT HEALTHY] All {report.PassedChecks.Count} reflection checks passed (GameAssembly={report.GameAssemblyVersion})");
                }
            }
        }

        private static void CheckSubsystem(CompatReport report, string name, Action check)
        {
            int beforeWarn = report.Warnings.Count;
            int beforeFatal = report.FatalErrors.Count;
            try { check(); }
            catch (Exception e)
            {
                report.Warnings.Add(new CheckFailure { Check = name, Reason = e.GetType().Name + ": " + e.Message });
            }
            bool ok = report.Warnings.Count == beforeWarn && report.FatalErrors.Count == beforeFatal;
            report.Subsystems[name] = ok ? "ok" : "degraded";
        }

        private static void CheckProp(CompatReport report, string checkName, object obj, string propName, bool fatal)
        {
            if (obj == null)
            {
                AddFail(report, checkName, $"parent object is null (cannot look up property '{propName}')", null, fatal);
                return;
            }
            var t = obj.GetType();
            var p = t.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) { report.PassedChecks.Add(checkName); return; }
            var candidates = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(x => x.Name).Distinct().OrderBy(x => x).Take(25).ToList();
            AddFail(report, checkName, $"property '{propName}' missing on type {t.FullName}", candidates, fatal);
        }

        private static void CheckMethod(CompatReport report, string checkName, object obj, string methodName, bool fatal)
        {
            if (obj == null)
            {
                AddFail(report, checkName, $"parent object is null (cannot look up method '{methodName}')", null, fatal);
                return;
            }
            var t = obj.GetType();
            if (t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(m => m.Name == methodName))
            {
                report.PassedChecks.Add(checkName);
                return;
            }
            var candidates = t.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => !m.IsSpecialName).Select(m => m.Name).Distinct().OrderBy(x => x).Take(25).ToList();
            AddFail(report, checkName, $"method '{methodName}' missing on type {t.FullName}", candidates, fatal);
        }

        private static void CheckEvent(CompatReport report, string checkName, object obj, string eventName, bool fatal)
        {
            if (obj == null)
            {
                AddFail(report, checkName, $"parent object is null (cannot look up event '{eventName}')", null, fatal);
                return;
            }
            var t = obj.GetType();
            if (t.GetEvent(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
            {
                report.PassedChecks.Add(checkName);
                return;
            }
            var candidates = t.GetEvents(BindingFlags.Instance | BindingFlags.Public).Select(e => e.Name).OrderBy(x => x).ToList();
            AddFail(report, checkName, $"event '{eventName}' missing on type {t.FullName}", candidates, fatal);
        }

        private static void AddFail(CompatReport report, string checkName, string reason, List<string> candidates, bool fatal)
        {
            var entry = new CheckFailure { Check = checkName, Reason = reason, CandidateMembers = candidates };
            if (fatal) report.FatalErrors.Add(entry);
            else report.Warnings.Add(entry);
        }

        private static void TripFatalLocked(string reason)
        {
            IsDisabled = true;
            DisableReason = reason;
            _report.Compatible = false;
            _report.Status = "incompatible";
            _report.DisableReason = reason;
            _report.DisabledAtUtc = DateTime.UtcNow.ToString("o");
            WriteReportToDiskLocked();

            Bridge.Log("================================================================");
            Bridge.Log("[BRIDGE GRACEFUL SHUTDOWN] Incompatible with current EA game build!");
            Bridge.Log("Reason: " + reason);
            Bridge.Log("Diagnostic report written to: " + ReportFilePath);
            Bridge.Log("Mutating APIs & PlanScheduler disabled; game will continue safely.");
            Bridge.Log("================================================================");

            PlanScheduler.ShutdownOnIncompatibility(reason);
        }

        private static void WriteReportToDiskLocked()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportFilePath));
                var json = JsonSerializer.Serialize(_report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ReportFilePath, json);
            }
            catch (Exception e)
            {
                Bridge.Log("Failed to write compat report: " + e.Message);
            }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }

    internal sealed class CompatReport
    {
        [JsonPropertyName("status")] public string Status { get; set; }
        [JsonPropertyName("compatible")] public bool Compatible { get; set; }
        [JsonPropertyName("bridge_version")] public string BridgeVersion { get; set; }
        [JsonPropertyName("runtime_version")] public string RuntimeVersion { get; set; }
        [JsonPropertyName("game_assembly_name")] public string GameAssemblyName { get; set; }
        [JsonPropertyName("game_assembly_version")] public string GameAssemblyVersion { get; set; }
        [JsonPropertyName("game_dll_last_write_utc")] public string GameDllLastWriteUtc { get; set; }
        [JsonPropertyName("checked_at_utc")] public string CheckedAtUtc { get; set; }
        [JsonPropertyName("disabled_at_utc")] public string DisabledAtUtc { get; set; }
        [JsonPropertyName("disable_reason")] public string DisableReason { get; set; }
        [JsonPropertyName("report_file")] public string ReportFile { get; set; }
        [JsonPropertyName("subsystems")] public Dictionary<string, string> Subsystems { get; set; } = new Dictionary<string, string>();
        [JsonPropertyName("passed_checks")] public List<string> PassedChecks { get; set; } = new List<string>();
        [JsonPropertyName("warnings")] public List<CheckFailure> Warnings { get; set; } = new List<CheckFailure>();
        [JsonPropertyName("fatal_errors")] public List<CheckFailure> FatalErrors { get; set; } = new List<CheckFailure>();
        [JsonPropertyName("recent_errors")] public List<ErrorRecord> RecentErrors { get; set; } = new List<ErrorRecord>();
    }

    internal sealed class CheckFailure
    {
        [JsonPropertyName("check")] public string Check { get; set; }
        [JsonPropertyName("reason")] public string Reason { get; set; }
        [JsonPropertyName("candidate_members")] public List<string> CandidateMembers { get; set; }
    }

    internal sealed class ErrorRecord
    {
        [JsonPropertyName("timestamp_utc")] public string TimestampUtc { get; set; }
        [JsonPropertyName("context")] public string Context { get; set; }
        [JsonPropertyName("exception_type")] public string ExceptionType { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; }
        [JsonPropertyName("stack_trace")] public string StackTrace { get; set; }
    }
}
