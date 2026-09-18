using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Minimal recursive-descent JSON reader (T-3293, plan §4.1/D-16/D-18), still needed by
    /// <see cref="SharqSourceMapWriter"/> even after the dimension ladder itself moved into the
    /// compiled <see cref="RungLadder"/> table (T-3674): Unity's <c>JsonUtility</c> has no
    /// dictionary support, and adding Newtonsoft as a dependency of the free core package to read
    /// small internal data files is the wrong trade. This reader returns a plain object graph
    /// (<see cref="Dictionary{TKey,TValue}"/> / <see cref="List{T}"/> / <c>string</c> /
    /// <c>long</c> / <c>double</c> / <c>bool</c> / <c>null</c>) — callers own their own typed
    /// projection. It is a READER only and has no writer and no opinion about the schema beyond
    /// well-formed JSON.
    /// </summary>
    internal static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (json == null) return null;
            var i = 0;
            var value = ParseValue(json, ref i);
            return value;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("MiniJson: unexpected end of input");
            switch (s[i])
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var obj = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // '{'
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return obj; }
            while (true)
            {
                SkipWs(s, ref i);
                var key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException($"MiniJson: expected ':' at {i}");
                i++;
                obj[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                throw new FormatException($"MiniJson: expected ',' or '}}' at {i}");
            }
            return obj;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var arr = new List<object>();
            i++; // '['
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return arr; }
            while (true)
            {
                arr.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                throw new FormatException($"MiniJson: expected ',' or ']' at {i}");
            }
            return arr;
        }

        private static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"')
                throw new FormatException($"MiniJson: expected '\"' at {i}");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                var c = s[i];
                if (c == '\\')
                {
                    i++;
                    if (i >= s.Length) throw new FormatException("MiniJson: unterminated escape");
                    var e = s[i];
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
                            var hex = s.Substring(i + 1, 4);
                            sb.Append((char)Convert.ToInt32(hex, 16));
                            i += 4;
                            break;
                        default: throw new FormatException($"MiniJson: bad escape '\\{e}' at {i}");
                    }
                    i++;
                }
                else { sb.Append(c); i++; }
            }
            if (i >= s.Length) throw new FormatException("MiniJson: unterminated string");
            i++; // closing '"'
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            var start = i;
            if (i < s.Length && s[i] == '-') i++;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            var isFloat = false;
            if (i < s.Length && s[i] == '.')
            {
                isFloat = true;
                i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                isFloat = true;
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            if (i == start) throw new FormatException($"MiniJson: expected a value at {i}");
            var text = s.Substring(start, i - start);
            return isFloat
                ? (object)double.Parse(text, CultureInfo.InvariantCulture)
                : long.Parse(text, CultureInfo.InvariantCulture);
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                throw new FormatException($"MiniJson: expected '{literal}' at {i}");
            i += literal.Length;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }
}
