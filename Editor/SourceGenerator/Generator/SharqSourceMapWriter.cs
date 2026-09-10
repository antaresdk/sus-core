using System.Collections.Generic;
using System.Text;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Builds the <c>&lt;Class&gt;.g.uss.map.json</c> (or <c>_scoped.g.uss.map.json</c>)
    /// content (T-3295, plan §4.4). Flat table of rows, deliberately NOT a VLQ sourcemap
    /// (plan §4.4): the only consumer is a Node-side build script reading a single internal
    /// file, and a two-runtime encoding library buys nothing back for that at 2510 rules.
    ///
    /// Hand-rolled JSON, not <c>MiniJson</c> (T-3293, read-only) or a third-party writer:
    /// the shape is fixed and tiny (an object, a string, a string, an array of 3-field
    /// objects) — writing it directly keeps this file dependency-free like the rest of the
    /// compile pipeline (D-1: no JSON library in the free package for one internal artifact).
    /// </summary>
    internal static class SharqSourceMapWriter
    {
        /// <param name="sharqPath">Verbatim <c>model.SourcePath</c> (caller's own path — this
        /// writer does not resolve it against a package root), forward-slashed.</param>
        /// <param name="sha1">Whole-file hash (<c>model.SourceSha1</c>) — the staleness check
        /// a consumer performs before trusting <paramref name="localLines"/>.</param>
        /// <param name="styleBodyFileLine"><c>model.StyleBodyLine</c> — added to every
        /// <paramref name="localLines"/> row's <c>Src</c> (1-based, local to
        /// <c>model.StyleBody</c>) to produce a real file line.</param>
        public static string Build(string sharqPath, string sha1, int styleBodyFileLine,
            IReadOnlyList<SharqMapLine> localLines)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"sharq\": ").Append(JsonString(sharqPath.Replace('\\', '/'))).Append(",\n");
            sb.Append("  \"sha1\": ").Append(JsonString(sha1)).Append(",\n");
            sb.Append("  \"lines\": [");
            if (localLines.Count == 0)
            {
                sb.Append("]\n");
            }
            else
            {
                sb.Append('\n');
                for (var i = 0; i < localLines.Count; i++)
                {
                    var l = localLines[i];
                    var fileSrc = styleBodyFileLine + (l.Src - 1);
                    sb.Append("    { \"uss\": ").Append(l.Uss)
                      .Append(", \"src\": ").Append(fileSrc)
                      .Append(", \"origin\": ").Append(JsonString(l.Origin ?? "style")).Append(" }");
                    sb.Append(i < localLines.Count - 1 ? ",\n" : "\n");
                }
                sb.Append("  ]\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string JsonString(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
