using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Compile-time resolution of <c>rung(&lt;family&gt;, &lt;rung&gt;)</c> — the ONLY arithmetic
    /// of the Sharq style layer (T-3293, plan §4.1 D-7; table source T-3674, plan
    /// `ARCH-20260918-RUNG-LADDER-SHIP.md` D-1). A declaration value like
    /// <c>min-height: rung(control-height, xs);</c> becomes
    /// <c>min-height: var(--sk-control-h-xs, 28px);</c>: both the token NAME and the fallback
    /// NUMBER come from <see cref="RungLadder"/>, a table generated INSIDE the package — the
    /// same arithmetic that builds the shipped kit sheet, read here rather than re-derived by a
    /// second formula, and present in
    /// every project that installs `com.sharq-it.sus.core` (`file:`, git pin, registry, a
    /// Tuanjie project, a folder outside the monorepo) with no disk search required.
    ///
    /// Runs as a pure TEXT pass over the raw <c>&lt;style&gt;</c> body inside
    /// <see cref="SharqFileParser"/>, before <see cref="CssScanner"/> ever sees it — so every
    /// downstream stage (<see cref="VariantsCompiler"/>, <see cref="ScopedCssGenerator"/>,
    /// <see cref="BuildMethodGenerator"/>) sees an ordinary <c>var(...)</c> call and needs zero
    /// awareness of <c>rung()</c> having existed, and both compilation entry points
    /// (<c>BuildMethodGenerator.GenerateCode</c> and <c>StyleParser.Parse</c>, called on the same
    /// <see cref="SharqFileModel"/> — T-3292's report) see IDENTICAL already-resolved text rather
    /// than risking two independent readings of the same call. None of the 194-file corpus or the
    /// 3 fixtures (T-3289/T-3290) contains the text <c>rung(</c> — the pass is a no-op on all of
    /// them (single substring check before the regex even runs), which is what keeps D-13's
    /// zero-diff guarantee true here BY CONSTRUCTION, exactly like T-3291/T-3292's own gates.
    ///
    /// "family outside the ladder" / "rung outside the family" (card T-3293) are BOTH compile
    /// errors, not silent passthrough: a <c>rung()</c> call that fails to resolve throws, naming
    /// the <c>.sharq</c> file and the 1-based line the call sits on (computed by
    /// <see cref="SharqFileParser"/> from the real file, not just "somewhere in &lt;style&gt;").
    ///
    /// D-4 (T-3675 sibling defect): a <c>rung(</c> call whose arguments do NOT match the
    /// <c>&lt;family&gt;, &lt;rung-name&gt;</c> shape — e.g. an argument mangled into
    /// <c>var(--sk-space-10, 10)</c> by an unrelated tool — used to fail the regex silently and
    /// ride the passthrough text straight into the shipped `.g.uss` as an invalid CSS value. Any
    /// <c>rung(</c> substring left after the well-formed calls are replaced is now a compile
    /// error of its own, naming the file, the line, and the malformed call text.
    /// </summary>
    internal static class RungResolver
    {
        private static readonly Regex CallRe = new(
            @"rung\(\s*([A-Za-z][\w-]*)\s*,\s*([A-Za-z0-9][\w-]*)\s*\)", RegexOptions.Compiled);

        /// <summary>
        /// Resolves every <c>rung(...)</c> call in <paramref name="body"/> (the already-trimmed
        /// <c>&lt;style&gt;</c> text). Returns <paramref name="body"/> UNCHANGED (same reference)
        /// when it contains no <c>rung(</c> at all — the corpus/fixture fast path. Throws
        /// <see cref="InvalidOperationException"/> naming file + line for every call that failed
        /// to resolve or was malformed (D-4), collected, not one-at-a-time, so a file with several
        /// bad calls reports all of them in one message — same convention <see cref="StyleParser"/>
        /// already uses.
        /// </summary>
        public static string ResolveAll(string body, string sourcePath, string className, int startLine)
        {
            if (string.IsNullOrEmpty(body) || body.IndexOf("rung(", StringComparison.Ordinal) < 0)
                return body;

            var fileLabel = string.IsNullOrEmpty(className) ? Path.GetFileName(sourcePath) : className + ".sharq";
            return ResolveAll(body, fileLabel, startLine);
        }

        /// <summary>
        /// Pure overload — no disk access, resolves against the package-shipped
        /// <see cref="RungLadder"/> table directly. Exists so the resolution arithmetic
        /// (<see cref="TryResolve"/>) is testable without depending on
        /// <see cref="SharqFileParser"/>'s file-path plumbing.
        /// </summary>
        internal static string ResolveAll(string body, string fileLabel, int startLine)
        {
            if (string.IsNullOrEmpty(body) || body.IndexOf("rung(", StringComparison.Ordinal) < 0)
                return body;

            var errors = new List<string>();

            var result = CallRe.Replace(body, m =>
            {
                var line = startLine + CountNewlines(body, 0, m.Index);
                if (!TryResolve(m.Groups[1].Value, m.Groups[2].Value, out var replacement, out var err))
                {
                    errors.Add($"{fileLabel}:{line}: {err}");
                    return m.Value;
                }
                return replacement;
            });

            // D-4: a rung( call the regex above did not match at all (malformed arguments, e.g.
            // an unrelated tool rewrote the rung name into `var(--sk-space-10, 10)`) still leaves
            // the substring `rung(` in `result` — report each occurrence instead of shipping it.
            // Scanned against a COMMENT-MASKED copy (`/* ... */` blanked, same length/positions):
            // the corpus documents dimension-literal exceptions with prose that names `rung()`
            // by itself inside a trailing comment — that is not a call, and flagging it would
            // make every such comment a compile error (T-3547's own family of false positive).
            var masked = MaskComments(result);
            if (masked.IndexOf("rung(", StringComparison.Ordinal) >= 0)
            {
                var searchFrom = 0;
                while (true)
                {
                    var idx = masked.IndexOf("rung(", searchFrom, StringComparison.Ordinal);
                    if (idx < 0) break;
                    var line = startLine + CountNewlines(result, 0, idx);
                    var callText = ExtractCallText(result, idx);
                    errors.Add($"{fileLabel}:{line}: rung() call is malformed: arguments must be "
                        + $"`<family>, <rung-name>` — got `{callText}`");
                    searchFrom = idx + "rung(".Length;
                }
            }

            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" | ", errors));

            return result;
        }

        internal static bool TryResolve(string familyName, string rung, out string replacement, out string error)
        {
            replacement = null;
            error = null;

            if (!RungLadder.Table.TryGetValue(familyName, out var entries))
            {
                error = $"rung(): family '{familyName}' is not in the dimension ladder — "
                    + $"available: {string.Join(", ", RungLadder.Families)}";
                return false;
            }

            RungLadder.Entry entry = default;
            var found = false;
            foreach (var e in entries)
            {
                if (!string.Equals(e.Rung, rung, StringComparison.Ordinal)) continue;
                entry = e;
                found = true;
                break;
            }

            if (!found)
            {
                error = $"rung(): rung '{rung}' is not in family '{familyName}' — "
                    + $"available rungs: {string.Join(", ", entries.Select(e => e.Rung))}";
                return false;
            }

            replacement = $"var({entry.Token}, {entry.Value}px)";
            return true;
        }

        /// <summary>Extracts the full <c>rung(...)</c> call text starting at <paramref name="start"/>
        /// (the index of the 'r' in "rung(") for the error message, matching parens (a malformed
        /// call's argument can itself contain a nested call, e.g. <c>rung(space, var(--x, 1))</c>).
        /// Falls back to a short prefix if the parens never balance (truncated/broken source).</summary>
        private static string ExtractCallText(string s, int start)
        {
            var open = s.IndexOf('(', start);
            if (open < 0) return s.Substring(start, Math.Min(60, s.Length - start));

            var depth = 0;
            for (var i = open; i < s.Length; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')')
                {
                    depth--;
                    if (depth == 0) return s.Substring(start, i - start + 1);
                }
            }
            return s.Substring(start, Math.Min(60, s.Length - start));
        }

        private static int CountNewlines(string s, int from, int to)
        {
            var n = 0;
            var end = Math.Min(to, s.Length);
            for (var i = from; i < end; i++)
                if (s[i] == '\n') n++;
            return n;
        }

        /// <summary>Blanks every <c>/* ... */</c> comment's content with spaces (newlines kept
        /// literal) so the D-4 leftover-<c>rung(</c> scan does not mistake documentation prose —
        /// e.g. a comment that names <c>rung()</c> by itself next to an `sus:dimension-literal`
        /// marker — for a malformed call. Same length as the input, so indices found in the result stay
        /// valid offsets into the ORIGINAL (unmasked) string for text/line extraction.</summary>
        private static string MaskComments(string s)
        {
            var chars = s.ToCharArray();
            var i = 0;
            while (i < chars.Length - 1)
            {
                if (chars[i] == '/' && chars[i + 1] == '*')
                {
                    var j = i;
                    while (j < chars.Length - 1 && !(chars[j] == '*' && chars[j + 1] == '/')) j++;
                    var end = Math.Min(j + 2, chars.Length);
                    for (var k = i; k < end; k++)
                        if (chars[k] != '\n') chars[k] = ' ';
                    i = end;
                }
                else i++;
            }
            return new string(chars);
        }
    }
}
