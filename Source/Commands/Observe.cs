using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace AgentBridge
{
    /// <summary>Read-only commands.</summary>
    internal static class Observe
    {
        [Command("help", Access.Read, "commands with access level and args {filter?, access?}; compact form: name -> help")]
        private static object Help(Args a)
        {
            string? f = a.Str("filter"), acc = a.Str("access");
            return Registry.Commands.Values.Where(c => (f == null || c.Name.Has(f) || c.Help.Has(f)) && (acc == null || c.Access.ToString().Equals(acc, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => c.Access).ThenBy(c => c.Name)
                .ToDictionary(c => c.Name, c => "[" + c.Access + (c.Source == "core" ? "" : " " + c.Source) + "] " + c.Help);
        }

        [Command("capabilities", Access.Read, "bridge version, runtime, Harmony features, settings gates, extension commands")]
        private static object Capabilities(Args a) => new
        {
            version = Bridge.Version, protocol = Bridge.Protocol, runtime = Bridge.RuntimeName, os = Environment.OSVersion.Platform.ToString(),
            game = VersionControl.CurrentVersionStringWithRev, language = LanguageDatabase.activeLanguage?.folderName,
            harmony = Bridge.HarmonyActive, behaviourPump = PumpBehaviour.Frames > 0, cellTargeting = Bridge.HarmonyActive, exactTicks = Bridge.HarmonyActive, eventHooks = Bridge.HarmonyActive,
            settings = new { Bridge.Settings.readOnly, Bridge.Settings.allowDev, Bridge.Settings.allowPower, Bridge.Settings.requireDevMode, devMode = Prefs.DevMode },
            extensions = Registry.Commands.Values.Where(c => c.Source != "core").Select(c => c.Source + ":" + c.Name).ToList(),
            integrations = Integrations.Detected(),
        };

        [Command("state", Access.Read, "program state, tick, speed, map, selection, windows, targeting")]
        internal static object State(Args a)
        {
            var game = Current.Game;
            bool playing = G.Playing;
            return new
            {
                programState = Current.ProgramState.ToString(),
                loading = LongEventHandler.AnyEventNowOrWaiting ? true : (bool?)null,
                devMode = Prefs.DevMode,
                tick = game?.tickManager?.TicksGame,
                speed = game?.tickManager?.CurTimeSpeed.ToString(),
                date = playing ? GenDate.DateFullStringAt(GenTicks.TicksAbs, Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile)) : null,
                map = playing ? Find.CurrentMap.uniqueID : (int?)null,
                maps = game?.Maps.Select(m => new { id = m.uniqueID, size = new[] { m.Size.x, m.Size.z }, home = m.IsPlayerHome }).ToList(),
                selected = playing ? Find.Selector.SelectedObjects.OfType<Thing>().Select(t => t.ThingID).ToList() : null,
                targeting = playing && Find.Targeter.IsTargeting ? true : (bool?)null,
                forcePause = Find.WindowStack?.WindowsForcePause == true ? true : (bool?)null,
                debugTool = DebugTools.curTool != null ? true : (bool?)null,
                windows = Find.WindowStack?.Windows.Select(w => w.GetType().Name).Where(n => n != "ImmediateWindow").ToList(),
                save = Bridge.LastSave,
            };
        }

        [Command("snapshot", Access.Read, "one-call overview {after?}: state, map, colonists, hostiles, alerts, new feed events, windows")]
        private static object Snapshot(Args a)
        {
            bool playing = G.Playing;
            return new
            {
                state = State(a),
                map = playing ? MapInfo(a) : null,
                colonists = playing ? Find.CurrentMap.mapPawns.FreeColonistsSpawned.Select(G.PawnBrief).ToList() : null,
                hostiles = playing ? Find.CurrentMap.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer) && !p.Dead).Take(50).Select(G.PawnBrief).ToList() : null,
                alerts = playing ? Alerts(a) : null,
                feed = Feed.Since(a.Long("after", Feed.Last), null, a.Int("limit", 50)),
                windows = Windows(a),
            };
        }

        [Command("map", Access.Read, "map summary: size, biome, weather, temperature, season, hostiles, wealth")]
        private static object MapInfo(Args a)
        {
            var map = G.Map;
            return new
            {
                id = map.uniqueID, size = new[] { map.Size.x, map.Size.z }, biome = map.Biome.defName, weather = map.weatherManager.curWeather.defName,
                outdoorTemp = map.mapTemperature.OutdoorTemp, season = GenLocalDate.Season(map).ToString(), hour = GenLocalDate.HourOfDay(map),
                colonists = map.mapPawns.FreeColonistsSpawnedCount, wealth = map.wealthWatcher.WealthTotal,
                hostiles = map.mapPawns.AllPawnsSpawned.Count(p => p.HostileTo(Faction.OfPlayer) && !p.Dead && !p.Downed),
                danger = map.dangerWatcher.DangerRating.ToString(),
                conditions = map.gameConditionManager.ActiveConditions.Select(c => c.def.defName).ToList(),
            };
        }

        [Command("pawns", Access.Read, "pawns {filter: colonists|player|prisoners|hostile|animals|all, near:[x,z], radius=10, limit=300}; false flags omitted")]
        private static object Pawns(Args a)
        {
            var map = G.Map;
            IEnumerable<Pawn> list = (a.Str("filter") ?? "colonists") switch
            {
                "colonists" => map.mapPawns.FreeColonistsSpawned,
                "player" => map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer),
                "prisoners" => map.mapPawns.PrisonersOfColonySpawned,
                "hostile" => map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer)),
                "animals" => map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.Animal),
                _ => map.mapPawns.AllPawnsSpawned,
            };
            if (a.Pair("near") is IntVec3 c) { float r = a.Float("radius", 10); list = list.Where(p => p.Position.InHorDistOf(c, r)); }
            return list.Take(a.Int("limit", 300)).Select(G.PawnBrief).ToList();
        }

        [Command("pawn", Access.Read, "pawn detail {id}: health, capacities, needs, skills, gear, queue, mental state")]
        private static object Pawn(Args a)
        {
            var p = G.Pawn(a);
            return new
            {
                brief = G.PawnBrief(p),
                hediffs = p.health.hediffSet.hediffs.Select(h => new { def = h.def.defName, label = h.LabelCap, part = h.Part?.Label, severity = h.Severity, bleed = h.BleedRate > 0 ? h.BleedRate : (float?)null }).ToList(),
                capacities = DefDatabase<PawnCapacityDef>.AllDefs.Where(d => d.showOnHumanlikes || !p.RaceProps.Humanlike).ToDictionary(d => d.defName, d => p.health.capacities.GetLevel(d)),
                needs = p.needs?.AllNeeds.ToDictionary(n => n.def.defName, n => n.CurLevelPercentage),
                skills = p.skills?.skills.ToDictionary(s => s.def.defName, s => s.TotallyDisabled ? -1 : s.Level),
                traits = p.story?.traits?.allTraits.Select(t => t.def.defName).ToList(),
                apparel = p.apparel?.WornApparel.Select(t => t.ThingID).ToList(),
                inventory = p.inventory?.innerContainer.Select(t => new { id = t.ThingID, def = t.def.defName, count = t.stackCount }).ToList(),
                queue = p.jobs?.jobQueue.Select(j => j.job.def.defName).ToList(),
                stance = p.stances?.curStance?.GetType().Name,
                mentalState = p.MentalStateDef?.defName,
                incapable = p.story != null ? p.CombinedDisabledWorkTags.ToString() : null,
            };
        }

        [Command("things", Access.Read, "things {def?, category?: Item|Building|Plant|..., near:[x,z], radius=8, limit=200}")]
        private static object Things(Args a)
        {
            var map = G.Map; string? def = a.Str("def"), cat = a.Str("category");
            IEnumerable<Thing> list = map.listerThings.AllThings;
            if (def != null) list = list.Where(t => t.def.defName.Equals(def, StringComparison.OrdinalIgnoreCase));
            if (cat != null) list = list.Where(t => t.def.category.ToString().Equals(cat, StringComparison.OrdinalIgnoreCase));
            if (a.Pair("near") is IntVec3 c) { float r = a.Float("radius", 8); list = list.Where(t => t.Spawned && t.Position.InHorDistOf(c, r)); }
            return list.Take(a.Int("limit", 200)).Select(G.ThingBrief).ToList();
        }

        [Command("cell", Access.Read, "cell detail {x,z}: terrain, roof, snow, temperature, zone, room, things")]
        private static object Cell(Args a)
        {
            var map = G.Map; var c = G.Cell(a);
            return new
            {
                pos = c, terrain = c.GetTerrain(map)?.defName, roof = c.GetRoof(map)?.defName, fogged = c.Fogged(map) ? true : (bool?)null,
                snow = map.snowGrid.GetDepth(c), temperature = c.GetTemperature(map), walkable = c.Walkable(map), standable = c.Standable(map),
                zone = map.zoneManager.ZoneAt(c)?.label, room = c.GetRoom(map)?.Role?.defName,
                things = c.GetThingList(map).Select(G.ThingBrief).ToList(),
            };
        }

        [Command("area", Access.Read, "text map {x,z,radius<=40}: one char per cell, north row first; see legend")]
        private static object Area(Args a)
        {
            var map = G.Map; var c = G.Cell(a); int r = Args.Clamp(a.Int("radius", 15), 1, 40);
            var rows = new List<string>();
            var sb = new StringBuilder();
            for (int z = c.z + r; z >= c.z - r; z--)
            {
                sb.Length = 0;
                for (int x = c.x - r; x <= c.x + r; x++) sb.Append(Glyph(map, new IntVec3(x, 0, z), x == c.x && z == c.z));
                rows.Add(sb.ToString());
            }
            return new { origin = new[] { c.x - r, c.z + r }, legend = "row0=north(z=origin1) col0=x=origin0. C colonist H hostile a animal p pawn W wall # rock D door b building i item T tree ~ water , roofed . open ? fog + center", rows };
        }

        private static char Glyph(Map map, IntVec3 cell, bool center)
        {
            if (!cell.InBounds(map)) return ' ';
            if (cell.Fogged(map)) return '?';
            var things = cell.GetThingList(map);
            foreach (var t in things) if (t is Pawn pawn) return pawn.IsColonist ? 'C' : pawn.HostileTo(Faction.OfPlayer) ? 'H' : pawn.RaceProps.Animal ? 'a' : 'p';
            char ch = '.';
            foreach (var t in things)
            {
                if (t.def.IsDoor) return 'D';
                if (t.def.IsEdifice() && t.def.passability == Traversability.Impassable) return t.def.mineable ? '#' : 'W';
                if (t.def.category == ThingCategory.Building) ch = 'b';
                else if (t.def.category == ThingCategory.Item && ch == '.') ch = 'i';
                else if (t.def.category == ThingCategory.Plant && t.def.plant != null && t.def.plant.IsTree && ch == '.') ch = 'T';
            }
            if (ch != '.') return ch;
            if (!cell.Walkable(map)) return '~';
            return center ? '+' : cell.Roofed(map) ? ',' : '.';
        }

        [Command("alerts", Access.Read, "active alerts")]
        private static object Alerts(Args a)
        {
            if (!(Find.UIRoot is UIRoot_Play ui)) throw new CommandError("not in play");
            var active = (IEnumerable<Alert>)typeof(AlertsReadout).GetField("activeAlerts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ui.alerts);
            return active.Select(al => new { type = al.GetType().Name, label = al.Label, priority = al.Priority.ToString(), explanation = a.Bool("explain", false) ? al.GetExplanation().ToString() : null }).ToList();
        }

        [Command("letters", Access.Read, "letter stack {text=false}")]
        private static object Letters(Args a) => (G.Playing ? Find.LetterStack.LettersListForReading : new List<Letter>())
            .Select((l, i) => new { index = i, label = l.Label.ToString(), def = l.def.defName, text = a.Bool("text", false) ? (l as ChoiceLetter)?.Text.ToString() : null }).ToList();

        [Command("feed", Access.Read, "events after seq {after=0, kind?: log|message|letter|incident|death, limit=200}")]
        private static object FeedCmd(Args a) => Feed.Since(a.Long("after", 0), a.Str("kind"), a.Int("limit", 200));

        [Command("windows", Access.Read, "open windows with dialog text, buttons and options")]
        internal static object Windows(Args a) => (Find.WindowStack?.Windows ?? new List<Window>()).Select((w, i) => new { w, i }).Where(x => !(x.w is ImmediateWindow)).Select(x => new
        {
            index = x.i, type = x.w.GetType().Name, title = x.w.optionalTitle, forcePause = x.w.forcePause ? true : (bool?)null,
            text = (x.w as Dialog_MessageBox)?.text.ToString() ?? (x.w is Dialog_NodeTree nt ? Ui.NodeOf(nt)?.text.ToString() : null),
            buttons = x.w is Dialog_MessageBox m ? new[] { m.buttonAText, m.buttonBText, m.buttonCText }.Where(b => b != null).ToList() : null,
            options = x.w is FloatMenu fm ? Ui.FloatOptions(fm).Select(o => o.Label).ToList() : x.w is Dialog_NodeTree n2 ? Ui.NodeOptions(n2) : null,
        }).ToList();

        [Command("gizmos", Access.Read, "command buttons of a thing {id}; use 'key' (language-independent) with gizmo")]
        private static object Gizmos(Args a)
            => Ui.GizmosOf(G.Thing(a.Need("id"))).Select((g, i) => new { index = i, key = Ui.GizmoKey(g), label = Ui.GizmoLabel(g), disabled = g.Disabled ? g.disabledReason ?? "disabled" : null }).ToList();

        [Command("menu", Access.Read, "right-click options for pawns at a cell {pawns:[ids], x,z}")]
        private static object Menu(Args a) => Ui.MenuOptions(a).Select((o, i) => new { index = i, label = o.Label, disabled = o.Disabled ? true : (bool?)null }).ToList();

        [Command("debug-list", Access.Read, "debug actions {filter?, path?, limit=300}: children of path or search all")]
        private static object DebugList(Args a)
        {
            Dialog_Debug.TrySetupNodeGraph();
            string? path = a.Str("path"), filter = a.Str("filter");
            var root = path == null ? Dialog_Debug.rootNode : Dialog_Debug.GetNode(path) ?? throw new CommandError("no debug node " + path);
            root.TrySetupChildren();
            IEnumerable<DebugActionNode> nodes = path != null && filter == null ? root.children : Walk(root, 6);
            if (filter != null) nodes = nodes.Where(n => n.Path.Has(filter));
            return nodes.Take(a.Int("limit", 300)).Select(n => new { path = n.Path, type = n.actionType == DebugActionType.Action ? null : n.actionType.ToString(), submenu = n.childGetter != null || n.children.Count > 0 ? true : (bool?)null, on = n.settingsField != null ? n.On : (bool?)null }).ToList();
        }

        private static IEnumerable<DebugActionNode> Walk(DebugActionNode node, int depth)
        {
            foreach (var child in node.children)
            {
                yield return child;
                if (depth > 0 && child.childGetter == null) foreach (var sub in Walk(child, depth - 1)) yield return sub;
            }
        }

        [Command("saves", Access.Read, "save files {filter?, limit=100}, newest first")]
        private static object Saves(Args a)
        {
            string? f = a.Str("filter");
            return GenFilePaths.AllSavedGameFiles.Where(x => f == null || x.Name.Has(f)).OrderByDescending(x => x.LastWriteTimeUtc)
                .Select(x => new { name = System.IO.Path.GetFileNameWithoutExtension(x.Name), modified = x.LastWriteTime.ToString("s"), mb = Math.Round(x.Length / 1048576.0, 1) }).Take(a.Int("limit", 100)).ToList();
        }

        [Command("defs", Access.Read, "search defs {type=ThingDef, filter, limit=100}")]
        private static object Defs(Args a)
        {
            string f = a.Str("filter") ?? "";
            var all = (IEnumerable<Def>)GenGeneric.GetStaticPropertyOnGenericType(typeof(DefDatabase<>), G.DefType(a.Str("type") ?? "ThingDef"), "AllDefs");
            return all.Where(d => d.defName.Has(f) || (d.label ?? "").Has(f)).Take(a.Int("limit", 100)).Select(d => new { d.defName, d.label, mod = d.modContentPack?.PackageId }).ToList();
        }

        [Command("def", Access.Read, "dump a def's fields {type=ThingDef, name, depth=1}")]
        private static object? Def(Args a) => Plain.Dump(G.AnyDef(a.Str("type") ?? "ThingDef", a.Need("name")), a.Int("depth", 1));

        [Command("inspect", Access.Read, "inspect string, description and comps of a thing {id}")]
        private static object Inspect(Args a)
        {
            var t = G.Thing(a.Need("id"));
            return new
            {
                brief = G.ThingBrief(t), inspect = t.GetInspectString(), desc = t.DescriptionFlavor,
                comps = (t as ThingWithComps)?.AllComps.Select(c => new { type = c.GetType().Name, inspect = c.CompInspectStringExtra() }).ToList(),
            };
        }

        [Command("resources", Access.Read, "counted colony resources (defName -> count)")]
        private static object Resources(Args a) => G.Map.resourceCounter.AllCountedAmounts.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key.defName, kv => kv.Value);

        [Command("factions", Access.Read, "factions with goodwill and hostility")]
        private static object Factions(Args a) => Find.FactionManager.AllFactionsListForReading.Select(f => new { id = f.loadID, name = f.Name, def = f.def.defName, goodwill = f.IsPlayer ? (int?)null : f.PlayerGoodwill, hostile = !f.IsPlayer && f.HostileTo(Faction.OfPlayer) ? true : (bool?)null, defeated = f.defeated ? true : (bool?)null }).ToList();

        [Command("quests", Access.Read, "available and ongoing quests")]
        private static object Quests(Args a) => Find.QuestManager.QuestsListForReading.Where(q => q.State == QuestState.NotYetAccepted || q.State == QuestState.Ongoing)
            .Select(q => new { id = q.id, name = q.name, state = q.State.ToString(), desc = a.Bool("text", false) ? q.description.ToString() : null }).ToList();

        [Command("world", Access.Read, "world tile info {tile?}, player caravans and settlements")]
        private static object World(Args a)
        {
            int tile = a.Int("tile", Find.CurrentMap?.Tile ?? 0);
            return new
            {
                tile, biome = Find.WorldGrid[tile].PrimaryBiome?.defName, temperature = Find.World.tileTemperatures.GetOutdoorTemp(tile),
                caravans = Find.WorldObjects.Caravans.Where(c => c.IsPlayerControlled).Select(c => new { id = c.ID, name = c.Name, tile = (int)c.Tile, pawns = c.PawnsListForReading.Select(p => p.ThingID).ToList() }).ToList(),
                settlements = Find.WorldObjects.Settlements.Where(s => s.Faction == Faction.OfPlayer).Select(s => new { id = s.ID, name = s.Name, tile = (int)s.Tile }).ToList(),
            };
        }

        [Command("research", Access.Read, "current research and available projects {list=false}")]
        private static object Research(Args a)
        {
            var rm = Find.ResearchManager; var cur = rm.GetProject();
            return new
            {
                current = cur?.defName, progress = cur?.ProgressPercent,
                available = a.Bool("list", false) ? DefDatabase<ResearchProjectDef>.AllDefs.Where(d => !d.IsFinished && d.CanStartNow).Select(d => new { d.defName, cost = d.CostApparent }).ToList() : null,
            };
        }

        [Command("perf", Access.Read, "fps, tick rate multiplier, managed memory")]
        private static object Perf(Args a) => new { fps = 1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f), tickMultiplier = Current.Game?.tickManager?.TickRateMultiplier, memoryMb = GC.GetTotalMemory(false) / 1048576, frame = Time.frameCount };

        [Command("mods", Access.Read, "running mods in load order with assembly SHA256 {sha=false, filter?}")]
        private static object Mods(Args a)
        {
            bool sha = a.Bool("sha", false); string? f = a.Str("filter");
            return LoadedModManager.RunningModsListForReading.Select((m, i) => new { m, i }).Where(x => f == null || x.m.PackageId.Has(f) || x.m.Name.Has(f)).Select(x => new
            {
                order = x.i, id = x.m.PackageId, name = x.m.Name,
                assemblies = sha ? x.m.assemblies.loadedAssemblies.Select(asm => new { name = asm.GetName().Name, sha256 = Sha(asm) }).ToList() : null,
            }).ToList();
        }

        private static string? Sha(Assembly asm)
        {
            try
            {
                if (!System.IO.File.Exists(asm.Location)) return null;
                using (var s = System.Security.Cryptography.SHA256.Create()) return string.Concat(s.ComputeHash(System.IO.File.ReadAllBytes(asm.Location)).Select(b => b.ToString("x2")));
            }
            catch { return null; }
        }
    }
}
