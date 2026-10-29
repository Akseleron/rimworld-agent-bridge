using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using Verse;

namespace AgentBridge
{
    /// <summary>A command that completes on a later frame. Poll returns null until done.</summary>
    public sealed class Pending
    {
        private readonly Func<object?> poll, onTimeout;
        public readonly float Deadline;
        public readonly double Seconds;
        public Pending(Func<object?> poll, double seconds, Func<object?> onTimeout)
        {
            this.poll = poll; this.onTimeout = onTimeout; Seconds = seconds; Deadline = Time.realtimeSinceStartup + (float)seconds;
        }
        public object? Poll() => poll();
        public object? OnTimeout() => onTimeout();
    }

    public sealed class ClientInfo
    {
        public string Name = "";
        public int Requests, Errors;
        public DateTime FirstSeen, LastSeen;
        public string LastCommand = "";
    }

    public sealed class LogEntry
    {
        public DateTime Time;
        public string Agent = "", Command = "";
        public double Ms;
        public bool Ok;
        public string? Error;
    }

    /// <summary>Bridge core. The socket threads only parse and queue requests; every command runs on the
    /// game main thread from Pump (driven by a Harmony Root.Update postfix and/or a MonoBehaviour, once per frame).</summary>
    [StaticConstructorOnStartup]
    public static class Bridge
    {
        public const string Version = "1.1.3";
        public const string Protocol = "rwb-1";
        private static readonly Queue<Request> queue = new Queue<Request>();
        private static readonly List<Request> waiting = new List<Request>();
        private static Server? server;
        private static string token = "";
        private static int lastPumpFrame = -1;
        // Bumped on every stop: requests from a stopped server are answered with an error, never run.
        private static volatile int generation;
        internal static string? LastSave;
        internal static bool HarmonyActive;
        public static readonly Dictionary<string, ClientInfo> Clients = new Dictionary<string, ClientInfo>();
        public static readonly List<LogEntry> RecentLog = new List<LogEntry>();
        public static int Port => server?.Port ?? 0;
        public static bool Running => server != null;
        public static string Token => token;
        public static Settings Settings => AgentBridgeMod.Settings;

        static Bridge()
        {
            try
            {
                Registry.ScanOwn();
                int external = Registry.ScanMods();
                HarmonyActive = TryHarmony();
                var go = new GameObject("AgentBridge");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<PumpBehaviour>();
                if (Settings.enabled) Start();
                Telemetry.SessionStart();
                Log.Message($"[AgentBridge] {Version}: {Registry.Commands.Count} commands ({external} from other mods), Harmony {(HarmonyActive ? "on" : "off")}, " + (Running ? "listening on 127.0.0.1:" + Port : "server disabled"));
            }
            catch (Exception error) { Log.Error("[AgentBridge] Startup failed: " + error); }
        }

        // Harmony is optional: everything referencing it lives in HarmonyPatches, so a missing 0Harmony
        // only fails this call (type load) instead of the whole assembly.
        private static bool TryHarmony()
        {
            try { HarmonyPatches.Apply(); return true; }
            catch (Exception e) { Log.Warning("[AgentBridge] Harmony unavailable, running with reduced features (no cell targeting, exact tick stop or event hooks): " + e.GetType().Name); return false; }
        }

