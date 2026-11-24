using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Melvor2Bridge
{
    internal static class StateQueries
    {
        public static string BuildState()
        {
            var game = Bridge.GetGame();
            if (game == null)
                return Bridge.Serialize(new Dictionary<string, object>
                {
                    ["error"] = "game not found yet (main menu or still loading?)"
                });

            // Read snapshot safely
            var result = new Dictionary<string, object>();
            result["character"] = Bridge.Prop(game, "CharacterName")?.ToString();

            // Currencies
            var currencies = new Dictionary<string, object>();
            var wallet = Bridge.FindComponent(game, "MelvorBase:Bank", "Wallet");
            var stacks = Bridge.Prop(wallet, "Stacks") as IEnumerable;
            if (stacks != null)
            {
                foreach (var kv in stacks)
                {
                    var kt = kv.GetType();
                    var key = kt.GetProperty("Key")?.GetValue(kv);
                    var val = kt.GetProperty("Value")?.GetValue(kv);
                    var id = Bridge.Prop(key, "Id")?.ToString();
                    var qty = Bridge.Prop(val, "Quantity");
                    if (id != null && qty != null) currencies[id] = qty;
                }
            }
            result["currencies"] = currencies;

            // Skills
            var skills = new Dictionary<string, object>();
            int totalLevel = 0;
            var allEntities = Bridge.GetAllEntities(game);
            if (allEntities != null)
            {
                foreach (var entity in allEntities)
                {
                    var comps = Bridge.Prop(entity, "Components") as IDictionary;
                    if (comps == null || !comps.Contains("Experience")) continue;
                    var exp = comps["Experience"];
                    var xp = Convert.ToDouble(Bridge.Prop(exp, "Xp") ?? 0.0, CultureInfo.InvariantCulture);
                    var level = Bridge.LevelFromXp(xp);
                    totalLevel += level;
                    var sname = entity.GetType().Name;
                    if (sname.EndsWith("Entity")) sname = sname.Substring(0, sname.Length - 6);
                    skills[sname] = new Dictionary<string, object>
                    {
                        ["id"] = Bridge.Prop(entity, "Id")?.ToString(),
                        ["level"] = level,
                        ["xp"] = (long)xp
                    };
                }
            }
            result["total_level"] = totalLevel;
            result["skills"] = skills;

            // Active actions
            var activeList = new List<Dictionary<string, object>>();
            var am = Bridge.Prop(game, "ActionManager");
            var activeActions = Bridge.Prop(am, "ActiveActions") as IEnumerable;
            if (activeActions != null)
            {
                foreach (var action in activeActions)
                {
                    var entity = Bridge.Prop(action, "Entity");
                    var desc = Bridge.Prop(action, "CurrentDescription");
                    var item = new Dictionary<string, object>
                    {
                        ["entity"] = Bridge.Prop(entity, "Id")?.ToString(),
                        ["component"] = Bridge.Prop(action, "Id")?.ToString(),
                        ["action"] = Bridge.NameOf(desc),
                        ["action_id"] = Bridge.Prop(desc, "Id")?.ToString(),
                        ["started"] = Bridge.Prop(action, "IsStarted")
                    };
                    var timer = Bridge.Prop(action, "ActionTimer");
                    if (timer != null)
                    {
                        item["timer_wait_ms"] = Bridge.Prop(timer, "WaitTime");
                        item["timer_time_left_ms"] = Bridge.Prop(timer, "TimeLeft");
                    }
                    activeList.Add(item);
                }
            }
            result["active_actions"] = activeList;

            // Quick combat HP & food snapshot
            var player = Bridge.FindComponent(game, "MelvorBase:Combat", "Player");
            var health = Bridge.Prop(player, "GlobalHealth");
            if (health != null)
            {
                result["health"] = new Dictionary<string, object>
                {
                    ["current"] = Bridge.Prop(health, "Current"),
                    ["max"] = Bridge.Prop(health, "Max"),
                    ["percent"] = Bridge.Prop(health, "Percent")
                };
            }

            var foodComp = Bridge.FindComponent(game, "MelvorBase:Combat", "Food");
            var selFood = Bridge.Prop(foodComp, "SelectedFood");
            if (selFood != null)
            {
                var fItem = Bridge.Prop(selFood, "Item");
                var fid = Bridge.Prop(fItem, "Id")?.ToString();
                result["selected_food"] = new Dictionary<string, object>
                {
                    ["id"] = fid,
                    ["name"] = Bridge.NameOf(fItem),
                    ["quantity"] = fid != null ? Bridge.GetBankItemQuantity(game, fid) : 0
                };
            }

            // Quests summary
            var qComp = Bridge.FindComponent(game, "MelvorBase:Quests", "Quests");
            if (qComp != null)
            {
                int inProg = 0, completed = 0, claimed = 0, unstarted = 0;
                var inProgList = new List<string>();
                var claimableList = new List<string>();
                foreach (var aq in Bridge.EnumerateDictionaryValues(Bridge.Prop(qComp, "ActiveQuests")))
                {
                    var st = Bridge.Prop(aq, "State")?.ToString();
                    var qid = Bridge.Prop(Bridge.Prop(aq, "Data"), "Id")?.ToString();
                    if (st == "InProgress") { inProg++; if (qid != null) inProgList.Add(qid); }
                    else if (st == "Completed") { completed++; if (qid != null) claimableList.Add(qid); }
                    else if (st == "Claimed") claimed++;
                    else unstarted++;
                }
                result["quests_summary"] = new Dictionary<string, object>
                {
                    ["in_progress"] = inProg,
                    ["claimable"] = completed,
                    ["claimed"] = claimed,
                    ["unstarted"] = unstarted,
                    ["active_ids"] = inProgList,
                    ["claimable_ids"] = claimableList
                };
            }

            result["plan"] = PlanScheduler.GetShortStatus();
            return Bridge.Serialize(result);
        }

        public static string BuildBank(string filter)
        {
            var game = Bridge.GetGame();
            if (game == null) return Bridge.Serialize(new { error = "game not found" });

            var items = (List<Dictionary<string, object>>)Bridge.RunOnMain(() =>
            {
                var list = new List<Dictionary<string, object>>();
                var inv = Bridge.FindComponent(game, "MelvorBase:Bank", "Inventory");
                var stacks = Bridge.Prop(inv, "Stacks") as IEnumerable;
                if (stacks == null) return list;
                string f = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
                foreach (var st in stacks)
                {
                    var item = Bridge.Prop(st, "Item");
                    var id = Bridge.Prop(item, "Id")?.ToString();
                    var name = Bridge.NameOf(item);
                    var qty = Convert.ToInt32(Bridge.Prop(st, "Quantity") ?? 0);
                    if (f != null
                        && (id == null || id.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)
                        && (name == null || name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0))
                        continue;
                    list.Add(new Dictionary<string, object>
                    {
                        ["item"] = id,
                        ["name"] = name,
                        ["quantity"] = qty,
                        ["locked"] = Bridge.Prop(st, "IsLocked"),
                        ["placeholder"] = Bridge.Prop(st, "IsPlaceholder")
                    });
                }
                return list;
            });

            return Bridge.Serialize(new Dictionary<string, object>
            {
                ["count"] = items.Count,
                ["items"] = items
            });
        }

        public static string BuildQuests(string filter)
        {
            var game = Bridge.GetGame();
            if (game == null) return Bridge.Serialize(new { error = "game not found" });
            var qComp = Bridge.FindComponent(game, "MelvorBase:Quests", "Quests");
            if (qComp == null) return Bridge.Serialize(new { error = "QuestsComponent not found" });

            var list = new List<Dictionary<string, object>>();
            foreach (var aq in Bridge.EnumerateDictionaryValues(Bridge.Prop(qComp, "ActiveQuests")))
            {
                var data = Bridge.Prop(aq, "Data");
                var qid = Bridge.Prop(data, "Id")?.ToString();
                var state = Bridge.Prop(aq, "State")?.ToString();
                if (!string.IsNullOrWhiteSpace(filter)
                    && !string.Equals(state, filter, StringComparison.OrdinalIgnoreCase)
                    && !(filter.Equals("active", StringComparison.OrdinalIgnoreCase) && (state == "InProgress" || state == "Completed")))
                    continue;

                var reqsObj = Bridge.Prop(data, "Requirements");
                var reqsMet = reqsObj == null || (Bridge.Prop(reqsObj, "AreMet") is bool rm && rm);

                var stepsOut = new List<Dictionary<string, object>>();
                var activeSteps = Bridge.Prop(aq, "ActiveSteps") as IEnumerable;
                if (activeSteps != null)
                {
                    foreach (var step in activeSteps)
                    {
                        var tasksOut = new List<Dictionary<string, object>>();
                        var tasks = Bridge.Prop(step, "ActiveTasks") as IEnumerable;
                        if (tasks != null)
                        {
                            foreach (var task in tasks)
                            {
                                var goalsOut = new List<Dictionary<string, object>>();
                                var goals = Bridge.Prop(task, "ActiveGoals") as IEnumerable;
                                if (goals != null)
                                {
                                    foreach (var g in goals)
                                    {
                                        goalsOut.Add(new Dictionary<string, object>
                                        {
                                            ["progress"] = Bridge.Prop(g, "CurrentProgress"),
                                            ["max"] = Bridge.Prop(g, "MaxProgress"),
                                            ["complete"] = Bridge.Prop(g, "IsComplete"),
                                            ["active"] = Bridge.Prop(g, "IsActive")
                                        });
                                    }
                                }
                                bool hasCosts = Bridge.Prop(task, "HasCosts") is bool hc && hc;
                                bool canAfford = false;
                                if (hasCosts)
                                {
                                    try
                                    {
                                        var getTracker = qComp.GetType().GetMethod("GetStepCostsTracker");
                                        var tracker = getTracker?.Invoke(qComp, new[] { task });
                                        if (tracker != null && Bridge.Prop(tracker, "CanAfford") is bool ca)
                                            canAfford = ca;
                                    }
                                    catch { }
                                }
                                tasksOut.Add(new Dictionary<string, object>
                                {
                                    ["id"] = Bridge.Prop(task, "Id")?.ToString(),
                                    ["state"] = Bridge.Prop(task, "State")?.ToString(),
                                    ["costs_met"] = Bridge.Prop(task, "CostsMet"),
                                    ["has_costs"] = hasCosts,
                                    ["can_afford_costs"] = canAfford,
                                    ["condition_goals_confirmed"] = Bridge.Prop(task, "ConditionGoalsConfirmed"),
                                    ["goals"] = goalsOut
                                });
                            }
                        }
                        stepsOut.Add(new Dictionary<string, object>
                        {
                            ["id"] = Bridge.Prop(step, "Id")?.ToString(),
                            ["state"] = Bridge.Prop(step, "State")?.ToString(),
                            ["tasks"] = tasksOut
                        });
                    }
                }

                list.Add(new Dictionary<string, object>
                {
                    ["id"] = qid,
                    ["name"] = Bridge.NameOf(data),
                    ["category"] = Bridge.Prop(Bridge.Prop(data, "Category"), "Id")?.ToString(),
                    ["state"] = state,
                    ["requirements_met"] = reqsMet,
                    ["claimable"] = state == "Completed",
                    ["steps"] = stepsOut
                });
            }

            return Bridge.Serialize(new Dictionary<string, object>
            {
                ["count"] = list.Count,
                ["quests"] = list
            });
        }

        public static string BuildActions(string skillFilter)
        {
            var game = Bridge.GetGame();
            if (game == null) return Bridge.Serialize(new { error = "game not found yet" });

            var list = new List<Dictionary<string, object>>();
            var all = Bridge.GetAllEntities(game);
            string needle = string.IsNullOrWhiteSpace(skillFilter) ? null : skillFilter.Split(':')[^1].Trim();

            if (all != null)
            {
                foreach (var entity in all)
                {
                    var eid = Bridge.Prop(entity, "Id")?.ToString() ?? "";
                    var ename = entity.GetType().Name;
                    if (needle != null
                        && eid.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0
                        && ename.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var comps = Bridge.Prop(entity, "Components") as IDictionary;
                    if (comps == null) continue;
                    foreach (DictionaryEntry entry in comps)
                    {
                        var comp = entry.Value;
                        if (!Bridge.IsActionComponent(comp)) continue;
                        list.Add(new Dictionary<string, object>
                        {
                            ["entity"] = eid,
                            ["entity_name"] = ename,
                            ["component"] = Bridge.Prop(comp, "Id")?.ToString(),
                            ["type"] = comp.GetType().Name,
                            ["selected"] = Bridge.NameOf(Bridge.Prop(comp, "CurrentDescription")),
                            ["started"] = Bridge.Prop(comp, "IsStarted"),
                            ["options"] = ExtractOptions(comp)
                        });
                    }
                }
            }
            return Bridge.Serialize(new Dictionary<string, object> { ["actions"] = list });
        }

        private static List<Dictionary<string, object>> ExtractOptions(object comp)
        {
            var opts = new List<Dictionary<string, object>>();
            var seen = new HashSet<string>();
            foreach (var propName in new[] { "ActiveRecipes", "Recipes", "Rocks", "Trees", "Animals", "ActiveCourses", "Courses", "Constellations", "ActiveConstellations" })
            {
                var dict = Bridge.Prop(comp, propName);
                if (dict == null) continue;
                foreach (var val in Bridge.EnumerateDictionaryValues(dict))
                {
                    var unwrapped = UnwrapEntry(val);
                    var id = (Bridge.Prop(unwrapped, "Id") ?? Bridge.Prop(val, "Id"))?.ToString();
                    if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                    var reqs = Bridge.Prop(unwrapped, "Requirements");
                    bool unlocked = reqs == null || (Bridge.Prop(reqs, "AreMet") is bool b && b);
                    opts.Add(new Dictionary<string, object>
                    {
                        ["id"] = id,
                        ["name"] = Bridge.NameOf(unwrapped) ?? Bridge.NameOf(val),
                        ["unlocked"] = unlocked
                    });
                }
            }
            return opts;
        }

        public static string BuildShop(string categoryFilter, bool availableOnly)
        {
            var game = Bridge.GetGame();
            if (game == null) return Bridge.Serialize(new { error = "game not found" });
            var shop = Bridge.FindComponent(game, "MelvorBase:Shop", "Shop");
            if (shop == null) return Bridge.Serialize(new { error = "ShopComponent not found" });

            var purchases = Bridge.Prop(shop, "Purchases") as IEnumerable;
            var getPurchased = shop.GetType().GetMethod("GetUpgradesPurchased");
            var getCostTracker = shop.GetType().GetMethod("GetPurchaseCostTracker");

            var list = new List<Dictionary<string, object>>();
            if (purchases != null)
            {
                foreach (var sp in purchases)
                {
                    var id = Bridge.Prop(sp, "Id")?.ToString();
                    var subcat = Bridge.Prop(Bridge.Prop(sp, "Subcategory"), "Id")?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(categoryFilter)
                        && subcat.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) < 0
                        && (id == null || id.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) < 0))
                        continue;

                    int bought = getPurchased != null && getPurchased.Invoke(shop, new[] { sp }) is int bp ? bp : 0;
                    int limit = Convert.ToInt32(Bridge.Prop(sp, "DefaultBuyLimit") ?? 0);
                    var reqs = Bridge.Prop(sp, "Requirements");
                    bool reqsMet = reqs == null || (Bridge.Prop(reqs, "AreMet") is bool rm && rm);
                    bool canAfford = false;
                    try
                    {
                        var tracker = getCostTracker?.Invoke(shop, new[] { sp });
                        if (tracker != null && Bridge.Prop(tracker, "CanAfford") is bool ca) canAfford = ca;
                    }
                    catch { }

                    bool maxed = limit > 0 && bought >= limit;
                    if (availableOnly && (!reqsMet || maxed || !canAfford)) continue;

                    list.Add(new Dictionary<string, object>
                    {
                        ["id"] = id,
                        ["name"] = Bridge.NameOf(sp),
                        ["subcategory"] = subcat,
                        ["purchased"] = bought,
                        ["buy_limit"] = limit,
                        ["requirements_met"] = reqsMet,
                        ["can_afford"] = canAfford
                    });
                }
            }
            return Bridge.Serialize(new Dictionary<string, object>
            {
                ["count"] = list.Count,
                ["purchases"] = list
            });
        }

        public static string BuildCombat()
        {
            var game = Bridge.GetGame();
            if (game == null) return Bridge.Serialize(new { error = "game not found" });

            var outDict = new Dictionary<string, object>();

            // Action & Health
            var action = Bridge.FindComponent(game, "MelvorBase:Combat", "Action");
            outDict["in_combat"] = Bridge.Prop(action, "IsStarted");

            var player = Bridge.FindComponent(game, "MelvorBase:Combat", "Player");
            var pHealth = Bridge.Prop(player, "GlobalHealth");
            if (pHealth != null)
            {
                outDict["player_health"] = new Dictionary<string, object>
                {
                    ["current"] = Bridge.Prop(pHealth, "Current"),
                    ["max"] = Bridge.Prop(pHealth, "Max"),
                    ["percent"] = Bridge.Prop(pHealth, "Percent")
                };
            }

            var enemyComp = Bridge.FindComponent(game, "MelvorBase:Combat", "Enemy");
            var enemyData = Bridge.Prop(enemyComp, "Enemy") ?? Bridge.Prop(enemyComp, "Data");
            var eHealth = Bridge.Prop(enemyComp, "Health");
            if (enemyData != null)
            {
                outDict["current_enemy"] = new Dictionary<string, object>
                {
                    ["id"] = Bridge.Prop(enemyData, "Id")?.ToString(),
                    ["name"] = Bridge.NameOf(enemyData),
                    ["hp"] = Bridge.Prop(eHealth, "Current"),
                    ["max_hp"] = Bridge.Prop(eHealth, "Max")
                };
            }

            // Areas
            var areas = Bridge.FindComponent(game, "MelvorBase:Combat", "Areas");
            if (areas != null)
            {
                outDict["area"] = new Dictionary<string, object>
                {
                    ["selected_area"] = Bridge.Prop(Bridge.Field(areas, "_selectedArea"), "Id")?.ToString(),
                    ["selected_enemy"] = Bridge.Prop(Bridge.Field(areas, "_selectedEnemy"), "Id")?.ToString(),
                    ["selected_dungeon"] = Bridge.Prop(Bridge.Field(areas, "_selectedDungeon"), "Id")?.ToString(),
                    ["progress"] = Bridge.Prop(areas, "CurrentProgress"),
                    ["max_progress"] = Bridge.Prop(areas, "MaxProgress")
                };
            }

            // Equipment
            var eqComp = Bridge.FindComponent(game, "MelvorBase:Combat", "Equipment");
            var eqMap = new Dictionary<string, object>();
            foreach (var slotObj in Bridge.EnumerateDictionaryValues(Bridge.Prop(eqComp, "ActiveSlots") ?? Bridge.Field(eqComp, "_activeSlots")))
            {
                var slotId = Bridge.Prop(Bridge.Prop(slotObj, "Slot"), "Id")?.ToString();
                var equipped = Bridge.Prop(slotObj, "Equipped");
                if (slotId == null || equipped == null) continue;
                var item = Bridge.Prop(equipped, "Item");
                eqMap[slotId] = new Dictionary<string, object>
                {
                    ["item"] = Bridge.Prop(item, "Id")?.ToString(),
                    ["name"] = Bridge.NameOf(item),
                    ["quantity"] = Bridge.Prop(slotObj, "Quantity")
                };
            }
            outDict["equipment"] = eqMap;

            // Food
            var foodComp = Bridge.FindComponent(game, "MelvorBase:Combat", "Food");
            var selFood = Bridge.Prop(foodComp, "SelectedFood");
            if (selFood != null)
            {
                var fItem = Bridge.Prop(selFood, "Item");
                var fid = Bridge.Prop(fItem, "Id")?.ToString();
                outDict["food"] = new Dictionary<string, object>
                {
                    ["item"] = fid,
                    ["name"] = Bridge.NameOf(fItem),
                    ["quantity"] = fid != null ? Bridge.GetBankItemQuantity(game, fid) : 0
                };
            }

            // Spells & Prayers
            var spells = Bridge.FindComponent(game, "MelvorBase:Combat", "MagicSpells");
            if (spells != null)
            {
                outDict["magic"] = new Dictionary<string, object>
                {
                    ["selected_spell"] = Bridge.Prop(Bridge.Prop(Bridge.Prop(spells, "SelectedSpell"), "Data"), "Id")?.ToString(),
                    ["selected_imbuement"] = Bridge.Prop(Bridge.Prop(Bridge.Prop(spells, "SelectedImbuement"), "Data"), "Id")?.ToString()
                };
            }

            var prayers = Bridge.FindComponent(game, "MelvorBase:Combat", "Prayer");
            var activePrayers = new List<string>();
            foreach (var pSlot in Bridge.EnumerateDictionaryValues(Bridge.Prop(prayers, "ActiveSlots")))
            {
                var sel = Bridge.Prop(pSlot, "Selected");
                var pid = Bridge.Prop(Bridge.Prop(sel, "Data"), "Id")?.ToString();
                if (pid != null) activePrayers.Add(pid);
            }
            outDict["selected_prayers"] = activePrayers;

            // Slayer Tasks
            var slayer = Bridge.FindComponent(game, "MelvorBase:Slayer", "Tasks");
            var slayerList = new List<Dictionary<string, object>>();
            foreach (var m in Bridge.EnumerateDictionaryValues(Bridge.Prop(slayer, "ActiveMasters")))
            {
                var cur = Bridge.Prop(m, "CurrentTask");
                if (cur == null) continue;
                int len = Convert.ToInt32(Bridge.Prop(m, "TaskLength") ?? 0);
                int prog = Convert.ToInt32(Bridge.Prop(m, "TaskProgress") ?? 0);
                slayerList.Add(new Dictionary<string, object>
                {
                    ["master"] = Bridge.Prop(Bridge.Prop(m, "Data"), "Id")?.ToString(),
                    ["enemy"] = Bridge.Prop(cur, "Id")?.ToString(),
                    ["enemy_name"] = Bridge.NameOf(cur),
                    ["progress"] = prog,
                    ["length"] = len,
                    ["remaining"] = Math.Max(0, len - prog),
                    ["auto_slayer"] = Bridge.Prop(m, "AutoSlayerEnabled")
                });
            }
            outDict["slayer_tasks"] = slayerList;

            return Bridge.Serialize(outDict);
        }

        public static string BuildPassive()
        {
            var game = Bridge.GetGame();
            if (game == null) return Bridge.Serialize(new { error = "game not found" });

            var outDict = new Dictionary<string, object>();

            // Farming plots
            var farm = Bridge.FindComponent(game, "MelvorBase:Farming", "Passive");
            var plots = new List<Dictionary<string, object>>();
            foreach (var ap in Bridge.EnumerateDictionaryValues(Bridge.Prop(farm, "ActivePlots")))
            {
                var pdata = Bridge.Prop(ap, "Plot");
                bool purchased = Bridge.Prop(ap, "IsPurchased") is bool isp && isp;
                var planted = Bridge.Prop(ap, "PlantedRecipe");
                plots.Add(new Dictionary<string, object>
                {
                    ["id"] = Bridge.Prop(pdata, "Id")?.ToString(),
                    ["category"] = Bridge.Prop(Bridge.Prop(pdata, "Category"), "Id")?.ToString(),
                    ["purchased"] = purchased,
                    ["can_harvest"] = Bridge.Prop(ap, "CanHarvest"),
                    ["can_plant"] = Bridge.Prop(ap, "CanPlant"),
                    ["can_water"] = Bridge.Prop(ap, "CanWater"),
                    ["can_purchase"] = Bridge.Prop(ap, "CanPurchase"),
                    ["planted"] = Bridge.Prop(Bridge.Prop(planted, "Recipe"), "Id")?.ToString()
                });
            }
            outDict["farming_plots"] = plots;

            // Ranching lands
            var ranch = Bridge.FindComponent(game, "MelvorBase:Ranching", "Passive");
            var lands = new List<Dictionary<string, object>>();
            foreach (var al in Bridge.EnumerateDictionaryValues(Bridge.Prop(ranch, "ActiveLand")))
            {
                var ldata = Bridge.Prop(al, "Land");
                var enc = Bridge.Prop(al, "ActiveEnclosure");
                var animals = Bridge.Prop(enc, "ActiveAnimals") as IEnumerable;
                int animalCount = 0;
                if (animals != null) foreach (var _ in animals) animalCount++;
                lands.Add(new Dictionary<string, object>
                {
                    ["id"] = Bridge.Prop(ldata, "Id")?.ToString(),
                    ["purchased"] = Bridge.Prop(al, "IsPurchased"),
                    ["requires_attention"] = Bridge.Prop(al, "RequiresAttention"),
                    ["enclosure"] = Bridge.Prop(Bridge.Prop(enc, "FirstEnclosure"), "Id")?.ToString(),
                    ["animal_count"] = animalCount
                });
            }
            outDict["ranching_lands"] = lands;
            outDict["ranching_requires_attention"] = Bridge.Prop(ranch, "RequiresAttention");

            return Bridge.Serialize(outDict);
        }

        // ------------------------------------------------------------------
        // /probe (fixed numeric depth via System.Text.Json) & /dump
        // ------------------------------------------------------------------

        public static string Probe(string body)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = doc.RootElement;
            string entityId = root.TryGetProperty("entity", out var pe) ? pe.GetString() : null;
            string componentId = root.TryGetProperty("component", out var pc) ? pc.GetString() : null;
            string recipeId = root.TryGetProperty("recipe", out var pr) ? pr.GetString() : null;
            int depth = 2;
            if (root.TryGetProperty("depth", out var pd))
            {
                if (pd.ValueKind == JsonValueKind.Number && pd.TryGetInt32(out var dn)) depth = dn;
                else if (pd.ValueKind == JsonValueKind.String && int.TryParse(pd.GetString(), out var ds)) depth = ds;
            }
            depth = Math.Clamp(depth, 0, 8);

            var game = Bridge.GetGame();
            var comp = Bridge.FindComponent(game, entityId, componentId);
            if (comp == null) return Bridge.Serialize(new { error = "component not found" });

            object target = comp;
            if (recipeId != null)
            {
                target = GameOperations.FindOption(comp, recipeId);
                if (target == null) return Bridge.Serialize(new { error = "recipe not found" });
            }
            var sb = new StringBuilder();
            AppendGraph(sb, target, depth, new HashSet<object>());
            return sb.ToString();
        }

        public static string DumpContent()
        {
            return Bridge.Serialize(new
            {
                note = "Offline extraction from Melvor2.exe PCK (knowledge/extract_offline.py) replaces live /dump with 100% complete data (including shop prices & quests)."
            });
        }

        internal static object UnwrapEntry(object entry)
        {
            if (entry == null) return null;
            var name = entry.GetType().Name;
            var recipe = entry;
            if (name.StartsWith("Active"))
            {
                var innerName = name.Substring(6);
                foreach (var p in entry.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    try
                    {
                        if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                        var v = p.GetValue(entry);
                        if (v != null && v.GetType().Name == innerName) { recipe = v; break; }
                    }
                    catch { }
                }
            }
            return Bridge.Prop(recipe, "Recipe") ?? Bridge.Prop(recipe, "Data") ?? recipe;
        }

        private static bool IsSimple(object v) =>
            v == null || v is bool || v is string || v is char || v is byte || v is sbyte
            || v is short || v is ushort || v is int || v is uint || v is long || v is ulong
            || v is float || v is double || v is Enum;

        private static void AppendGraph(StringBuilder sb, object obj, int rem, HashSet<object> seen)
        {
            if (obj == null) { sb.Append("null"); return; }
            if (IsSimple(obj))
            {
                sb.Append(JsonSerializer.Serialize(obj is Enum ? obj.ToString() : obj));
                return;
            }
            var t = obj.GetType();
            var tn = t.Name;
            if (tn.StartsWith("CompressedTexture") || tn.StartsWith("Audio") || tn.StartsWith("Image") || tn == "NodePath")
            {
                sb.Append(JsonSerializer.Serialize("<" + tn + ">"));
                return;
            }
            if (obj is IEnumerable enumerable && !(obj is IDictionary))
            {
                sb.Append('[');
                int i = 0;
                foreach (var item in enumerable)
                {
                    if (i >= 10) { sb.Append(",\"…\""); break; }
                    if (i > 0) sb.Append(',');
                    AppendGraph(sb, item, rem - 1, seen);
                    i++;
                }
                sb.Append(']');
                return;
            }
            if (rem <= 0) { sb.Append(JsonSerializer.Serialize("<" + tn + ">")); return; }
            if (!seen.Add(obj)) { sb.Append(JsonSerializer.Serialize("<cycle:" + tn + ">")); return; }
            try
            {
                sb.Append("{\"$type\":").Append(JsonSerializer.Serialize(tn));
                int written = 0;
                foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (written >= 40) break;
                    object v;
                    try
                    {
                        if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                        v = p.GetValue(obj);
                    }
                    catch { continue; }
                    if (v == null) continue;
                    sb.Append(',').Append(JsonSerializer.Serialize(p.Name)).Append(':');
                    AppendGraph(sb, v, IsSimple(v) ? 0 : rem - 1, seen);
                    written++;
                }
                sb.Append('}');
            }
            finally { seen.Remove(obj); }
        }
    }
}
