using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Melvor2Bridge
{
    /// <summary>
    /// Unified dispatcher for all live Melvor Idle 2 game operations.
    /// Used both by direct HTTP endpoints (/op, /start, /stop) and by the
    /// autonomous in-game PlanScheduler. Every mutating call is marshalled
    /// onto the Godot main thread via Bridge.RunOnMain.
    /// </summary>
    internal static class GameOperations
    {
        internal static readonly HashSet<string> ContinuousOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "start_action", "combat_fight", "combat_dungeon", "wait"
        };

        public static string HandleOpHttp(string body)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = doc.RootElement;
            string op = GetStr(root, "op");
            if (string.IsNullOrWhiteSpace(op))
                return Bridge.Serialize(new { ok = false, error = "missing 'op'" });
            JsonElement p = root.TryGetProperty("params", out var pp) ? pp : root;
            var res = ExecuteOp(op, p);
            return Bridge.Serialize(res);
        }

        public static string HandleLegacyStart(string body)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var res = ExecuteOp("start_action", doc.RootElement);
            return Bridge.Serialize(res);
        }

        public static string HandleLegacyStop(string body)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var res = ExecuteOp("stop_action", doc.RootElement);
            return Bridge.Serialize(res);
        }

        internal static Dictionary<string, object> ExecuteOp(string op, JsonElement p)
        {
            var game = Bridge.GetGame();
            if (game == null)
                return Err("game not found yet (main menu or still loading?)");

            switch (op.ToLowerInvariant())
            {
                // --- Active Skill Actions ---
                case "start_action": return OpStartAction(game, p);
                case "stop_action": return OpStopAction(game, p);
                case "wait": return Ok("wait", "waiting on current state");
                case "fishing_select_bait": return OpFishingBait(game, p);
                case "firemaking_bonfire_add": return OpBonfireAdd(game, p);
                case "firemaking_bonfire_upgrade": return OpBonfireUpgrade(game);
                case "astrology_unlock_node": return OpAstrologyUnlockNode(game, p);
                case "agility_create_shortcut": return OpAgilityShortcut(game, p);

                // --- Combat / Equipment / Loadouts / Slayer ---
                case "combat_fight": return OpCombatFight(game, p);
                case "combat_dungeon": return OpCombatDungeon(game, p);
                case "combat_stop": return OpCombatStop(game);
                case "select_food": return OpSelectFood(game, p);
                case "eat_food": return OpEatFood(game);
                case "equip": return OpEquip(game, p);
                case "unequip": return OpUnequip(game, p);
                case "equip_tool": return OpEquipTool(game, p);
                case "unequip_tool": return OpUnequipTool(game, p);
                case "swap_loadout": return OpSwapLoadout(game, p);
                case "select_spell": return OpSelectSpell(game, p);
                case "select_imbuement": return OpSelectImbuement(game, p);
                case "toggle_prayer": return OpTogglePrayer(game, p);
                case "set_stance": return OpSetStance(game, p);
                case "slayer_task": return OpSlayerTask(game, p);

                // --- Bank & Shop ---
                case "shop_buy": return OpShopBuy(game, p);
                case "bank_sell": return OpBankSell(game, p);
                case "bank_open": return OpBankOpen(game, p);
                case "bank_bury": return OpBankBury(game, p);
                case "bank_upgrade": return OpBankUpgrade(game, p);

                // --- Quests ---
                case "quest_start": return OpQuestStart(game, p);
                case "quest_submit": return OpQuestSubmit(game, p);
                case "quest_confirm": return OpQuestConfirm(game, p);
                case "quest_claim": return OpQuestClaim(game, p);

                // --- Passive Skills: Farming & Ranching ---
                case "farming_harvest_all": return OpFarmingHarvestAll(game, p);
                case "farming_plant_all": return OpFarmingPlantAll(game, p);
                case "farming_water_all": return OpFarmingWaterAll(game, p);
                case "farming_purchase_plots": return OpFarmingPurchasePlots(game, p);
                case "farming_grain_mill": return OpFarmingGrainMill(game, p);
                case "ranching_maintain": return OpRanchingMaintain(game, p);
                case "ranching_buy_animal": return OpRanchingBuyAnimal(game, p);
                case "ranching_toggle_treat": return OpRanchingToggleTreat(game, p);

                default:
                    return Err($"unknown op: {op}");
            }
        }

        // ------------------------------------------------------------------
        // Active Skill Actions
        // ------------------------------------------------------------------

        private static Dictionary<string, object> OpStartAction(object game, JsonElement p)
        {
            string entityId = GetStr(p, "entity");
            string compId = GetStr(p, "component");
            string recipeId = GetStr(p, "recipe");
            string pathId = GetStr(p, "path");
            if (string.IsNullOrWhiteSpace(entityId))
                return Err("missing 'entity'");

            entityId = Bridge.NormalizeId(entityId);
            var action = Bridge.FindComponent(game, entityId, compId);
            if (action == null)
                return Err($"action component not found: {entityId}/{compId ?? "(any)"}");

            string typeName = action.GetType().Name;
            string normRecipe = string.IsNullOrWhiteSpace(recipeId) ? null : Bridge.NormalizeId(recipeId);

            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                bool alreadyStarted = Bridge.Prop(action, "IsStarted") is bool b0 && b0;
                var curDescId = Bridge.Prop(Bridge.Prop(action, "CurrentDescription"), "Id")?.ToString();

                // Toggle-safe check: Mining/Woodcutting/Thieving/Astrology/Agility toggle OFF if re-selected!
                if (alreadyStarted && normRecipe != null
                    && string.Equals(curDescId, normRecipe, StringComparison.OrdinalIgnoreCase))
                {
                    return new Dictionary<string, object>
                    {
                        ["ok"] = true,
                        ["started"] = true,
                        ["already_running"] = true,
                        ["entity"] = entityId,
                        ["component"] = Bridge.Prop(action, "Id")?.ToString(),
                        ["recipe"] = normRecipe
                    };
                }

                if (normRecipe != null)
                {
                    var option = FindOption(action, normRecipe);
                    if (option == null)
                        return Err($"recipe/option not found on {entityId}: {normRecipe}");

                    // If switching recipes within the same already-running Artisan/Crafting/Fishing
                    // skill, stop the old recipe first so SelectRecipe + TryStart/StartCreating
                    // actually switches to the new recipe instead of leaving the old one running.
                    bool isToggleGather = typeName == "MiningActionComponent"
                        || typeName == "WoodcuttingActionComponent"
                        || typeName == "ThievingActionComponent"
                        || typeName == "AstrologyActionComponent"
                        || typeName == "AgilityActionComponent";
                    if (alreadyStarted && !isToggleGather)
                    {
                        var tryStop = action.GetType().GetMethod("TryStop", new[] { typeof(bool) });
                        if (tryStop != null) tryStop.Invoke(action, new object[] { true });
                        else action.GetType().GetMethod("TryStop", Type.EmptyTypes)?.Invoke(action, null);
                    }

                    // Special case: Hunting -> SelectAnimal + StartTracking
                    if (typeName == "HuntingActionComponent")
                    {
                        var sel = action.GetType().GetMethod("SelectAnimal");
                        sel?.Invoke(action, new[] { option });
                        var st = action.GetType().GetMethod("StartTracking");
                        st?.Invoke(action, null);
                    }
                    // Special case: Thieving -> TryStartActiveRecipe
                    else if (typeName == "ThievingActionComponent")
                    {
                        var ts = action.GetType().GetMethod("TryStartActiveRecipe");
                        ts?.Invoke(action, new[] { option });
                    }
                    // Special case: Astrology -> StudyConstellation
                    else if (typeName == "AstrologyActionComponent")
                    {
                        var sc = action.GetType().GetMethod("StudyConstellation");
                        sc?.Invoke(action, new[] { option });
                    }
                    // Special case: Agility -> optionally SelectPath then SelectCourse
                    else if (typeName == "AgilityActionComponent")
                    {
                        if (!string.IsNullOrWhiteSpace(pathId))
                        {
                            var pathObj = FindOptionInDict(Bridge.Prop(option, "ActivePaths"), Bridge.NormalizeId(pathId));
                            if (pathObj != null)
                                option.GetType().GetMethod("SelectPath")?.Invoke(option, new[] { pathObj });
                        }
                        action.GetType().GetMethod("SelectCourse")?.Invoke(action, new[] { option });
                    }
                    else
                    {
                        var select = FindSelectMethod(action, option);
                        if (select == null)
                            return Err($"no Select* method on {typeName} accepts {option.GetType().Name}");
                        select.Invoke(action, new[] { option });
                    }
                }

                // If still not started (e.g. Artisan / Fishing / Cooking / Smithing), call TryStart or StartCreating
                bool started = Bridge.Prop(action, "IsStarted") is bool b1 && b1;
                if (!started)
                {
                    var startCreating = action.GetType().GetMethod("StartCreating", Type.EmptyTypes);
                    if (startCreating != null)
                    {
                        startCreating.Invoke(action, null);
                        started = Bridge.Prop(action, "IsStarted") is bool b3 && b3;
                    }
                    if (!started)
                    {
                        var tryStart = action.GetType().GetMethod("TryStart", Type.EmptyTypes);
                        if (tryStart != null)
                        {
                            var res = tryStart.Invoke(action, null);
                            started = (res is bool sb && sb) || (Bridge.Prop(action, "IsStarted") is bool b2 && b2);
                        }
                    }
                }

                return new Dictionary<string, object>
                {
                    ["ok"] = started,
                    ["started"] = started,
                    ["entity"] = entityId,
                    ["component"] = Bridge.Prop(action, "Id")?.ToString(),
                    ["recipe"] = normRecipe,
                    ["action"] = Bridge.NameOf(Bridge.Prop(action, "CurrentDescription"))
                };
            });
        }

        private static Dictionary<string, object> OpStopAction(object game, JsonElement p)
        {
            string entityId = GetStr(p, "entity");
            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                var am = Bridge.Prop(game, "ActionManager");
                if (string.IsNullOrWhiteSpace(entityId))
                {
                    var m = am?.GetType().GetMethod("TryStopActive", Type.EmptyTypes);
                    bool stopped = m != null && m.Invoke(am, null) is bool b && b;
                    return new Dictionary<string, object> { ["ok"] = true, ["stopped"] = stopped };
                }

                var nid = Bridge.NormalizeId(entityId);
                var active = Bridge.Prop(am, "ActiveActions") as IEnumerable;
                if (active != null)
                {
                    foreach (var action in active)
                    {
                        var ent = Bridge.Prop(action, "Entity");
                        if (!string.Equals(Bridge.Prop(ent, "Id")?.ToString(), nid, StringComparison.OrdinalIgnoreCase))
                            continue;
                        var tryStop = action.GetType().GetMethod("TryStop", new[] { typeof(bool) });
                        var res = tryStop != null
                            ? tryStop.Invoke(action, new object[] { true })
                            : action.GetType().GetMethod("TryStop", Type.EmptyTypes)?.Invoke(action, null);
                        bool stopped = res is bool b && b;
                        return new Dictionary<string, object>
                        {
                            ["ok"] = true,
                            ["stopped"] = stopped,
                            ["entity"] = nid
                        };
                    }
                }
                return new Dictionary<string, object>
                {
                    ["ok"] = true,
                    ["stopped"] = false,
                    ["entity"] = nid,
                    ["note"] = "no active action on this entity"
                };
            });
        }

        private static Dictionary<string, object> OpFishingBait(object game, JsonElement p)
        {
            string itemId = GetStr(p, "item");
            var comp = Bridge.FindComponent(game, "MelvorBase:Fishing", "BaitComponent");
            if (comp == null) return Err("FishingBaitComponent not found");
            object item = string.IsNullOrWhiteSpace(itemId) ? null : Bridge.LookupDataObject(game, "Item", itemId);
            if (!string.IsNullOrWhiteSpace(itemId) && item == null) return Err($"item not found: {itemId}");
            Bridge.RunOnMain(() => { comp.GetType().GetMethod("SelectBait")?.Invoke(comp, new[] { item }); return null; });
            return Ok("fishing_select_bait", itemId ?? "(cleared)");
        }

        private static Dictionary<string, object> OpBonfireAdd(object game, JsonElement p)
        {
            int amount = GetInt(p, "amount", 10);
            var comp = Bridge.FindComponent(game, "MelvorBase:Firemaking", "Bonfire");
            if (comp == null) return Err("FiremakingBonfireComponent not found");
            Bridge.RunOnMain(() => { comp.GetType().GetMethod("AddLogs")?.Invoke(comp, new object[] { amount }); return null; });
            return Ok("firemaking_bonfire_add", $"added {amount} logs");
        }

        private static Dictionary<string, object> OpBonfireUpgrade(object game)
        {
            var comp = Bridge.FindComponent(game, "MelvorBase:Firemaking", "Bonfire");
            if (comp == null) return Err("FiremakingBonfireComponent not found");
            Bridge.RunOnMain(() => { comp.GetType().GetMethod("PurchaseNextBonfire")?.Invoke(comp, null); return null; });
            return Ok("firemaking_bonfire_upgrade", "purchased next bonfire");
        }

        private static Dictionary<string, object> OpAstrologyUnlockNode(object game, JsonElement p)
        {
            string conId = GetStr(p, "constellation");
            string nodeId = GetStr(p, "node");
            bool major = GetBool(p, "major", false);
            var action = Bridge.FindComponent(game, "MelvorBase:Astrology", "Action");
            if (action == null) return Err("AstrologyActionComponent not found");
            var con = FindOption(action, Bridge.NormalizeId(conId));
            var node = Bridge.LookupDataObject(game, "AstrologyNode", nodeId);
            if (con == null) return Err($"constellation not found: {conId}");
            if (node == null) return Err($"node not found: {nodeId}");
            bool unlocked = (bool)Bridge.RunOnMain(() =>
            {
                var m = con.GetType().GetMethod(major ? "UnlockMajorNode" : "UnlockMinorNode");
                return m != null && m.Invoke(con, new[] { node }) is bool b && b;
            });
            return new Dictionary<string, object> { ["ok"] = unlocked, ["unlocked"] = unlocked, ["constellation"] = conId, ["node"] = nodeId };
        }

        private static Dictionary<string, object> OpAgilityShortcut(object game, JsonElement p)
        {
            string courseId = GetStr(p, "course");
            string obstacleId = GetStr(p, "obstacle");
            var action = Bridge.FindComponent(game, "MelvorBase:Agility", "Action");
            var course = FindOption(action, Bridge.NormalizeId(courseId));
            if (course == null) return Err($"course not found: {courseId}");
            string normObs = Bridge.NormalizeId(obstacleId);
            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                foreach (var path in Bridge.EnumerateDictionaryValues(Bridge.Prop(course, "ActivePaths")))
                {
                    foreach (var obs in Bridge.EnumerateDictionaryValues(Bridge.Prop(path, "Obstacles") ?? Bridge.Prop(path, "ActiveObstacles")))
                    {
                        var odata = Bridge.Prop(obs, "Obstacle");
                        if (string.Equals(Bridge.Prop(odata, "Id")?.ToString(), normObs, StringComparison.OrdinalIgnoreCase))
                        {
                            obs.GetType().GetMethod("CreateShortcut")?.Invoke(obs, null);
                            return Ok("agility_create_shortcut", normObs);
                        }
                    }
                }
                return Err($"obstacle not found in course {courseId}: {obstacleId}");
            });
        }

        // ------------------------------------------------------------------
        // Combat / Equipment / Loadouts / Slayer
        // ------------------------------------------------------------------

        private static Dictionary<string, object> OpCombatFight(object game, JsonElement p)
        {
            string areaId = GetStr(p, "area");
            string enemyId = GetStr(p, "enemy");
            if (string.IsNullOrWhiteSpace(areaId) || string.IsNullOrWhiteSpace(enemyId))
                return Err("requires 'area' and 'enemy'");

            var areas = Bridge.FindComponent(game, "MelvorBase:Combat", "Areas");
            var action = Bridge.FindComponent(game, "MelvorBase:Combat", "Action");
            var area = Bridge.LookupDataObject(game, "CombatArea", areaId);
            var enemy = Bridge.LookupDataObject(game, "Enemy", enemyId);
            if (areas == null || action == null) return Err("Combat components not found");
            if (area == null) return Err($"CombatArea not found: {areaId}");
            if (enemy == null) return Err($"Enemy not found: {enemyId}");

            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                areas.GetType().GetMethod("SelectEnemy")?.Invoke(areas, new[] { area, enemy });
                var tryStart = action.GetType().GetMethod("TryStart", Type.EmptyTypes);
                bool started = (Bridge.Prop(action, "IsStarted") is bool b0 && b0)
                    || (tryStart != null && tryStart.Invoke(action, null) is bool b1 && b1)
                    || (Bridge.Prop(action, "IsStarted") is bool b2 && b2);
                return new Dictionary<string, object>
                {
                    ["ok"] = started,
                    ["started"] = started,
                    ["area"] = Bridge.NormalizeId(areaId),
                    ["enemy"] = Bridge.NormalizeId(enemyId)
                };
            });
        }

        private static Dictionary<string, object> OpCombatDungeon(object game, JsonElement p)
        {
            string dungeonId = GetStr(p, "dungeon");
            if (string.IsNullOrWhiteSpace(dungeonId)) return Err("missing 'dungeon'");

            var areas = Bridge.FindComponent(game, "MelvorBase:Combat", "Areas");
            var action = Bridge.FindComponent(game, "MelvorBase:Combat", "Action");
            var dungeon = Bridge.LookupDataObject(game, "Dungeon", dungeonId);
            if (areas == null || action == null) return Err("Combat components not found");
            if (dungeon == null) return Err($"Dungeon not found: {dungeonId}");

            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                areas.GetType().GetMethod("SelectDungeon")?.Invoke(areas, new[] { dungeon });
                var tryStart = action.GetType().GetMethod("TryStart", Type.EmptyTypes);
                bool started = (Bridge.Prop(action, "IsStarted") is bool b0 && b0)
                    || (tryStart != null && tryStart.Invoke(action, null) is bool b1 && b1)
                    || (Bridge.Prop(action, "IsStarted") is bool b2 && b2);
                return new Dictionary<string, object>
                {
                    ["ok"] = started,
                    ["started"] = started,
                    ["dungeon"] = Bridge.NormalizeId(dungeonId)
                };
            });
        }

        private static Dictionary<string, object> OpCombatStop(object game)
        {
            var areas = Bridge.FindComponent(game, "MelvorBase:Combat", "Areas");
            var action = Bridge.FindComponent(game, "MelvorBase:Combat", "Action");
            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                action?.GetType().GetMethod("TryStop", new[] { typeof(bool) })?.Invoke(action, new object[] { true });
                areas?.GetType().GetMethod("UnselectArea", Type.EmptyTypes)?.Invoke(areas, null);
                return Ok("combat_stop", "left combat");
            });
        }

        private static Dictionary<string, object> OpSelectFood(object game, JsonElement p)
        {
            string itemId = GetStr(p, "item");
            var food = Bridge.FindComponent(game, "MelvorBase:Combat", "Food");
            if (food == null) return Err("FoodComponent not found");
            if (string.IsNullOrWhiteSpace(itemId))
            {
                Bridge.RunOnMain(() => { food.GetType().GetMethod("UnselectFood")?.Invoke(food, null); return null; });
                return Ok("select_food", "unselected");
            }
            var item = Bridge.LookupDataObject(game, "Item", itemId);
            if (item == null) return Err($"item not found: {itemId}");
            Bridge.RunOnMain(() => { food.GetType().GetMethod("SelectFood")?.Invoke(food, new[] { item }); return null; });
            return Ok("select_food", Bridge.NormalizeId(itemId));
        }

        private static Dictionary<string, object> OpEatFood(object game)
        {
            var food = Bridge.FindComponent(game, "MelvorBase:Combat", "Food");
            if (food == null) return Err("FoodComponent not found");
            Bridge.RunOnMain(() => { food.GetType().GetMethod("ManualEat")?.Invoke(food, null); return null; });
            return Ok("eat_food", "ate food");
        }

        private static Dictionary<string, object> OpEquip(object game, JsonElement p)
        {
            string itemId = GetStr(p, "item");
            string slotId = GetStr(p, "slot");
            int qty = GetInt(p, "quantity", -1);
            if (string.IsNullOrWhiteSpace(itemId)) return Err("missing 'item'");

            var comp = Bridge.FindComponent(game, "MelvorBase:Combat", "ItemEquipping");
            var item = Bridge.LookupDataObject(game, "Item", itemId);
            if (comp == null) return Err("ItemEquippingComponent not found");
            if (item == null) return Err($"item not found: {itemId}");

            object slot = string.IsNullOrWhiteSpace(slotId)
                ? AutoDetectEquipmentSlot(game, item)
                : Bridge.LookupDataObject(game, "EquipmentSlot", slotId);
            if (slot == null) return Err($"could not resolve EquipmentSlot for {itemId} (specify 'slot')");

            int finalQty = qty > 0 ? qty : Math.Max(1, Bridge.GetBankItemQuantity(game, itemId));
            // Non-stackable equipment uses qty 1
            if (qty <= 0)
            {
                // Check CanEquipQuantity on EquipmentCapability if possible
                finalQty = 1;
                var bankQty = Bridge.GetBankItemQuantity(game, itemId);
                if (IsStackableEquipment(item) && bankQty > 0) finalQty = bankQty;
            }

            Bridge.RunOnMain(() =>
            {
                comp.GetType().GetMethod("EquipItem")?.Invoke(comp, new[] { item, (object)finalQty, slot });
                return null;
            });
            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["op"] = "equip",
                ["item"] = Bridge.NormalizeId(itemId),
                ["slot"] = Bridge.Prop(slot, "Id")?.ToString(),
                ["quantity"] = finalQty
            };
        }

        private static Dictionary<string, object> OpUnequip(object game, JsonElement p)
        {
            string slotId = GetStr(p, "slot");
            if (string.IsNullOrWhiteSpace(slotId)) return Err("missing 'slot'");
            var comp = Bridge.FindComponent(game, "MelvorBase:Combat", "ItemEquipping");
            var slot = Bridge.LookupDataObject(game, "EquipmentSlot", slotId);
            if (comp == null || slot == null) return Err($"slot not found: {slotId}");
            Bridge.RunOnMain(() => { comp.GetType().GetMethod("UnequipSlot")?.Invoke(comp, new[] { slot }); return null; });
            return Ok("unequip", Bridge.NormalizeId(slotId));
        }

        private static Dictionary<string, object> OpEquipTool(object game, JsonElement p)
        {
            string entityId = GetStr(p, "entity");
            string itemId = GetStr(p, "item");
            string slotId = GetStr(p, "slot");
            int qty = GetInt(p, "quantity", 1);
            if (string.IsNullOrWhiteSpace(entityId) || string.IsNullOrWhiteSpace(itemId))
                return Err("requires 'entity' and 'item'");

            var comp = Bridge.FindComponent(game, entityId, "ToolbeltEquipping");
            var tb = Bridge.FindComponent(game, entityId, "Toolbelt");
            var item = Bridge.LookupDataObject(game, "Item", itemId);
            if (comp == null || tb == null) return Err($"Toolbelt components not found on {entityId}");
            if (item == null) return Err($"item not found: {itemId}");

            object slot = string.IsNullOrWhiteSpace(slotId)
                ? AutoDetectToolbeltSlot(tb, item)
                : Bridge.LookupDataObject(game, "ToolbeltSlot", slotId);
            if (slot == null) return Err($"could not resolve ToolbeltSlot for {itemId} on {entityId} (specify 'slot')");

            Bridge.RunOnMain(() =>
            {
                comp.GetType().GetMethod("EquipItem")?.Invoke(comp, new[] { item, (object)qty, slot });
                return null;
            });
            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["op"] = "equip_tool",
                ["entity"] = Bridge.NormalizeId(entityId),
                ["item"] = Bridge.NormalizeId(itemId),
                ["slot"] = Bridge.Prop(slot, "Id")?.ToString()
            };
        }

        private static Dictionary<string, object> OpUnequipTool(object game, JsonElement p)
        {
            string entityId = GetStr(p, "entity");
            string slotId = GetStr(p, "slot");
            var comp = Bridge.FindComponent(game, entityId, "ToolbeltEquipping");
            var slot = Bridge.LookupDataObject(game, "ToolbeltSlot", slotId);
            if (comp == null || slot == null) return Err("ToolbeltEquipping or ToolbeltSlot not found");
            Bridge.RunOnMain(() => { comp.GetType().GetMethod("UnequipSlot")?.Invoke(comp, new[] { slot }); return null; });
            return Ok("unequip_tool", $"{entityId}/{slotId}");
        }

        private static Dictionary<string, object> OpSwapLoadout(object game, JsonElement p)
        {
            string slotId = GetStr(p, "slot");
            var comp = Bridge.FindComponent(game, "MelvorBase:Combat", "Loadouts");
            var slot = Bridge.LookupDataObject(game, "LoadoutSlot", slotId);
            if (comp == null || slot == null) return Err($"LoadoutSlot not found: {slotId}");
            Bridge.RunOnMain(() => { comp.GetType().GetMethod("SwapToLoadout")?.Invoke(comp, new[] { slot }); return null; });
            return Ok("swap_loadout", Bridge.NormalizeId(slotId));
        }

        private static Dictionary<string, object> OpSelectSpell(object game, JsonElement p)
        {
            string spellId = GetStr(p, "spell");
            var comp = Bridge.FindComponent(game, "MelvorBase:Combat", "MagicSpells");
            var spell = Bridge.LookupDataObject(game, "MagicSpell", spellId);
            if (comp == null || spell == null) return Err($"MagicSpell not found: {spellId}");
            Bridge.RunOnMain(() => { comp.GetType().GetMethod("SelectSpell")?.Invoke(comp, new[] { spell }); return null; });
            return Ok("select_spell", Bridge.NormalizeId(spellId));
        }

        private static Dictionary<string, object> OpSelectImbuement(object game, JsonElement p)
        {
            string imbId = GetStr(p, "imbuement");
            var comp = Bridge.FindComponent(game, "MelvorBase:Combat", "MagicSpells");
            if (comp == null) return Err("MagicSpellsComponent not found");
            if (string.IsNullOrWhiteSpace(imbId))
            {
                Bridge.RunOnMain(() => { comp.GetType().GetMethod("DeselectImbuement")?.Invoke(comp, null); return null; });
                return Ok("select_imbuement", "deselected");
            }
            var imb = Bridge.LookupDataObject(game, "Imbuement", imbId);
            if (imb == null) return Err($"Imbuement not found: {imbId}");
            Bridge.RunOnMain(() => { comp.GetType().GetMethod("SelectImbuement")?.Invoke(comp, new[] { imb }); return null; });
            return Ok("select_imbuement", Bridge.NormalizeId(imbId));
        }

        private static Dictionary<string, object> OpTogglePrayer(object game, JsonElement p)
        {
            string prayerId = GetStr(p, "prayer");
            var comp = Bridge.FindComponent(game, "MelvorBase:Combat", "Prayer");
            var prayer = Bridge.LookupDataObject(game, "Prayer", prayerId);
            if (comp == null || prayer == null) return Err($"Prayer not found: {prayerId}");
            bool? desired = p.TryGetProperty("enabled", out var pe) && (pe.ValueKind == JsonValueKind.True || pe.ValueKind == JsonValueKind.False)
                ? pe.GetBoolean()
                : (bool?)null;

            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                var isSel = comp.GetType().GetMethod("IsPrayerSelected");
                bool currently = isSel != null && isSel.Invoke(comp, new[] { prayer }) is bool b && b;
                if (desired.HasValue && currently == desired.Value)
                    return new Dictionary<string, object> { ["ok"] = true, ["prayer"] = Bridge.NormalizeId(prayerId), ["selected"] = currently, ["unchanged"] = true };
                comp.GetType().GetMethod("TogglePrayer")?.Invoke(comp, new[] { prayer });
                bool now = isSel != null && isSel.Invoke(comp, new[] { prayer }) is bool b2 && b2;
                return new Dictionary<string, object> { ["ok"] = true, ["prayer"] = Bridge.NormalizeId(prayerId), ["selected"] = now };
            });
        }

        private static Dictionary<string, object> OpSetStance(object game, JsonElement p)
        {
            string stanceId = GetStr(p, "stance");
            string wtId = GetStr(p, "weapon_type");
            var player = Bridge.FindComponent(game, "MelvorBase:Combat", "Player");
            var stance = Bridge.LookupDataObject(game, "AttackStance", stanceId);
            if (player == null || stance == null) return Err($"AttackStance not found: {stanceId}");

            object wt = string.IsNullOrWhiteSpace(wtId)
                ? Bridge.Prop(Bridge.Prop(Bridge.Prop(player, "Equipment"), "EquippedWeapon"), "Weapon")
                : Bridge.LookupDataObject(game, "WeaponType", wtId);
            if (wt != null && wt.GetType().Name != "WeaponType")
                wt = Bridge.Prop(wt, "Type");
            if (wt == null) return Err("could not determine WeaponType (pass 'weapon_type')");

            Bridge.RunOnMain(() => { player.GetType().GetMethod("SetSelectedStance")?.Invoke(player, new[] { wt, stance }); return null; });
            return Ok("set_stance", Bridge.NormalizeId(stanceId));
        }

        private static Dictionary<string, object> OpSlayerTask(object game, JsonElement p)
        {
            string masterId = GetStr(p, "master") ?? "MelvorBase:Kuren";
            string actionName = (GetStr(p, "action") ?? "start").ToLowerInvariant();
            var slayer = Bridge.FindComponent(game, "MelvorBase:Slayer", "Tasks");
            if (slayer == null) return Err("SlayerTasksComponent not found");
            var normMaster = Bridge.NormalizeId(masterId);

            object targetMaster = null;
            foreach (var m in Bridge.EnumerateDictionaryValues(Bridge.Prop(slayer, "ActiveMasters")))
            {
                if (string.Equals(Bridge.Prop(Bridge.Prop(m, "Data"), "Id")?.ToString(), normMaster, StringComparison.OrdinalIgnoreCase))
                {
                    targetMaster = m;
                    break;
                }
            }
            if (targetMaster == null) return Err($"SlayerMaster not found: {normMaster}");

            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                switch (actionName)
                {
                    case "start": targetMaster.GetType().GetMethod("StartInitialTask")?.Invoke(targetMaster, null); break;
                    case "reroll": targetMaster.GetType().GetMethod("RerollTask")?.Invoke(targetMaster, null); break;
                    case "extend": targetMaster.GetType().GetMethod("ExtendTask")?.Invoke(targetMaster, null); break;
                    case "auto_on": targetMaster.GetType().GetProperty("AutoSlayerEnabled")?.SetValue(targetMaster, true); break;
                    case "auto_off": targetMaster.GetType().GetProperty("AutoSlayerEnabled")?.SetValue(targetMaster, false); break;
                    default: return Err($"unknown slayer action: {actionName}");
                }
                var cur = Bridge.Prop(targetMaster, "CurrentTask");
                return new Dictionary<string, object>
                {
                    ["ok"] = true,
                    ["master"] = normMaster,
                    ["action"] = actionName,
                    ["enemy"] = Bridge.Prop(cur, "Id")?.ToString(),
                    ["length"] = Bridge.Prop(targetMaster, "TaskLength")
                };
            });
        }

        // ------------------------------------------------------------------
        // Bank & Shop
        // ------------------------------------------------------------------

        private static Dictionary<string, object> OpShopBuy(object game, JsonElement p)
        {
            string purchaseId = GetStr(p, "purchase") ?? GetStr(p, "id");
            int qty = GetInt(p, "quantity", 1);
            if (string.IsNullOrWhiteSpace(purchaseId)) return Err("missing 'purchase'");

            var shop = Bridge.FindComponent(game, "MelvorBase:Shop", "Shop");
            var sp = Bridge.LookupDataObject(game, "ShopPurchase", purchaseId);
            if (shop == null || sp == null) return Err($"ShopPurchase not found: {purchaseId}");

            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                var res = shop.GetType().GetMethod("ProcessPurchase")?.Invoke(shop, new[] { sp, (object)qty });
                string resStr = res?.ToString() ?? "Unknown";
                return new Dictionary<string, object>
                {
                    ["ok"] = resStr == "Success",
                    ["result"] = resStr,
                    ["purchase"] = Bridge.NormalizeId(purchaseId),
                    ["quantity"] = qty
                };
            });
        }

        private static Dictionary<string, object> OpBankSell(object game, JsonElement p)
        {
            string itemId = GetStr(p, "item");
            int qty = GetInt(p, "quantity", -1);
            var sale = Bridge.FindComponent(game, "MelvorBase:Bank", "ItemSales");
            var item = Bridge.LookupDataObject(game, "Item", itemId);
            if (sale == null || item == null) return Err($"Item or ItemSaleComponent not found: {itemId}");

            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                int have = Bridge.GetBankItemQuantity(game, itemId);
                int sellQty = qty <= 0 ? have : Math.Min(have, qty);
                if (sellQty <= 0) return Err($"no quantity of {itemId} to sell");
                var stats = sale.GetType().GetMethod("GetItemStats")?.Invoke(sale, new[] { item });
                sale.GetType().GetMethod("SellItem")?.Invoke(sale, new[] { item, stats, sellQty });
                return new Dictionary<string, object> { ["ok"] = true, ["sold"] = sellQty, ["item"] = Bridge.NormalizeId(itemId) };
            });
        }

        private static Dictionary<string, object> OpBankOpen(object game, JsonElement p)
        {
            string itemId = GetStr(p, "item");
            int qty = GetInt(p, "quantity", -1);
            var opening = Bridge.FindComponent(game, "MelvorBase:Bank", "ItemOpening");
            var item = Bridge.LookupDataObject(game, "Item", itemId);
            if (opening == null || item == null) return Err($"Item or ItemOpeningComponent not found: {itemId}");

            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                int have = Bridge.GetBankItemQuantity(game, itemId);
                int openQty = qty <= 0 ? have : Math.Min(have, qty);
                if (openQty <= 0) return Err($"no quantity of {itemId} to open");
                opening.GetType().GetMethod("OpenItem")?.Invoke(opening, new[] { item, (object)openQty });
                return new Dictionary<string, object> { ["ok"] = true, ["opened"] = openQty, ["item"] = Bridge.NormalizeId(itemId) };
            });
        }

        private static Dictionary<string, object> OpBankBury(object game, JsonElement p)
        {
            string itemId = GetStr(p, "item");
            int qty = GetInt(p, "quantity", -1);
            var burying = Bridge.FindComponent(game, "MelvorBase:Bank", "ItemBurying");
            var item = Bridge.LookupDataObject(game, "Item", itemId);
            if (burying == null || item == null) return Err($"Item or ItemBuryingComponent not found: {itemId}");

            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                int have = Bridge.GetBankItemQuantity(game, itemId);
                int buryQty = qty <= 0 ? have : Math.Min(have, qty);
                if (buryQty <= 0) return Err($"no quantity of {itemId} to bury");
                var stats = burying.GetType().GetMethod("GetItemStats")?.Invoke(burying, new[] { item });
                burying.GetType().GetMethod("BuryItem")?.Invoke(burying, new[] { item, stats, buryQty });
                return new Dictionary<string, object> { ["ok"] = true, ["buried"] = buryQty, ["item"] = Bridge.NormalizeId(itemId) };
            });
        }

        private static Dictionary<string, object> OpBankUpgrade(object game, JsonElement p)
        {
            string upId = GetStr(p, "upgrade");
            int qty = GetInt(p, "quantity", 1);
            var upgComp = Bridge.FindComponent(game, "MelvorBase:Bank", "ItemUpgrading");
            var upg = Bridge.LookupDataObject(game, "ItemUpgrade", upId);
            if (upgComp == null || upg == null) return Err($"ItemUpgrade not found: {upId}");

            bool ok = (bool)Bridge.RunOnMain(() =>
            {
                var res = upgComp.GetType().GetMethod("ExecuteUpgrade")?.Invoke(upgComp, new[] { upg, (object)qty });
                return res is bool b && b;
            });
            return new Dictionary<string, object> { ["ok"] = ok, ["upgrade"] = Bridge.NormalizeId(upId), ["quantity"] = qty };
        }

        // ------------------------------------------------------------------
        // Quests
        // ------------------------------------------------------------------

        private static object FindActiveQuest(object game, string questId, out object qComp)
        {
            qComp = Bridge.FindComponent(game, "MelvorBase:Quests", "Quests");
            if (qComp == null || string.IsNullOrWhiteSpace(questId)) return null;
            var nid = Bridge.NormalizeId(questId);
            foreach (var aq in Bridge.EnumerateDictionaryValues(Bridge.Prop(qComp, "ActiveQuests")))
            {
                var id = Bridge.Prop(Bridge.Prop(aq, "Data"), "Id")?.ToString();
                if (string.Equals(id, nid, StringComparison.OrdinalIgnoreCase))
                    return aq;
            }
            return null;
        }

        private static Dictionary<string, object> OpQuestStart(object game, JsonElement p)
        {
            string qid = GetStr(p, "quest");
            var aq = FindActiveQuest(game, qid, out _);
            if (aq == null) return Err($"quest not found: {qid}");
            Bridge.RunOnMain(() => { aq.GetType().GetMethod("Start", Type.EmptyTypes)?.Invoke(aq, null); return null; });
            return new Dictionary<string, object> { ["ok"] = true, ["quest"] = Bridge.NormalizeId(qid), ["state"] = Bridge.Prop(aq, "State")?.ToString() };
        }

        private static Dictionary<string, object> OpQuestSubmit(object game, JsonElement p)
        {
            string qid = GetStr(p, "quest");
            string stepId = GetStr(p, "step");
            string taskId = GetStr(p, "task");
            var aq = FindActiveQuest(game, qid, out var qComp);
            if (aq == null) return Err($"quest not found: {qid}");

            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                var steps = Bridge.Prop(aq, "ActiveSteps") as IEnumerable;
                if (steps != null)
                {
                    foreach (var step in steps)
                    {
                        var sid = Bridge.Prop(step, "Id")?.ToString();
                        if (stepId != null && sid != stepId) continue;
                        if (stepId == null && Bridge.Prop(step, "State")?.ToString() != "Active") continue;
                        var tasks = Bridge.Prop(step, "ActiveTasks") as IEnumerable;
                        if (tasks == null) continue;
                        foreach (var task in tasks)
                        {
                            var tid = Bridge.Prop(task, "Id")?.ToString();
                            if (taskId != null && tid != taskId) continue;
                            bool costsMet = Bridge.Prop(task, "CostsMet") is bool cm && cm;
                            if (taskId == null && costsMet) continue;
                            var ok = qComp.GetType().GetMethod("ExecuteTaskCosts")?.Invoke(qComp, new[] { task });
                            bool submitted = ok is bool b && b;
                            return new Dictionary<string, object>
                            {
                                ["ok"] = submitted,
                                ["quest"] = Bridge.NormalizeId(qid),
                                ["step"] = sid,
                                ["task"] = tid,
                                ["quest_state"] = Bridge.Prop(aq, "State")?.ToString()
                            };
                        }
                    }
                }
                return Err($"no active task awaiting cost submission on {qid}");
            });
        }

        private static Dictionary<string, object> OpQuestConfirm(object game, JsonElement p)
        {
            string qid = GetStr(p, "quest");
            var aq = FindActiveQuest(game, qid, out _);
            if (aq == null) return Err($"quest not found: {qid}");
            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                int confirmed = 0;
                var steps = Bridge.Prop(aq, "ActiveSteps") as IEnumerable;
                if (steps != null)
                {
                    foreach (var step in steps)
                    {
                        if (Bridge.Prop(step, "State")?.ToString() != "Active") continue;
                        var tasks = Bridge.Prop(step, "ActiveTasks") as IEnumerable;
                        if (tasks == null) continue;
                        foreach (var task in tasks)
                        {
                            task.GetType().GetMethod("ConfirmConditionGoals")?.Invoke(task, null);
                            confirmed++;
                        }
                    }
                }
                return new Dictionary<string, object> { ["ok"] = confirmed > 0, ["confirmed_tasks"] = confirmed, ["quest_state"] = Bridge.Prop(aq, "State")?.ToString() };
            });
        }

        private static Dictionary<string, object> OpQuestClaim(object game, JsonElement p)
        {
            string qid = GetStr(p, "quest");
            var aq = FindActiveQuest(game, qid, out var qComp);
            if (aq == null) return Err($"quest not found: {qid}");
            bool claimed = (bool)Bridge.RunOnMain(() =>
            {
                var res = qComp.GetType().GetMethod("ClaimRewards")?.Invoke(qComp, new[] { aq });
                return res is bool b && b;
            });
            return new Dictionary<string, object>
            {
                ["ok"] = claimed,
                ["claimed"] = claimed,
                ["quest"] = Bridge.NormalizeId(qid),
                ["state"] = Bridge.Prop(aq, "State")?.ToString()
            };
        }

        // ------------------------------------------------------------------
        // Passive Skills: Farming & Ranching
        // ------------------------------------------------------------------

        private static Dictionary<string, object> OpFarmingHarvestAll(object game, JsonElement p)
        {
            string catFilter = GetStr(p, "category");
            var farm = Bridge.FindComponent(game, "MelvorBase:Farming", "Passive");
            if (farm == null) return Err("FarmingPassiveComponent not found");
            int harvested = (int)Bridge.RunOnMain(() =>
            {
                int count = 0;
                foreach (var plot in Bridge.EnumerateDictionaryValues(Bridge.Prop(farm, "ActivePlots")))
                {
                    if (!MatchesFarmingCategory(plot, catFilter)) continue;
                    if (Bridge.Prop(plot, "CanHarvest") is bool ch && ch)
                    {
                        plot.GetType().GetMethod("Harvest", Type.EmptyTypes)?.Invoke(plot, null);
                        count++;
                    }
                }
                return count;
            });
            return new Dictionary<string, object> { ["ok"] = true, ["harvested"] = harvested };
        }

        private static Dictionary<string, object> OpFarmingPlantAll(object game, JsonElement p)
        {
            string recipeId = GetStr(p, "recipe");
            string catFilter = GetStr(p, "category");
            if (string.IsNullOrWhiteSpace(recipeId)) return Err("missing 'recipe'");
            var farm = Bridge.FindComponent(game, "MelvorBase:Farming", "Passive");
            var recipe = Bridge.LookupDataObject(game, "FarmingRecipe", recipeId);
            if (farm == null || recipe == null) return Err($"FarmingRecipe not found: {recipeId}");

            string recipeCat = Bridge.Prop(Bridge.Prop(recipe, "Category"), "Id")?.ToString();
            string effectiveCat = string.IsNullOrWhiteSpace(catFilter) ? recipeCat : Bridge.NormalizeId(catFilter);

            int planted = (int)Bridge.RunOnMain(() =>
            {
                int count = 0;
                foreach (var plot in Bridge.EnumerateDictionaryValues(Bridge.Prop(farm, "ActivePlots")))
                {
                    if (!MatchesFarmingCategory(plot, effectiveCat)) continue;
                    if (!(Bridge.Prop(plot, "IsPurchased") is bool isp && isp)) continue;
                    if (Bridge.Prop(plot, "CanHarvest") is bool ch && ch)
                        plot.GetType().GetMethod("Harvest", Type.EmptyTypes)?.Invoke(plot, null);
                    if (!(Bridge.Prop(plot, "CanPlant") is bool cp && cp)) continue;
                    plot.GetType().GetMethod("SelectRecipe")?.Invoke(plot, new[] { recipe });
                    var canPlantSel = plot.GetType().GetMethod("CanPlantSelectedRecipe")?.Invoke(plot, null);
                    if (canPlantSel is bool ok && ok)
                    {
                        plot.GetType().GetMethod("Plant", Type.EmptyTypes)?.Invoke(plot, null);
                        count++;
                    }
                }
                return count;
            });
            return new Dictionary<string, object> { ["ok"] = true, ["planted"] = planted, ["recipe"] = Bridge.NormalizeId(recipeId) };
        }

        private static Dictionary<string, object> OpFarmingWaterAll(object game, JsonElement p)
        {
            string catFilter = GetStr(p, "category");
            var farm = Bridge.FindComponent(game, "MelvorBase:Farming", "Passive");
            if (farm == null) return Err("FarmingPassiveComponent not found");
            int watered = (int)Bridge.RunOnMain(() =>
            {
                int count = 0;
                foreach (var plot in Bridge.EnumerateDictionaryValues(Bridge.Prop(farm, "ActivePlots")))
                {
                    if (!MatchesFarmingCategory(plot, catFilter)) continue;
                    if (Bridge.Prop(plot, "CanWater") is bool cw && cw)
                    {
                        plot.GetType().GetMethod("Water", Type.EmptyTypes)?.Invoke(plot, null);
                        count++;
                    }
                }
                return count;
            });
            return new Dictionary<string, object> { ["ok"] = true, ["watered"] = watered };
        }

        private static Dictionary<string, object> OpFarmingPurchasePlots(object game, JsonElement p)
        {
            string catFilter = GetStr(p, "category");
            int maxCount = GetInt(p, "max_count", 10);
            var farm = Bridge.FindComponent(game, "MelvorBase:Farming", "Passive");
            if (farm == null) return Err("FarmingPassiveComponent not found");
            int bought = (int)Bridge.RunOnMain(() =>
            {
                int count = 0;
                foreach (var plot in Bridge.EnumerateDictionaryValues(Bridge.Prop(farm, "ActivePlots")))
                {
                    if (count >= maxCount) break;
                    if (!MatchesFarmingCategory(plot, catFilter)) continue;
                    if (Bridge.Prop(plot, "CanPurchase") is bool cp && cp)
                    {
                        plot.GetType().GetMethod("Purchase", Type.EmptyTypes)?.Invoke(plot, null);
                        if (Bridge.Prop(plot, "IsPurchased") is bool isp && isp) count++;
                    }
                }
                return count;
            });
            return new Dictionary<string, object> { ["ok"] = true, ["purchased_plots"] = bought };
        }

        private static Dictionary<string, object> OpFarmingGrainMill(object game, JsonElement p)
        {
            string act = (GetStr(p, "action") ?? "collect").ToLowerInvariant();
            string recipeId = GetStr(p, "recipe");
            var mill = Bridge.FindComponent(game, "MelvorBase:Farming", "GrainMill");
            if (mill == null) return Err("FarmingGrainMillComponent not found");
            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                switch (act)
                {
                    case "collect": mill.GetType().GetMethod("CollectReadyItems")?.Invoke(mill, null); break;
                    case "deposit_all": mill.GetType().GetMethod("DepositAllToGrainMill")?.Invoke(mill, null); break;
                    case "withdraw_all": mill.GetType().GetMethod("WithdrawAllFromGrainMill")?.Invoke(mill, null); break;
                    case "start": mill.GetType().GetMethod("ToggleGrainMillTimer")?.Invoke(mill, new object[] { true }); break;
                    case "stop": mill.GetType().GetMethod("ToggleGrainMillTimer")?.Invoke(mill, new object[] { false }); break;
                    case "select":
                        var opt = FindOption(mill, Bridge.NormalizeId(recipeId));
                        if (opt == null) return Err($"GrainMill recipe not found: {recipeId}");
                        mill.GetType().GetMethod("SelectRecipe")?.Invoke(mill, new[] { opt });
                        break;
                    default: return Err($"unknown grain mill action: {act}");
                }
                return Ok("farming_grain_mill", act);
            });
        }

        private static Dictionary<string, object> OpRanchingMaintain(object game, JsonElement p)
        {
            bool pet = GetBool(p, "pet_all", true);
            bool repair = GetBool(p, "repair_all", true);
            bool cure = GetBool(p, "cure_all", true);
            var ranch = Bridge.FindComponent(game, "MelvorBase:Ranching", "Passive");
            if (ranch == null) return Err("RanchingPassiveComponent not found");
            int cured = (int)Bridge.RunOnMain(() =>
            {
                if (repair) ranch.GetType().GetMethod("RepairAllEnclosures")?.Invoke(ranch, null);
                if (pet) ranch.GetType().GetMethod("PetAllAnimals")?.Invoke(ranch, null);
                int c = 0;
                if (cure)
                {
                    foreach (var land in Bridge.EnumerateDictionaryValues(Bridge.Prop(ranch, "ActiveLand")))
                    {
                        var enc = Bridge.Prop(land, "ActiveEnclosure");
                        var animals = Bridge.Prop(enc, "ActiveAnimals") as IEnumerable;
                        if (animals == null) continue;
                        foreach (var ani in animals)
                        {
                            var isSick = Bridge.Prop(ani, "IsSick") ?? Bridge.Prop(ani, "HasIllness");
                            if (isSick is bool s && s)
                            {
                                ani.GetType().GetMethod("CureIllness")?.Invoke(ani, null);
                                c++;
                            }
                        }
                    }
                }
                return c;
            });
            return new Dictionary<string, object> { ["ok"] = true, ["pet"] = pet, ["repaired"] = repair, ["cured"] = cured };
        }

        private static Dictionary<string, object> OpRanchingBuyAnimal(object game, JsonElement p)
        {
            string landId = GetStr(p, "land");
            string animalId = GetStr(p, "animal");
            var ranch = Bridge.FindComponent(game, "MelvorBase:Ranching", "Passive");
            var animal = Bridge.LookupDataObject(game, "RanchingAnimal", animalId);
            if (ranch == null || animal == null) return Err($"RanchingAnimal not found: {animalId}");
            var normLand = Bridge.NormalizeId(landId);
            return (Dictionary<string, object>)Bridge.RunOnMain(() =>
            {
                foreach (var al in Bridge.EnumerateDictionaryValues(Bridge.Prop(ranch, "ActiveLand")))
                {
                    var id = Bridge.Prop(Bridge.Prop(al, "Land"), "Id")?.ToString();
                    if (!string.Equals(id, normLand, StringComparison.OrdinalIgnoreCase)) continue;
                    var enc = Bridge.Prop(al, "ActiveEnclosure");
                    if (enc == null) return Err($"no enclosure built on land {normLand}");
                    var ok = enc.GetType().GetMethod("AddNewAnimal")?.Invoke(enc, new[] { animal, (object)false });
                    bool added = ok is bool b && b;
                    return new Dictionary<string, object> { ["ok"] = added, ["land"] = normLand, ["animal"] = Bridge.NormalizeId(animalId) };
                }
                return Err($"RanchingLand not found: {normLand}");
            });
        }

        private static Dictionary<string, object> OpRanchingToggleTreat(object game, JsonElement p)
        {
            string itemId = GetStr(p, "item");
            var ranch = Bridge.FindComponent(game, "MelvorBase:Ranching", "Passive");
            var item = Bridge.LookupDataObject(game, "Item", itemId);
            if (ranch == null || item == null) return Err($"Item not found: {itemId}");
            Bridge.RunOnMain(() => { ranch.GetType().GetMethod("ToggleTreat")?.Invoke(ranch, new[] { item }); return null; });
            return Ok("ranching_toggle_treat", Bridge.NormalizeId(itemId));
        }

        // ------------------------------------------------------------------
        // Helper lookup methods
        // ------------------------------------------------------------------

        private static bool MatchesFarmingCategory(object plot, string catFilter)
        {
            if (string.IsNullOrWhiteSpace(catFilter)) return true;
            var catId = Bridge.Prop(Bridge.Prop(Bridge.Prop(plot, "Plot"), "Category"), "Id")?.ToString() ?? "";
            return catId.IndexOf(catFilter.Split(':')[^1], StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static object AutoDetectEquipmentSlot(object game, object item)
        {
            // Look through Combat.Equipment.ActiveSlots to find a slot whose accepted type matches item's EquipmentCapability.Type
            var eq = Bridge.FindComponent(game, "MelvorBase:Combat", "Equipment");
            var caps = Bridge.Prop(item, "Capabilities");
            object eqCap = null;
            if (caps != null)
            {
                foreach (var c in Bridge.EnumerateDictionaryValues(caps))
                {
                    if (c.GetType().Name == "EquipmentCapability") { eqCap = c; break; }
                }
            }
            var eqType = Bridge.Prop(eqCap, "Type");
            var eqTypeId = Bridge.Prop(eqType, "Id")?.ToString();
            if (eqTypeId == null) return null;

            foreach (var activeSlot in Bridge.EnumerateDictionaryValues(Bridge.Prop(eq, "ActiveSlots") ?? Bridge.Field(eq, "_activeSlots")))
            {
                var slot = Bridge.Prop(activeSlot, "Slot");
                var slotTypeId = Bridge.Prop(Bridge.Prop(slot, "Type"), "Id")?.ToString()
                    ?? Bridge.Prop(slot, "Id")?.ToString();
                if (string.Equals(slotTypeId, eqTypeId, StringComparison.OrdinalIgnoreCase))
                    return slot;
            }
            // Fallback: try direct lookup by EquipmentSlot with same short name
            return Bridge.LookupDataObject(game, "EquipmentSlot", eqTypeId);
        }

        private static bool IsStackableEquipment(object item)
        {
            var caps = Bridge.Prop(item, "Capabilities");
            if (caps == null) return false;
            foreach (var c in Bridge.EnumerateDictionaryValues(caps))
            {
                if (c.GetType().Name == "EquipmentCapability")
                    return Bridge.Prop(c, "CanEquipQuantity") is bool b && b;
            }
            return false;
        }

        private static object AutoDetectToolbeltSlot(object toolbeltComp, object item)
        {
            var caps = Bridge.Prop(item, "Capabilities");
            object tbCap = null;
            if (caps != null)
            {
                foreach (var c in Bridge.EnumerateDictionaryValues(caps))
                {
                    if (c.GetType().Name == "ToolbeltCapability") { tbCap = c; break; }
                }
            }
            var slotTypeId = Bridge.Prop(Bridge.Prop(tbCap, "Type") ?? Bridge.Prop(tbCap, "SlotType"), "Id")?.ToString();
            foreach (var activeSlot in Bridge.EnumerateDictionaryValues(Bridge.Prop(toolbeltComp, "ActiveSlots") ?? Bridge.Field(toolbeltComp, "_activeSlots")))
            {
                var slot = Bridge.Prop(activeSlot, "Slot");
                var stId = Bridge.Prop(Bridge.Prop(slot, "Type") ?? Bridge.Prop(slot, "SlotType"), "Id")?.ToString();
                if (slotTypeId != null && string.Equals(stId, slotTypeId, StringComparison.OrdinalIgnoreCase))
                    return slot;
            }
            return null;
        }

        internal static object FindOption(object comp, string recipeId)
        {
            if (comp == null || string.IsNullOrWhiteSpace(recipeId)) return null;
            var nid = Bridge.NormalizeId(recipeId);
            foreach (var propName in new[] { "ActiveRecipes", "Recipes", "Rocks", "Trees", "Animals", "ActiveCourses", "Courses", "Constellations", "ActiveConstellations", "ActiveCategories" })
            {
                var found = FindOptionInDict(Bridge.Prop(comp, propName), nid);
                if (found != null) return found;
            }
            foreach (var prop in comp.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                object dictObj;
                try
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                    dictObj = prop.GetValue(comp);
                }
                catch { continue; }
                var found = FindOptionInDict(dictObj, nid);
                if (found != null) return found;
            }
            return null;
        }

        private static object FindOptionInDict(object dictObj, string normId)
        {
            if (dictObj == null || dictObj is string) return null;
            foreach (var val in Bridge.EnumerateDictionaryValues(dictObj))
            {
                var directId = Bridge.Prop(val, "Id")?.ToString();
                if (string.Equals(directId, normId, StringComparison.OrdinalIgnoreCase)) return val;
                var unwrapped = StateQueries.UnwrapEntry(val);
                var innerId = Bridge.Prop(unwrapped, "Id")?.ToString();
                if (string.Equals(innerId, normId, StringComparison.OrdinalIgnoreCase)) return val;
            }
            return null;
        }

        private static MethodInfo FindSelectMethod(object action, object option)
        {
            var optionType = option.GetType();
            var type = action.GetType();
            MethodInfo assignable = null;
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!m.Name.StartsWith("Select")) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1) continue;
                var pt = ps[0].ParameterType;
                if (pt == optionType) return m;
                if (pt.IsAssignableFrom(optionType) && assignable == null)
                    assignable = m;
            }
            return assignable;
        }

        private static string GetStr(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;

        private static int GetInt(JsonElement e, string name, int def)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return def;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
            return def;
        }

        private static bool GetBool(JsonElement e, string name, bool def)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return def;
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            return def;
        }

        private static Dictionary<string, object> Ok(string op, string detail) =>
            new Dictionary<string, object> { ["ok"] = true, ["op"] = op, ["detail"] = detail };

        private static Dictionary<string, object> Err(string msg) =>
            new Dictionary<string, object> { ["ok"] = false, ["error"] = msg };
    }
}
