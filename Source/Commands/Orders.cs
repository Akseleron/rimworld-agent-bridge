using System;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AgentBridge
{
    /// <summary>Normal player actions: time, pawn orders, colony management, save/load.</summary>
    internal static class Orders
    {
        private static object Speed(TimeSpeed s) { G.Ticks.CurTimeSpeed = s; return new { speed = s.ToString(), tick = G.Ticks.TicksGame }; }

        [Command("pause", Access.Act, "pause the game")]
        private static object Pause(Args a) => Speed(TimeSpeed.Paused);

        [Command("speed", Access.Act, "set speed {speed 0..4} (4 = ultrafast, needs Dev mode in vanilla)")]
        private static object SetSpeed(Args a) => Speed((TimeSpeed)Args.Clamp(a.Int("speed", 1), 0, 4));

        [Command("tick", Access.Act, "run n ticks then pause {n, speed=3, timeout=60}; stops early with stop=blocked-by-window or paused-externally")]
        private static object Tick(Args a)
        {
            var tm = G.Ticks;
            int n = Args.Clamp(a.Int("n", 60), 1, 60000 * 15), start = tm.TicksGame, target = start + n;
            Hooks.TickTarget = target;
            tm.CurTimeSpeed = (TimeSpeed)Args.Clamp(a.Int("speed", 3), 1, 4);
            int last = start; float stalledSince = Time.realtimeSinceStartup;
            object Done(string? stop)
            {
                Hooks.TickTarget = -1;
                tm.CurTimeSpeed = TimeSpeed.Paused;
                return new { tick = tm.TicksGame, ran = tm.TicksGame - start, stop, windows = stop == "blocked-by-window" ? Find.WindowStack.Windows.Where(w => w.forcePause).Select(w => w.GetType().Name).ToList() : null };
            }
            return new Pending(() =>
            {
                if (tm.TicksGame >= target) return Done(null);
                if (tm.TicksGame != last) { last = tm.TicksGame; stalledSince = Time.realtimeSinceStartup; return null; }
                if (tm.Paused && Hooks.TickTarget >= 0) return Done("paused-externally");
                return Time.realtimeSinceStartup - stalledSince > 1.5f && Find.WindowStack.WindowsForcePause ? Done("blocked-by-window") : null;
            }, a.Timeout, () => Done("timeout"));
        }

        [Command("draft", Access.Act, "draft or undraft {ids, on=true}")]
        private static object Draft(Args a)
        {
            bool on = a.Bool("on", true);
            return G.Pawns(a).Select(p =>
            {
                if (p.drafter == null) return (object)new { id = p.ThingID, error = "cannot be drafted" };
                p.drafter.Drafted = on;
                return new { id = p.ThingID, drafted = p.Drafted };
            }).ToList();
        }

        [Command("move", Access.Act, "go to a cell {ids, x,z} (forced order; draft first for combat positioning)")]
        private static object Move(Args a)
        {
            var c = G.Cell(a);
            return G.Pawns(a).Select(p =>
            {
                if (p.Downed || p.Dead) return (object)new { id = p.ThingID, error = p.Dead ? "dead" : "downed" };
                var dest = RCellFinder.BestOrderedGotoDestNear(c, p);
                if (!dest.IsValid) return (object)new { id = p.ThingID, error = "unreachable" };
                var job = JobMaker.MakeJob(JobDefOf.Goto, dest); job.playerForced = true;
                return new { id = p.ThingID, ok = p.jobs.TryTakeOrderedJob(job, JobTag.DraftedOrder), dest };
            }).ToList();
        }

        [Command("attack", Access.Act, "attack {ids, target:id | x,z, melee=false}")]
        private static object Attack(Args a)
        {
            var target = G.Target(a); bool melee = a.Bool("melee", false);
            return G.Pawns(a).Select(p =>
            {
                if (p.Downed || p.Dead) return (object)new { id = p.ThingID, error = p.Dead ? "dead" : "downed" };
                bool ranged = !melee && p.equipment?.Primary?.def.IsRangedWeapon == true;
                var job = JobMaker.MakeJob(ranged ? JobDefOf.AttackStatic : JobDefOf.AttackMelee, target); job.playerForced = true;
                return (object)new { id = p.ThingID, ok = p.jobs.TryTakeOrderedJob(job, JobTag.DraftedOrder), job = job.def.defName };
            }).ToList();
        }

        [Command("job-end", Access.Act, "interrupt current jobs {ids}")]
        private static object JobEnd(Args a) { foreach (var p in G.Pawns(a)) p.jobs.EndCurrentJob(JobCondition.InterruptForced); return "ok"; }

        [Command("forbid", Access.Act, "forbid or allow items {ids, on=true}")]
        private static object Forbid(Args a) { bool on = a.Bool("on", true); foreach (var id in a.Strings("ids")) G.Thing(id).SetForbidden(on, false); return on; }

        [Command("equip", Access.Act, "walk to and equip a weapon / wear apparel {id, target}")]
        private static object Equip(Args a)
        {
            var p = G.Pawn(a); var t = G.Thing(a.Need("target"));
            var job = JobMaker.MakeJob(t.def.IsApparel ? JobDefOf.Wear : JobDefOf.Equip, t); job.playerForced = true;
            return p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        [Command("drop", Access.Act, "drop the primary weapon or a worn apparel {id, target?}")]
        private static object? Drop(Args a)
        {
            var p = G.Pawn(a); string? target = a.Str("target");
            if (p.equipment?.Primary is ThingWithComps w && (target == null || target == w.ThingID)) { p.equipment.TryDropEquipment(w, out var d, p.Position); return d?.ThingID; }
            var ap = p.apparel?.WornApparel.FirstOrDefault(x => x.ThingID == target) ?? throw new CommandError("nothing to drop");
            p.apparel.TryDrop(ap, out var dropped);
            return dropped?.ThingID;
        }

        [Command("designate", Access.Act, "designate an area {designator: e.g. Mine|PlantsCut|Deconstruct|Hunt|Haul, x,z, x2?,z2?}; without designator lists them")]
        private static object Designate(Args a)
        {
            var all = DefDatabase<DesignationCategoryDef>.AllDefs.SelectMany(d => d.AllResolvedDesignators).ToList();
            string Names() => string.Join(" ", all.Select(d => d.GetType().Name.Replace("Designator_", "")).Distinct().ToArray());
            string want = a.Str("designator") ?? throw new CommandError("need designator; have: " + Names());
            var des = all.FirstOrDefault(d => d.GetType().Name.Equals("Designator_" + want, StringComparison.OrdinalIgnoreCase) || d.GetType().Name.Equals(want, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(d => d.GetType().Name.Has(want))
                ?? throw new CommandError("no designator " + want + "; have: " + Names());
            var c1 = G.Cell(a); var c2 = a.Has("x2") ? G.Cell(a, "x2", "z2") : c1;
            var rect = CellRect.FromLimits(c1, c2);
            var cells = rect.Cells.Where(c => des.CanDesignateCell(c).Accepted).ToList();
            if (cells.Count > 0) des.DesignateMultiCell(cells);
            return new { designator = des.GetType().Name, accepted = cells.Count, total = rect.Area };
        }

        [Command("build", Access.Act, "place a construction blueprint {def, x,z, rot=0..3, stuff?}")]
        internal static object Build(Args a)
        {
            var map = G.Map; var def = G.Def<ThingDef>(a.Str("def")); var c = G.Cell(a); var rot = new Rot4(a.Int("rot", 0));
            var stuff = a.Str("stuff") is string s ? G.Def<ThingDef>(s) : def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            var report = GenConstruct.CanPlaceBlueprintAt(def, c, rot, map, false, null, null, stuff);
            if (!report.Accepted) throw new CommandError("cannot place: " + report.Reason);
            return GenConstruct.PlaceBlueprintForBuild(def, c, map, rot, Faction.OfPlayer, stuff).ThingID;
        }

        [Command("zone", Access.Act, "create zone {type: stockpile|growing|dumping, x,z,x2,z2, plant?}")]
        private static object Zone(Args a)
        {
            var map = G.Map; var c1 = G.Cell(a); var c2 = a.Has("x2") ? G.Cell(a, "x2", "z2") : c1;
            Verse.Zone zone = (a.Str("type") ?? "stockpile") switch
            {
                "growing" => new Zone_Growing(map.zoneManager),
                "dumping" => new Zone_Stockpile(StorageSettingsPreset.DumpingStockpile, map.zoneManager),
                _ => new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager),
            };
            map.zoneManager.RegisterZone(zone);
            foreach (var cell in CellRect.FromLimits(c1, c2).Cells.Where(x => map.zoneManager.ZoneAt(x) == null && x.Standable(map))) zone.AddCell(cell);
            if (zone.Cells.Count == 0) { zone.Delete(); throw new CommandError("no free standable cells in that rectangle"); }
            if (zone is Zone_Growing g && a.Str("plant") is string plant) g.SetPlantDefToGrow(G.Def<ThingDef>(plant));
            return new { zone = zone.label, cells = zone.Cells.Count };
        }

        [Command("bills", Access.Act, "workbench bills {id, op: list|add|remove|clear, recipe?, count?, index?}")]
        private static object Bills(Args a)
        {
            var bench = G.Thing(a.Need("id")) as IBillGiver ?? throw new CommandError("not a workbench");
            var stack = bench.BillStack;
            switch (a.Str("op") ?? "list")
            {
                case "add":
                    var bill = G.Def<RecipeDef>(a.Str("recipe")).MakeNewBill();
                    if (bill is Bill_Production bp && a.Has("count")) { bp.repeatMode = BillRepeatModeDefOf.RepeatCount; bp.repeatCount = a.Int("count", 1); }
                    stack.AddBill(bill); break;
                case "remove": stack.Delete(stack.Bills[a.Int("index", 0)]); break;
                case "clear": stack.Clear(); break;
            }
            return new { bills = stack.Bills.Select(b => b.LabelCap.ToString()).ToList(), recipes = ((Thing)bench).def.AllRecipes.Where(r => r.AvailableNow).Select(r => r.defName).ToList() };
        }

        [Command("work", Access.Act, "work priorities {id, work?, priority 0..4?}; 1,2,4 switch on manual priorities")]
        private static object Work(Args a)
        {
            var p = G.Pawn(a);
            if (p.workSettings == null) throw new CommandError("pawn has no work settings");
            bool manualEnabled = false;
            if (a.Has("work"))
            {
                int priority = Args.Clamp(a.Int("priority", 3), 0, 4);
                if (priority != 0 && priority != 3 && !Find.PlaySettings.useWorkPriorities) { Find.PlaySettings.useWorkPriorities = true; manualEnabled = true; }
                if (!p.workSettings.EverWork) p.workSettings.EnableAndInitialize();
                p.workSettings.SetPriority(G.Def<WorkTypeDef>(a.Str("work")), priority);
            }
            return new { manual = Find.PlaySettings.useWorkPriorities, manualEnabled = manualEnabled ? true : (bool?)null, priorities = DefDatabase<WorkTypeDef>.AllDefs.ToDictionary(w => w.defName, w => p.WorkTypeIsDisabled(w) ? -1 : p.workSettings.GetPriority(w)) };
        }

        [Command("schedule", Access.Act, "timetable {id, hour?, assignment: Anything|Work|Joy|Sleep|Meditate}")]
        private static object Schedule(Args a)
        {
            var p = G.Pawn(a);
            if (p.timetable == null) throw new CommandError("pawn has no schedule");
            if (a.Has("hour")) p.timetable.SetAssignment(a.Int("hour", 0), G.Def<TimeAssignmentDef>(a.Str("assignment") ?? "Anything"));
            return string.Join(" ", Enumerable.Range(0, 24).Select(h => p.timetable.GetAssignment(h).defName.Substring(0, 2)).ToArray());
        }

        [Command("research-set", Access.Act, "set current research project {def}")]
        private static object ResearchSet(Args a) { Find.ResearchManager.SetCurrentProject(G.Def<ResearchProjectDef>(a.Str("def"))); return Find.ResearchManager.GetProject()?.defName ?? "none"; }

        [Command("quest-accept", Access.Act, "accept quest {id}")]
        private static object QuestAccept(Args a)
        {
            var q = Find.QuestManager.QuestsListForReading.FirstOrDefault(x => x.id == a.Int("id", -1)) ?? throw new CommandError("no quest");
            q.Accept(Find.Maps.SelectMany(m => m.mapPawns.FreeColonists).FirstOrDefault());
            return q.State.ToString();
        }

        [Command("save", Access.Act, "save game as a NEW file {name}; never overwrites")]
        private static object Save(Args a)
        {
            if (!G.Playing) throw new CommandError("no game to save");
            string name = a.Need("name");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new CommandError("bad file name");
            // Folder mods (RimSaves) prefix the physical name with "<folder>#§#"; match by suffix too.
            FileInfo? Match(DateTime after) => GenFilePaths.AllSavedGameFiles.FirstOrDefault(f => (Path.GetFileNameWithoutExtension(f.Name) == name || f.Name.EndsWith("#§#" + name + ".rws", StringComparison.Ordinal)) && f.LastWriteTimeUtc >= after);
            if (Match(DateTime.MinValue) is FileInfo existing) throw new CommandError("save exists (" + existing.Name + "); the bridge never overwrites");
            var started = DateTime.UtcNow.AddSeconds(-1);
            LongEventHandler.QueueLongEvent(() => GameDataSaveLoader.SaveGame(name), "SavingLongEvent", false, null);
            return new Pending(() => !LongEventHandler.AnyEventNowOrWaiting && Match(started) is FileInfo f ? new { saved = Bridge.LastSave = Path.GetFileNameWithoutExtension(f.Name) } : null,
                a.Timeout, () => new CommandError("save not confirmed within timeout"));
        }

        [Command("load", Access.Act, "load a save {name} (exact name from saves); waits until playable")]
        private static object Load(Args a)
        {
            string name = a.Need("name");
            if (!File.Exists(GenFilePaths.FilePathForSavedGame(name))) throw new CommandError("no save '" + name + "'; see saves");
            var before = Current.Game;
            GameDataSaveLoader.CheckVersionAndLoadGame(name);
            // Done only when a different Game is fully playing; the old one still reports Playing for a frame.
            return new Pending(() => Current.Game != null && !ReferenceEquals(Current.Game, before) && G.Playing && !LongEventHandler.AnyEventNowOrWaiting
                    ? new { loaded = Bridge.LastSave = name, tick = Find.TickManager.TicksGame } : null,
                a.Timeout, () => new { loading = name, done = false, windows = Observe.Windows(a) });
        }

        [Command("main-menu", Access.Act, "quit to main menu without saving")]
        private static object MainMenu(Args a) { GenScene.GoToMainMenu(); return "ok"; }

        [Command("quit", Access.Act, "quit RimWorld without saving {confirm:true}")]
        private static object Quit(Args a)
        {
            if (!a.Bool("confirm", false)) throw new CommandError("pass confirm=true; quits RimWorld without saving");
            LongEventHandler.ExecuteWhenFinished(Root.Shutdown);
            return "quitting";
        }
    }
}
