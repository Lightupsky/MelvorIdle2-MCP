// Melvor Idle 2 in-game bridge (v2 — full game API + autonomous plan scheduler).
//
// Bootstrapped by the native hostfxr wrapper via StartupHook.Bootstrap()
// ([UnmanagedCallersOnly]). Uses pure reflection + System.Text.Json so the
// assembly has zero compile-time dependency on GamesByMalcs.Melvor2.dll or
// GodotSharp.dll and loads cleanly on the game's self-contained .NET 10 runtime.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

public static class StartupHook
{
    public static void Initialize()
    {
        try
        {
            Melvor2Bridge.Bridge.Log("Initialize() called (v" + Melvor2Bridge.Bridge.BridgeVersion + ")");
            var thread = new Thread(Melvor2Bridge.Bridge.Run)
            {
                IsBackground = true,
                Name = "Melvor2Bridge"
            };
            thread.Start();
        }
        catch (Exception e)
        {
            Melvor2Bridge.Bridge.Log("[FATAL] Initialize() failed cleanly without crashing game: " + e);
        }
    }

    [UnmanagedCallersOnly]
    public static void Bootstrap()
    {
        try
        {
            Melvor2Bridge.Bridge.Log("Bootstrap() called from native host (v" + Melvor2Bridge.Bridge.BridgeVersion + ")");
            Initialize();
        }
        catch
        {
            // Never allow managed exceptions to cross [UnmanagedCallersOnly] boundary
        }
    }
}

namespace Melvor2Bridge
{
    internal static class Bridge
    {
        public const int Port = 17817;
        public const string BridgeVersion = "2.2.0";

        private static WeakReference _gameRef;
        private static readonly Queue<Action> _mainJobs = new Queue<Action>();
        private static bool _loggedNoMainLoop;
        private static bool _loggedRootFail;

        internal static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        public static void Run()
        {
            try
            {
                // Start the autonomous background plan scheduler thread
                PlanScheduler.StartBackgroundLoop();

                var listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
                listener.Start();
                Log("listening on 127.0.0.1:" + Port + " (v" + BridgeVersion + ")");

                while (true)
                {
                    HttpListenerContext ctx;
                    try { ctx = listener.GetContext(); }
                    catch { break; }
                    ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
                }
            }
            catch (Exception e)
            {
                Log("fatal: " + e);
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            try
            {
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                var path = ctx.Request.Url.AbsolutePath.TrimEnd('/');
                if (string.IsNullOrEmpty(path) || path == "/dashboard" || path == "/index.html")
                {
                    RespondHtml(ctx, LoadEmbeddedDashboard());
                    return;
                }

                var query = ctx.Request.QueryString;
                string body = null;
                using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    body = sr.ReadToEnd();

                string json;
                switch (path)
                {
                    case "/ping":
                        json = Serialize(new Dictionary<string, object>
                        {
                            ["ok"] = true,
                            ["version"] = BridgeVersion,
                            ["compatible"] = !CompatibilityGuard.IsDisabled,
                            ["health_status"] = CompatibilityGuard.GetReport().Status,
                            ["game_found"] = GetGame() != null,
                            ["plan_status"] = PlanScheduler.GetShortStatus()
                        });
                        break;
                    case "/health":
                        json = Serialize(CompatibilityGuard.GetReport());
                        break;

                    // --- Read endpoints ---
                    case "/state":
                        json = Safe(() => StateQueries.BuildState());
                        break;
                    case "/bank":
                        json = Safe(() => StateQueries.BuildBank(query["q"]));
                        break;
                    case "/quests":
                        json = Safe(() => StateQueries.BuildQuests(query["filter"]));
                        break;
                    case "/actions":
                        json = Safe(() => StateQueries.BuildActions(query["skill"]));
                        break;
                    case "/shop":
                        json = Safe(() => StateQueries.BuildShop(query["category"], query["available"] == "true"));
                        break;
                    case "/combat":
                        json = Safe(() => StateQueries.BuildCombat());
                        break;
                    case "/passive":
                        json = Safe(() => StateQueries.BuildPassive());
                        break;
                    case "/mastery":
                        json = Safe(() => StateQueries.BuildMastery(query["skill"]));
                        break;
                    case "/probe":
                        json = Safe(() => StateQueries.Probe(body));
                        break;
                    case "/dump":
                        json = Safe(() => StateQueries.DumpContent());
                        break;

                    // --- Direct Operation endpoints (gated by EA CompatibilityGuard) ---
                    case "/op":
                        json = GuardMutator(() => GameOperations.HandleOpHttp(body));
                        break;
                    case "/start":
                        json = GuardMutator(() => GameOperations.HandleLegacyStart(body));
                        break;
                    case "/stop":
                        json = GuardMutator(() => GameOperations.HandleLegacyStop(body));
                        break;

                    // --- Autonomous Plan / Queue Scheduler endpoints ---
                    case "/plan/set":
                        json = GuardMutator(() => PlanScheduler.HandleSetPlan(body));
                        break;
                    case "/plan/status":
                        json = Safe(() => PlanScheduler.HandleStatus());
                        break;
                    case "/plan/pause":
                        json = Safe(() => PlanScheduler.HandlePause());
                        break;
                    case "/plan/resume":
                        json = GuardMutator(() => PlanScheduler.HandleResume());
                        break;
                    case "/plan/skip":
                        json = GuardMutator(() => PlanScheduler.HandleSkip());
                        break;
                    case "/plan/cancel":
                        json = Safe(() => PlanScheduler.HandleCancel(body));
                        break;

                    default:
                        json = Serialize(new Dictionary<string, object>
                        {
                            ["error"] = "unknown path " + path
                        });
                        break;
                }
                Respond(ctx, json);
            }
            catch (Exception e)
            {
                try
                {
                    Respond(ctx, Serialize(new Dictionary<string, object>
                    {
                        ["error"] = e.Message
                    }));
                }
                catch { }
            }
        }

        private static string GuardMutator(Func<string> f)
        {
            if (CompatibilityGuard.IsDisabled)
            {
                return Serialize(new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["compatible"] = false,
                    ["error"] = "Bridge disabled due to Early Access game update incompatibility: " + CompatibilityGuard.DisableReason,
                    ["report_file"] = CompatibilityGuard.ReportFilePath
                });
            }
            return Safe(f);
        }

