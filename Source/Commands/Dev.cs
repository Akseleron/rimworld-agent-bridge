using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace AgentBridge
{
    /// <summary>World editing equivalent to Dev mode tools.</summary>
    internal static class Dev
    {
        [Command("spawn", Access.Dev, "spawn {def, x,z, count=1, stuff?, quality?, faction?, rot?} or pawns {kind, x,z, count, faction?}")]
        private static object Spawn(Args a)
        {
            var map = G.Map; var c = G.Cell(a); int count = Args.Clamp(a.Int("count", 1), 1, 100000);
            var ids = new List<string>();
            if (a.Has("kind"))
            {
                var kind = G.Def<PawnKindDef>(a.Str("kind"));
                var faction = G.Faction(a) ?? (kind.defaultFactionDef != null ? Find.FactionManager.FirstFactionOfDef(kind.defaultFactionDef) : null);
                for (int i = 0; i < Math.Min(count, 100); i++)
                {
                    var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, mustBeCapableOfViolence: false));
                    GenSpawn.Spawn(pawn, CellFinder.StandableCellNear(c, map, 5), map);
                    ids.Add(pawn.ThingID);
                }
                return ids;
            }
            var def = G.Def<ThingDef>(a.Str("def"));
            var stuff = a.Str("stuff") is string s ? G.Def<ThingDef>(s) : def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            for (int left = count; left > 0 && ids.Count < 500;)
            {
                var thing = ThingMaker.MakeThing(def, stuff);
                thing.stackCount = Math.Min(left, def.stackLimit); left -= thing.stackCount;
                if (a.Str("quality") is string q) thing.TryGetComp<CompQuality>()?.SetQuality((QualityCategory)Enum.Parse(typeof(QualityCategory), q, true), ArtGenerationContext.Outsider);
                if (G.Faction(a) is Faction fac && def.CanHaveFaction) thing.SetFaction(fac);
                Thing? placed = def.category == ThingCategory.Building ? GenSpawn.Spawn(thing, c, map, new Rot4(a.Int("rot", 0)))
                    : GenPlace.TryPlaceThing(thing, c, map, ThingPlaceMode.Near, out var res) ? res : null;
                if (placed != null) ids.Add(placed.ThingID);
            }
            return ids;
        }

        [Command("destroy", Access.Dev, "destroy things {ids}")]
        private static object Destroy(Args a) { foreach (var id in a.Strings("ids").Concat(a.Strings("id"))) G.Thing(id).Destroy(DestroyMode.Vanish); return "ok"; }

        [Command("teleport", Access.Dev, "move a thing instantly {id, x,z}")]
        private static object Teleport(Args a)
        {
            var t = G.Thing(a.Need("id")); var c = G.Cell(a);
            if (t is Pawn p) { p.pather?.StopDead(); p.Position = c; p.Notify_Teleported(); } else t.Position = c;
            return t.Position;
        }

        [Command("kill", Access.Dev, "kill pawns {ids}")]
        private static object Kill(Args a) { foreach (var p in G.Pawns(a)) p.Kill(null); return "ok"; }

        [Command("damage", Access.Dev, "deal damage {id, amount=10, def=Blunt, part?, armorPen=0}")]
        private static object Damage(Args a)
        {
            var t = G.Thing(a.Need("id"));
            var info = new DamageInfo(G.Def<DamageDef>(a.Str("def") ?? "Blunt"), a.Float("amount", 10), a.Float("armorPen", 0));
            if (t is Pawn p && a.Str("part") is string part)
                info.SetHitPart(p.health.hediffSet.GetNotMissingParts().FirstOrDefault(x => x.def.defName.Equals(part, StringComparison.OrdinalIgnoreCase) || x.Label == part) ?? throw new CommandError("no body part " + part));
            var r = t.TakeDamage(info);
            return new { dealt = r.totalDamageDealt, hp = t.HitPoints, destroyed = t.Destroyed ? true : (bool?)null, dead = (t as Pawn)?.Dead == true ? true : (bool?)null };
        }

        [Command("heal", Access.Dev, "resurrect, remove injuries/curable illness, fill needs {ids}")]
        private static object Heal(Args a)
        {
            foreach (var p in G.Pawns(a))
            {
                if (p.Dead) ResurrectionUtility.TryResurrect(p);
                foreach (var h in p.health.hediffSet.hediffs.Where(h => h is Hediff_Injury || h is Hediff_MissingPart || (h.def.isBad && h.def.everCurableByItem)).ToList()) p.health.RemoveHediff(h);
                if (p.needs != null) foreach (var n in p.needs.AllNeeds) n.CurLevelPercentage = 1f;
            }
            return "ok";
        }

        [Command("hediff", Access.Dev, "add or remove a hediff {id, def, severity?, part?, remove=false}")]
        private static object Hediff(Args a)
        {
            var p = G.Pawn(a); var def = G.Def<HediffDef>(a.Str("def"));
            if (a.Bool("remove", false)) { foreach (var h in p.health.hediffSet.hediffs.Where(h => h.def == def).ToList()) p.health.RemoveHediff(h); return "removed"; }
            var part = a.Str("part") is string pn ? p.health.hediffSet.GetNotMissingParts().FirstOrDefault(x => x.def.defName.Equals(pn, StringComparison.OrdinalIgnoreCase) || x.Label == pn) : null;
            var hediff = HediffMaker.MakeHediff(def, p, part);
            if (a.Has("severity")) hediff.Severity = a.Float("severity", hediff.Severity);
            p.health.AddHediff(hediff, part);
            return new { added = def.defName, severity = hediff.Severity };
        }

        [Command("need", Access.Dev, "set a need level {id, need, value 0..1}")]
        private static object Need(Args a)
        {
            var p = G.Pawn(a); string want = a.Need("need");
            var n = p.needs?.AllNeeds.FirstOrDefault(x => x.def.defName.Equals(want, StringComparison.OrdinalIgnoreCase)) ?? throw new CommandError("no need " + want);
            n.CurLevelPercentage = a.Float("value", 1);
            return n.CurLevelPercentage;
        }

        [Command("skill", Access.Dev, "get/set a skill {id, skill, level?}")]
        private static object Skill(Args a)
        {
            var s = G.Pawn(a).skills?.GetSkill(G.Def<SkillDef>(a.Str("skill"))) ?? throw new CommandError("pawn has no skills");
            if (a.Has("level")) s.Level = a.Int("level", s.Level);
            return new { level = s.Level, disabled = s.TotallyDisabled ? true : (bool?)null, passion = s.passion.ToString() };
        }

        [Command("incident", Access.Dev, "fire an incident {def, points?, faction?, strategy?, arrival?, force=true}; without def lists them")]
        private static object Incident(Args a)
        {
            if (!a.Has("def")) return DefDatabase<IncidentDef>.AllDefs.Where(d => a.Str("filter") == null || d.defName.Has(a.Str("filter")!)).Select(d => d.defName + ":" + d.category?.defName).ToList();
            var def = G.Def<IncidentDef>(a.Str("def"));
            var parms = StorytellerUtility.DefaultParmsNow(def.category, G.Map);
            parms.forced = a.Bool("force", true);
            if (a.Has("points")) parms.points = a.Float("points", parms.points);
            if (G.Faction(a) is Faction f) parms.faction = f;
            if (a.Str("strategy") is string st) parms.raidStrategy = G.Def<RaidStrategyDef>(st);
            if (a.Str("arrival") is string ar) parms.raidArrivalMode = G.Def<PawnsArrivalModeDef>(ar);
            bool ok = (parms.forced || def.Worker.CanFireNow(parms)) && def.Worker.TryExecute(parms);
            return new { fired = ok, points = parms.points, faction = parms.faction?.Name };
        }

        [Command("weather", Access.Dev, "change weather {def}")]
        private static object Weather(Args a) { G.Map.weatherManager.TransitionTo(G.Def<WeatherDef>(a.Str("def"))); return "ok"; }

        [Command("condition", Access.Dev, "start a game condition {def, ticks=60000}")]
        private static object Condition(Args a)
        {
            var c = GameConditionMaker.MakeCondition(G.Def<GameConditionDef>(a.Str("def")), a.Int("ticks", 60000));
            G.Map.gameConditionManager.RegisterCondition(c);
            return c.def.defName;
        }

        [Command("godmode", Access.Dev, "toggle or set god mode {on?}")]
        private static object GodMode(Args a) { DebugSettings.godMode = a.Bool("on", !DebugSettings.godMode); return DebugSettings.godMode; }

        [Command("goodwill", Access.Dev, "change faction goodwill {id, delta}")]
        private static object Goodwill(Args a)
        {
            var f = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => x.loadID == a.Int("id", -1)) ?? throw new CommandError("no faction id; see factions");
            f.TryAffectGoodwillWith(Faction.OfPlayer, a.Int("delta", 0), false, false);
            return f.PlayerGoodwill;
        }

        [Command("research-finish", Access.Dev, "finish research {def | all=true}")]
        private static object ResearchFinish(Args a)
        {
            var rm = Find.ResearchManager;
            var defs = a.Bool("all", false) ? DefDatabase<ResearchProjectDef>.AllDefs.Where(d => !d.IsFinished).ToList() : new List<ResearchProjectDef> { G.Def<ResearchProjectDef>(a.Str("def")) };
            foreach (var d in defs) rm.FinishProject(d, false, null, false);
            return defs.Count;
        }

        [Command("build-instant", Access.Dev, "spawn a finished building {def, x,z, rot=0, stuff?}")]
        private static object BuildInstant(Args a)
        {
            var def = G.Def<ThingDef>(a.Str("def"));
            var stuff = a.Str("stuff") is string s ? G.Def<ThingDef>(s) : def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            var thing = ThingMaker.MakeThing(def, stuff); thing.SetFactionDirect(Faction.OfPlayer);
            return GenSpawn.Spawn(thing, G.Cell(a), G.Map, new Rot4(a.Int("rot", 0))).ThingID;
        }

        [Command("debug", Access.Dev, "run any Dev-mode debug action {path (see debug-list), x,z for map tools, pawn/ids for pawn tools, filter for submenus}")]
        private static object Debug(Args a)
        {
            string path = a.Need("path");
            var node = Dialog_Debug.GetNode(path) ?? throw new CommandError("no debug node " + path + "; see debug-list filter=...");
            node.TrySetupChildren();
            if (node.children.Count > 0)
            {
                string? f = a.Str("filter");
                var labels = node.children.Select(c => c.label).Where(l => f == null || l.Has(f)).ToList();
                return new { submenu = labels.Take(a.Int("limit", 100)).ToList(), total = labels.Count, hint = "append \\<label> to path" };
            }
            if (node.settingsField != null) { node.action?.Invoke(); return new { setting = path, on = node.On }; }
            switch (node.actionType)
            {
                case DebugActionType.Action: node.action(); break;
                case DebugActionType.ToolMap: G.AtCell(G.Cell(a), () => { node.action(); return 0; }); break;
                case DebugActionType.ToolMapForPawns:
                    var pawns = a.Has("pawn") ? G.Pawns(a, "pawn") : a.Has("ids") ? G.Pawns(a) : G.Cell(a).GetThingList(G.Map).OfType<Pawn>().ToList();
                    if (pawns.Count == 0) throw new CommandError("no pawns");
                    foreach (var p in pawns) node.pawnAction(p);
                    return new { ran = path, pawns = pawns.Select(p => p.ThingID).ToList() };
                default: throw new CommandError("unsupported action type " + node.actionType);
            }
            return new { ran = path, windows = Find.WindowStack.Windows.Where(w => !(w is ImmediateWindow)).Select(w => w.GetType().Name).ToList(), tool = DebugTools.curTool != null ? true : (bool?)null };
        }

        [Command("quicktest", Access.Dev, "start a throwaway dev quick-test colony (unsaved); waits until playable")]
        private static object QuickTest(Args a)
        {
            LongEventHandler.QueueLongEvent(() => { Root_Play.SetupForQuickTestPlay(); PageUtility.InitGameStart(); }, "GeneratingMap", true, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
            return new Pending(() => G.Playing && !LongEventHandler.AnyEventNowOrWaiting ? new { map = Find.CurrentMap.uniqueID } : null, Math.Max(a.Timeout, 300), () => "still generating");
        }

        [Command("screenshot", Access.Act, "save a game screenshot {name}; returns path once written")]
        private static object Screenshot(Args a)
        {
            string name = a.Str("name") ?? "agentbridge-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new CommandError("bad name");
            string path = Path.Combine(GenFilePaths.ScreenshotFolderPath, name + ".png");
            if (File.Exists(path)) throw new CommandError("exists: " + path);
            ScreenshotTaker.TakeNonSteamShot(name);
            return new Pending(() => File.Exists(path) && new FileInfo(path).Length > 0 ? new { path } : null, Math.Min(a.Timeout, 15), () => new CommandError("screenshot not written (renderer may not support capture)"));
        }
    }
}
