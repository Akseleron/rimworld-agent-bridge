using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AgentBridge
{
    /// <summary>The only type that references Harmony. Patches are applied by code (no [HarmonyPatch] attributes),
    /// so the rest of the assembly loads even when 0Harmony is absent. Each patch is independent: a method
    /// renamed by a game update disables one feature, not the bridge.</summary>
    internal static class HarmonyPatches
    {
        internal static void Apply()
        {
            var h = new Harmony("akseleron.agentbridge");
            void Patch(Type type, string method, Type[]? args, string? prefix = null, string? postfix = null)
            {
                try
                {
                    var original = args == null ? AccessTools.Method(type, method) : AccessTools.Method(type, method, args);
                    if (original == null) { Log.Warning("[AgentBridge] patch target missing: " + type.Name + "." + method); return; }
                    h.Patch(original,
                        prefix: prefix == null ? null : new HarmonyMethod(typeof(Hooks), prefix),
                        postfix: postfix == null ? null : new HarmonyMethod(typeof(Hooks), postfix));
                }
                catch (Exception e) { Log.Warning("[AgentBridge] patch " + type.Name + "." + method + " failed: " + e.Message); }
            }
            Patch(typeof(Root), nameof(Root.Update), null, postfix: nameof(Hooks.Pump));
            // Overrides call base first and then draw everything else, so patch the overrides to draw on top.
            Patch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI), null, postfix: nameof(Hooks.Overlay));
            Patch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.UIRootOnGUI), null, postfix: nameof(Hooks.Overlay));
            Patch(typeof(UI), nameof(UI.MouseCell), null, prefix: nameof(Hooks.MouseCell));
            Patch(typeof(UI), nameof(UI.MouseMapPosition), null, prefix: nameof(Hooks.MouseMapPosition));
            Patch(typeof(LogMessageQueue), nameof(LogMessageQueue.Enqueue), new[] { typeof(LogMessage) }, postfix: nameof(Hooks.LogMessage));
            Patch(typeof(Messages), nameof(Messages.Message), new[] { typeof(Message), typeof(bool) }, postfix: nameof(Hooks.Message));
            Patch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), new[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) }, postfix: nameof(Hooks.Letter));
            Patch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute), null, postfix: nameof(Hooks.Incident));
            Patch(typeof(Pawn), nameof(Pawn.Kill), null, postfix: nameof(Hooks.Death));
            Patch(typeof(TickManager), nameof(TickManager.DoSingleTick), null, postfix: nameof(Hooks.TickStop));
        }

        internal static object PatchInfo(string? owner, string? spec)
        {
            if (spec != null)
            {
                int colon = spec.LastIndexOf(':');
                var type = AccessTools.TypeByName(spec.Substring(0, colon)) ?? throw new CommandError("no type");
                return AccessTools.GetDeclaredMethods(type).Where(m => m.Name == spec.Substring(colon + 1)).Select(m => Harmony.GetPatchInfo(m) is Patches info ? new
                {
                    method = m.ToString(),
                    prefixes = info.Prefixes.Select(p => p.owner + " " + p.PatchMethod.DeclaringType?.FullName).ToList(),
                    postfixes = info.Postfixes.Select(p => p.owner + " " + p.PatchMethod.DeclaringType?.FullName).ToList(),
                    transpilers = info.Transpilers.Select(p => p.owner + " " + p.PatchMethod.DeclaringType?.FullName).ToList(),
                    finalizers = info.Finalizers.Select(p => p.owner + " " + p.PatchMethod.DeclaringType?.FullName).ToList(),
                } : null).Where(x => x != null).ToList();
            }
            return Harmony.GetAllPatchedMethods().Select(m => new { m, info = Harmony.GetPatchInfo(m) })
                .Where(x => owner == null || x.info.Owners.Contains(owner))
                .Select(x => new { method = x.m.DeclaringType?.FullName + ":" + x.m.Name, owners = x.info.Owners.ToList() }).ToList();
        }
    }

    /// <summary>Hook bodies; free of Harmony types so they are safe to reference from anywhere.</summary>
    internal static class Hooks
    {
        internal static IntVec3? MouseOverride;
        internal static int TickTarget = -1;

        internal static void Pump() { try { Bridge.Pump(); } catch (Exception e) { Log.ErrorOnce("[AgentBridge] pump: " + e, 0x5eb71d); } }
        internal static void Overlay() { try { AgentBridge.Overlay.Draw(); } catch { } }

        internal static bool MouseCell(ref IntVec3 __result) { if (!(MouseOverride is IntVec3 c)) return true; __result = c; return false; }
        internal static bool MouseMapPosition(ref Vector3 __result) { if (!(MouseOverride is IntVec3 c)) return true; __result = c.ToVector3Shifted(); return false; }

        // These run inside the game's own code (every death, letter, log line): a throw here would break it, so each is
        // contained. The log hook swallows silently, a logged failure would re-enter it.
        internal static void LogMessage(LogMessage msg) { try { Feed.Add("log", msg.type.ToString(), msg.text); } catch { } }
        internal static void Message(Message msg) { try { Feed.Add("message", msg.def?.defName ?? "", msg.text); } catch { } }
        internal static void Letter(Letter let) { try { Feed.Add("letter", let.def?.defName ?? "", let.Label.ToString() + ((let as ChoiceLetter)?.Text is TaggedString t ? ": " + t : "")); } catch { } }
        internal static void Incident(IncidentWorker __instance, IncidentParms parms, bool __result) { try { Feed.Add("incident", __result ? "fired" : "failed", __instance.def.defName + " points=" + parms.points); } catch { } }
        internal static void Death(Pawn __instance, DamageInfo? dinfo)
        {
            try
            {
                // Visible as a player sees it: on a map, not under fog, not psychically invisible.
                bool hidden = __instance.MapHeld == null || __instance.PositionHeld.Fogged(__instance.MapHeld) || __instance.IsHiddenFromPlayer();
                Feed.Add("death", __instance.Faction?.def.defName ?? "", __instance.ThingID + " " + __instance.LabelShort + (dinfo is DamageInfo d ? " by " + d.Def?.defName + (d.Instigator != null ? " from " + d.Instigator.ThingID : "") : ""), hidden);
            }
            catch { }
        }

        // TickManagerUpdate breaks its loop as soon as Paused is set, so this stops on the exact tick at any speed.
        internal static void TickStop(TickManager __instance)
        {
            if (TickTarget >= 0 && __instance.TicksGame >= TickTarget) { TickTarget = -1; __instance.CurTimeSpeed = TimeSpeed.Paused; }
        }
    }
}