        private static string LoadEmbeddedDashboard()
        {
            try
            {
                using var stream = typeof(Bridge).Assembly.GetManifestResourceStream("dashboard.html");
                if (stream != null)
                {
                    using var sr = new StreamReader(stream, Encoding.UTF8);
                    return sr.ReadToEnd();
                }
            }
            catch { }
            return "<html><body><h2>Melvor2Bridge v" + BridgeVersion + "</h2><p>Dashboard resource not embedded.</p></body></html>";
        }

        private static void RespondHtml(HttpListenerContext ctx, string html)
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static void Respond(HttpListenerContext ctx, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        internal static string Safe(Func<string> f)
        {
            try { return f(); }
            catch (Exception e)
            {
                var inner = UnwrapException(e);
                var trace = inner.StackTrace ?? "";
                if (trace.Length > 500) trace = trace.Substring(0, 500);
                return Serialize(new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = inner.GetType().Name + ": " + inner.Message,
                    ["trace"] = trace
                });
            }
        }

        internal static Exception UnwrapException(Exception e)
        {
            var inner = e;
            while (inner is TargetInvocationException tie && tie.InnerException != null)
                inner = tie.InnerException;
            return inner;
        }

        internal static string Serialize(object obj)
        {
            return JsonSerializer.Serialize(obj);
        }

        // ------------------------------------------------------------------
        // Main-thread marshalling (Godot SceneTreeTimer.Timeout)
        // ------------------------------------------------------------------

        internal static object RunOnMain(Func<object> job, int timeoutMs = 8000)
        {
            object result = null;
            Exception error = null;
            var done = new ManualResetEventSlim(false);
            lock (_mainJobs)
            {
                _mainJobs.Enqueue(() =>
                {
                    try { result = job(); }
                    catch (Exception e) { error = e; }
                    finally { done.Set(); }
                });
            }
            ScheduleDrain();
            if (!done.Wait(timeoutMs))
                throw new TimeoutException("Timeout waiting for Godot main thread");
            if (error != null)
                throw UnwrapException(error);
            return result;
        }

