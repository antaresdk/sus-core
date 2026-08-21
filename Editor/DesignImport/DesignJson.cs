using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sharq.Core.Editor.DesignImport
{
    /// <summary>Minimal JSON DOM for design-token files (no System.Text.Json — Editor-safe).</summary>
    public abstract class JsonNode
    {
        public virtual JsonObject AsObject() => this as JsonObject;
        public virtual JsonArray AsArray() => this as JsonArray;
        public virtual string AsString() => null;
        public virtual bool? AsBool() => null;
        public virtual double? AsNumber() => null;
        public bool IsNull => this is JsonNull;
    }

    public sealed class JsonNull : JsonNode { }

    public sealed class JsonString : JsonNode
    {
        public string Value { get; }
        public JsonString(string value) { Value = value ?? ""; }
        public override string AsString() => Value;
    }

    public sealed class JsonNumber : JsonNode
    {
        public double Value { get; }
        public JsonNumber(double value) { Value = value; }
        public override double? AsNumber() => Value;
        public override string AsString() => Value.ToString(CultureInfo.InvariantCulture);
    }

    public sealed class JsonBool : JsonNode
    {
        public bool Value { get; }
        public JsonBool(bool value) { Value = value; }
        public override bool? AsBool() => Value;
        public override string AsString() => Value ? "true" : "false";
    }

    public sealed class JsonArray : JsonNode
    {
        public List<JsonNode> Items { get; } = new List<JsonNode>();
        public override JsonArray AsArray() => this;
    }

    public sealed class JsonObject : JsonNode
    {
        public Dictionary<string, JsonNode> Props { get; } =
            new Dictionary<string, JsonNode>(StringComparer.Ordinal);

        public override JsonObject AsObject() => this;

        public bool TryGet(string key, out JsonNode node) => Props.TryGetValue(key, out node);

        public JsonNode Get(string key) =>
            Props.TryGetValue(key, out var n) ? n : null;

        public string GetString(string key, string fallback = "")
        {
            if (!Props.TryGetValue(key, out var n) || n == null || n.IsNull) return fallback;
            return n.AsString() ?? n.AsNumber()?.ToString(CultureInfo.InvariantCulture) ?? fallback;
        }
    }

    public static class DesignJson
    {
        public static JsonNode Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new Parser(text);
            var node = p.ParseValue();
            p.SkipWs();
            if (!p.Eof) throw p.Error("trailing content after JSON value");
            return node;
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s) { _s = s; }

            public bool Eof => _i >= _s.Length;

            public Exception Error(string msg) =>
                new FormatException($"JSON@{_i}: {msg}");

            public void SkipWs()
            {
                while (_i < _s.Length)
                {
                    var c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') _i++;
                    else break;
                }
            }

            public JsonNode ParseValue()
            {
                SkipWs();
                if (Eof) throw Error("unexpected end");
                var c = _s[_i];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return new JsonString(ParseString());
                if (c == 't' || c == 'f') return ParseBool();
                if (c == 'n') return ParseNull();
                if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                throw Error($"unexpected '{c}'");
            }

            JsonObject ParseObject()
            {
                _i++; // {
                var obj = new JsonObject();
                SkipWs();
                if (Peek('}')) { _i++; return obj; }
                while (true)
                {
                    SkipWs();
                    if (!Peek('"')) throw Error("expected property name");
                    var key = ParseString();
                    SkipWs();
                    if (!Peek(':')) throw Error("expected ':'");
                    _i++;
                    var val = ParseValue();
                    obj.Props[key] = val;
                    SkipWs();
                    if (Peek('}')) { _i++; return obj; }
                    if (!Peek(',')) throw Error("expected ',' or '}'");
                    _i++;
                }
            }

            JsonArray ParseArray()
            {
                _i++; // [
                var arr = new JsonArray();
                SkipWs();
                if (Peek(']')) { _i++; return arr; }
                while (true)
                {
                    arr.Items.Add(ParseValue());
                    SkipWs();
                    if (Peek(']')) { _i++; return arr; }
                    if (!Peek(',')) throw Error("expected ',' or ']'");
                    _i++;
                }
            }

            string ParseString()
            {
                _i++; // "
                var sb = new StringBuilder();
                while (!Eof)
                {
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (Eof) throw Error("unterminated escape");
                        var e = _s[_i++];
                        switch (e)
                        {
                            case '"': case '\\': case '/': sb.Append(e); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_i + 4 > _s.Length) throw Error("bad unicode escape");
                                var hex = _s.Substring(_i, 4);
                                _i += 4;
                                sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                break;
                            default:
                                throw Error($"bad escape \\{e}");
                        }
                    }
                    else sb.Append(c);
                }
                throw Error("unterminated string");
            }

            JsonBool ParseBool()
            {
                if (Match("true")) return new JsonBool(true);
                if (Match("false")) return new JsonBool(false);
                throw Error("expected true/false");
            }

            JsonNull ParseNull()
            {
                if (Match("null")) return new JsonNull();
                throw Error("expected null");
            }

            JsonNumber ParseNumber()
            {
                var start = _i;
                if (Peek('-')) _i++;
                if (Peek('0')) _i++;
                else
                {
                    if (!IsDigit(PeekChar())) throw Error("expected digit");
                    while (IsDigit(PeekChar())) _i++;
                }
                if (Peek('.'))
                {
                    _i++;
                    if (!IsDigit(PeekChar())) throw Error("expected fraction digit");
                    while (IsDigit(PeekChar())) _i++;
                }
                if (Peek('e') || Peek('E'))
                {
                    _i++;
                    if (Peek('+') || Peek('-')) _i++;
                    if (!IsDigit(PeekChar())) throw Error("expected exponent digit");
                    while (IsDigit(PeekChar())) _i++;
                }
                var slice = _s.Substring(start, _i - start);
                if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                    throw Error($"bad number '{slice}'");
                return new JsonNumber(n);
            }

            bool Match(string lit)
            {
                if (_i + lit.Length > _s.Length) return false;
                if (string.CompareOrdinal(_s, _i, lit, 0, lit.Length) != 0) return false;
                _i += lit.Length;
                return true;
            }

            bool Peek(char c) => _i < _s.Length && _s[_i] == c;
            char PeekChar() => _i < _s.Length ? _s[_i] : '\0';
            static bool IsDigit(char c) => c >= '0' && c <= '9';
        }

        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
