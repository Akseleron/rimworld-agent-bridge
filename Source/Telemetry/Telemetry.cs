using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using RimWorld;
using Verse;

namespace AgentBridge
{
    /// <summary>Anonymous, opt-out statistics and error reports. Collected: mod/game version, runtime, OS family,
    /// language, session length, per-command success/error counts, generic agent kinds, and sanitised stack
    /// traces of Agent Bridge failures. Never: save names, pawn names, file paths, user names, mod lists, IPs
    /// beyond what any HTTPS request reveals. Nothing is sent when no endpoint is configured.</summary>
    public static class Telemetry
    {
        /// <summary>Collection endpoint (see telemetry/README.md). Empty = disabled. RWB_TELEMETRY_URL overrides.</summary>
        public const string DefaultEndpoint = "https://agent-bridge-telemetry.ivanzhe03.workers.dev/v1/report";
        private static string Endpoint => Environment.GetEnvironmentVariable("RWB_TELEMETRY_URL") ?? DefaultEndpoint;
        public static bool HasEndpoint => !string.IsNullOrEmpty(Endpoint);

        private static readonly object gate = new object();
        private static readonly Dictionary<string, int[]> commands = new Dictionary<string, int[]>();
        private static readonly Dictionary<string, ErrorInfo> errors = new Dictionary<string, ErrorInfo>();
        private static DateTime sessionStart = DateTime.UtcNow, lastSent = DateTime.UtcNow;
        /// <summary>Result of the last delivery attempt ("200 at 12:00:01", "error: …"); shown in settings.</summary>
        public static volatile string? LastStatus;

        private sealed class ErrorInfo { public string signature = "", type = "", command = "", trace = ""; public int count; }

        public static void SessionStart()
        {
            sessionStart = lastSent = DateTime.UtcNow;
            Application.quitting += () => Send(true);
        }

        public static void CountCommand(string cmd, bool ok)
        {
            if (!Bridge.Settings.telemetry) return;
            lock (gate)
            {
                // Only registered names are counted; arbitrary strings from clients never leave the machine.
                if (!Registry.Commands.ContainsKey(cmd)) cmd = "<unknown>";
                if (!commands.TryGetValue(cmd, out var c)) commands[cmd] = c = new int[2];
                c[ok ? 0 : 1]++;
            }
            if ((DateTime.UtcNow - lastSent).TotalMinutes > 30) Send(false);
        }

        public static void ReportException(string cmd, Exception e)
        {
            if (!Bridge.Settings.crashReports) return;
            string trace = Sanitize(e.GetType().FullName + ": " + e.Message + "\n" + string.Join("\n", (e.StackTrace ?? "").Split('\n').Take(12).ToArray()));
            string signature = Hash(e.GetType().FullName + string.Join("|", (e.StackTrace ?? "").Split('\n').Take(4).Select(l => Regex.Replace(l, @"\[0x[0-9a-f]+\]|:\d+", "")).ToArray()));
            lock (gate)
            {
                if (!errors.TryGetValue(signature, out var info)) errors[signature] = info = new ErrorInfo { signature = signature, type = e.GetType().Name, command = cmd, trace = trace };
                info.count++;
            }
        }

