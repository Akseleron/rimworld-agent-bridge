using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace AgentBridge
{
    /// <summary>New colony through the real main-menu pages (scenario → storyteller → world → ideoligion → landing
    /// site → colonists). Each page keeps its own validation; the agent only fills choices and presses Next.</summary>
    internal static class NewGame
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Page? Top => Find.WindowStack.Windows.OfType<Page>().LastOrDefault();
        private static object? F(object o, string name) => o.GetType().GetField(name, Inst)?.GetValue(o);
        private static void SetF(object o, string name, object? v) => (o.GetType().GetField(name, Inst) ?? throw new CommandError("field " + name + " missing on " + o.GetType().Name)).SetValue(o, v);
        // Page_CreateWorldParams.CanDoNext starts world generation as a side effect, so never call it just to look.
        private static bool? SafeCanNext(Page p) => p is Page_CreateWorldParams ? (bool?)null : CanNext(p);
        private static bool CanNext(Page p) => (bool)p.GetType().GetMethod("CanDoNext", Inst, null, Type.EmptyTypes, null)!.Invoke(p, null);

        /// <summary>Presses Next like the button: CanDoNext then DoNext. The world page does its work inside
        /// CanDoNext (generation, then it opens the next page itself) and reports false.</summary>
        private static bool Press(Page p)
        {
            bool ok = CanNext(p);
            if (p is Page_CreateWorldParams) return true;
            if (ok) DoNext(p);
            return ok;
        }

        /// <summary>Language-independent scenario id: the ScenarioDef defName for built-in ones, else the file name.</summary>
        private static string ScenarioId(Scenario s) => DefDatabase<ScenarioDef>.AllDefs.FirstOrDefault(d => d.scenario == s)?.defName ?? s.fileName ?? s.name;
        private static void DoNext(Page p) => p.GetType().GetMethod("DoNext", Inst, null, Type.EmptyTypes, null)!.Invoke(p, null);

        private static object Describe(Page p)
        {
            object? options = p switch
            {
                Page_SelectScenario _ => ScenarioLister.AllScenarios().Where(s => s.showInUI).Select(s => ScenarioId(s) + " — " + s.name).ToList(),
                Page_SelectStoryteller _ => new { storytellers = DefDatabase<StorytellerDef>.AllDefs.Where(d => d.listVisible).Select(d => d.defName).ToList(), difficulties = DefDatabase<DifficultyDef>.AllDefs.Select(d => d.defName).ToList() },
                Page_CreateWorldParams _ => new { seed = F(p, "seedString"), coverage = F(p, "planetCoverage") },
                Page_SelectStartingSite _ => new { selectedTile = (int)Find.WorldInterface.SelectedTile, hint = "page-set site=random or tile=N" },
                Page_ConfigureStartingPawns _ => Find.GameInitData.startingAndOptionalPawns.Take(Find.GameInitData.startingPawnCount).Select(x => new { id = x.ThingID, name = x.LabelShortCap, age = x.ageTracker.AgeBiologicalYears, incapable = x.CombinedDisabledWorkTags.ToString(), skills = x.skills.skills.Where(s => s.Level >= 6).ToDictionary(s => s.def.defName, s => s.Level) }).ToList(),
                _ => null,
            };
            return new { page = p.GetType().Name, title = p.PageTitle, canNext = SafeCanNext(p), options };
        }

        [Command("page", Access.Read, "current new-game page with its choices (open one with newgame-open)")]
        private static object PageCmd(Args a) => Top is Page p ? Describe(p) : throw new CommandError("no setup page open");

        [Command("newgame-open", Access.Act, "open the New colony pages from the main menu")]
        private static object Open(Args a)
        {
            if (Current.ProgramState != ProgramState.Entry) throw new CommandError("only from the main menu (main-menu first)");
            Find.WindowStack.Add(new Page_SelectScenario());
            return Describe(Top!);
        }

        [Command("page-set", Access.Act, "fill the current page {scenario | storyteller,difficulty | seed,coverage | ideo: classic|preset | site: random | tile | reroll: pawnIndex}")]
        internal static object PageSet(Args a)
        {
            var p = Top ?? throw new CommandError("no setup page open");
            switch (p)
            {
                case Page_SelectScenario _ when a.Str("scenario") is string sc:
                    SetF(p, "curScen", ScenarioLister.AllScenarios().FirstOrDefault(s => ScenarioId(s).Equals(sc, StringComparison.OrdinalIgnoreCase))
                        ?? ScenarioLister.AllScenarios().FirstOrDefault(s => s.name.Equals(sc, StringComparison.OrdinalIgnoreCase) || s.name.Has(sc)) ?? throw new CommandError("no scenario " + sc + "; see page"));
                    break;
                case Page_SelectStoryteller _:
                    if (a.Str("storyteller") is string st) SetF(p, "storyteller", G.Def<StorytellerDef>(st));
                    if (a.Str("difficulty") is string df) { var d = G.Def<DifficultyDef>(df); SetF(p, "difficulty", d); ((Difficulty)F(p, "difficultyValues")!).CopyFrom(d); }
                    break;
                case Page_CreateWorldParams _:
                    if (a.Str("seed") is string seed) SetF(p, "seedString", seed);
                    if (a.Has("coverage")) SetF(p, "planetCoverage", a.Float("coverage", 0.3f));
                    break;
                case Page_SelectStartingSite _:
                    PlanetTile tile = a.Str("site") == "random" || !a.Has("tile") ? TileFinder.RandomStartingTile() : new PlanetTile(a.Int("tile", 0), Find.WorldGrid.Surface);
                    Find.WorldInterface.SelectedTile = tile;
                    break;
                case Page_ConfigureStartingPawns _ when a.Has("reroll"):
                    var pawn = Find.GameInitData.startingAndOptionalPawns[a.Int("reroll", 0)];
                    StartingPawnUtility.RandomizePawn(Find.GameInitData.startingAndOptionalPawns.IndexOf(pawn));
                    break;
                default:
                    if (p.GetType().Name == "Page_ChooseIdeoPreset")
                    {
                        var field = p.GetType().GetField("presetSelection", Inst)!;
                        field.SetValue(p, Enum.Parse(field.FieldType, a.Str("ideo") == "preset" ? "Preset" : "Classic"));
                        break;
                    }
                    throw new CommandError("nothing to set on " + p.GetType().Name + " with these args");
            }
            return Describe(p);
        }

        [Command("page-next", Access.Act, "press Next on the current page; waits for world generation and the landing")]
        internal static object PageNext(Args a)
        {
            var p = Top ?? throw new CommandError("no setup page open");
            if (!Press(p)) throw new CommandError("Next is not allowed yet on " + p.GetType().Name + " (fill it with page-set)");
            return new Pending(() =>
            {
                if (LongEventHandler.AnyEventNowOrWaiting) return null;
                if (G.Playing) return new { started = true, map = Find.CurrentMap.uniqueID };
                var w = Find.WindowStack.Windows.OfType<Dialog_MessageBox>().LastOrDefault();
                if (w != null) return new { confirm = w.text.ToString(), hint = "window button=A to accept, B to go back" };
                return Top is Page next && next != p ? Describe(next) : null;
            }, Math.Max(a.Timeout, 300), () => new { waiting = true });
        }

        [Command("newgame", Access.Act, "start a colony through all setup pages {scenario?, storyteller?, difficulty?, seed?, coverage?, ideo=classic, timeout=600}; waits until playable")]
        private static object Start(Args a)
        {
            if (Current.ProgramState != ProgramState.Entry) throw new CommandError("only from the main menu (main-menu first)");
            if (!(Top is Page)) Find.WindowStack.Add(new Page_SelectScenario());
            var log = new List<string>();
            Page? pressed = null;
            int stall = 0;
            return new Pending(() =>
            {
                if (LongEventHandler.AnyEventNowOrWaiting) return null;
                if (G.Playing) return new { started = true, map = Find.CurrentMap.uniqueID, steps = log };
                if (Find.WindowStack.Windows.OfType<Dialog_MessageBox>().LastOrDefault() is Dialog_MessageBox box)
                {
                    // Warnings such as "no one can cook": accept, as a player clicking through would, and report it.
                    log.Add("confirmed: " + box.text.ToString().Split('\n')[0]);
                    (box.buttonAAction ?? box.acceptAction)?.Invoke(); box.Close(false);
                    return null;
                }
                var p = Top;
                if (p == null) return ++stall > 600 ? new { started = false, steps = log } : null;
                if (p == pressed) return ++stall > 600 ? new { started = false, stuck = p.GetType().Name, steps = log } : null;
                stall = 0;
                try
                {
                    var args = a;
                    if (p is Page_SelectStartingSite) args = a.With("site", "random");
                    if (p.GetType().Name == "Page_ChooseIdeoPreset") args = a.With("ideo", a.Str("ideo") ?? "classic");
                    if (p is Page_SelectScenario && !a.Has("scenario") || p is Page_ConfigureStartingPawns) { }
                    else try { PageSet(args); } catch (CommandError) { }
                    log.Add(p.GetType().Name);
                    pressed = p;
                    if (!Press(p)) return new { started = false, stuck = p.GetType().Name, steps = log };
                }
                catch (Exception e) { return new CommandError("newgame failed on " + p.GetType().Name + ": " + (e.InnerException ?? e).Message); }
                return null;
            }, Math.Max(a.Timeout, 600), () => new { started = false, timeout = true, steps = log });
        }
    }
}
