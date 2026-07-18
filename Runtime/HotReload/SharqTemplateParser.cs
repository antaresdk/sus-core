using System.Collections.Generic;
using System.Text;

namespace Sharq.Core
{
    /// <summary>
    /// Runtime character-by-character XML template parser.
    /// Mirrors the Editor-side <c>TemplateParser</c> contract so the hot-reload
    /// template interpreter (E3) can run in DEVELOPMENT_BUILD without referencing
    /// any Editor assembly.
    /// </summary>
    public static class SharqTemplateParser
    {
        public static SharqTemplateNode Parse(string templateXml)
        {
            if (string.IsNullOrEmpty(templateXml))
                return new SharqTemplateNode { TagName = "root" };

            var root = ParseNode(new Scanner(templateXml.Trim()));
            if (root != null && root.Attributes.ContainsKey("$MainElement"))
            {
                root.IsMainElement = true;
                root.Attributes.Remove("$MainElement");
            }
            // Generator always treats the template root as the component ($MainElement semantics).
            if (root != null)
                root.IsMainElement = true;
            return root ?? new SharqTemplateNode { TagName = "root" };
        }

        private static SharqTemplateNode ParseNode(Scanner s)
        {
            s.SkipWhitespace();
            if (s.Peek != '<') return null;
            s.Advance();
            s.SkipWhitespace();

            var tagName = ReadTagName(s);
            if (string.IsNullOrEmpty(tagName)) return null;

            s.SkipWhitespace();
            var attrs = ReadAttributes(s);
            s.SkipWhitespace();

            var node = new SharqTemplateNode { TagName = tagName, Attributes = attrs };

            if (s.Peek == '/')
            {
                s.Advance(); s.Advance(); // '/>'
                node.IsSelfClosing = true;
                return node;
            }
            if (s.Peek == '>') s.Advance();
            else return node;

            var closeTag = $"</{tagName}>";
            while (!s.Eof)
            {
                s.SkipWhitespace();
                if (s.Eof) break;
                if (s.Peek == '<' && s.PeekAt(1) == '/')
                {
                    if (s.Match(closeTag)) s.Advance(closeTag.Length);
                    break;
                }
                if (s.Peek != '<') { SkipText(s); continue; }
                var child = ParseNode(s);
                if (child != null) node.Children.Add(child);
                else s.Advance();
            }
            return node;
        }

        private static string ReadTagName(Scanner s)
        {
            var sb = new StringBuilder();
            while (!s.Eof && s.Peek != '>' && s.Peek != '/' && !IsWs(s.Peek))
            { sb.Append(s.Current); s.Advance(); }
            return sb.ToString();
        }

        private static Dictionary<string, string> ReadAttributes(Scanner s)
        {
            var d = new Dictionary<string, string>();
            while (!s.Eof && s.Peek != '>' && s.Peek != '/')
            {
                s.SkipWhitespace();
                if (s.Peek == '>' || s.Peek == '/' || s.Eof) break;
                var key = ReadAttrKey(s);
                if (string.IsNullOrEmpty(key)) break;
                s.SkipWhitespace();
                if (s.Peek != '=') { d[key] = ""; continue; }
                s.Advance();
                s.SkipWhitespace();
                var q = s.Peek;
                if (q != '"' && q != '\'') break;
                s.Advance();
                var val = ReadUntil(s, q);
                s.Advance();
                d[key] = val;
            }
            return d;
        }

        private static string ReadAttrKey(Scanner s)
        {
            var sb = new StringBuilder();
            while (!s.Eof && !IsWs(s.Peek) && s.Peek != '=' && s.Peek != '>' && s.Peek != '/')
            { sb.Append(s.Current); s.Advance(); }
            return sb.ToString();
        }

        private static string ReadUntil(Scanner s, char term)
        {
            var sb = new StringBuilder();
            while (!s.Eof && s.Peek != term) { sb.Append(s.Current); s.Advance(); }
            return sb.ToString();
        }

        private static void SkipText(Scanner s)
        { while (!s.Eof && s.Peek != '<') s.Advance(); }

        private static bool IsWs(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r';

        private sealed class Scanner
        {
            private readonly string _t;
            private int _p;
            public Scanner(string t) { _t = t; }
            public char Peek => _p < _t.Length ? _t[_p] : '\0';
            public char PeekAt(int off) => _p + off < _t.Length ? _t[_p + off] : '\0';
            public char Current => Peek;
            public bool Eof => _p >= _t.Length;
            public void Advance(int n = 1) { _p += n; }
            public void SkipWhitespace() { while (!Eof && IsWs(Peek)) Advance(); }
            public bool Match(string s)
            {
                if (_p + s.Length > _t.Length) return false;
                for (int i = 0; i < s.Length; i++) if (_t[_p + i] != s[i]) return false;
                return true;
            }
        }
    }

    public sealed class SharqTemplateNode
    {
        public string TagName;
        public Dictionary<string, string> Attributes = new();
        public List<SharqTemplateNode> Children = new();
        public bool IsSelfClosing;
        /// <summary>True when root attrs apply to the host SusComponent (generator parity).</summary>
        public bool IsMainElement;
    }
}
