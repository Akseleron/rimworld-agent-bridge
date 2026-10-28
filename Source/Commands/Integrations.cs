using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AgentBridge
{
    /// <summary>Soft integrations: the bridge never requires these mods, but recognises them and tells agents how
    /// they change behaviour. All generic commands (gizmos, menus, debug actions, obj/eval, defs) already cover
    /// any installed mod's content; this table only adds hints, and mods may register real commands via
    /// AgentBridgeCommandAttribute or Api.Register.</summary>
    internal static class Integrations
    {
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["brrainz.harmony"] = "enables cell targeting, exact tick stops and event hooks",
            ["arandomkiwi.rimsaves"] = "save folders: physical names are '<folder>#§#<name>'; save/load/saves handle the prefix",
            ["fluffy.worktab"] = "detailed work priorities: use 'work' for basic levels, obj/eval for per-hour priorities",
            ["petetimessix.simplesidearms"] = "sidearm gizmo appears in 'gizmos' (key Gizmo_SidearmsList)",
            ["unlimitedhugs.hugslib"] = "HugsLib settings reachable via obj target=static:HugsLib.Settings.ModSettingsManager",
            ["void.charactereditor"] = "pawn editing via its debug/menu actions; prefer bridge skill/hediff/need commands",
            ["rwmt.multiplayer"] = "multiplayer: bridge actions run locally and may desync; use Read commands only",
            ["dubwise.dubsperformanceanalyzer"] = "profiling: combine with perf and patches",
        };

        internal static object Detected()
        {
            var running = new HashSet<string>(LoadedModManager.RunningModsListForReading.Select(m => m.PackageIdPlayerFacing.ToLowerInvariant()));
            return Known.Where(k => running.Contains(k.Key.ToLowerInvariant())).ToDictionary(k => k.Key, k => k.Value);
        }

        internal static bool Active(string packageId) => LoadedModManager.RunningModsListForReading.Any(m => m.PackageIdPlayerFacing.Equals(packageId, StringComparison.OrdinalIgnoreCase));
    }
}
