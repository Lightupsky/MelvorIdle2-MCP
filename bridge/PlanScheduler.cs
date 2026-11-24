using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Melvor2Bridge
{
    /// <summary>
    /// Autonomous in-game Action Plan / Queue Scheduler.
    ///
    /// Executes an ordered sequence of instant and continuous game operations
    /// based on duration, exact action/kill/dungeon counts (via GameEventBus &
    /// Combat event hooks), bank item targets, skill level targets, quest step
    /// completion, or resource exhaustion — without requiring the LLM Agent to
    /// remain connected or running.
    ///
    /// Persists plan definition, live progress, and step execution history to:
    ///   %APPDATA%\Games By Malcs\Melvor Idle 2\melvor2_plan.json
    /// </summary>
    internal static class PlanScheduler
    {
        private static readonly object LockObj = new object();
        private static bool _loopStarted;
        private static WeakReference _hookedGameRef;

        // Active plan state
        private static PlanState _state = new PlanState();

        // Runtime-only tracking for the currently executing step
        private static bool _stepDispatched;
        private static DateTime _stepStartedUtc;
        private static int _stepActionCount;
        private static int _stepSubActionCount;
        private static int _stepKillCount;
        private static int _stepDungeonCount;
        private static Dictionary<string, int> _stepStartItems = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static string _expectedEntity;
        private static string _expectedActionId;
        private static int _retryCount;
        private static DateTime _lastPersistUtc = DateTime.MinValue;
        private static DateTime _lastDiskCheckUtc = DateTime.MinValue;
        private static DateTime _lastSeenPlanFileMtimeUtc = DateTime.MinValue;

        internal static string PlanFilePath => Path.Combine(Bridge.DataFolder, "melvor2_plan.json");

        public static void StartBackgroundLoop()
        {
            lock (LockObj)
            {
                if (_loopStarted) return;
                _loopStarted = true;
            }

            LoadStateFromDisk();

            var t = new Thread(TickLoop)
            {
                IsBackground = true,
                Name = "Melvor2PlanScheduler"
            };
            t.Start();
            Bridge.Log("PlanScheduler background loop started");
        }

        public static void OnGameAttached(object game)
        {
            if (game == null) return;
            lock (LockObj)
            {
                if (_hookedGameRef != null && _hookedGameRef.IsAlive && ReferenceEquals(_hookedGameRef.Target, game))
                    return;
                _hookedGameRef = new WeakReference(game);
            }
            HookGameEvents(game);
        }

        // ------------------------------------------------------------------
        // Event hooks (exact action, kill, and dungeon completion counters)
        // ------------------------------------------------------------------

        private static void HookGameEvents(object game)
        {
            try
            {
                // 1. GameEventBus.ActionPerformed
                var busComp = Bridge.FindComponent(game, "MelvorBase:EventBus", "Bus");
                var eventBus = Bridge.Prop(busComp, "EventBus");
                var actionPerf = Bridge.Prop(eventBus, "ActionPerformed");
                if (actionPerf != null)
                {
                    var sub = actionPerf.GetType().GetMethod("Subscribe");
                    if (sub != null)
                    {
                        var handlerType = sub.GetParameters()[0].ParameterType;
                        var mi = typeof(PlanScheduler).GetMethod(nameof(OnActionPerformed), BindingFlags.Static | BindingFlags.NonPublic);
                        var del = Delegate.CreateDelegate(handlerType, mi);
                        sub.Invoke(actionPerf, new object[] { del });
                        Bridge.Log("PlanScheduler subscribed to EventBus.ActionPerformed");
                    }
                }

                // 2. Combat.Enemy.Killed
                var enemyComp = Bridge.FindComponent(game, "MelvorBase:Combat", "Enemy");
                var killedEv = enemyComp?.GetType().GetEvent("Killed");
                if (killedEv != null)
                {
                    var mi = typeof(PlanScheduler).GetMethod(nameof(OnEnemyKilled), BindingFlags.Static | BindingFlags.NonPublic);
                    var del = Delegate.CreateDelegate(killedEv.EventHandlerType, mi);
                    killedEv.AddEventHandler(enemyComp, del);
                    Bridge.Log("PlanScheduler subscribed to Enemy.Killed");
                }

                // 3. Combat.Areas.DungeonCompleted
                var areasComp = Bridge.FindComponent(game, "MelvorBase:Combat", "Areas");
                var dungEv = areasComp?.GetType().GetEvent("DungeonCompleted");
                if (dungEv != null)
                {
                    var mi = typeof(PlanScheduler).GetMethod(nameof(OnDungeonCompleted), BindingFlags.Static | BindingFlags.NonPublic);
                    var del = Delegate.CreateDelegate(dungEv.EventHandlerType, mi);
                    dungEv.AddEventHandler(areasComp, del);
                    Bridge.Log("PlanScheduler subscribed to CombatAreas.DungeonCompleted");
                }
            }
            catch (Exception e)
            {
                Bridge.Log("PlanScheduler HookGameEvents warning: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void OnActionPerformed(object sender, EventArgs e)
        {
            bool isSub = Bridge.Prop(e, "IsSubAction") is bool b && b;
            lock (LockObj)
            {
                if (_state.Status != "running" || !_stepDispatched) return;
                if (isSub) _stepSubActionCount++;
                else _stepActionCount++;
            }
        }

        private static void OnEnemyKilled(object sender, EventArgs e)
        {
            lock (LockObj)
            {
                if (_state.Status != "running" || !_stepDispatched) return;
                _stepKillCount++;
            }
        }

        private static void OnDungeonCompleted(object sender, EventArgs e)
        {
            lock (LockObj)
            {
                if (_state.Status != "running" || !_stepDispatched) return;
                _stepDungeonCount++;
            }
        }

        // ------------------------------------------------------------------
        // HTTP handlers
        // ------------------------------------------------------------------

        public static string HandleSetPlan(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return Bridge.Serialize(new { ok = false, error = "empty plan body" });

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var incoming = JsonSerializer.Deserialize<PlanDefinition>(body, opts);
            if (incoming == null || incoming.Steps == null || incoming.Steps.Count == 0)
                return Bridge.Serialize(new { ok = false, error = "plan must contain at least one step" });

            lock (LockObj)
            {
                _state = new PlanState
                {
                    Plan = incoming,
                    Status = incoming.AutoStart ? "running" : "paused",
                    CurrentIndex = 0,
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o"),
                    StatusReason = incoming.AutoStart ? "started" : "loaded_paused",
                    History = new List<StepHistoryEntry>()
                };
                ResetStepRuntime();
                PersistStateLocked();
            }

            Bridge.Log($"Plan loaded: '{incoming.Name}' ({incoming.Steps.Count} steps, status={_state.Status})");
            return HandleStatus();
        }

        public static string HandleStatus()
        {
            lock (LockObj)
            {
                SyncLiveProgressLocked();
                return JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = false });
            }
        }

        public static Dictionary<string, object> GetShortStatus()
        {
            lock (LockObj)
            {
                SyncLiveProgressLocked();
                return new Dictionary<string, object>
                {
                    ["name"] = _state.Plan?.Name,
                    ["status"] = _state.Status,
                    ["reason"] = _state.StatusReason,
                    ["current_index"] = _state.CurrentIndex,
                    ["total_steps"] = _state.Plan?.Steps?.Count ?? 0,
                    ["current_step_label"] = GetCurrentStepLabelLocked(),
                    ["step_elapsed_seconds"] = _state.CurrentStepProgress?.ElapsedSeconds ?? 0,
                    ["step_actions"] = _state.CurrentStepProgress?.ActionCount ?? 0,
                    ["step_kills"] = _state.CurrentStepProgress?.KillCount ?? 0
                };
            }
        }

        public static string HandlePause()
        {
            lock (LockObj)
            {
                if (_state.Status == "running")
                {
                    _state.Status = "paused";
                    _state.StatusReason = "paused_by_user";
                    _state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                    PersistStateLocked();
                }
            }
            return HandleStatus();
        }

        public static string HandleResume()
        {
            lock (LockObj)
            {
                if (_state.Plan != null && _state.CurrentIndex < _state.Plan.Steps.Count
                    && (_state.Status == "paused" || _state.Status == "pending_start"))
                {
                    _state.Status = "running";
                    _state.StatusReason = "resumed";
                    _state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                    // Re-sync expected entity if step was already dispatched, or re-dispatch cleanly
                    _stepDispatched = false;
                    PersistStateLocked();
                }
            }
            return HandleStatus();
        }

        public static string HandleSkip()
        {
            lock (LockObj)
            {
                if (_state.Plan != null && _state.CurrentIndex < _state.Plan.Steps.Count)
                {
                    RecordHistoryLocked("skipped", "skipped_by_user");
                    _state.CurrentIndex++;
                    ResetStepRuntime();
                    if (_state.CurrentIndex >= _state.Plan.Steps.Count)
                    {
                        _state.Status = "completed";
                        _state.StatusReason = "all_steps_finished";
                    }
                    PersistStateLocked();
                }
            }
            return HandleStatus();
        }

        public static string HandleCancel(string body)
        {
            bool stopGameAction = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("stop_action", out var sa) && sa.ValueKind == JsonValueKind.True)
                        stopGameAction = true;
                }
            }
            catch { }

            lock (LockObj)
            {
                if (_state.Status == "running" || _state.Status == "paused" || _state.Status == "pending_start")
                {
                    _state.Status = "cancelled";
                    _state.StatusReason = "cancelled_by_user";
                    _state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                    ResetStepRuntime();
                    PersistStateLocked();
                }
            }

            if (stopGameAction)
            {
                using var empty = JsonDocument.Parse("{}");
                GameOperations.ExecuteOp("stop_action", empty.RootElement);
            }
            return HandleStatus();
        }

        public static void ShutdownOnIncompatibility(string reason)
        {
            lock (LockObj)
            {
                if (_state.Status == "running" || _state.Status == "pending_start" || _state.Status == "paused")
                {
                    _state.Status = "incompatible";
                    _state.StatusReason = reason;
                    _state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                    ResetStepRuntime();
                    PersistStateLocked();
                }
            }
            Bridge.Log("PlanScheduler gracefully shut down due to EA incompatibility.");
        }

        // ------------------------------------------------------------------
        // Background 1-second execution loop
        // ------------------------------------------------------------------

        private static void TickLoop()
        {
            while (!CompatibilityGuard.IsDisabled)
            {
                try
                {
                    Thread.Sleep(1000);
                    if (CompatibilityGuard.IsDisabled) break;
                    TickOnce();
                    CompatibilityGuard.ResetCircuitBreaker();
                }
                catch (Exception e)
                {
                    CompatibilityGuard.RecordRuntimeError("PlanScheduler.TickOnce", e);
                }
            }
            Bridge.Log("PlanScheduler background loop exited cleanly.");
        }

        private static void TickOnce()
        {
            // Periodically check if an external tool (e.g. MCP while game was starting) updated melvor2_plan.json
            if ((DateTime.UtcNow - _lastDiskCheckUtc).TotalSeconds >= 5)
            {
                _lastDiskCheckUtc = DateTime.UtcNow;
                CheckExternalPlanFile();
            }

            var game = Bridge.GetGame();
            if (game == null) return; // Wait until player enters a save

            // Wait until save is fully loaded
            if (Bridge.Prop(game, "IsSaveLoaded") is bool loaded && !loaded) return;

            PlanStep stepToDispatch = null;
            PlanStep runningStep = null;
            PlanDefinition planRef = null;

            lock (LockObj)
            {
                if (_state.Status == "pending_start" && _state.Plan != null && _state.Plan.Steps.Count > 0)
                {
                    _state.Status = "running";
                    _state.StatusReason = "auto_started_on_game_load";
                    _state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                    PersistStateLocked();
                }

                if (_state.Status != "running" || _state.Plan == null) return;
                planRef = _state.Plan;

                if (_state.CurrentIndex >= planRef.Steps.Count)
                {
                    _state.Status = "completed";
                    _state.StatusReason = "all_steps_finished";
                    _state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                    PersistStateLocked();
                    if (planRef.StopOnComplete)
                    {
                        using var empty = JsonDocument.Parse("{}");
                        GameOperations.ExecuteOp("stop_action", empty.RootElement);
                    }
                    return;
                }

                if (!_stepDispatched)
                    stepToDispatch = planRef.Steps[_state.CurrentIndex];
                else
                    runningStep = planRef.Steps[_state.CurrentIndex];
            }

            if (stepToDispatch != null)
            {
                DispatchStep(game, stepToDispatch);
                return;
            }

            if (runningStep != null)
            {
                EvaluateRunningStep(game, planRef, runningStep);
            }
        }

        private static void DispatchStep(object game, PlanStep step)
        {
            Bridge.Log($"PlanScheduler dispatching step [{_state.CurrentIndex}] {step.Label ?? step.Op} (op={step.Op})");

            // Snapshot item quantities for item_gained conditions
            var snapshots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (step.Until?.ItemGained != null && !string.IsNullOrWhiteSpace(step.Until.ItemGained.Item))
            {
                snapshots[step.Until.ItemGained.Item] = Bridge.GetBankItemQuantity(game, step.Until.ItemGained.Item);
            }

            JsonElement paramEl;
            using var doc = step.Params.HasValue
                ? JsonDocument.Parse(step.Params.Value.GetRawText())
                : JsonDocument.Parse("{}");
            paramEl = doc.RootElement;

            var res = GameOperations.ExecuteOp(step.Op, paramEl);
            bool ok = res.TryGetValue("ok", out var okObj) && okObj is bool b && b;
            string err = res.TryGetValue("error", out var errObj) ? errObj?.ToString() : null;

            bool isContinuous = GameOperations.ContinuousOps.Contains(step.Op) && step.Until != null && step.Until.HasAnyCondition();

            lock (LockObj)
            {
                _stepStartedUtc = DateTime.UtcNow;
                _stepActionCount = 0;
                _stepSubActionCount = 0;
                _stepKillCount = 0;
                _stepDungeonCount = 0;
                _stepStartItems = snapshots;
                _retryCount = 0;

                if (!ok)
                {
                    string onErr = (step.OnError ?? "abort").ToLowerInvariant();
                    RecordHistoryLocked("failed", err ?? "operation returned ok=false");
                    if (onErr == "skip")
                    {
                        Bridge.Log($"Step [{_state.CurrentIndex}] failed ({err}), skipping as configured");
                        _state.CurrentIndex++;
                        ResetStepRuntime();
                    }
                    else
                    {
                        Bridge.Log($"Step [{_state.CurrentIndex}] failed ({err}), aborting plan");
                        _state.Status = "failed";
                        _state.StatusReason = $"step {_state.CurrentIndex} failed: {err ?? "ok=false"}";
                    }
                    PersistStateLocked();
                    return;
                }

                if (!isContinuous)
                {
                    // Instant one-shot operation — done immediately!
                    RecordHistoryLocked("done", "instant_op_succeeded");
                    _state.CurrentIndex++;
                    ResetStepRuntime();
                    if (_state.CurrentIndex >= _state.Plan.Steps.Count)
                    {
                        _state.Status = "completed";
                        _state.StatusReason = "all_steps_finished";
                    }
                    PersistStateLocked();
                    return;
                }

                // Continuous step started! Record expected active entity/action for manual override detection
                _stepDispatched = true;
                CaptureExpectedAction(game, step, paramEl);
                SyncLiveProgressLocked();
                PersistStateLocked();
            }
        }

        private static void CaptureExpectedAction(object game, PlanStep step, JsonElement p)
        {
            _expectedEntity = null;
            _expectedActionId = null;
            if (step.Op.Equals("wait", StringComparison.OrdinalIgnoreCase)) return;

            if (step.Op.Equals("combat_fight", StringComparison.OrdinalIgnoreCase)
                || step.Op.Equals("combat_dungeon", StringComparison.OrdinalIgnoreCase))
            {
                _expectedEntity = "MelvorBase:Combat";
                return;
            }

            if (p.TryGetProperty("entity", out var pe) && pe.ValueKind == JsonValueKind.String)
                _expectedEntity = Bridge.NormalizeId(pe.GetString());
            if (p.TryGetProperty("recipe", out var pr) && pr.ValueKind == JsonValueKind.String)
                _expectedActionId = Bridge.NormalizeId(pr.GetString());
        }

        private static void EvaluateRunningStep(object game, PlanDefinition plan, PlanStep step)
        {
            // 1. Check safety guards (e.g. low food during combat/thieving)
            if (step.Safety != null && step.Safety.MinFoodQty.HasValue && step.Safety.MinFoodQty.Value > 0)
            {
                var foodComp = Bridge.FindComponent(game, "MelvorBase:Combat", "Food");
                var selFood = Bridge.Prop(foodComp, "SelectedFood");
                int foodQty = 0;
                if (selFood != null)
                {
                    var fid = Bridge.Prop(Bridge.Prop(selFood, "Item"), "Id")?.ToString();
                    if (fid != null) foodQty = Bridge.GetBankItemQuantity(game, fid);
                }
                if (foodQty < step.Safety.MinFoodQty.Value)
                {
                    Bridge.Log($"SAFETY STOP: food qty {foodQty} < min {step.Safety.MinFoodQty.Value}");
                    if (step.Safety.StopOnLowFood)
                    {
                        using var empty = JsonDocument.Parse("{}");
                        GameOperations.ExecuteOp("stop_action", empty.RootElement);
                    }
                    lock (LockObj)
                    {
                        RecordHistoryLocked("safety_stopped", $"low_food ({foodQty} < {step.Safety.MinFoodQty.Value})");
                        _state.Status = "paused";
                        _state.StatusReason = $"safety:low_food ({foodQty})";
                        _stepDispatched = false;
                        PersistStateLocked();
                    }
                    return;
                }
            }

            // 2. Inspect active game action for manual override or unexpected stop
            if (!step.Op.Equals("wait", StringComparison.OrdinalIgnoreCase) && _expectedEntity != null)
            {
                var (isRunning, activeEntity, activeDescId) = GetCurrentActiveAction(game);

                if (isRunning && plan.PauseOnManualOverride)
                {
                    bool entityChanged = !string.Equals(activeEntity, _expectedEntity, StringComparison.OrdinalIgnoreCase);
                    bool recipeChanged = _expectedActionId != null && activeDescId != null
                        && !string.Equals(activeDescId, _expectedActionId, StringComparison.OrdinalIgnoreCase);
                    if (entityChanged || recipeChanged)
                    {
                        Bridge.Log($"Manual override detected (expected {_expectedEntity}/{_expectedActionId}, actual {activeEntity}/{activeDescId}); pausing plan");
                        lock (LockObj)
                        {
                            _state.Status = "paused";
                            _state.StatusReason = $"manual_override ({activeEntity}/{activeDescId})";
                            _stepDispatched = false;
                            PersistStateLocked();
                        }
                        return;
                    }
                }

                if (!isRunning)
                {
                    // Game stopped the action (e.g. out of raw materials, bank full, died)
                    bool exhaustAdvances = step.Until?.UntilExhausted == true
                        || string.Equals(step.OnUnexpectedStop, "advance", StringComparison.OrdinalIgnoreCase);
                    if (exhaustAdvances)
                    {
                        AdvanceStep("action_exhausted");
                        return;
                    }
                    if (string.Equals(step.OnUnexpectedStop, "retry", StringComparison.OrdinalIgnoreCase) && _retryCount < 3)
                    {
                        _retryCount++;
                        Bridge.Log($"Step [{_state.CurrentIndex}] stopped unexpectedly; retry #{_retryCount}");
                        lock (LockObj) { _stepDispatched = false; }
                        return;
                    }
                    lock (LockObj)
                    {
                        RecordHistoryLocked("stopped", "action_stopped_unexpectedly");
                        _state.Status = "paused";
                        _state.StatusReason = "action_stopped_unexpectedly";
                        _stepDispatched = false;
                        PersistStateLocked();
                    }
                    return;
                }
            }

            // 3. Evaluate stop conditions (`until`)
            string metReason = CheckUntilConditions(game, step);
            if (metReason != null)
            {
                AdvanceStep(metReason);
                return;
            }

            // 4. Periodic persistence (every 15s) so offline readers see fresh progress
            if ((DateTime.UtcNow - _lastPersistUtc).TotalSeconds >= 15)
            {
                lock (LockObj)
                {
                    SyncLiveProgressLocked();
                    PersistStateLocked();
                }
            }
        }

        private static string CheckUntilConditions(object game, PlanStep step)
        {
            var u = step.Until;
            if (u == null) return null;

            bool requireAll = string.Equals(step.UntilMode, "all", StringComparison.OrdinalIgnoreCase);
            var checks = new List<(string name, bool met)>();

            double elapsed;
            int actions, subActions, kills, dungeons;
            lock (LockObj)
            {
                elapsed = (DateTime.UtcNow - _stepStartedUtc).TotalSeconds;
                actions = _stepActionCount;
                subActions = _stepSubActionCount;
                kills = _stepKillCount;
                dungeons = _stepDungeonCount;
            }

            if (u.DurationSeconds.HasValue && u.DurationSeconds.Value > 0)
                checks.Add(($"duration>={u.DurationSeconds.Value}s", elapsed >= u.DurationSeconds.Value));

            if (u.ActionCount.HasValue && u.ActionCount.Value > 0)
                checks.Add(($"actions>={u.ActionCount.Value}", actions >= u.ActionCount.Value));

            if (u.SubActionCount.HasValue && u.SubActionCount.Value > 0)
                checks.Add(($"sub_actions>={u.SubActionCount.Value}", subActions >= u.SubActionCount.Value));

            if (u.KillCount.HasValue && u.KillCount.Value > 0)
                checks.Add(($"kills>={u.KillCount.Value}", kills >= u.KillCount.Value));

            if (u.DungeonCount.HasValue && u.DungeonCount.Value > 0)
                checks.Add(($"dungeons>={u.DungeonCount.Value}", dungeons >= u.DungeonCount.Value));

            if (u.ItemQty != null && !string.IsNullOrWhiteSpace(u.ItemQty.Item))
            {
                int cur = Bridge.GetBankItemQuantity(game, u.ItemQty.Item);
                if (u.ItemQty.Gte.HasValue)
                    checks.Add(($"item_qty({u.ItemQty.Item})>={u.ItemQty.Gte.Value}", cur >= u.ItemQty.Gte.Value));
                if (u.ItemQty.Lte.HasValue)
                    checks.Add(($"item_qty({u.ItemQty.Item})<={u.ItemQty.Lte.Value}", cur <= u.ItemQty.Lte.Value));
            }

            if (u.ItemGained != null && !string.IsNullOrWhiteSpace(u.ItemGained.Item) && u.ItemGained.Amount > 0)
            {
                int start = _stepStartItems.TryGetValue(u.ItemGained.Item, out var sq) ? sq : 0;
                int cur = Bridge.GetBankItemQuantity(game, u.ItemGained.Item);
                checks.Add(($"item_gained({u.ItemGained.Item})>={u.ItemGained.Amount}", (cur - start) >= u.ItemGained.Amount));
            }

            if (u.SkillLevel != null && !string.IsNullOrWhiteSpace(u.SkillLevel.Skill) && u.SkillLevel.Gte > 0)
            {
                int lvl = Bridge.GetSkillLevel(game, u.SkillLevel.Skill);
                checks.Add(($"skill_level({u.SkillLevel.Skill})>={u.SkillLevel.Gte}", lvl >= u.SkillLevel.Gte));
            }

            if (u.QuestStepDone != null && !string.IsNullOrWhiteSpace(u.QuestStepDone.Quest))
            {
                bool qMet = CheckQuestDone(game, u.QuestStepDone.Quest, u.QuestStepDone.Step);
                checks.Add(($"quest_done({u.QuestStepDone.Quest})", qMet));
            }

            if (checks.Count == 0) return null;

            if (requireAll)
            {
                foreach (var c in checks) if (!c.met) return null;
                return "all_met:" + string.Join("+", checks.ConvertAll(c => c.name));
            }
            else
            {
                foreach (var c in checks) if (c.met) return c.name;
                return null;
            }
        }

        private static bool CheckQuestDone(object game, string questId, string stepId)
        {
            var qComp = Bridge.FindComponent(game, "MelvorBase:Quests", "Quests");
            if (qComp == null) return false;
            var nid = Bridge.NormalizeId(questId);
            foreach (var aq in Bridge.EnumerateDictionaryValues(Bridge.Prop(qComp, "ActiveQuests")))
            {
                var id = Bridge.Prop(Bridge.Prop(aq, "Data"), "Id")?.ToString();
                if (!string.Equals(id, nid, StringComparison.OrdinalIgnoreCase)) continue;
                var qState = Bridge.Prop(aq, "State")?.ToString();
                if (qState == "Completed" || qState == "Claimed") return true;
                if (stepId != null)
                {
                    var steps = Bridge.Prop(aq, "ActiveSteps") as IEnumerable;
                    if (steps != null)
                    {
                        foreach (var s in steps)
                        {
                            if (Bridge.Prop(s, "Id")?.ToString() == stepId)
                                return Bridge.Prop(s, "State")?.ToString() == "Complete";
                        }
                    }
                }
                return false;
            }
            return false;
        }

        private static (bool isRunning, string entityId, string descId) GetCurrentActiveAction(object game)
        {
            var am = Bridge.Prop(game, "ActionManager");
            var active = Bridge.Prop(am, "ActiveActions") as IEnumerable;
            if (active != null)
            {
                foreach (var a in active)
                {
                    if (Bridge.Prop(a, "IsStarted") is bool st && st)
                    {
                        var eid = Bridge.Prop(Bridge.Prop(a, "Entity"), "Id")?.ToString();
                        var did = Bridge.Prop(Bridge.Prop(a, "CurrentDescription"), "Id")?.ToString();
                        return (true, eid, did);
                    }
                }
            }
            return (false, null, null);
        }

        private static void AdvanceStep(string reason)
        {
            Bridge.Log($"PlanScheduler step [{_state.CurrentIndex}] completed ({reason})");
            lock (LockObj)
            {
                RecordHistoryLocked("done", reason);
                _state.CurrentIndex++;
                ResetStepRuntime();
                if (_state.Plan != null && _state.CurrentIndex >= _state.Plan.Steps.Count)
                {
                    _state.Status = "completed";
                    _state.StatusReason = "all_steps_finished";
                    if (_state.Plan.StopOnComplete)
                    {
                        using var empty = JsonDocument.Parse("{}");
                        GameOperations.ExecuteOp("stop_action", empty.RootElement);
                    }
                }
                PersistStateLocked();
            }
        }

        // ------------------------------------------------------------------
        // State & Disk Persistence helpers
        // ------------------------------------------------------------------

        private static void ResetStepRuntime()
        {
            _stepDispatched = false;
            _stepActionCount = 0;
            _stepSubActionCount = 0;
            _stepKillCount = 0;
            _stepDungeonCount = 0;
            _stepStartItems.Clear();
            _expectedEntity = null;
            _expectedActionId = null;
            _retryCount = 0;
            _state.CurrentStepProgress = null;
        }

        private static string GetCurrentStepLabelLocked()
        {
            if (_state.Plan?.Steps == null || _state.CurrentIndex < 0 || _state.CurrentIndex >= _state.Plan.Steps.Count)
                return null;
            var s = _state.Plan.Steps[_state.CurrentIndex];
            return s.Label ?? s.Op;
        }

        private static void SyncLiveProgressLocked()
        {
            if (_state.Status == "running" && _stepDispatched)
            {
                _state.CurrentStepProgress = new StepProgress
                {
                    StepIndex = _state.CurrentIndex,
                    Label = GetCurrentStepLabelLocked(),
                    StartedAtUtc = _stepStartedUtc.ToString("o"),
                    ElapsedSeconds = Math.Round((DateTime.UtcNow - _stepStartedUtc).TotalSeconds, 1),
                    ActionCount = _stepActionCount,
                    SubActionCount = _stepSubActionCount,
                    KillCount = _stepKillCount,
                    DungeonCount = _stepDungeonCount
                };
            }
            _state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
        }

        private static void RecordHistoryLocked(string outcome, string reason)
        {
            var step = (_state.Plan?.Steps != null && _state.CurrentIndex < _state.Plan.Steps.Count)
                ? _state.Plan.Steps[_state.CurrentIndex]
                : null;
            _state.History.Add(new StepHistoryEntry
            {
                StepIndex = _state.CurrentIndex,
                Label = step?.Label ?? step?.Op ?? "?",
                Op = step?.Op,
                Outcome = outcome,
                Reason = reason,
                FinishedAtUtc = DateTime.UtcNow.ToString("o"),
                ElapsedSeconds = _stepDispatched ? Math.Round((DateTime.UtcNow - _stepStartedUtc).TotalSeconds, 1) : 0,
                ActionCount = _stepActionCount,
                KillCount = _stepKillCount,
                DungeonCount = _stepDungeonCount
            });
        }

        private static void PersistStateLocked()
        {
            try
            {
                SyncLiveProgressLocked();
                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PlanFilePath, json);
                _lastPersistUtc = DateTime.UtcNow;
                _lastSeenPlanFileMtimeUtc = File.GetLastWriteTimeUtc(PlanFilePath);
            }
            catch (Exception e)
            {
                Bridge.Log("PersistState failed: " + e.Message);
            }
        }

        private static void LoadStateFromDisk()
        {
            try
            {
                if (!File.Exists(PlanFilePath)) return;
                var mtime = File.GetLastWriteTimeUtc(PlanFilePath);
                var json = File.ReadAllText(PlanFilePath);
                var loaded = JsonSerializer.Deserialize<PlanState>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (loaded != null)
                {
                    lock (LockObj)
                    {
                        _state = loaded;
                        _lastSeenPlanFileMtimeUtc = mtime;
                        ResetStepRuntime();
                    }
                    Bridge.Log($"Loaded persisted plan '{loaded.Plan?.Name}' (status={loaded.Status}, step={loaded.CurrentIndex})");
                }
            }
            catch (Exception e)
            {
                Bridge.Log("LoadStateFromDisk warning: " + e.Message);
            }
        }

        private static void CheckExternalPlanFile()
        {
            try
            {
                if (!File.Exists(PlanFilePath)) return;
                var mtime = File.GetLastWriteTimeUtc(PlanFilePath);
                if (mtime <= _lastSeenPlanFileMtimeUtc) return;
                // File was updated externally (e.g. MCP wrote an offline plan while game was closed/loading)
                LoadStateFromDisk();
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // Plan JSON Schema DTOs
    // ------------------------------------------------------------------

    internal sealed class PlanDefinition
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "Unnamed Plan";
        [JsonPropertyName("auto_start")] public bool AutoStart { get; set; } = true;
        [JsonPropertyName("pause_on_manual_override")] public bool PauseOnManualOverride { get; set; } = true;
        [JsonPropertyName("stop_on_complete")] public bool StopOnComplete { get; set; } = false;
        [JsonPropertyName("steps")] public List<PlanStep> Steps { get; set; } = new List<PlanStep>();
    }

    internal sealed class PlanStep
    {
        [JsonPropertyName("label")] public string Label { get; set; }
        [JsonPropertyName("op")] public string Op { get; set; }
        [JsonPropertyName("params")] public JsonElement? Params { get; set; }
        [JsonPropertyName("until")] public StepUntil Until { get; set; }
        [JsonPropertyName("until_mode")] public string UntilMode { get; set; } = "any";
        [JsonPropertyName("on_unexpected_stop")] public string OnUnexpectedStop { get; set; } = "abort";
        [JsonPropertyName("on_error")] public string OnError { get; set; } = "abort";
        [JsonPropertyName("safety")] public StepSafety Safety { get; set; }
    }

    internal sealed class StepUntil
    {
        [JsonPropertyName("duration_seconds")] public double? DurationSeconds { get; set; }
        [JsonPropertyName("action_count")] public int? ActionCount { get; set; }
        [JsonPropertyName("sub_action_count")] public int? SubActionCount { get; set; }
        [JsonPropertyName("kill_count")] public int? KillCount { get; set; }
        [JsonPropertyName("dungeon_count")] public int? DungeonCount { get; set; }
        [JsonPropertyName("item_qty")] public ItemQtyCondition ItemQty { get; set; }
        [JsonPropertyName("item_gained")] public ItemGainedCondition ItemGained { get; set; }
        [JsonPropertyName("skill_level")] public SkillLevelCondition SkillLevel { get; set; }
        [JsonPropertyName("quest_step_done")] public QuestStepCondition QuestStepDone { get; set; }
        [JsonPropertyName("until_exhausted")] public bool UntilExhausted { get; set; }

        public bool HasAnyCondition() =>
            (DurationSeconds.HasValue && DurationSeconds.Value > 0)
            || (ActionCount.HasValue && ActionCount.Value > 0)
            || (SubActionCount.HasValue && SubActionCount.Value > 0)
            || (KillCount.HasValue && KillCount.Value > 0)
            || (DungeonCount.HasValue && DungeonCount.Value > 0)
            || ItemQty != null
            || ItemGained != null
            || SkillLevel != null
            || QuestStepDone != null
            || UntilExhausted;
    }

    internal sealed class ItemQtyCondition
    {
        [JsonPropertyName("item")] public string Item { get; set; }
        [JsonPropertyName("gte")] public int? Gte { get; set; }
        [JsonPropertyName("lte")] public int? Lte { get; set; }
    }

    internal sealed class ItemGainedCondition
    {
        [JsonPropertyName("item")] public string Item { get; set; }
        [JsonPropertyName("amount")] public int Amount { get; set; }
    }

    internal sealed class SkillLevelCondition
    {
        [JsonPropertyName("skill")] public string Skill { get; set; }
        [JsonPropertyName("gte")] public int Gte { get; set; }
    }

    internal sealed class QuestStepCondition
    {
        [JsonPropertyName("quest")] public string Quest { get; set; }
        [JsonPropertyName("step")] public string Step { get; set; }
    }

    internal sealed class StepSafety
    {
        [JsonPropertyName("min_food_qty")] public int? MinFoodQty { get; set; }
        [JsonPropertyName("stop_on_low_food")] public bool StopOnLowFood { get; set; } = true;
    }

    internal sealed class StepProgress
    {
        [JsonPropertyName("step_index")] public int StepIndex { get; set; }
        [JsonPropertyName("label")] public string Label { get; set; }
        [JsonPropertyName("started_at_utc")] public string StartedAtUtc { get; set; }
        [JsonPropertyName("elapsed_seconds")] public double ElapsedSeconds { get; set; }
        [JsonPropertyName("action_count")] public int ActionCount { get; set; }
        [JsonPropertyName("sub_action_count")] public int SubActionCount { get; set; }
        [JsonPropertyName("kill_count")] public int KillCount { get; set; }
        [JsonPropertyName("dungeon_count")] public int DungeonCount { get; set; }
    }

    internal sealed class StepHistoryEntry
    {
        [JsonPropertyName("step_index")] public int StepIndex { get; set; }
        [JsonPropertyName("label")] public string Label { get; set; }
        [JsonPropertyName("op")] public string Op { get; set; }
        [JsonPropertyName("outcome")] public string Outcome { get; set; }
        [JsonPropertyName("reason")] public string Reason { get; set; }
        [JsonPropertyName("finished_at_utc")] public string FinishedAtUtc { get; set; }
        [JsonPropertyName("elapsed_seconds")] public double ElapsedSeconds { get; set; }
        [JsonPropertyName("action_count")] public int ActionCount { get; set; }
        [JsonPropertyName("kill_count")] public int KillCount { get; set; }
        [JsonPropertyName("dungeon_count")] public int DungeonCount { get; set; }
    }

    internal sealed class PlanState
    {
        [JsonPropertyName("status")] public string Status { get; set; } = "idle";
        [JsonPropertyName("status_reason")] public string StatusReason { get; set; }
        [JsonPropertyName("current_index")] public int CurrentIndex { get; set; }
        [JsonPropertyName("created_at_utc")] public string CreatedAtUtc { get; set; }
        [JsonPropertyName("updated_at_utc")] public string UpdatedAtUtc { get; set; }
        [JsonPropertyName("current_step_progress")] public StepProgress CurrentStepProgress { get; set; }
        [JsonPropertyName("history")] public List<StepHistoryEntry> History { get; set; } = new List<StepHistoryEntry>();
        [JsonPropertyName("plan")] public PlanDefinition Plan { get; set; }
    }
}
