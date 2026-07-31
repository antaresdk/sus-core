#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;

namespace Sharq.Core.Editor.Diagnostics
{
    /// <summary>
    /// Editor-side companion to <see cref="Sharq.Core.Diagnostics.SusUiProbe"/>:
    /// setup validation as machine-readable JSON. Wraps the existing SusSetupValidator
    /// WITHOUT the Console log / modal dialog, so an agent can call it and parse the result.
    /// </summary>
    public static class SusUiProbeEditor
    {
        /// <summary>{ "ok": bool, "issues": [ { severity, category, message, fix? } ] }</summary>
        public static string ValidateSetupJson()
        {
            var issues = new List<SusValidationIssue>();
            SusSetupValidator.ValidateAll(issues);

            var sb = new StringBuilder();
            sb.Append("{\"ok\":").Append(HasErrors(issues) ? "false" : "true");
            sb.Append(",\"issues\":[");
            for (int i = 0; i < issues.Count; i++)
            {
                var it = issues[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                sb.Append("\"severity\":").Append(Q(it.Severity.ToString()));
                sb.Append(",\"category\":").Append(Q(it.Category));
                sb.Append(",\"message\":").Append(Q(it.Message));
                if (!string.IsNullOrEmpty(it.FixHint))
                    sb.Append(",\"fix\":").Append(Q(it.FixHint));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static bool HasErrors(List<SusValidationIssue> issues)
        {
            foreach (var it in issues)
                if (it.Severity == SusValidationSeverity.Error) return true;
            return false;
        }

        private static string Q(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty) + "\"";
        }
    }
}
#endif