        public static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        /// <summary>Where agents find port and token. Under the user profile: Steam's Linux container (pressure-vessel)
        /// hides /run/user and /tmp from the host, while the home directory is shared.</summary>
        public static string DescriptorPath
        {
            get
            {
                string? env = Environment.GetEnvironmentVariable("RWB_DESCRIPTOR");
                if (!string.IsNullOrEmpty(env)) return env!;
                return IsWindows
                    ? Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentBridge"), "descriptor.json")
                    : Path.Combine(Path.Combine(Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local"), "state"), "rimworld-agent-bridge"), "descriptor.json");
            }
        }

        public static void Start()
        {
            if (server != null) return;
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            token = string.Concat(bytes.Select(b => b.ToString("x2")));
            string? fixedToken = Environment.GetEnvironmentVariable("RWB_TOKEN");
            if (fixedToken != null && fixedToken.Length >= 16) token = fixedToken;
            int wanted = int.TryParse(Environment.GetEnvironmentVariable("RWB_PORT"), out var p) ? p : Settings.port;
            server = Server.Start(wanted, Handle);
            if (server == null) { Log.Error("[AgentBridge] No free port in " + wanted + ".." + (wanted + 9)); return; }
            WriteDescriptor();
        }

        public static void Stop()
        {
            generation++;
            server?.Stop(); server = null;
            try { File.Delete(DescriptorPath); } catch { }
        }

        /// <summary>New token, all agents disconnected. A disabled server stays off.</summary>
        public static void RegenerateToken() { Stop(); if (Settings.enabled) Start(); }

        private static void WriteDescriptor()
        {
            string path = DescriptorPath, temp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var assemblies = LoadedModManager.RunningModsListForReading.SelectMany(m => m.assemblies.loadedAssemblies)
                .Select(a => { try { return a.Location; } catch { return ""; } }).Where(l => !string.IsNullOrEmpty(l)).Distinct().ToList();
            File.WriteAllText(temp, Json.Write(new
            {
                protocol = Protocol, version = Version, port = Port, token, pid = Process.GetCurrentProcess().Id,
                gameRoot = Path.GetDirectoryName(GenFilePaths.ModsFolderPath.TrimEnd('/', '\\')),
                managed = Path.GetDirectoryName(typeof(Game).Assembly.Location),
                runtime = RuntimeName, os = Environment.OSVersion.Platform.ToString(), assemblies,
            }));
            if (!IsWindows) RestrictToOwner(temp);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        public static string RuntimeName => Type.GetType("Mono.Runtime") != null ? "mono" : "coreclr";

        private static void RestrictToOwner(string file)
        {
            try { Process.Start(new ProcessStartInfo("chmod", "600 \"" + file + "\"") { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(2000); }
            catch { }
        }

        internal static string ErrorJson(string message) => Json.Write(new Dictionary<string, object?> { ["ok"] = false, ["error"] = message });

        // ---------- socket thread ----------
        private static (int, string) Handle(string method, string path, Dictionary<string, string> headers, string body)
        {
            if (path.StartsWith("/health")) return (200, Json.Write(new { ok = true, protocol = Protocol, version = Version }));
            string presented = headers.TryGetValue("X-Token", out var t) ? t : headers.TryGetValue("Authorization", out var auth) && auth.StartsWith("Bearer ") ? auth.Substring(7) : "";
            if (!FixedTimeEquals(presented, token)) return (401, ErrorJson("bad token (read it from the descriptor: " + DescriptorPath + ")"));
            if (method != "POST") return (405, ErrorJson("POST a JSON object {\"cmd\":...}"));
            object? parsed;
            try { parsed = Json.Parse(body.Length == 0 ? "{}" : body); }
            catch (Exception e) { return (400, ErrorJson("invalid JSON: " + e.Message)); }
            if (!(parsed is Dictionary<string, object?> raw)) return (400, ErrorJson("body must be a JSON object"));
            string agent = headers.TryGetValue("X-Agent", out var xa) ? xa : headers.TryGetValue("User-Agent", out var ua) ? ua : "unknown";
            // Shown in the settings window and the overlay: printable characters only, short.
            agent = new string(agent.Where(ch => !char.IsControl(ch) && ch != '<' && ch != '>').Take(48).ToArray());
            if (agent.Length == 0) agent = "unknown";
            var request = new Request(new Args(raw), agent, generation);
            double timeout;
            try { timeout = request.Args.Timeout; } catch (Exception e) { return (400, ErrorJson(e.Message)); }
            lock (queue) queue.Enqueue(request);
            // Waiting commands may set a longer deadline than the requested timeout (newgame waits at least 600 s).
            var waited = Stopwatch.StartNew();
            while (!request.Done.WaitOne(TimeSpan.FromSeconds(1)))
            {
                if (waited.Elapsed.TotalSeconds < Math.Max(timeout, request.PendingSeconds) + 15) continue;
                request.Cancelled = true;  // the event stays alive: the main thread may still finish it
                return (504, ErrorJson("main thread did not answer (game loading, frozen or minimized?)"));
            }
            request.Done.Close();
            return (200, request.Result!);
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // ---------- main thread ----------
        private static readonly HashSet<string> LoadSafe = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "state", "help", "feed", "wait", "perf", "mods", "patches", "windows", "capabilities" };

        internal static void Pump()
        {
            if (Time.frameCount == lastPumpFrame) return;
            lastPumpFrame = Time.frameCount;
            Feed.PollFallback();
            if (!Settings.consentAsked && Time.frameCount % 120 == 0) Telemetry.MaybeAskConsent();
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                var w = waiting[i];
                // A client that gave up still gets the command's timeout cleanup (tick clears its stop target and pauses).
                if (w.Cancelled) { waiting.RemoveAt(i); try { w.Pending!.OnTimeout(); } catch { } continue; }
                object? result;
                try { result = w.Pending!.Poll(); }
                catch (Exception error) { result = new CommandError(error.Message); }
                if (result == null && Time.realtimeSinceStartup > w.Pending!.Deadline) result = w.Pending.OnTimeout();
                if (result != null) { waiting.RemoveAt(i); Finish(w, result); }
            }
            if (queue.Count == 0) return;
            bool loading = LongEventHandler.AnyEventNowOrWaiting;
            var clock = Stopwatch.StartNew();
            var deferred = new List<Request>();
            // Time budget instead of a count: many cheap queries finish in one frame, heavy ones yield.
            while (clock.ElapsedMilliseconds < 12)
            {
                Request request;
                lock (queue) { if (queue.Count == 0) break; request = queue.Dequeue(); }
                if (request.Cancelled) continue;
                if (request.Generation != generation) { Finish(request, new CommandError("Agent Bridge server was stopped")); continue; }
                if (loading && !LoadSafe.Contains(request.Args.Cmd)) { deferred.Add(request); continue; }
                object? result = Execute(request.Args);
                if (result is Pending pending) { request.Pending = pending; request.PendingSeconds = pending.Seconds; waiting.Add(request); }
                else Finish(request, result);
            }
            if (deferred.Count > 0) lock (queue) foreach (var d in deferred) queue.Enqueue(d);
        }

        /// <summary>Runs one command with access checks. Also used by batch.</summary>
        internal static object? Execute(Args a)
        {
            if (!Registry.Commands.TryGetValue(a.Cmd, out var cmd)) return new CommandError("unknown cmd '" + a.Cmd + "'; try help");
            if (Denied(cmd.Access) is string why) return new CommandError(why);
            try { return cmd.Run(a); }
            catch (CommandError e) { return e; }
            catch (Exception e)
            {
                var inner = e is System.Reflection.TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                // Reflection and agent-supplied code (obj, exec/eval, extension commands) fail on the agent's input,
                // not on Agent Bridge: answer the agent, but do not file it as a mod crash.
                if (cmd.Access == Access.Power || cmd.Access == Access.Inspect || cmd.Source != "core") return new CommandError(inner.GetType().Name + ": " + inner.Message);
                // Game commands sent from the main menu: the agent's timing, not a crash.
                if (inner is NullReferenceException && Current.Game == null) return new CommandError("no game loaded (main menu?); start or load a colony first");
                Telemetry.ReportException(a.Cmd, inner);
                Log.Warning("[AgentBridge] " + a.Cmd + ": " + inner);
                return new CommandError(inner.GetType().Name + ": " + inner.Message);
            }
        }

        public static string? Denied(Access access)
        {
            var s = Settings;
            if (access == Access.Read) return null;
            if (access == Access.Inspect) return s.fairPlay ? "fair-play mode: internal state is hidden (Agent Bridge settings)" : null;
            if (s.fairPlay && access >= Access.Dev) return "fair-play mode: only what a player can do (Agent Bridge settings)";
            if (s.readOnly) return "read-only mode is on (Agent Bridge mod settings)";
            if (access == Access.Dev && !s.allowDev) return "dev commands are disabled in Agent Bridge settings";
            if (access == Access.Power && !s.allowPower) return "power commands (eval/reflection) are disabled in Agent Bridge settings";
            if (access >= Access.Dev && s.requireDevMode && !Prefs.DevMode) return "enable Dev mode in game options (or untick 'require Dev mode' in Agent Bridge settings)";
            return null;
        }

        private static void Finish(Request request, object? result)
        {
            string? error = (result as CommandError)?.Message;
            try
            {
                request.Result = error != null ? ErrorJson(error)
                    : "{\"ok\":true,\"result\":" + Json.Write(result, request.Args.Fields) + "}";
            }
            catch (Exception e) { error = "serialize: " + e.Message; request.Result = ErrorJson(error); }
            double ms = (DateTime.UtcNow - request.Created).TotalMilliseconds;
            Record(request.Agent, request.Args.Cmd, ms, error);
            request.Done.Set();
        }

        private static void Record(string agent, string cmd, double ms, string? error)
        {
            var now = DateTime.Now;
            if (!Clients.TryGetValue(agent, out var c)) Clients[agent] = c = new ClientInfo { Name = agent, FirstSeen = now };
            c.Requests++; if (error != null) c.Errors++;
            c.LastSeen = now; c.LastCommand = cmd;
            RecentLog.Add(new LogEntry { Time = now, Agent = agent, Command = cmd, Ms = ms, Ok = error == null, Error = error });
            if (RecentLog.Count > 200) RecentLog.RemoveRange(0, RecentLog.Count - 200);
            Telemetry.CountCommand(cmd, error == null);
        }

        private sealed class Request
        {
            public readonly Args Args;
            public readonly string Agent;
            public readonly DateTime Created = DateTime.UtcNow;
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public string? Result;
            public readonly int Generation;
            public volatile bool Cancelled;
            public Pending? Pending;
            public double PendingSeconds;  // written on the main thread, read by the waiting socket thread
            public Request(Args args, string agent, int generation) { Args = args; Agent = agent; Generation = generation; }
        }
    }

    /// <summary>Per-frame driver that works without Harmony (Unity and Waylith both deliver MonoBehaviour messages).</summary>
    public sealed class PumpBehaviour : MonoBehaviour
    {
        internal static int Frames;
        public void Update() { Frames++; try { Bridge.Pump(); } catch (Exception e) { Log.ErrorOnce("[AgentBridge] pump: " + e, 0x5eb71d); } }
        public void OnGUI() { if (!Bridge.HarmonyActive) Overlay.Draw(); }
    }
}