        /// <summary>Removes user names and absolute paths from text.</summary>
        public static string Sanitize(string text)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home)) text = text.Replace(home, "~");
            text = text.Replace(Environment.UserName, "<user>");
            text = Regex.Replace(text, @"([A-Za-z]:)?[\\/](?:[^\\/\s:""']+[\\/])+", "…/");
            return text;
        }

        private static string Hash(string s)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create()) return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(s)).Take(8).Select(b => b.ToString("x2")));
        }

        private static string AgentKind(string name)
        {
            string n = name.ToLowerInvariant();
            foreach (var k in new[] { "claude", "codex", "cursor", "gemini", "copilot", "mcp", "rwb", "curl", "python" }) if (n.Contains(k)) return k;
            return "other";
        }

        private static Dictionary<string, object?> Payload()
        {
            lock (gate)
            {
                return new Dictionary<string, object?>
                {
                    ["schema"] = 1,
                    ["install"] = Bridge.Settings.installId,
                    ["mod"] = Bridge.Version,
                    ["game"] = VersionControl.CurrentVersionString,
                    ["runtime"] = Bridge.RuntimeName,
                    ["os"] = Environment.OSVersion.Platform.ToString(),
                    ["lang"] = LanguageDatabase.activeLanguage?.folderName,
                    ["harmony"] = Bridge.HarmonyActive,
                    ["minutes"] = (int)(DateTime.UtcNow - sessionStart).TotalMinutes,
                    ["agents"] = Bridge.Settings.telemetry ? Bridge.Clients.Keys.Select(AgentKind).Distinct().ToList() : null,
                    ["commands"] = Bridge.Settings.telemetry ? commands.ToDictionary(k => k.Key, k => k.Value.ToList()) : null,
                    ["errors"] = Bridge.Settings.crashReports ? errors.Values.Select(e => new Dictionary<string, object?> { ["sig"] = e.signature, ["type"] = e.type, ["cmd"] = e.command, ["count"] = e.count, ["trace"] = e.trace }).ToList() : null,
                };
            }
        }

        public static string Preview() => (HasEndpoint ? "" : "AB_NoEndpoint".Translate() + "\n\n") + Json.Write(Payload());

        /// <summary>Sends pending data now and waits for the result (used by the 'telemetry' command and tests).</summary>
        internal static string SendNow() { Send(true); return LastStatus ?? "nothing sent"; }

        private static void Send(bool final)
        {
            var s = Bridge.Settings;
            if (!HasEndpoint || !(s.telemetry || s.crashReports)) return;
            lastSent = DateTime.UtcNow;
            string body = Json.Write(Payload());
            lock (gate) { commands.Clear(); errors.Clear(); }
            void Post()
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(Endpoint);
                    req.Method = "POST"; req.ContentType = "application/json"; req.Timeout = 5000;
                    var bytes = Encoding.UTF8.GetBytes(body);
                    using (var stream = req.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                    using (req.GetResponse()) LastStatus = "delivered " + DateTime.Now.ToString("HH:mm");
                }
                // 429 = the receiver already has a recent report from this install; the data is merged next time.
                catch (WebException e) when (e.Response is HttpWebResponse r) { LastStatus = ((int)r.StatusCode == 429 ? "postponed " : "rejected (" + (int)r.StatusCode + ") ") + DateTime.Now.ToString("HH:mm"); }
                catch (Exception e) { LastStatus = "offline (" + e.GetType().Name + ")"; /* best effort: never disturb the game */ }
            }
            if (final) Post(); else new Thread(Post) { IsBackground = true }.Start();
        }

        /// <summary>One-time consent prompt in the main menu; checkboxes default on as requested, the player decides.</summary>
        internal static void MaybeAskConsent()
        {
            var s = Bridge.Settings;
            if (s.consentAsked || Current.ProgramState != ProgramState.Entry || LongEventHandler.AnyEventNowOrWaiting || Find.WindowStack == null) return;
            s.consentAsked = true;
            Find.WindowStack.Add(new Dialog_MessageBox("AB_ConsentText".Translate(), "AB_ConsentAccept".Translate(), () => { s.telemetry = s.crashReports = true; s.Write(); },
                "AB_ConsentDecline".Translate(), () => { s.telemetry = s.crashReports = false; s.Write(); }, "AB_ConsentTitle".Translate()));
            s.Write();
        }
    }

    /// <summary>Zero-backend bug reports: prefilled GitHub issue plus full text on the clipboard.</summary>
    public static class BugReport
    {
        public const string Repo = "https://github.com/Akseleron/rimworld-agent-bridge";

        public static string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("**Agent Bridge** " + Bridge.Version + " | RimWorld " + VersionControl.CurrentVersionStringWithRev + " | " + Bridge.RuntimeName + " | " + Environment.OSVersion.Platform + " | Harmony " + Bridge.HarmonyActive + " | lang " + LanguageDatabase.activeLanguage?.folderName);
            sb.AppendLine();
            sb.AppendLine("### What happened\n<!-- describe the steps and the command you sent -->\n");
            var failures = Bridge.RecentLog.Where(e => !e.Ok).Reverse().Take(10).ToList();
            if (failures.Count > 0)
            {
                sb.AppendLine("### Recent failed commands\n```");
                foreach (var f in failures) sb.AppendLine(f.Time.ToString("HH:mm:ss") + " " + f.Command + ": " + Telemetry.Sanitize(f.Error ?? ""));
                sb.AppendLine("```");
            }
            var logErrors = Log.Messages.Where(m => m.type == LogMessageType.Error && m.text.Contains("AgentBridge")).Reverse().Take(3).ToList();
            if (logErrors.Count > 0)
            {
                sb.AppendLine("### Log errors\n```");
                foreach (var m in logErrors) sb.AppendLine(Telemetry.Sanitize(m.text.Length > 1500 ? m.text.Substring(0, 1500) : m.text));
                sb.AppendLine("```");
            }
            return sb.ToString();
        }

        public static void Open()
        {
            string body = Build();
            GUIUtility.systemCopyBuffer = body;
            string shortBody = body.Length > 5000 ? body.Substring(0, 5000) + "\n…(full report is on your clipboard)" : body;
            Application.OpenURL(Repo + "/issues/new?labels=bug&title=" + Uri.EscapeDataString("[bug] ") + "&body=" + Uri.EscapeDataString(shortBody));
        }
    }
}