        private static void ScheduleDrain()
        {
            object mainLoop = CallGodotEngineGetMainLoop();
            if (mainLoop == null) throw new InvalidOperationException("Godot main loop not ready");
            MethodInfo createTimer = null;
            object[] args = null;
            foreach (var m in mainLoop.GetType().GetMethods())
            {
                if (m.Name != "CreateTimer") continue;
                var ps = m.GetParameters();
                if (ps.Length == 0 || ps[0].ParameterType != typeof(double)) continue;
                args = new object[ps.Length];
                args[0] = 0.001;
                for (int i = 1; i < ps.Length; i++) args[i] = ps[i].DefaultValue;
                createTimer = m;
                break;
            }
            if (createTimer == null) throw new MissingMethodException("SceneTree.CreateTimer");
            var timer = createTimer.Invoke(mainLoop, args);
            if (timer == null) throw new InvalidOperationException("CreateTimer returned null");
            var ev = timer.GetType().GetEvent("Timeout");
            if (ev == null) throw new MissingFieldException("SceneTreeTimer.Timeout");
            MethodInfo handler = ev.EventHandlerType == typeof(Action)
                ? typeof(Bridge).GetMethod("DrainAction", BindingFlags.Static | BindingFlags.NonPublic)
                : typeof(Bridge).GetMethod("DrainEventHandler", BindingFlags.Static | BindingFlags.NonPublic);
            ev.AddEventHandler(timer, Delegate.CreateDelegate(ev.EventHandlerType, handler));
        }

        private static void Drain()
        {
            while (true)
            {
                Action job;
                lock (_mainJobs)
                {
                    if (_mainJobs.Count == 0) return;
                    job = _mainJobs.Dequeue();
                }
                job();
            }
        }

        private static void DrainAction() => Drain();
        private static void DrainEventHandler(object sender, EventArgs e) => Drain();

        // ------------------------------------------------------------------
        // Game instance discovery
        // ------------------------------------------------------------------

        internal static object GetGame()
        {
            if (_gameRef != null && _gameRef.IsAlive)
            {
                var cached = _gameRef.Target;
                if (cached != null)
                {
                    // Verify not disposed
                    var isDisposed = Field(cached, "_isDisposed");
                    if (!(isDisposed is bool d && d))
                        return cached;
                }
            }

            object game = FindGame();
            if (game != null)
            {
                _gameRef = new WeakReference(game);
                CompatibilityGuard.RunSelfCheck(game);
                if (!CompatibilityGuard.IsDisabled)
                    PlanScheduler.OnGameAttached(game);
            }
            return game;
        }

        private static object FindGame()
        {
            try
            {
                object mainLoop = CallGodotEngineGetMainLoop();
                if (mainLoop == null) return null;
                object root = Prop(mainLoop, "Root") ?? Prop(mainLoop, "root");
                if (root == null) return null;

                var currentScene = Prop(mainLoop, "CurrentScene");
                if (currentScene != null && currentScene.GetType().Name == "Main")
                {
                    var viaScene = FindGameInMain(currentScene);
                    if (viaScene != null) return viaScene;
                }

                return FindGameUnder(root, 0);
            }
            catch (Exception e)
            {
                if (!_loggedNoMainLoop)
                {
                    _loggedNoMainLoop = true;
                    Log("FindGame failed: " + e.GetType().Name + ": " + e.Message);
                }
                return null;
            }
        }

        private static object FindGameUnder(object node, int depth)
        {
            foreach (object child in EnumerateGodotChildren(node))
            {
                if (child.GetType().Name == "Main")
                {
                    var game = FindGameInMain(child);
                    if (game != null) return game;
                }
                if (depth < 3)
                {
                    var nested = FindGameUnder(child, depth + 1);
                    if (nested != null) return nested;
                }
            }
            return null;
        }

