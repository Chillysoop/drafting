using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GreenFlag.Sim
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// A small JSON tree for save files (roadmap step 35). The sim has no engine or package references, and the save format
    /// has to stay readable and diffable, so this is written out by hand: objects keep their key order, numbers keep the text
    /// they were written with, and Write always gives the same bytes for the same tree (invariant culture, "\n" line ends).
    /// </summary>
    public sealed class JsonValue
    {
        public JsonKind Kind { get; private set; }
        bool b;
        string s; // a string's text, or a number's digits
        List<JsonValue> arr;
        List<KeyValuePair<string, JsonValue>> obj;

        static readonly CultureInfo inv = CultureInfo.InvariantCulture;
        public static readonly JsonValue Null = new JsonValue { Kind = JsonKind.Null };

        public static JsonValue Obj() => new JsonValue { Kind = JsonKind.Object, obj = new List<KeyValuePair<string, JsonValue>>() };
        public static JsonValue Arr() => new JsonValue { Kind = JsonKind.Array, arr = new List<JsonValue>() };
        public static JsonValue Of(bool v) => new JsonValue { Kind = JsonKind.Bool, b = v };
        public static JsonValue Of(long v) => new JsonValue { Kind = JsonKind.Number, s = v.ToString(inv) };
        /// <summary>A double, written so it reads back bit for bit.</summary>
        public static JsonValue Of(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("JSON has no NaN or infinity");
            return new JsonValue { Kind = JsonKind.Number, s = v.ToString("R", inv) };
        }
        public static JsonValue Of(string v) => v == null ? Null : new JsonValue { Kind = JsonKind.String, s = v };
        public static JsonValue Of(IEnumerable<string> v)
        {
            if (v == null) return Null;
            var a = Arr();
            foreach (var x in v) a.Add(Of(x));
            return a;
        }

        public bool IsNull => Kind == JsonKind.Null;

        // ------------------------------------------------------------------ objects

        /// <summary>Sets a key (replacing it in place if it is there already). Returns this, so writes chain.</summary>
        public JsonValue Set(string key, JsonValue v)
        {
            Need(JsonKind.Object);
            v = v ?? Null;
            for (int i = 0; i < obj.Count; i++)
                if (obj[i].Key == key) { obj[i] = new KeyValuePair<string, JsonValue>(key, v); return this; }
            obj.Add(new KeyValuePair<string, JsonValue>(key, v));
            return this;
        }
        public JsonValue Set(string key, bool v) => Set(key, Of(v));
        public JsonValue Set(string key, long v) => Set(key, Of(v));
        public JsonValue Set(string key, double v) => Set(key, Of(v));
        public JsonValue Set(string key, string v) => Set(key, Of(v));

        public bool Remove(string key)
        {
            Need(JsonKind.Object);
            return obj.RemoveAll(kv => kv.Key == key) > 0;
        }

        public bool Has(string key) => Kind == JsonKind.Object && obj.Exists(kv => kv.Key == key);

        /// <summary>A key's value, or JSON null when it is missing (so old saves without a newer key read as defaults).</summary>
        public JsonValue this[string key]
        {
            get
            {
                Need(JsonKind.Object);
                foreach (var kv in obj) if (kv.Key == key) return kv.Value;
                return Null;
            }
        }

        public IEnumerable<KeyValuePair<string, JsonValue>> Fields { get { Need(JsonKind.Object); return obj; } }

        // ------------------------------------------------------------------ arrays

        public JsonValue Add(JsonValue v) { Need(JsonKind.Array); arr.Add(v ?? Null); return this; }
        public int Count => Kind == JsonKind.Array ? arr.Count : Kind == JsonKind.Object ? obj.Count : 0;
        public JsonValue this[int i] { get { Need(JsonKind.Array); return arr[i]; } }
        public IEnumerable<JsonValue> Items { get { if (Kind == JsonKind.Null) yield break; Need(JsonKind.Array); foreach (var v in arr) yield return v; } }

        // ------------------------------------------------------------------ reading values

        public bool Bool(bool fallback = false) => Kind == JsonKind.Null ? fallback : Kind == JsonKind.Bool ? b : throw Bad("a true or false");
        public long Long(long fallback = 0) => Kind == JsonKind.Null ? fallback : Kind == JsonKind.Number && long.TryParse(s, NumberStyles.AllowLeadingSign, inv, out var v) ? v : throw Bad("a whole number");
        public int Int(int fallback = 0) { long v = Long(fallback); if (v < int.MinValue || v > int.MaxValue) throw Bad("a 32-bit number"); return (int)v; }
        public uint UInt(uint fallback = 0) { long v = Long(fallback); if (v < 0 || v > uint.MaxValue) throw Bad("an unsigned 32-bit number"); return (uint)v; }
        public double Double(double fallback = 0) => Kind == JsonKind.Null ? fallback : Kind == JsonKind.Number ? double.Parse(s, NumberStyles.Float, inv) : throw Bad("a number");
        public string Str(string fallback = null) => Kind == JsonKind.Null ? fallback : Kind == JsonKind.String ? s : throw Bad("a string");
        public List<string> Strings() { var o = new List<string>(); foreach (var v in Items) o.Add(v.Str()); return o; }
        public T Enum<T>(T fallback) where T : struct
        {
            if (Kind == JsonKind.Null) return fallback;
            var t = Str();
            if (System.Enum.TryParse<T>(t, false, out var v) && System.Enum.IsDefined(typeof(T), v) && t.Length > 0 && !char.IsDigit(t[0]) && t[0] != '-') return v;
            throw new FormatException($"Save file: \"{t}\" is not a {typeof(T).Name}");
        }

        void Need(JsonKind k) { if (Kind != k) throw Bad(k == JsonKind.Object ? "an object" : "an array"); }
        FormatException Bad(string what) => new FormatException($"Save file: expected {what}, found {Kind.ToString().ToLowerInvariant()}");

        // ------------------------------------------------------------------ writing

        /// <summary>The JSON text: two-space indents, or one line when compact.</summary>
        public string Write(bool compact = false)
        {
            var sb = new StringBuilder();
            WriteTo(sb, compact, 0);
            if (!compact) sb.Append('\n');
            return sb.ToString();
        }

        void WriteTo(StringBuilder sb, bool compact, int depth)
        {
            switch (Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(b ? "true" : "false"); break;
                case JsonKind.Number: sb.Append(s); break;
                case JsonKind.String: Quote(sb, s); break;
                case JsonKind.Array:
                    // arrays of plain values stay on one line, which keeps decks and card lists short
                    bool flat = compact || arr.TrueForAll(v => v.Kind != JsonKind.Array && v.Kind != JsonKind.Object);
                    sb.Append('[');
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (i > 0) sb.Append(flat && !compact ? ", " : ",");
                        if (!flat) Indent(sb, depth + 1);
                        arr[i].WriteTo(sb, compact, depth + 1);
                    }
                    if (!flat && arr.Count > 0) Indent(sb, depth);
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    sb.Append('{');
                    for (int i = 0; i < obj.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (!compact) Indent(sb, depth + 1);
                        Quote(sb, obj[i].Key);
                        sb.Append(compact ? ":" : ": ");
                        obj[i].Value.WriteTo(sb, compact, depth + 1);
                    }
                    if (!compact && obj.Count > 0) Indent(sb, depth);
                    sb.Append('}');
                    break;
            }
        }

        static void Indent(StringBuilder sb, int depth) { sb.Append('\n'); sb.Append(' ', depth * 2); }

        static void Quote(StringBuilder sb, string t)
        {
            sb.Append('"');
            foreach (char c in t)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", inv));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------------ parsing

        /// <summary>Parses JSON text. Throws FormatException (with the character position) on anything malformed.</summary>
        public static JsonValue Parse(string text)
        {
            if (text == null) throw new FormatException("Save file: no text");
            int i = 0;
            var v = ParseValue(text, ref i, 0);
            SkipWs(text, ref i);
            if (i != text.Length) throw At(text, i, "unexpected text after the end");
            return v;
        }

        static FormatException At(string t, int i, string what) => new FormatException($"Save file: {what} at character {i}");

        static void SkipWs(string t, ref int i) { while (i < t.Length && (t[i] == ' ' || t[i] == '\n' || t[i] == '\r' || t[i] == '\t' || t[i] == '﻿')) i++; }

        static JsonValue ParseValue(string t, ref int i, int depth)
        {
            if (depth > 64) throw At(t, i, "nesting too deep");
            SkipWs(t, ref i);
            if (i >= t.Length) throw At(t, i, "unexpected end");
            char c = t[i];
            if (c == '{')
            {
                i++;
                var o = Obj();
                SkipWs(t, ref i);
                if (i < t.Length && t[i] == '}') { i++; return o; }
                while (true)
                {
                    SkipWs(t, ref i);
                    if (i >= t.Length || t[i] != '"') throw At(t, i, "expected a key");
                    var key = ParseString(t, ref i);
                    SkipWs(t, ref i);
                    if (i >= t.Length || t[i] != ':') throw At(t, i, "expected ':'");
                    i++;
                    o.obj.Add(new KeyValuePair<string, JsonValue>(key, ParseValue(t, ref i, depth + 1)));
                    SkipWs(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    if (i < t.Length && t[i] == '}') { i++; return o; }
                    throw At(t, i, "expected ',' or '}'");
                }
            }
            if (c == '[')
            {
                i++;
                var a = Arr();
                SkipWs(t, ref i);
                if (i < t.Length && t[i] == ']') { i++; return a; }
                while (true)
                {
                    a.arr.Add(ParseValue(t, ref i, depth + 1));
                    SkipWs(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    if (i < t.Length && t[i] == ']') { i++; return a; }
                    throw At(t, i, "expected ',' or ']'");
                }
            }
            if (c == '"') return new JsonValue { Kind = JsonKind.String, s = ParseString(t, ref i) };
            if (Word(t, ref i, "true")) return Of(true);
            if (Word(t, ref i, "false")) return Of(false);
            if (Word(t, ref i, "null")) return Null;
            int st = i;
            if (i < t.Length && t[i] == '-') i++;
            int digits = i;
            while (i < t.Length && char.IsDigit(t[i])) i++;
            if (i == digits) throw At(t, st, "unexpected character");
            if (i < t.Length && t[i] == '.') { i++; int f = i; while (i < t.Length && char.IsDigit(t[i])) i++; if (i == f) throw At(t, i, "bad number"); }
            if (i < t.Length && (t[i] == 'e' || t[i] == 'E'))
            {
                i++;
                if (i < t.Length && (t[i] == '+' || t[i] == '-')) i++;
                int e = i; while (i < t.Length && char.IsDigit(t[i])) i++;
                if (i == e) throw At(t, i, "bad number");
            }
            return new JsonValue { Kind = JsonKind.Number, s = t.Substring(st, i - st) };
        }

        static bool Word(string t, ref int i, string w)
        {
            if (string.CompareOrdinal(t, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        static string ParseString(string t, ref int i)
        {
            i++; // the opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= t.Length) throw At(t, i, "unterminated string");
                char c = t[i++];
                if (c == '"') return sb.ToString();
                if (c < 0x20) throw At(t, i - 1, "control character in a string");
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= t.Length) throw At(t, i, "unterminated string");
                char e = t[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > t.Length || !int.TryParse(t.Substring(i, 4), NumberStyles.AllowHexSpecifier, inv, out var u)) throw At(t, i, "bad \\u escape");
                        sb.Append((char)u); i += 4; break;
                    default: throw At(t, i - 1, "bad escape");
                }
            }
        }
    }
}
