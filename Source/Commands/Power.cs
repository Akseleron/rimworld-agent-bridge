using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;

namespace AgentBridge
{
    /// <summary>Introspection, code execution and flow control.</summary>
    internal static class Power
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        private static Type TypeByName(string n) => GenTypes.GetTypeInAnyAssembly(n) ?? AppDomain.CurrentDomain.GetAssemblies().Select(asm => asm.GetType(n, false)).FirstOrDefault(t => t != null) ?? throw new CommandError("no type " + n);

        private static object Root(string target)
        {
            int i = target.IndexOf(':');
            string kind = i < 0 ? target : target.Substring(0, i), arg = i < 0 ? "" : target.Substring(i + 1);
            switch (kind)
            {
                case "thing": return G.Thing(arg);
                case "map": return G.Map;
                case "game": return Current.Game ?? throw new CommandError("no game");
                case "world": return Find.World ?? throw new CommandError("no world");
                case "mapcomp": return G.Map.components.FirstOrDefault(c => c.GetType().Name == arg || c.GetType().FullName == arg) ?? throw new CommandError("no map component " + arg);
                case "gamecomp": return Current.Game.components.FirstOrDefault(c => c.GetType().Name == arg || c.GetType().FullName == arg) ?? throw new CommandError("no game component " + arg);
                case "worldcomp": return Find.World.components.FirstOrDefault(c => c.GetType().Name == arg || c.GetType().FullName == arg) ?? throw new CommandError("no world component " + arg);
                case "static": return TypeByName(arg);
                default: throw new CommandError("target: thing:ID map game world mapcomp:T gamecomp:T worldcomp:T static:Ns.Type");
            }
        }

        private static object? Member(object? owner, Type type, string name)
        {
            if (name.StartsWith("["))
            {
                int idx = int.Parse(name.Substring(1, name.Length - 2));
                return owner is IList list ? list[idx] : (owner as IEnumerable)?.Cast<object>().ElementAt(idx);
            }
            var f = type.GetField(name, Any); if (f != null) return f.GetValue(f.IsStatic ? null : owner);
            var p = type.GetProperty(name, Any); if (p != null) return p.GetValue(p.GetGetMethod(true).IsStatic ? null : owner, null);
            var m = type.GetMethod(name, Any, null, Type.EmptyTypes, null); if (m != null) return m.Invoke(m.IsStatic ? null : owner, null);
            throw new CommandError("no member " + name + " on " + type.Name);
        }

        private static object? Convert(object? v, Type t)
        {
            if (v == null) return null;
            if (t == typeof(IntVec3) && v is List<object?> l) return new IntVec3(System.Convert.ToInt32(l[0]), 0, System.Convert.ToInt32(l[l.Count - 1]));
            if (typeof(Def).IsAssignableFrom(t)) return G.AnyDef(t.Name, v.ToString());
            if (typeof(Thing).IsAssignableFrom(t)) return G.Thing(v.ToString());
            if (t.IsEnum) return Enum.Parse(t, v.ToString(), true);
            if (t == typeof(string)) return v.ToString();
            return System.Convert.ChangeType(v, Nullable.GetUnderlyingType(t) ?? t, System.Globalization.CultureInfo.InvariantCulture);
        }

        [Command("obj", Access.Read, "reflect live objects {target, path='a.b[2].c', op: get|members (Read) | set|call (Power), value?, args?, depth=1}")]
        private static object? Obj(Args a)
        {
            string op = a.Str("op") ?? "get";
            if ((op == "set" || op == "call") && Bridge.Denied(Access.Power) is string why) throw new CommandError(why);
            object root = Root(a.Need("target"));
            object? owner = root is Type ? null : root;
            Type type = root as Type ?? root.GetType();
            var parts = Regex.Matches(a.Str("path") ?? "", @"[^.\[\]]+|\[\d+\]").Cast<Match>().Select(m => m.Value).ToList();
            for (int i = 0; i < parts.Count - 1; i++)
            {
                owner = Member(owner, type, parts[i]);
                type = owner?.GetType() ?? throw new CommandError("null at " + parts[i]);
            }
            string? last = parts.Count > 0 ? parts[parts.Count - 1] : null;
            int depth = a.Int("depth", 1);
            switch (op)
            {
                case "members":
                    var t = last == null ? type : Member(owner, type, last)?.GetType() ?? type;
                    return new { type = t.FullName, members = t.GetMembers(Any).Where(m => m is FieldInfo || m is PropertyInfo || (m is MethodInfo mi && !mi.IsSpecialName)).Select(m => m.MemberType.ToString()[0] + " " + m.Name).Distinct().OrderBy(x => x).Take(500).ToList() };
                case "set":
                    if (last == null) throw new CommandError("need path");
                    var f = type.GetField(last, Any);
                    if (f != null) f.SetValue(f.IsStatic ? null : owner, Convert(a.Get("value"), f.FieldType));
                    else { var p = type.GetProperty(last, Any) ?? throw new CommandError("no member " + last); p.SetValue(p.GetGetMethod(true).IsStatic ? null : owner, Convert(a.Get("value"), p.PropertyType), null); }
                    return Plain.Dump(Member(owner, type, last), depth);
                case "call":
                    if (last == null) throw new CommandError("need path");
                    var args = a.List("args");
                    var method = type.GetMethods(Any).FirstOrDefault(m => m.Name == last && m.GetParameters().Length == args.Count) ?? throw new CommandError("no method " + last + "/" + args.Count);
                    try { return Plain.Dump(method.Invoke(method.IsStatic ? null : owner, method.GetParameters().Select((p, i) => Convert(args[i], p.ParameterType)).ToArray()), depth); }
                    catch (TargetInvocationException e) { throw new CommandError((e.InnerException ?? e).ToString()); }
                default:
                    return Plain.Dump(last == null ? owner ?? root : Member(owner, type, last), depth);
            }
        }

