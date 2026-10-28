using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Verse;

namespace AgentBridge
{
    /// <summary>Thrown for expected, user-facing failures: becomes {"ok":false,"error":...} without a log stack.</summary>
    public sealed class CommandError : Exception
    {
        public CommandError(string message) : base(message) { }
    }

    /// <summary>Typed view over a JSON request object. Accepts numbers given as strings so CLI input stays forgiving.</summary>
    public sealed class Args
    {
        public readonly Dictionary<string, object?> Raw;
        public Args(Dictionary<string, object?> raw) { Raw = raw; }

        public string Cmd => Str("cmd") ?? "state";
        public bool Has(string n) => Raw.TryGetValue(n, out var v) && v != null;
        public object? Get(string n) => Raw.TryGetValue(n, out var v) ? v : null;

        public string? Str(string n) => Get(n) switch
        {
            null => null,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            var o => Json.Write(o),
        };

        public string Need(string n) => Str(n) ?? throw new CommandError("need '" + n + "'");

        public int Int(string n, int d)
        {
            switch (Get(n))
            {
                case null: return d;
                case long l: return (int)l;
                case double x: return (int)x;
                case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i): return i;
                case bool b: return b ? 1 : 0;
                default: throw new CommandError("'" + n + "' must be an integer");
            }
        }

        public long Long(string n, long d) => Get(n) switch { null => d, long l => l, double x => (long)x, string s => long.Parse(s, CultureInfo.InvariantCulture), _ => throw new CommandError("'" + n + "' must be an integer") };

        public float Float(string n, float d) => Get(n) switch
        {
            null => d, long l => l, double x => (float)x,
            string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) => f,
            _ => throw new CommandError("'" + n + "' must be a number"),
        };

        public bool Bool(string n, bool d) => Get(n) switch { null => d, bool b => b, string s => s == "true" || s == "1", long l => l != 0, _ => d };

        /// <summary>A string or a list of strings ("ids":"Human1" and "ids":["Human1"] are both fine).</summary>
        public List<string> Strings(string n) => Get(n) switch
        {
            null => new List<string>(),
            List<object?> list => list.Where(x => x != null).Select(x => x is string s ? s : Json.Write(x)).ToList(),
            var o => new List<string> { o is string s ? s : Json.Write(o) },
        };

        public List<object?> List(string n) => Get(n) as List<object?> ?? new List<object?>();

        public IntVec3? Pair(string n)
        {
            if (!(Get(n) is List<object?> l) || l.Count < 2) return null;
            int At(int i) => l[i] is long x ? (int)x : l[i] is double dd ? (int)dd : int.Parse(l[i]!.ToString(), CultureInfo.InvariantCulture);
            return new IntVec3(At(0), 0, At(l.Count - 1));
        }

        public HashSet<string>? Fields => Has("fields") ? new HashSet<string>(Strings("fields")) : null;

        /// <summary>Deadline in seconds for waiting commands; the HTTP layer waits a little longer.</summary>
        public double Timeout => Clamp(Get("timeout") switch { long l => l, double x => x, string s => double.Parse(s, CultureInfo.InvariantCulture), _ => 60 }, 1, 900);

        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        public Args With(string key, object? value) { var copy = new Dictionary<string, object?>(Raw) { [key] = value }; return new Args(copy); }
    }
}
