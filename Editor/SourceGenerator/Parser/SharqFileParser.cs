using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Parsed .sharq file model. Contains the template, script body, and style sections.
    /// </summary>
    internal class SharqFileModel
    {
        public string ClassName;
        public string TemplateXml;   // raw content of <template>
        public string ScriptBody;    // raw content of <script> (minus $using)
        public string StyleBody;     // raw content of <style>
        public bool IsStyleScoped;
        public string SourcePath;
        public List<string> Usings = new();       // extracted from $using directives
        public List<string> Validators = new();   // "Validate_Health(int value) => value >= 0;" stubs
        public string BaseClass;                  // from $extends; null → SusComponent
        /// <summary>
        /// Optional C# namespace for the generated partial class.
        /// Set from package <c>sharq.gen.json</c> <c>namespace</c> (or future per-file override).
        /// Empty / null → emit into the global namespace (legacy).
        /// </summary>
        public string Namespace;
    }

    /// <summary>
    /// Splits a .sharq file into its sections: &lt;template&gt;, &lt;script&gt;, &lt;style&gt;.
    ///
    /// P2.1: uses a tag-aware scanner instead of a non-greedy regex. The old
    /// <c>&lt;template&gt;(.*?)&lt;/template&gt;</c> broke when <c>&lt;/template&gt;</c>
    /// appeared inside an HTML comment or a quoted attribute value. The scanner locates
    /// the matching close by skipping comments and whole child tags (respecting quotes),
    /// and tracking nested same-name depth. <c>&lt;script&gt;</c>/<c>&lt;style&gt;</c> are
    /// treated as raw-text blocks (first matching close wins, per the HTML raw-text rule),
    /// so C# generics like <c>List&lt;T&gt;</c> are never mistaken for markup.
    /// </summary>
    internal static class SharqFileParser
    {
        public static SharqFileModel Parse(string sharqContent, string filePath)
        {
            if (string.IsNullOrEmpty(sharqContent))
                throw new ArgumentNullException(nameof(sharqContent));

            var model = new SharqFileModel
            {
                ClassName = Path.GetFileNameWithoutExtension(filePath),
                SourcePath = filePath
            };

            // ─── Extract <template> (XML-aware close finding) ────────
            if (TryExtractSection(sharqContent, "template", xmlAware: true, out _, out var templateBody))
            {
                // Strip HTML comments <!-- ... -->
                var raw = Regex.Replace(templateBody, @"<!--.*?-->", "", RegexOptions.Singleline);
                model.TemplateXml = raw.Trim();
            }

            // ─── Extract <script> (raw text) ─────────────────────────
            if (TryExtractSection(sharqContent, "script", xmlAware: false, out _, out var scriptBody))
            {
                var parsed = ScriptParser.Parse(scriptBody);
                model.Usings = parsed.Usings;
                model.ScriptBody = parsed.Body;
                model.BaseClass = parsed.BaseClass;
            }

            // ─── Extract <style> (raw text) ──────────────────────────
            if (TryExtractSection(sharqContent, "style", xmlAware: false, out var styleAttrs, out var styleBody))
            {
                model.StyleBody = styleBody.Trim();
                model.IsStyleScoped = styleAttrs.Contains(Constants.ScopedStyleAttr);
            }

            return model;
        }

        // ─── Section scanner ──────────────────────────────────────────

        /// <summary>
        /// Locates a top-level <c>&lt;name …&gt; … &lt;/name&gt;</c> section and returns its
        /// attribute string and inner body. Returns false when the section is absent or
        /// its close tag is missing.
        /// </summary>
        private static bool TryExtractSection(string src, string name, bool xmlAware,
            out string attrs, out string body)
        {
            attrs = string.Empty;
            body = null;

            int open = FindOpenTag(src, name, out int openTagEnd, out attrs);
            if (open < 0) return false;

            int contentStart = openTagEnd + 1;
            int close = xmlAware
                ? FindXmlClose(src, contentStart, name)
                : FindRawTextClose(src, contentStart, name);
            if (close < 0) return false;

            body = src.Substring(contentStart, close - contentStart);
            return true;
        }

        // Finds "<name ...>" (or "<name>"), returns index of '<', the index of the
        // closing '>' of the open tag, and the attribute substring.
        private static int FindOpenTag(string src, string name, out int openTagEnd, out string attrs)
        {
            openTagEnd = -1;
            attrs = string.Empty;
            int from = 0;
            while (true)
            {
                int lt = src.IndexOf('<', from);
                if (lt < 0) return -1;

                if (IsTagNameAt(src, lt + 1, name))
                {
                    int afterName = lt + 1 + name.Length;
                    char boundary = afterName < src.Length ? src[afterName] : '\0';
                    if (boundary == '>' || boundary == '/' || IsWhitespace(boundary))
                    {
                        int gt = FindTagEnd(src, lt);
                        if (gt < 0) return -1;
                        openTagEnd = gt;
                        attrs = src.Substring(afterName, gt - afterName);
                        return lt;
                    }
                }
                from = lt + 1;
            }
        }

        // Scans from a leading '<' to the matching '>', skipping quoted strings so that
        // attribute values containing '>' don't terminate the tag prematurely.
        private static int FindTagEnd(string src, int ltIndex)
        {
            int i = ltIndex + 1;
            while (i < src.Length)
            {
                char c = src[i];
                if (c == '"' || c == '\'') { i = SkipQuoted(src, i); continue; }
                if (c == '>') return i;
                i++;
            }
            return -1;
        }

        // XML-aware close search: skips comments and whole child tags (respecting quotes),
        // tracks nested same-name depth. Returns the index of the matching "</name>".
        private static int FindXmlClose(string src, int start, string name)
        {
            int i = start;
            int depth = 0;
            while (i < src.Length)
            {
                char c = src[i];
                if (c != '<') { i++; continue; }

                // HTML comment
                if (Matches(src, i, "<!--")) { i = SkipComment(src, i); continue; }

                // Closing tag "</...>"
                if (i + 1 < src.Length && src[i + 1] == '/')
                {
                    if (IsTagNameAt(src, i + 2, name) && IsCloseBoundary(src, i + 2 + name.Length))
                    {
                        if (depth == 0) return i;
                        depth--;
                    }
                    int g = src.IndexOf('>', i);
                    i = g >= 0 ? g + 1 : src.Length;
                    continue;
                }

                // Opening tag of the same name → nested block
                if (IsTagNameAt(src, i + 1, name) && IsOpenBoundary(src, i + 1 + name.Length))
                {
                    int te = FindTagEnd(src, i);
                    if (te < 0) { i = src.Length; continue; }
                    if (te == 0 || src[te - 1] != '/') depth++; // not self-closing
                    i = te + 1;
                    continue;
                }

                // Any other tag — skip it whole (respecting quotes).
                int te2 = FindTagEnd(src, i);
                i = te2 >= 0 ? te2 + 1 : src.Length;
            }
            return -1;
        }

        // Raw-text close search (script/style): first "</name>" wins. Content is not
        // treated as markup, so '<' in C#/CSS is ignored.
        private static int FindRawTextClose(string src, int start, string name)
        {
            int i = start;
            while (true)
            {
                int idx = src.IndexOf("</", i, StringComparison.Ordinal);
                if (idx < 0) return -1;
                if (IsTagNameAt(src, idx + 2, name) && IsCloseBoundary(src, idx + 2 + name.Length))
                    return idx;
                i = idx + 2;
            }
        }

        // ─── Low-level helpers ────────────────────────────────────────

        private static bool IsTagNameAt(string src, int pos, string name)
        {
            if (pos + name.Length > src.Length) return false;
            for (int k = 0; k < name.Length; k++)
                if (src[pos + k] != name[k]) return false;
            // Ensure it's not a longer name (e.g. "template" vs "templatex").
            int after = pos + name.Length;
            if (after < src.Length)
            {
                char c = src[after];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') return false;
            }
            return true;
        }

        private static bool IsCloseBoundary(string src, int pos)
        {
            // After "</name" expect optional whitespace then '>'.
            int i = pos;
            while (i < src.Length && IsWhitespace(src[i])) i++;
            return i < src.Length && src[i] == '>';
        }

        private static bool IsOpenBoundary(string src, int pos)
        {
            if (pos >= src.Length) return false;
            char c = src[pos];
            return c == '>' || c == '/' || IsWhitespace(c);
        }

        private static int SkipQuoted(string src, int quoteIndex)
        {
            char quote = src[quoteIndex];
            int i = quoteIndex + 1;
            while (i < src.Length)
            {
                if (src[i] == '\\') { i += 2; continue; }
                if (src[i] == quote) return i + 1;
                i++;
            }
            return src.Length;
        }

        private static int SkipComment(string src, int i)
        {
            i += 4; // skip "<!--"
            int end = src.IndexOf("-->", i, StringComparison.Ordinal);
            return end < 0 ? src.Length : end + 3;
        }

        private static bool Matches(string src, int i, string token)
        {
            if (i + token.Length > src.Length) return false;
            for (int k = 0; k < token.Length; k++)
                if (src[i + k] != token[k]) return false;
            return true;
        }

        private static bool IsWhitespace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r';
    }
}