        [Command("patches", Access.Read, "Harmony patches {method?: 'Ns.Type:Method', owner?}")]
        private static object Patches(Args a)
        {
            if (!Bridge.HarmonyActive) throw new CommandError("Harmony not loaded");
            return HarmonyPatches.PatchInfo(a.Str("owner"), a.Str("method"));
        }

        private static readonly Dictionary<string, Assembly> execCache = new Dictionary<string, Assembly>();

        [Command("exec", Access.Power, "run a compiled DLL's static method {dll, type=Script, method=Run, args?, depth=2}; use 'rwb eval' to compile C#")]
        private static object? Exec(Args a)
        {
            string dll = a.Need("dll");
            byte[] bytes = File.ReadAllBytes(dll);
            string key;
            using (var sha = System.Security.Cryptography.SHA256.Create()) key = string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
            if (!execCache.TryGetValue(key, out var asm)) execCache[key] = asm = Assembly.Load(bytes);
            var type = asm.GetType(a.Str("type") ?? "Script") ?? throw new CommandError("no type");
            var m = type.GetMethod(a.Str("method") ?? "Run", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new CommandError("no method");
            object? result;
            try { result = m.Invoke(null, m.GetParameters().Length == 1 ? new object[] { (a.Get("args") as Dictionary<string, object?>) ?? new Dictionary<string, object?>() } : null); }
            catch (TargetInvocationException e) { throw new CommandError((e.InnerException ?? e).ToString()); }
            return result is Pending pending ? pending : Plain.Dump(result, a.Int("depth", 2));
        }

        [Command("wait", Access.Read, "wait for {until: ticks|loaded|paused|no-hostiles|log|message|letter|incident|death, n?, pattern? (regex), after? (feed seq, default now), timeout=60}")]
        private static object Wait(Args a)
        {
            string until = a.Str("until") ?? "loaded";
            long from = a.Long("after", Feed.Last);
            var rx = a.Str("pattern") is string pat ? new Regex(pat, RegexOptions.IgnoreCase) : null;
            int start = Current.Game?.tickManager?.TicksGame ?? 0, n = a.Int("n", 60);
            Func<object?> poll;
            switch (until)
            {
                case "loaded": poll = () => (Current.ProgramState == ProgramState.Playing || Current.ProgramState == ProgramState.Entry) && !LongEventHandler.AnyEventNowOrWaiting ? new { state = Current.ProgramState.ToString() } : null; break;
                case "ticks": poll = () => G.Ticks.TicksGame >= start + n ? new { tick = G.Ticks.TicksGame } : null; break;
                case "paused": poll = () => G.Ticks.Paused ? new { tick = G.Ticks.TicksGame } : null; break;
                case "no-hostiles": poll = () => !G.Map.mapPawns.AllPawnsSpawned.Any(p => p.HostileTo(Faction.OfPlayer) && !p.Downed && !p.Dead) ? new { tick = G.Ticks.TicksGame } : null; break;
                case "log": case "message": case "letter": case "incident": case "death": poll = () => Feed.Find(from, until, rx); break;
                default: throw new CommandError("until: ticks|loaded|paused|no-hostiles|log|message|letter|incident|death");
            }
            return new Pending(poll, a.Timeout, () => new { timedOut = true, tick = Current.Game?.tickManager?.TicksGame, last = Feed.Last });
        }

        [Command("batch", Access.Read, "run several commands in one frame {cmds:[{cmd,...}], stopOnError=true}; each is access-checked")]
        private static object Batch(Args a)
        {
            bool stop = a.Bool("stopOnError", true);
            var results = new List<object?>();
            foreach (var item in a.List("cmds"))
            {
                if (!(item is Dictionary<string, object?> d)) { results.Add(new { ok = false, error = "not an object" }); if (stop) break; continue; }
                var r = Bridge.Execute(new Args(d));
                if (r is Pending) { results.Add(new { ok = false, error = "waiting commands are not allowed in batch" }); if (stop) break; continue; }
                if (r is CommandError e) { results.Add(new { ok = false, error = e.Message }); if (stop) break; continue; }
                results.Add(new { ok = true, result = r });
            }
            return results;
        }
    }
}
