using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace AgentBridge
{
    // Maps game objects to compact, language-independent JSON-friendly values.
    public static class Plain
    {
        private static readonly Assembly Own = typeof(Plain).Assembly;

        /// <summary>Short form used everywhere in results: ids for things, defNames for defs, [x,z] for cells.
        /// Unknown foreign objects become their ToString() so results never explode into huge graphs.</summary>
        public static object? Of(object? v)
        {
            switch (v)
            {
                case null: return null;
                case string _: case bool _: case IDictionary _: return v;
                case IntVec3 c: return c.IsValid ? new[] { c.x, c.z } : null;
                case IntVec2 c2: return new[] { c2.x, c2.z };
                case Vector3 v3: return new[] { Round(v3.x), Round(v3.y), Round(v3.z) };
                case Vector2 v2: return new[] { Round(v2.x), Round(v2.y) };
                case Rot4 r: return r.AsInt;
                case Def d: return d.defName;
                case Thing t: return t.ThingID;
                case Faction f: return f.Name;
                case Map m: return "map:" + m.uniqueID;
                case TaggedString ts: return ts.ToString();
                case LocalTargetInfo lt: return lt.HasThing ? (object)lt.Thing.ThingID : lt.Cell.IsValid ? new[] { lt.Cell.x, lt.Cell.z } : null;
                case Type type: return type.FullName;
                case IEnumerable _: return v;
            }
            var t2 = v.GetType();
            if (t2.IsPrimitive || t2.IsEnum || v is decimal) return v;
            if (t2.Assembly == Own || IsAnonymous(t2)) return v;
            return v.ToString();
        }

        public static bool IsAnonymous(Type t) => t.Name.Contains("AnonymousType") || (t.Name.StartsWith("<>") && t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false));

        private static float Round(float f) => (float)Math.Round(f, 3);

        /// <summary>Deep reflection dump for introspection commands: fields of arbitrary objects down to depth.</summary>
        /// <summary>Agent-chosen dump depth. Field graphs fan out exponentially (defs reference defs), so it is capped.</summary>
        public static int Depth(Args a, int d) => Args.Clamp(a.Int("depth", d), 0, 4);

        [ThreadStatic] private static int budget;

        /// <summary>Plain JSON-friendly view of any object, at most 20000 nodes (a deep dump of linked defs explodes otherwise).</summary>
        public static object? Dump(object? v, int depth)
        {
            budget = 20000;
            return Inner(v, depth);
        }

        private static object? Inner(object? v, int depth)
        {
            if (v == null) return null;
            if (--budget < 0) return "…";
            if (v is Type) return Of(v);
            var type = v.GetType();
            if (type.IsPrimitive || type.IsEnum || v is string || v is decimal || v is IntVec3 || v is Vector3 || v is TaggedString || v is LocalTargetInfo || v is Rot4) return Of(v);
            if (depth <= 0 && (v is Def || v is Thing || v is Faction || v is Map)) return Of(v);
            if (IsAnonymous(type)) return type.GetProperties().ToDictionary(p => p.Name, p => Inner(p.GetValue(v, null), depth));
            if (v is IDictionary dict) return depth < 0 ? (object)dict.Count : dict.Keys.Cast<object>().Take(200).ToDictionary(k => k?.ToString() ?? "null", k => Inner(dict[k], depth - 1));
            if (v is IEnumerable seq) return depth < 0 ? (object)"<list>" : seq.Cast<object?>().Take(200).Select(x => Inner(x, depth - 1)).ToList();
            if (depth <= 0) return v.ToString();
            var result = new Dictionary<string, object?> { ["$type"] = type.FullName };
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.Name.IndexOf('<') >= 0 || result.ContainsKey(f.Name)) continue;
                    try { result[f.Name] = Inner(f.GetValue(v), depth - 1); }
                    catch (Exception e) { result[f.Name] = "!" + e.GetType().Name; }
                }
            return result;
        }
    }
}
