using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentBridge
{
    // Dependency-free JSON for net472 (Unity Mono) and CoreCLR (Waylith) alike.
    // Parse produces Dictionary<string, object?>, List<object?>, string, long, double, bool or null.
    public static class Json
    {
        public static object? Parse(string text)
        {
            int i = 0;
            var value = ReadValue(text, ref i);
            SkipWs(text, ref i);
            if (i != text.Length) throw new FormatException("trailing characters at " + i);
            return value;
        }

        private static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object? ReadValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            switch (c)
            {
                case '{':
                {
                    var obj = new Dictionary<string, object?>(StringComparer.Ordinal); i++;
                    SkipWs(s, ref i);
                    if (s[i] == '}') { i++; return obj; }
                    while (true)
                    {
                        SkipWs(s, ref i);
                        string key = ReadString(s, ref i);
                        SkipWs(s, ref i);
                        if (s[i++] != ':') throw new FormatException("expected : at " + i);
                        obj[key] = ReadValue(s, ref i);
                        SkipWs(s, ref i);
                        if (s[i] == ',') { i++; continue; }
                        if (s[i] == '}') { i++; return obj; }
                        throw new FormatException("expected , or } at " + i);
                    }
                }
                case '[':
                {
                    var list = new List<object?>(); i++;
                    SkipWs(s, ref i);
                    if (s[i] == ']') { i++; return list; }
                    while (true)
                    {
                        list.Add(ReadValue(s, ref i));
                        SkipWs(s, ref i);
                        if (s[i] == ',') { i++; continue; }
                        if (s[i] == ']') { i++; return list; }
                        throw new FormatException("expected , or ] at " + i);
                    }
                }
                case '"': return ReadString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default:
                {
                    int start = i;
                    while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                    string num = s.Substring(start, i - start);
                    if (num.Length == 0) throw new FormatException("unexpected '" + c + "' at " + start);
                    if (num.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
                    return double.Parse(num, NumberStyles.Float, CultureInfo.InvariantCulture);
                }
            }
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException("bad literal at " + i);
            i += word.Length;
        }

        private static string ReadString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("expected string at " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
        }

        // ---------- writing ----------
        private static readonly Regex RichText = new Regex("</?(color|b|i|size)(=[^>]*)?>", RegexOptions.Compiled);

        /// <summary>Serialises plain data, anonymous objects, dictionaries and lists. Null members are omitted,
        /// Unity rich-text tags are stripped, and <paramref name="fields"/> projects the top object or the
        /// elements of a top-level list. Unknown object types are written via Plain.Of (ids for things/defs).</summary>
        public static string Write(object? value, ICollection<string>? fields = null)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value, fields, value is IList && !(value is string) ? 1 : 0, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object? v, ICollection<string>? fields, int projectDepth, int nesting)
        {
            if (nesting > 64) { sb.Append("\"<depth>\""); return; }
            v = Plain.Of(v);
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s.IndexOf('<') >= 0 ? RichText.Replace(s, "") : s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case float f: WriteDouble(sb, f); return;
                case double d: WriteDouble(sb, d); return;
                case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); return;
                case IDictionary dict:
                {
                    sb.Append('{'); bool first = true;
                    foreach (DictionaryEntry kv in dict)
                    {
                        string key = Convert.ToString(kv.Key, CultureInfo.InvariantCulture) ?? "null";
                        if (kv.Value == null || (fields != null && projectDepth <= 1 && !fields.Contains(key))) continue;
                        if (!first) sb.Append(','); first = false;
                        WriteString(sb, key); sb.Append(':');
                        WriteValue(sb, kv.Value, fields, projectDepth + 2, nesting + 1);
                    }
                    sb.Append('}'); return;
                }
                case IEnumerable seq:
                {
                    sb.Append('['); bool first = true;
                    foreach (var item in seq)
                    {
                        if (!first) sb.Append(','); first = false;
                        WriteValue(sb, item, fields, projectDepth == 1 ? 1 : projectDepth + 2, nesting + 1);
                    }
                    sb.Append(']'); return;
                }
            }
            var type = v.GetType();
            if (type.IsPrimitive) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            if (type.IsEnum) { WriteString(sb, v.ToString()); return; }
            // Anonymous objects and POCO results: public properties and fields.
            sb.Append('{'); bool firstMember = true;
            foreach (var (name, value) in Members(v))
            {
                if (value == null || (fields != null && projectDepth <= 1 && !fields.Contains(name))) continue;
                if (!firstMember) sb.Append(','); firstMember = false;
                WriteString(sb, name); sb.Append(':');
                WriteValue(sb, value, fields, projectDepth + 2, nesting + 1);
            }
            sb.Append('}');
        }

        private static readonly Dictionary<Type, MemberInfo[]> memberCache = new Dictionary<Type, MemberInfo[]>();

        private static IEnumerable<(string, object?)> Members(object v)
        {
            var type = v.GetType();
            MemberInfo[] members;
            lock (memberCache)
                if (!memberCache.TryGetValue(type, out members))
                    memberCache[type] = members = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0).Cast<MemberInfo>()
                        .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance)).ToArray();
            foreach (var m in members)
            {
                object? value;
                try { value = m is PropertyInfo p ? p.GetValue(v, null) : ((FieldInfo)m).GetValue(v); }
                catch (Exception e) { value = "!" + (e.InnerException ?? e).GetType().Name; }
                yield return (m.Name, value);
            }
        }

        private static void WriteDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { WriteString(sb, d.ToString(CultureInfo.InvariantCulture)); return; }
            // Agents do not need float noise: 4 significant decimals keep payloads short.
            double r = Math.Round(d, 4);
            sb.Append(r == Math.Floor(r) && Math.Abs(r) < 1e15 ? ((long)r).ToString(CultureInfo.InvariantCulture) : r.ToString("R", CultureInfo.InvariantCulture));
        }

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
