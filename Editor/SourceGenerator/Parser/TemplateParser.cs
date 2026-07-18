using System;
using System.Collections.Generic;
using System.Text;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Character-by-character XML template parser. Handles multi-line attributes
    /// and self-closing tags reliably. Phase 0 v2.
    /// </summary>
    internal static class TemplateParser
    {
        private const char LT = '<';
        private const char GT = '>';
        private const char SLASH = '/';
        private const char SPACE = ' ';
        private const char TAB = '\t';
        private const char NL = '\n';
        private const char CR = '\r';
        private const char EQ = '=';
        private const char DQUOTE = '"';
        private const char SQUOTE = '\'';

        public static TemplateNode Parse(string templateXml, string className)
        {
            if (string.IsNullOrEmpty(templateXml))
                return new TemplateNode { TagName = "root" };

            var trimmed = templateXml.Trim();
            var root = ParseNode(new Scanner(trimmed));

            if (root != null && root.IsMainElement)
                root.Attributes.Remove(Constants.MainElement);

            return root ?? new TemplateNode { TagName = "root" };
        }

        private static TemplateNode ParseNode(Scanner s)
        {
            s.SkipWhitespace();
            if (s.Peek != LT) return null;

            s.Advance(); // skip '<'
            s.SkipWhitespace();

            // Read tag name
            var tagName = ReadTagName(s);
            if (string.IsNullOrEmpty(tagName)) return null;

            s.SkipWhitespace();
            var attrs = ReadAttributes(s);
            s.SkipWhitespace();

            var node = new TemplateNode
            {
                TagName = tagName,
                Attributes = attrs
            };

            if (attrs.ContainsKey(Constants.MainElement))
                node.IsMainElement = true;

            // Self-closing?
            if (s.Peek == SLASH)
            {
                s.Advance(); // '/'
                s.Advance(); // '>' (but skip anyway)
                node.IsSelfClosing = true;
                return node;
            }

            // Open tag: skip '>'
            if (s.Peek == GT)
                s.Advance();
            else
                return node; // malformed

            // Parse children until closing tag
            var closeTag = $"</{tagName}>";
            while (!s.Eof)
            {
                s.SkipWhitespace();
                if (s.Eof) break;

                // Any closing tag terminates this element's children.
                if (s.Peek == LT && s.PeekNext(1) == SLASH)
                {
                    // Only consume it when it actually matches THIS element's close tag.
                    // A mismatched close (malformed / implicitly-closed element) is left in
                    // place so an ancestor can consume it — prevents the scanner from
                    // skipping the wrong number of characters (old blind Advance bug).
                    if (s.Match(closeTag))
                        s.Advance(closeTag.Length);
                    break;
                }

                // Text content — skip it
                if (s.Peek != LT)
                {
                    SkipTextContent(s);
                    continue;
                }

                var child = ParseNode(s);
                if (child != null)
                    node.Children.Add(child);
                else
                    s.Advance(); // skip unknown char
            }

            return node;
        }

        private static string ReadTagName(Scanner s)
        {
            var sb = new StringBuilder();
            while (!s.Eof && !IsWhitespace(s.Peek) && s.Peek != GT && s.Peek != SLASH)
            {
                sb.Append(s.Current);
                s.Advance();
            }
            return sb.ToString();
        }

        private static Dictionary<string, string> ReadAttributes(Scanner s)
        {
            var dict = new Dictionary<string, string>();

            while (!s.Eof && s.Peek != GT && s.Peek != SLASH)
            {
                s.SkipWhitespace();
                if (s.Peek == GT || s.Peek == SLASH || s.Eof) break;

                // Read key
                var key = ReadAttrKey(s);
                if (string.IsNullOrEmpty(key)) break;

                s.SkipWhitespace();

                // Boolean attribute (e.g. $MainElement) — no '=' follows
                if (s.Peek != EQ)
                {
                    dict[key] = string.Empty;
                    continue;
                }
                s.Advance(); // skip '='

                s.SkipWhitespace();

                // Read value (quoted)
                var quote = s.Peek;
                if (quote != DQUOTE && quote != SQUOTE) break;
                s.Advance();

                var value = ReadUntil(s, quote);
                s.Advance(); // skip closing quote

                dict[key] = value;
            }

            return dict;
        }

        private static string ReadAttrKey(Scanner s)
        {
            var sb = new StringBuilder();
            while (!s.Eof && !IsWhitespace(s.Peek) && s.Peek != EQ && s.Peek != GT && s.Peek != SLASH)
            {
                sb.Append(s.Current);
                s.Advance();
            }
            return sb.ToString();
        }

        private static string ReadUntil(Scanner s, char terminator)
        {
            var sb = new StringBuilder();
            while (!s.Eof && s.Peek != terminator)
            {
                sb.Append(s.Current);
                s.Advance();
            }
            return sb.ToString();
        }

        private static void SkipTextContent(Scanner s)
        {
            while (!s.Eof && s.Peek != LT)
                s.Advance();
        }

        private static bool IsWhitespace(char c) => c == SPACE || c == TAB || c == NL || c == CR;

        // ─── Scanner ──────────────────────────────────────────────────

        private class Scanner
        {
            private readonly string _text;
            private int _pos;

            public Scanner(string text) { _text = text; _pos = 0; }

            public char Peek => _pos < _text.Length ? _text[_pos] : '\0';
            public char PeekNext(int offset) => (_pos + offset) < _text.Length ? _text[_pos + offset] : '\0';
            public char Current => Peek;
            public bool Eof => _pos >= _text.Length;

            public void Advance(int n = 1) { _pos += n; }

            public void SkipWhitespace()
            {
                while (!Eof && IsWhitespace(Peek))
                    Advance();
            }

            public bool Match(string s)
            {
                if (_pos + s.Length > _text.Length) return false;
                for (int i = 0; i < s.Length; i++)
                    if (_text[_pos + i] != s[i]) return false;
                return true;
            }
        }
    }

    internal class TemplateNode
    {
        public string TagName;
        public Dictionary<string, string> Attributes = new();
        public List<TemplateNode> Children = new();
        public bool IsSelfClosing;
        public bool IsMainElement;
    }
}
