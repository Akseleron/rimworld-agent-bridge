using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace AgentBridge
{
    public sealed class Settings : ModSettings
    {
        public bool enabled = true, readOnly, allowDev = true, allowPower = true, requireDevMode = true, showOverlay = true;
        public bool telemetry = true, crashReports = true, consentAsked, fairPlay;
        public int port = 8765;
        public string installId = "";

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref port, "port", 8765);
            Scribe_Values.Look(ref readOnly, "readOnly", false);
            Scribe_Values.Look(ref allowDev, "allowDev", true);
            Scribe_Values.Look(ref allowPower, "allowPower", true);
            Scribe_Values.Look(ref requireDevMode, "requireDevMode", true);
            Scribe_Values.Look(ref showOverlay, "showOverlay", true);
            Scribe_Values.Look(ref fairPlay, "fairPlay", false);
            Scribe_Values.Look(ref telemetry, "telemetry", true);
            Scribe_Values.Look(ref crashReports, "crashReports", true);
            Scribe_Values.Look(ref consentAsked, "consentAsked", false);
            Scribe_Values.Look(ref installId, "installId", "");
        }
    }

    public sealed class AgentBridgeMod : Mod
    {
        public static Settings Settings = new Settings();
        public static ModContentPack? Pack;
        private Vector2 scroll;
        private string portBuffer = "";

        public AgentBridgeMod(ModContentPack content) : base(content)
        {
            Pack = content;
            Settings = GetSettings<Settings>();
            if (string.IsNullOrEmpty(Settings.installId)) { Settings.installId = Guid.NewGuid().ToString("N"); WriteSettings(); }
            portBuffer = Settings.port.ToString();
        }

        public override string SettingsCategory() => "AB_SettingsCategory".Translate();

        public override void WriteSettings()
        {
            base.WriteSettings();
            if (Settings.enabled && !Bridge.Running) Bridge.Start();
            if (!Settings.enabled && Bridge.Running) Bridge.Stop();
        }

        public static string ToolsDir => Path.Combine(Pack?.RootDir ?? "", "Tools");
        public static string McpCommand => "claude mcp add rimworld -- python3 \"" + Path.Combine(ToolsDir, "rwb_mcp.py") + "\"\n" +
                                           "codex mcp add rimworld -- python3 \"" + Path.Combine(ToolsDir, "rwb_mcp.py") + "\"";

        // Two buttons per row keep the whole left column visible in the fixed-size settings window.
        private static bool ButtonPair(Listing_Standard l, string a, string b, out bool second)
        {
            var row = l.GetRect(30f); l.Gap(4f);
            var font = Text.Font; Text.Font = GameFont.Tiny;  // long translations must fit half a column
            second = Widgets.ButtonText(row.RightHalf().ContractedBy(2f, 0f), b.Translate());
            bool first = Widgets.ButtonText(row.LeftHalf().ContractedBy(2f, 0f), a.Translate());
            Text.Font = font;
            return first || second;
        }

        private static void Copy(string text) { GUIUtility.systemCopyBuffer = text; Messages.Message("AB_Copied".Translate(), MessageTypeDefOf.SilentInput, false); }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var s = Settings;
            var left = new Rect(inRect.x, inRect.y, inRect.width * 0.48f, inRect.height);
            var right = new Rect(inRect.x + inRect.width * 0.52f, inRect.y, inRect.width * 0.48f, inRect.height);

            var l = new Listing_Standard();
            l.Begin(left);
            // The dialog already shows the mod name as its title; only the version goes here.
            GUI.color = Color.gray; l.Label("v" + Bridge.Version); GUI.color = Color.white;
            l.Label(Bridge.Running ? "AB_Status_Listening".Translate(Bridge.Port, Registry.Commands.Count) : "AB_Status_Off".Translate());
            l.Label("AB_Status_Harmony".Translate((Bridge.HarmonyActive ? "AB_On" : "AB_Off").Translate()));
            l.GapLine();
            l.Label("AB_Section_Server".Translate());
            l.CheckboxLabeled("AB_Enable".Translate(), ref s.enabled, "AB_Enable_Tip".Translate());
            var portRow = l.GetRect(Text.LineHeight);
            Widgets.Label(portRow.LeftPart(0.7f), "AB_Port".Translate());
            Widgets.TextFieldNumeric(portRow.RightPart(0.28f), ref s.port, ref portBuffer, 1024, 65000);
            l.CheckboxLabeled("AB_FairPlay".Translate(), ref s.fairPlay, "AB_FairPlay_Tip".Translate());
            l.CheckboxLabeled("AB_ReadOnly".Translate(), ref s.readOnly);
            l.CheckboxLabeled("AB_AllowDev".Translate(), ref s.allowDev);
            l.CheckboxLabeled("AB_AllowPower".Translate(), ref s.allowPower);
            l.CheckboxLabeled("AB_RequireDevMode".Translate(), ref s.requireDevMode);
            l.CheckboxLabeled("AB_ShowOverlay".Translate(), ref s.showOverlay);
            if (ButtonPair(l, "AB_CopyMcp", "AB_CopyConnection", out bool second)) { if (second) Copy("descriptor: " + Bridge.DescriptorPath + "\nurl: http://127.0.0.1:" + Bridge.Port + "/\ntoken: " + Bridge.Token); else Copy(McpCommand); }
            if (ButtonPair(l, "AB_NewToken", "AB_OpenTools", out second)) { if (second) Application.OpenURL("file://" + ToolsDir); else { Bridge.RegenerateToken(); Bridge.Clients.Clear(); } }
            l.GapLine();
            l.Label("AB_Section_Privacy".Translate());
            l.CheckboxLabeled("AB_Telemetry".Translate(), ref s.telemetry, "AB_Telemetry_Tip".Translate());
            l.CheckboxLabeled("AB_CrashReports".Translate(), ref s.crashReports, "AB_CrashReports_Tip".Translate());
            if (!Telemetry.HasEndpoint) { GUI.color = Color.gray; l.Label("AB_NoEndpoint".Translate()); GUI.color = Color.white; }
            else if (Telemetry.LastStatus != null) { GUI.color = Color.gray; l.Label("AB_LastReport".Translate(Telemetry.LastStatus)); GUI.color = Color.white; }
            if (ButtonPair(l, "AB_PreviewData", "AB_ReportBug", out second)) { if (second) BugReport.Open(); else Find.WindowStack.Add(new Dialog_MessageBox(Telemetry.Preview())); }
            l.End();

            var r = new Listing_Standard();
            r.Begin(right);
            r.Label("AB_Section_Agents".Translate());
            if (Bridge.Clients.Count == 0) { GUI.color = Color.gray; r.Label("AB_NoAgents".Translate()); GUI.color = Color.white; }
            foreach (var c in Bridge.Clients.Values.OrderByDescending(c => c.LastSeen).Take(8))
                r.Label("AB_AgentLine".Translate(c.Name, c.Requests, c.Errors, c.LastCommand, (int)(DateTime.Now - c.LastSeen).TotalSeconds));
            r.GapLine();
            r.Label("AB_Section_Recent".Translate());
            var listRect = r.GetRect(Math.Max(100f, right.height - r.CurHeight - 10f));
            var recent = Bridge.RecentLog.AsEnumerable().Reverse().Take(100).ToList();
            var view = new Rect(0, 0, listRect.width - 16f, recent.Count * 22f);
            Widgets.BeginScrollView(listRect, ref scroll, view);
            for (int i = 0; i < recent.Count; i++)
            {
                var e = recent[i];
                GUI.color = e.Ok ? Color.white : new Color(1f, 0.6f, 0.6f);
                Widgets.Label(new Rect(0, i * 22f, view.width, 22f), e.Time.ToString("HH:mm:ss") + "  " + e.Agent + "  " + e.Command + "  " + e.Ms.ToString("0") + "ms" + (e.Ok ? "" : "  " + e.Error));
            }
            GUI.color = Color.white;
            Widgets.EndScrollView();
            r.End();
        }
    }

    /// <summary>Small top-centre label while an agent is acting, so the player always knows the game is driven.</summary>
    internal static class Overlay
    {
        internal static void Draw()
        {
            if (!AgentBridgeMod.Settings.showOverlay || Event.current.type != EventType.Repaint || Bridge.RecentLog.Count == 0) return;
            var last = Bridge.RecentLog[Bridge.RecentLog.Count - 1];
            if ((DateTime.Now - last.Time).TotalSeconds > 6) return;
            var font = Text.Font; var anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            // Left edge below the top bar area that other mods often use; Small font stays readable on any UI scale.
            Text.Font = GameFont.Small;
            var rect = new Rect(8f, 48f, 360f, 26f);
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.55f));
            GUI.color = last.Ok ? new Color(0.6f, 0.9f, 1f) : new Color(1f, 0.6f, 0.6f);
            Widgets.Label(rect, "AB_Overlay".Translate(last.Agent, last.Command));
            GUI.color = Color.white; Text.Font = font; Text.Anchor = anchor;
        }
    }
}
