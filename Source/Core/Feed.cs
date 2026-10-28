using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;

namespace AgentBridge
{
    /// <summary>Ring buffer of game events with increasing sequence numbers: log lines, messages, letters,
    /// incidents, deaths. Agents read it with feed/wait after=&lt;seq&gt; instead of polling the whole state.</summary>
    public static class Feed
    {
        public sealed class Entry
        {
            public long seq;
            public string kind = "", level = "";
            public string? text;
            public int? tick;
        }

        private static readonly object gate = new object();
        private static readonly Queue<Entry> items = new Queue<Entry>();
        private static long seq;
        private static LogMessage? lastSeenLog;
        private static int lastLetterCount = -1;

        public static long Last { get { lock (gate) return seq; } }

        public static void Add(string kind, string level, string? text)
        {
            if (text != null && text.Length > 1500) text = text.Substring(0, 1500) + "…";
            lock (gate)
            {
                items.Enqueue(new Entry { seq = ++seq, kind = kind, level = level, text = text, tick = Current.Game?.tickManager?.TicksGame });
                while (items.Count > 2000) items.Dequeue();
            }
        }

        public static Entry? Find(long after, string kind, Regex? rx)
        {
            lock (gate) return items.FirstOrDefault(i => i.seq > after && i.kind == kind && (rx == null || rx.IsMatch(i.level + " " + i.text)));
        }

        public static object Since(long after, string? kind, int limit)
        {
            lock (gate)
            {
                var list = items.Where(i => i.seq > after && (kind == null || i.kind == kind)).ToList();
                return new { last = seq, items = list.Skip(System.Math.Max(0, list.Count - limit)).ToList() };
            }
        }

        /// <summary>Without Harmony there are no hooks; tail Log.Messages and the letter stack instead.</summary>
        internal static void PollFallback()
        {
            if (Bridge.HarmonyActive) return;
            var messages = Log.Messages.ToList();
            int start = lastSeenLog == null ? messages.Count : messages.LastIndexOf(lastSeenLog) + 1;
            for (int i = start; i < messages.Count; i++) Add("log", messages[i].type.ToString(), messages[i].text);
            if (messages.Count > 0) lastSeenLog = messages[messages.Count - 1];
            var letters = Current.ProgramState == ProgramState.Playing ? Verse.Find.LetterStack?.LettersListForReading : null;
            if (letters != null)
            {
                if (lastLetterCount >= 0) for (int i = lastLetterCount; i < letters.Count; i++) Add("letter", letters[i].def?.defName ?? "", letters[i].Label.ToString());
                lastLetterCount = letters.Count;
            }
        }
    }
}