        private static object CallGodotEngineGetMainLoop()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type engine;
                try { engine = asm.GetType("Godot.Engine"); } catch { continue; }
                if (engine == null) continue;
                var method = engine.GetMethod("GetMainLoop", BindingFlags.Public | BindingFlags.Static);
                if (method == null) continue;
                return method.Invoke(null, null);
            }
            return null;
        }

        private static IEnumerable EnumerateGodotChildren(object node)
        {
            object children;
            try { children = CallOptional(node, "GetChildren"); }
            catch
            {
                if (!_loggedRootFail) _loggedRootFail = true;
                yield break;
            }
            if (children is IEnumerable enumerable)
            {
                foreach (var c in enumerable) yield return c;
            }
        }

        private static object FindGameInMain(object main)
        {
            var values = new List<object>();
            var t = main.GetType();
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                try { var v = f.GetValue(main); if (v != null) values.Add(v); } catch { }
            }
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                try
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                    var v = p.GetValue(main);
                    if (v != null) values.Add(v);
                }
                catch { }
            }

            foreach (var v in values)
            {
                if (v.GetType().Name != "GameState") continue;
                var g = TryGetActiveGame(v);
                if (g != null) return g;
            }
            foreach (var v in values)
            {
                var g = TryGetActiveGame(v);
                if (g != null) return g;
            }
            return null;
        }

        private static object TryGetActiveGame(object state)
        {
            try
            {
                var t = state.GetType();
                var prop = t.GetProperty("Game", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop == null) return null;
                var game = prop.GetValue(state);
                if (game == null || game.GetType().Name != "Game") return null;
                var isActive = t.GetProperty("IsActive", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (isActive != null && isActive.GetValue(state) is bool b && !b) return null;
                return game;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        // Reflection & Game Registry Helpers
        // ------------------------------------------------------------------

        internal static object Prop(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p?.GetValue(obj);
        }

        internal static object Field(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return f.GetValue(obj);
                t = t.BaseType;
            }
            return null;
        }

        internal static MethodInfo FindCallable(Type t, string name)
        {
            foreach (var m in t.GetMethods())
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if (ps.Length == 0 || Array.TrueForAll(ps, p => p.IsOptional))
                    return m;
            }
            return null;
        }

        internal static object CallOptional(object obj, string name)
        {
            var m = FindCallable(obj.GetType(), name);
            if (m == null) return null;
            var ps = m.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++) args[i] = ps[i].DefaultValue;
            return m.Invoke(obj, args);
        }

        internal static string NameOf(object described)
        {
            if (described == null) return null;
            var display = Prop(described, "DisplayName") ?? described;
            var msg = Prop(display, "Message") ?? Prop(display, "Singular") ?? Prop(display, "Text");
            return msg?.ToString() ?? display.ToString();
        }

        internal static string NormalizeId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return id;
            return id.Contains(':') ? id : "MelvorBase:" + id;
        }

        internal static Assembly GameAssembly
        {
            get
            {
                var g = GetGame();
                return g?.GetType().Assembly;
            }
        }

        internal static Type FindGameType(string simpleName)
        {
            var asm = GameAssembly;
            if (asm == null) return null;
            foreach (var t in asm.GetTypes())
            {
                if (t.Name == simpleName && !t.IsGenericTypeDefinition)
                    return t;
            }
            return null;
        }

        internal static object LookupDataObject(object game, string simpleTypeName, string id)
        {
            if (game == null || string.IsNullOrWhiteSpace(id)) return null;
            var nid = NormalizeId(id);
            var data = Prop(game, "Data");
            var db = Prop(data, "Database");
            if (db == null) return null;
            var targetType = FindGameType(simpleTypeName);
            if (targetType == null) return null;

            // Database.TryGetObject<T>(string id, out T value)
            foreach (var m in db.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (m.Name == "TryGetObject" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
                {
                    var gm = m.MakeGenericMethod(targetType);
                    var args = new object[] { nid, null };
                    if (gm.Invoke(db, args) is bool ok && ok)
                        return args[1];
                    return null;
                }
            }
            return null;
        }

        internal static IEnumerable GetAllEntities(object game)
        {
            var entities = Prop(game, "Entities");
            return entities?.GetType().GetMethod("GetAllObjects")?.Invoke(entities, null) as IEnumerable;
        }

        internal static object FindEntity(object game, string entityId)
        {
            if (game == null || string.IsNullOrWhiteSpace(entityId)) return null;
            var nid = NormalizeId(entityId);
            var all = GetAllEntities(game);
            if (all == null) return null;
            foreach (var e in all)
            {
                var id = Prop(e, "Id")?.ToString();
                if (string.Equals(id, nid, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.GetType().Name, entityId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.GetType().Name, entityId + "Entity", StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return null;
        }

        internal static object FindComponent(object game, string entityId, string componentId)
        {
            var entity = FindEntity(game, entityId);
            if (entity == null) return null;
            var components = Prop(entity, "Components") as IDictionary;
            if (components == null) return null;
            if (componentId != null && components.Contains(componentId))
                return components[componentId];
            foreach (DictionaryEntry de in components)
            {
                if (de.Value == null) continue;
                var cid = Prop(de.Value, "Id")?.ToString();
                if (componentId != null)
                {
                    if (string.Equals(cid, componentId, StringComparison.OrdinalIgnoreCase))
                        return de.Value;
                }
                else if (IsActionComponent(de.Value))
                {
                    return de.Value;
                }
            }
            return null;
        }

        internal static bool IsActionComponent(object comp)
        {
            if (comp == null) return false;
            var t = comp.GetType();
            while (t != null)
            {
                if (t.Name == "ActionComponent") return true;
                t = t.BaseType;
            }
            return false;
        }

        internal static IEnumerable<object> EnumerateDictionaryValues(object dictObj)
        {
            if (dictObj == null) yield break;
            if (dictObj is IDictionary idict)
            {
                foreach (DictionaryEntry de in idict)
                    if (de.Value != null) yield return de.Value;
                yield break;
            }
            var vprop = dictObj.GetType().GetProperty("Values");
            var vobj = vprop?.GetValue(dictObj);
            if (vobj is IEnumerable venum)
            {
                foreach (var v in venum)
                    if (v != null) yield return v;
                yield break;
            }
            if (dictObj is IEnumerable ienum && !(dictObj is string))
            {
                foreach (var item in ienum)
                {
                    if (item == null) continue;
                    var kt = item.GetType();
                    if (kt.Name.StartsWith("KeyValuePair"))
                    {
                        var val = kt.GetProperty("Value")?.GetValue(item);
                        if (val != null) yield return val;
                    }
                    else yield return item;
                }
            }
        }

        internal static int GetBankItemQuantity(object game, string itemId)
        {
            var inv = FindComponent(game, "MelvorBase:Bank", "Inventory");
            var item = LookupDataObject(game, "Item", itemId);
            if (inv == null || item == null) return 0;
            var m = inv.GetType().GetMethod("GetQuantity", new[] { item.GetType() });
            if (m != null && m.Invoke(inv, new[] { item }) is int q)
                return q;
            return 0;
        }

        internal static int GetSkillLevel(object game, string skillNameOrId)
        {
            var exp = FindComponent(game, skillNameOrId, "Experience");
            if (exp == null) return 0;
            var xp = Convert.ToDouble(Prop(exp, "Xp") ?? 0.0, CultureInfo.InvariantCulture);
            return LevelFromXp(xp);
        }

        // ------------------------------------------------------------------
        // Experience table (StandardExperienceTable.Default)
        // ------------------------------------------------------------------

        private static readonly List<double> XpCache = new List<double> { 0.0 };
        private static double _xpSum;

        internal static double XpForLevel(int level)
        {
            if (level < 1) return 0;
            lock (XpCache)
            {
                while (XpCache.Count < level)
                {
                    int i = XpCache.Count;
                    _xpSum += Math.Floor(i + 300.0 * Math.Pow(2.0, i / 7.0));
                    XpCache.Add(Math.Floor(0.25 * _xpSum));
                }
                return XpCache[level - 1];
            }
        }

        internal static int LevelFromXp(double xp)
        {
            if (xp <= 0) return 1;
            int level = 1;
            while (level < 120 && xp >= XpForLevel(level + 1)) level++;
            return level;
        }

        internal static string DataFolder
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Games By Malcs", "Melvor Idle 2");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        internal static void Log(string msg)
        {
            try
            {
                var dir = Path.Combine(DataFolder, "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "melvor2_bridge.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}
