using System;
using System.Collections.Generic;
using System.Text;

namespace Sharq.Core.Editor.DesignImport
{
    /// <summary>
    /// Minimal unified diff for Editor dry-run preview (ARCH-DESIGN-IMPORT §5.2 / §7.1c).
    /// Pure string algorithm — shared by CLI and Editor; no network.
    /// </summary>
    public static class DesignDiff
    {
        /// <summary>
        /// Unified diff of <paramref name="oldText"/> → <paramref name="newText"/>.
        /// Empty string when texts are equal (after newline normalize).
        /// </summary>
        public static string Unified(
            string oldText,
            string newText,
            string oldLabel = "a/imported-tokens.uss",
            string newLabel = "b/imported-tokens.uss")
        {
            oldText = Normalize(oldText);
            newText = Normalize(newText);
            if (string.Equals(oldText, newText, StringComparison.Ordinal))
                return string.Empty;

            var a = SplitLines(oldText);
            var b = SplitLines(newText);
            var ops = BuildOps(a, b);

            var sb = new StringBuilder();
            sb.Append("--- ").Append(oldLabel ?? "a").Append('\n');
            sb.Append("+++ ").Append(newLabel ?? "b").Append('\n');
            sb.Append("@@ -1,").Append(a.Length)
                .Append(" +1,").Append(b.Length).Append(" @@\n");

            foreach (var op in ops)
            {
                switch (op.Kind)
                {
                    case DiffOpKind.Equal:
                        sb.Append(' ').Append(op.Line).Append('\n');
                        break;
                    case DiffOpKind.Delete:
                        sb.Append('-').Append(op.Line).Append('\n');
                        break;
                    case DiffOpKind.Insert:
                        sb.Append('+').Append(op.Line).Append('\n');
                        break;
                }
            }

            return sb.ToString();
        }

        static string Normalize(string text) =>
            (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

        static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            var parts = text.Split('\n');
            if (parts.Length > 0 && parts[parts.Length - 1].Length == 0)
            {
                var trimmed = new string[parts.Length - 1];
                Array.Copy(parts, trimmed, trimmed.Length);
                return trimmed;
            }
            return parts;
        }

        enum DiffOpKind { Equal, Delete, Insert }

        struct DiffOp
        {
            public DiffOpKind Kind;
            public string Line;
        }

        /// <summary>LCS DP — token USS sheets are small; clarity over asymptotic.</summary>
        static List<DiffOp> BuildOps(string[] a, string[] b)
        {
            var n = a.Length;
            var m = b.Length;
            var dp = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
            {
                for (var j = m - 1; j >= 0; j--)
                {
                    if (string.Equals(a[i], b[j], StringComparison.Ordinal))
                        dp[i, j] = dp[i + 1, j + 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
                }
            }

            var ops = new List<DiffOp>(n + m);
            var x = 0;
            var y = 0;
            while (x < n && y < m)
            {
                if (string.Equals(a[x], b[y], StringComparison.Ordinal))
                {
                    ops.Add(new DiffOp { Kind = DiffOpKind.Equal, Line = a[x] });
                    x++;
                    y++;
                }
                else if (dp[x + 1, y] >= dp[x, y + 1])
                {
                    ops.Add(new DiffOp { Kind = DiffOpKind.Delete, Line = a[x] });
                    x++;
                }
                else
                {
                    ops.Add(new DiffOp { Kind = DiffOpKind.Insert, Line = b[y] });
                    y++;
                }
            }
            while (x < n)
            {
                ops.Add(new DiffOp { Kind = DiffOpKind.Delete, Line = a[x] });
                x++;
            }
            while (y < m)
            {
                ops.Add(new DiffOp { Kind = DiffOpKind.Insert, Line = b[y] });
                y++;
            }
            return ops;
        }
    }
}
