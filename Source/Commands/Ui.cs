using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace AgentBridge
{
    /// <summary>Everything a player does with the mouse: gizmos, targeting, float menus, dialogs, windows, letters.</summary>
    internal static class Ui
    {
        private static readonly FieldInfo FloatMenuOptions = typeof(FloatMenu).GetField("options", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly FieldInfo NodeField = typeof(Dialog_NodeTree).GetField("curNode", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly FieldInfo DiaText = typeof(DiaOption).GetField("text", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo DiaActivate = typeof(DiaOption).GetMethod("Activate", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly FieldInfo TargeterAction = typeof(Targeter).GetField("action", BindingFlags.NonPublic | BindingFlags.Instance)!;

        internal static List<FloatMenuOption> FloatOptions(FloatMenu menu) => (List<FloatMenuOption>)FloatMenuOptions.GetValue(menu);
        internal static DiaNode? NodeOf(Dialog_NodeTree d) => (DiaNode?)NodeField.GetValue(d);
        internal static List<string> NodeOptions(Dialog_NodeTree d) => NodeOf(d)?.options.Select(o => (string)DiaText.GetValue(o)).ToList() ?? new List<string>();

        internal static List<Gizmo> GizmosOf(ISelectable s) => s.GetGizmos().Where(g => g.Visible && !G.DevOnly(GizmoLabel(g))).ToList();

        internal static string GizmoLabel(Gizmo g)
        {
            if (g is Command_VerbTarget cv && string.IsNullOrEmpty(cv.LabelCap)) return cv.verb?.EquipmentSource?.LabelCap ?? cv.verb?.verbProps.label ?? "VerbTarget";
            if (g is Command c) return !string.IsNullOrEmpty(c.LabelCap) ? c.LabelCap : c.defaultLabel ?? c.GetType().Name;
            return g.GetType().Name;
        }

        /// <summary>Language-independent id: ability/verb def, hotkey def, or type + icon name.</summary>
        internal static string GizmoKey(Gizmo g)
        {
            switch (g)
            {
                case Command_Ability ca: return "ability:" + ca.Ability.def.defName;
                case Command_VerbTarget cv: return "verb:" + (cv.verb?.EquipmentSource?.def.defName ?? cv.verb?.GetType().Name);
                case Command c when c.hotKey != null: return "key:" + c.hotKey.defName;
                case Command c when c.icon != null && !string.IsNullOrEmpty(c.icon.name): return g.GetType().Name + ":" + c.icon.name;
                default: return g.GetType().Name;
            }
        }

        /// <summary>x,z or target=<ThingID> (the target's current cell — traders and animals keep moving).</summary>
        internal static IntVec3 MenuCell(Args a) => a.Has("target") ? G.Thing(a.Need("target")).Position : G.Cell(a);

        internal static List<FloatMenuOption> MenuOptions(Args a)
        {
            var pawns = G.OwnPawns(a, "pawns"); var c = MenuCell(a);
            return G.AtCell(c, () => FloatMenuMakerMap.GetOptions(pawns, c.ToVector3Shifted(), out _)).Where(o => !G.DevOnly(o.Label)).ToList();
        }

        [Command("select", Access.Act, "select things {ids:[...]} (empty clears)")]
        private static object Select(Args a)
        {
            var map = G.Map;
            Find.Selector.ClearSelection();
            foreach (var id in a.Strings("ids")) Find.Selector.Select(G.Thing(id), false, false);
            return Find.Selector.SelectedObjects.OfType<Thing>().Select(t => t.ThingID).ToList();
        }

        [Command("camera", Access.Act, "jump camera {x,z | id}")]
        private static object Camera(Args a)
        {
            if (a.Has("id")) CameraJumper.TryJump(G.Thing(a.Need("id"))); else Find.CameraDriver.JumpToCurrentMapLoc(G.Cell(a));
            return "ok";
        }

        [Command("gizmo", Access.Act, "press a gizmo {id, key|label|index, target?|x,z?}; if it starts targeting, pass the target or call 'target'")]
        private static object Gizmo(Args a)
        {
            var t = G.Selectable(a.Need("id"));
            var list = GizmosOf(t);
            Gizmo? g;
            if (a.Has("index")) g = list.ElementAtOrDefault(a.Int("index", -1));
            else if (a.Str("key") is string key) g = list.FirstOrDefault(x => GizmoKey(x).Equals(key, StringComparison.OrdinalIgnoreCase));
            else
            {
                string label = a.Str("label") ?? throw new CommandError("need key, label or index; see gizmos");
                g = list.FirstOrDefault(x => GizmoLabel(x).Equals(label, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault(x => GizmoLabel(x).Has(label));
            }
            if (g == null) throw new CommandError("no such gizmo; have: " + string.Join(" | ", list.Select(GizmoKey).ToArray()));
            if (g.Disabled) throw new CommandError("gizmo disabled: " + g.disabledReason);
            if (t is Thing th && !Find.Selector.IsSelected(th)) { Find.Selector.ClearSelection(); Find.Selector.Select(th, false, false); }
            if (t is RimWorld.Planet.WorldObject wo) { Find.WorldSelector.ClearSelection(); Find.WorldSelector.Select(wo, false); }
            g.ProcessInput(new Event());
            if (Find.Targeter.IsTargeting && (a.Has("target") || a.Has("x"))) return new { gizmo = GizmoKey(g), target = Target(a) };
            return new { gizmo = GizmoKey(g), targeting = Find.Targeter.IsTargeting ? true : (bool?)null, worldTargeting = Find.WorldTargeter.IsTargeting ? true : (bool?)null, windows = Find.WindowStack.Windows.Where(w => !(w is ImmediateWindow)).Select(w => w.GetType().Name).ToList() };
        }

        [Command("target", Access.Act, "complete active targeting {target:id | x,z}")]
        private static object Target(Args a)
        {
            var targeter = Find.Targeter;
            if (!targeter.IsTargeting) throw new CommandError("not targeting; press a targeting gizmo first");
            var target = G.Target(a);
            var action = (Action<LocalTargetInfo>?)TargeterAction.GetValue(targeter);
            try
            {
                G.AtCell(target.Cell, () =>
                {
                    if (targeter.targetingSource is ITargetingSource source)
                    {
                        if (!source.CanHitTarget(target) || !source.ValidateTarget(target, false)) throw new CommandError("invalid target for this ability/verb");
                        source.OrderForceTarget(target);
                    }
                    else if (action != null)
                    {
                        // What Targeter does before calling its action: target parameters, the optional validator, fog.
                        if (Bridge.Settings.fairPlay)
                        {
                            var parms = typeof(Targeter).GetField("targetParams", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(targeter) as TargetingParameters;
                            var validator = typeof(Targeter).GetField("targetValidator", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(targeter) as Func<LocalTargetInfo, bool>;
                            if (target.Cell.Fogged(G.Map) || (target.HasThing && !G.Seen(target.Thing)) || (parms != null && !parms.CanTarget(target.ToTargetInfo(G.Map))) || (validator != null && !validator(target)))
                                throw new CommandError("invalid target");
                        }
                        action(target);
                    }
                    else throw new CommandError("targeter has no action");
                    return 0;
                });
            }
            finally { targeter.StopTargeting(); }
            return new { target = Plain.Of(target) };
        }

        [Command("menu-choose", Access.Act, "choose a right-click option {pawns, x,z | target:id, label|index}")]
        private static object MenuChoose(Args a)
        {
            var options = MenuOptions(a);
            FloatMenuOption? option = a.Has("index") ? options.ElementAtOrDefault(a.Int("index", -1)) : options.FirstOrDefault(o => o.Label.Has(a.Need("label")));
            if (option == null) throw new CommandError("no option; have: " + string.Join(" | ", options.Select(o => o.Label).ToArray()));
            if (option.Disabled) throw new CommandError("option disabled: " + option.Label);
            G.AtCell(MenuCell(a), () => { option.Chosen(true, null); return 0; });
            return new { chosen = option.Label };
        }

        [Command("window", Access.Act, "act on a window {index?=top, button: A|B|C|accept|cancel|close|option, option?|label?}")]
        private static object Window(Args a)
        {
            var windows = Find.WindowStack.Windows.Where(w => !(w is ImmediateWindow)).ToList();
            var all = Find.WindowStack.Windows;
            var w = a.Has("index") ? all.ElementAtOrDefault(a.Int("index", -1)) : windows.LastOrDefault();
            if (w == null) throw new CommandError("no window");
            if (Bridge.Settings.fairPlay && w.IsDebug) throw new CommandError("fair-play mode: developer windows are off limits");
            string button = a.Str("button") ?? (a.Has("option") || a.Has("label") ? "option" : "close");
            if (button == "option")
            {
                if (w is Dialog_NodeTree) return Dialog(a);
                if (!(w is FloatMenu fm)) throw new CommandError("window has no options");
                var opts = FloatOptions(fm).Where(o => !G.DevOnly(o.Label)).ToList();
                var o = a.Has("label") ? opts.FirstOrDefault(x => x.Label.Has(a.Need("label"))) : opts.ElementAtOrDefault(a.Int("option", -1));
                if (o == null) throw new CommandError("no option; have: " + string.Join(" | ", opts.Select(x => x.Label).ToArray()));
                o.Chosen(false, fm); fm.Close(false);
                return new { chosen = o.Label };
            }
            if (w is Dialog_MessageBox m && (button == "A" || button == "B" || button == "C"))
            {
                var act = button == "A" ? m.buttonAAction : button == "B" ? m.buttonBAction : m.buttonCAction;
                act?.Invoke(); m.Close(false);
                return new { pressed = button };
            }
            if (button == "accept") w.OnAcceptKeyPressed();
            else if (button == "cancel") w.OnCancelKeyPressed();
            else w.Close(false);
            return new { window = w.GetType().Name, button };
        }

        [Command("dialog", Access.Act, "event/quest dialog (Dialog_NodeTree): no args shows text+options; {index|label} chooses")]
        private static object Dialog(Args a)
        {
            var w = Find.WindowStack.Windows.LastOrDefault(x => x is Dialog_NodeTree) as Dialog_NodeTree ?? throw new CommandError("no event dialog open");
            var node = NodeOf(w)!;
            var labels = NodeOptions(w);
            if (!a.Has("index") && !a.Has("label") && !a.Has("option")) return new { text = node.text.ToString(), options = labels };
            int idx = a.Has("label") ? labels.FindIndex(l => l.Has(a.Need("label"))) : a.Int(a.Has("index") ? "index" : "option", -1);
            var option = node.options.ElementAtOrDefault(idx) ?? throw new CommandError("no option; have: " + string.Join(" | ", labels.ToArray()));
            if (option.disabled) throw new CommandError("disabled: " + option.disabledReason);
            DiaActivate.Invoke(option, null);
            var next = Find.WindowStack.Windows.OfType<Dialog_NodeTree>().FirstOrDefault();
            return new { chosen = labels[idx], next = next == null ? null : NodeOf(next)?.text.ToString() };
        }

        [Command("letter", Access.Act, "open or dismiss a letter {index=last, action: open|dismiss}")]
        private static object Letter(Args a)
        {
            var letters = Find.LetterStack.LettersListForReading;
            var l = letters.ElementAtOrDefault(a.Int("index", letters.Count - 1)) ?? throw new CommandError("no letter");
            if (a.Str("action") == "dismiss") Find.LetterStack.RemoveLetter(l); else l.OpenLetter();
            return new { letter = l.Label.ToString() };
        }
    }
}
