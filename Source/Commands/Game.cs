using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace AgentBridge
{
    /// <summary>Shared lookups for command handlers. Things are addressed by ThingID, cells by x,z of the current map.</summary>
    public static class G
    {
        public static bool Playing => Current.ProgramState == ProgramState.Playing && Current.Game != null && Find.CurrentMap != null;
        public static Map Map => Playing ? Find.CurrentMap : throw new CommandError("no map loaded (main menu?)");
        public static TickManager Ticks => Current.Game?.tickManager ?? throw new CommandError("no game loaded");

        public static IntVec3 Cell(Args a, string x = "x", string z = "z")
        {
            if (!a.Has(x) || !a.Has(z)) throw new CommandError("need " + x + "," + z);
            var c = new IntVec3(a.Int(x, 0), 0, a.Int(z, 0));
            if (!c.InBounds(Map)) throw new CommandError("cell (" + c.x + "," + c.z + ") out of bounds; map size " + Map.Size.x + "x" + Map.Size.z);
            return c;
        }

        /// <summary>Options and gizmos that the game adds only in Dev mode are labelled "Dev:"/"DEV:" in every language.
        /// Fair play hides them, so an agent cannot use them even when the player has Dev mode on.</summary>
        public static bool DevOnly(string? label) => Bridge.Settings.fairPlay && label != null && label.IndexOf("dev:", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Fair play: things under fog of war are invisible to the agent, as to the player.</summary>
        public static bool Seen(Thing t) => !Bridge.Settings.fairPlay || !t.Spawned || !t.Position.Fogged(t.Map);

        /// <summary>Gizmo owners: things by ThingID, or world objects as caravan:ID / wo:ID.</summary>
        public static ISelectable Selectable(string id)
        {
            if (id.StartsWith("caravan:") || id.StartsWith("wo:"))
            {
                string n = id.Substring(id.IndexOf(':') + 1);
                return Find.WorldObjects.AllWorldObjects.FirstOrDefault(o => o.ID.ToString() == n) ?? throw new CommandError("no world object " + id);
            }
            return Thing(id);
        }

        public static Thing Thing(string id)
        {
            if (Current.Game == null) throw new CommandError("no game loaded");
            Thing? t = null;
            // Pawns first: the usual target, a short list, and they include pawns that are carried or in containers.
            foreach (var map in Find.Maps) if ((t = map.mapPawns.AllPawns.FirstOrDefault(p => p.ThingID == id)) != null) break;
            if (t == null && ThingIndex().TryGetValue(id, out var cached) && !cached.Destroyed) t = cached;
            // Miss: the thing may have been spawned after this frame's index was built (spawn + use in one batch).
            if (t == null) foreach (var map in Find.Maps) if ((t = map.listerThings.AllThings.FirstOrDefault(x => x.ThingID == id)) != null) { index![id] = t; break; }
            if (t == null) throw new CommandError("no thing '" + id + "' (ids look like Human123; see pawns/things)");
            return Seen(t) ? t : throw new CommandError("no visible thing '" + id + "' (fog of war)");
        }

        private static Dictionary<string, Thing>? index;
        private static int indexFrame = -1;
        private static Game? indexGame;

        /// <summary>ThingID index of all spawned things, built at most once per frame (maps hold tens of thousands of
        /// things, and agents often look up several per frame via batch). Destroyed entries are skipped by callers.</summary>
        private static Dictionary<string, Thing> ThingIndex()
        {
            if (index == null || indexFrame != UnityEngine.Time.frameCount || !ReferenceEquals(indexGame, Current.Game))
            {
                index = new Dictionary<string, Thing>();
                foreach (var map in Find.Maps)
                    foreach (var x in map.listerThings.AllThings) index[x.ThingID] = x;
                indexFrame = UnityEngine.Time.frameCount; indexGame = Current.Game;
            }
            return index;
        }

        public static List<Pawn> Pawns(Args a, string key = "ids")
        {
            var ids = a.Strings(key);
            if (ids.Count == 0 && key == "ids") ids = a.Strings("id");
            if (ids.Count == 0) throw new CommandError("need '" + key + "' (pawn ThingIDs)");
            return ids.Select(i => Thing(i) as Pawn ?? throw new CommandError(i + " is not a pawn")).ToList();
        }

        public static Pawn Pawn(Args a) => Pawns(a).First();

        public static T Def<T>(string? name) where T : Def
            => DefDatabase<T>.GetNamedSilentFail(name ?? "") ?? throw new CommandError("no " + typeof(T).Name + " '" + name + "'; search with defs type=" + typeof(T).Name + " filter=...");

        public static Def AnyDef(string type, string name)
        {
            var defType = DefType(type);
            return (Def?)GenGeneric.InvokeStaticMethodOnGenericType(typeof(DefDatabase<>), defType, "GetNamedSilentFail", name) ?? throw new CommandError("no " + type + " '" + name + "'");
        }

        public static Type DefType(string type) => GenTypes.GetTypeInAnyAssembly(type) ?? GenTypes.GetTypeInAnyAssembly("Verse." + type) ?? GenTypes.GetTypeInAnyAssembly("RimWorld." + type) ?? throw new CommandError("no def type " + type);

        public static Faction? Faction(Args a)
        {
            string? f = a.Str("faction");
            if (f == null) return null;
            if (f.Equals("player", StringComparison.OrdinalIgnoreCase)) return RimWorld.Faction.OfPlayer;
            return Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => x.def.defName.Equals(f, StringComparison.OrdinalIgnoreCase) || x.Name == f || x.loadID.ToString() == f)
                ?? Find.FactionManager.FirstFactionOfDef(Def<FactionDef>(f));
        }

        public static LocalTargetInfo Target(Args a) => a.Has("target") ? new LocalTargetInfo(Thing(a.Need("target"))) : new LocalTargetInfo(Cell(a));

        /// <summary>Runs code while UI.MouseCell reports the given cell (needs Harmony; otherwise the real cursor is used).</summary>
        public static T AtCell<T>(IntVec3 cell, Func<T> f)
        {
            Hooks.MouseOverride = cell;
            try { return f(); } finally { Hooks.MouseOverride = null; }
        }

        public static bool Has(this string s, string part) => s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        public static object PawnBrief(Pawn p) => new
        {
            id = p.ThingID, name = p.LabelShortCap, kind = p.kindDef?.defName, faction = p.Faction?.Name, pos = p.Spawned ? (object)p.Position : null,
            hostile = p.HostileTo(RimWorld.Faction.OfPlayer) ? true : (bool?)null, drafted = p.Drafted ? true : (bool?)null,
            downed = p.Downed ? true : (bool?)null, dead = p.Dead ? true : (bool?)null,
            health = p.health?.summaryHealth?.SummaryHealthPercent, job = p.CurJob?.def.defName, jobTarget = p.CurJob?.targetA.IsValid == true ? (object)p.CurJob.targetA : null,
            mood = p.needs?.mood?.CurLevelPercentage, weapon = p.equipment?.Primary?.def.defName,
        };

        public static object ThingBrief(Thing t) => new
        {
            id = t.ThingID, def = t.def.defName, label = t.LabelCap.ToString(), pos = t.Spawned ? (object)t.Position : null, stack = t.stackCount > 1 ? t.stackCount : (int?)null,
            hp = t.def.useHitPoints ? t.HitPoints : (int?)null, maxHp = t.def.useHitPoints ? t.MaxHitPoints : (int?)null, faction = t.Faction?.Name,
            category = t.def.category.ToString(), forbidden = t.IsForbidden(RimWorld.Faction.OfPlayer) ? true : (bool?)null,
        };
    }
}
