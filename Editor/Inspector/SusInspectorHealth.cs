using System;
using System.Collections.Generic;
using System.Text;
using Sharq.Core.Editor.Diagnostics;
using UnityEngine;

namespace Sharq.Core.Editor.Inspector
{
    public enum SusInspectorTab
    {
        Overview,
        Inspect,
        Health,
        Compile,
        Connect,
        Settings,
    }

    public enum SusIssueSeverity
    {
        Blocking,
        Error,
        Warning,
        Info,
    }

    public sealed class SusHealthIssue
    {
        public string Id;
        public SusIssueSeverity Severity;
        public string Title;
        public string Detail;
        public string Source;
        public string SuggestedFix;
        public Action FixAction;
    }

    public sealed class SusHealthReport
    {
        public readonly List<SusHealthIssue> Issues = new();
        public DateTime RanAtUtc;
        public int Blocking => Count(SusIssueSeverity.Blocking);
        public int Errors => Count(SusIssueSeverity.Error);
        public int Warnings => Count(SusIssueSeverity.Warning);
        public int Infos => Count(SusIssueSeverity.Info);
        public bool IsHealthy => Blocking == 0 && Errors == 0;

        int Count(SusIssueSeverity s)
        {
            var n = 0;
            foreach (var i in Issues)
                if (i.Severity == s) n++;
            return n;
        }

        /// <summary>Plain-text dump for clipboard / Unity Console.</summary>
        public string FormatPlain()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== SUS Health === {RanAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Blocking {Blocking}  Errors {Errors}  Warnings {Warnings}  Info {Infos}");
            sb.AppendLine();
            foreach (var i in Issues)
            {
                sb.AppendLine($"{SeverityTag(i.Severity)} {i.Title}");
                if (!string.IsNullOrEmpty(i.Detail) && i.Detail != i.Title)
                    sb.AppendLine($"  {i.Detail}");
                if (!string.IsNullOrEmpty(i.SuggestedFix))
                    sb.AppendLine($"  → {i.SuggestedFix}");
                if (!string.IsNullOrEmpty(i.Source))
                    sb.AppendLine($"  source: {i.Source}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static string SeverityTag(SusIssueSeverity s) => s switch
        {
            SusIssueSeverity.Blocking => "[BLOCK]",
            SusIssueSeverity.Error => "[ERROR]",
            SusIssueSeverity.Warning => "[WARN]",
            _ => "[INFO]",
        };
    }

    public sealed class SusCompileLogEntry
    {
        public DateTime Time;
        public string Kind;   // USS | TMPL | DEFER | GEN | DEL
        public string Message;
    }

    /// <summary>Maps <see cref="SusSetupValidator"/> issues into Inspector health cards.</summary>
    public static class SusHealthRunner
    {
        public const string PrefEchoConsole = "Sharq.Inspector.EchoHealthConsole";
        public const string PrefEchoInfo = "Sharq.Inspector.EchoHealthInfo";

        static string s_lastEchoFingerprint;

        /// <param name="echoToConsole">
        /// When true (default), Error/Warning/Blocking are also written to the Unity Console
        /// (deduped by fingerprint so Auto-run on focus does not spam).
        /// </param>
        /// <param name="forceEcho">Bypass fingerprint (e.g. explicit "Run all checks").</param>
        public static SusHealthReport Run(bool echoToConsole = true, bool forceEcho = false)
        {
            var report = new SusHealthReport { RanAtUtc = DateTime.UtcNow };
            var raw = new List<SusValidationIssue>();
            SusSetupValidator.ValidateAll(raw);

            foreach (var v in raw)
            {
                report.Issues.Add(new SusHealthIssue
                {
                    Id = $"{v.Category}.{StableHash(v.Message)}",
                    Severity = Map(v.Severity),
                    Title = $"[{v.Category}] {Truncate(v.Message, 120)}",
                    Detail = v.Message,
                    Source = "SusSetupValidator",
                    SuggestedFix = v.FixHint,
                    FixAction = SuggestFix(v),
                });
            }

            report.Issues.Add(new SusHealthIssue
            {
                Id = "hr.remote",
                Severity = SusIssueSeverity.Info,
                Title = RemoteHotReloadPushService.IsEnabled
                    ? "Remote hot reload: ON"
                    : "Remote hot reload: OFF",
                Detail = $"URL: {RemoteHotReloadPushService.SessionMcpUrl}",
                Source = "EditorPrefs",
                SuggestedFix = "Toggle in Settings / Connect",
            });

            report.Issues.Add(new SusHealthIssue
            {
                Id = "hr.statepreserve",
                Severity = SusIssueSeverity.Info,
                Title = SusConfig.Instance.HotReloadStatePreserve
                    ? "State preserve (E2b): ON"
                    : "State preserve (E2b): OFF",
                Detail = "Requires Unity pref: Script Changes While Playing = Recompile And Continue Playing",
                Source = "sus.config.json",
            });

            if (echoToConsole && UnityEditor.EditorPrefs.GetBool(PrefEchoConsole, true))
                EchoToUnityConsole(report, forceEcho);

            return report;
        }

        /// <summary>Write Error/Warning/Blocking (and optionally Info) to Unity Console.</summary>
        public static void EchoToUnityConsole(SusHealthReport report, bool force = false)
        {
            if (report == null) return;

            var includeInfo = UnityEditor.EditorPrefs.GetBool(PrefEchoInfo, false);
            var fp = Fingerprint(report, includeInfo);
            if (!force && fp == s_lastEchoFingerprint)
                return;
            s_lastEchoFingerprint = fp;

            var echoed = 0;
            foreach (var issue in report.Issues)
            {
                if (issue.Severity == SusIssueSeverity.Info && !includeInfo)
                    continue;

                var msg = FormatConsoleLine(issue);
                switch (issue.Severity)
                {
                    case SusIssueSeverity.Blocking:
                    case SusIssueSeverity.Error:
                        Debug.LogError(msg);
                        echoed++;
                        break;
                    case SusIssueSeverity.Warning:
                        Debug.LogWarning(msg);
                        echoed++;
                        break;
                    default:
                        Debug.Log(msg);
                        echoed++;
                        break;
                }
            }

            if (echoed == 0 && report.IsHealthy)
                Debug.Log("[SUS Health] ● Healthy — no errors or warnings.");
            else if (echoed > 0)
                Debug.Log($"[SUS Health] Echoed {echoed} issue(s) to Console " +
                          $"(err={report.Errors + report.Blocking}, warn={report.Warnings}).");
        }

        static string FormatConsoleLine(SusHealthIssue i)
        {
            var sb = new StringBuilder();
            sb.Append("[SUS Health] ");
            sb.Append(i.Title);
            if (!string.IsNullOrEmpty(i.SuggestedFix))
                sb.Append(" → ").Append(i.SuggestedFix);
            if (!string.IsNullOrEmpty(i.Source))
                sb.Append(" (").Append(i.Source).Append(')');
            return sb.ToString();
        }

        static string Fingerprint(SusHealthReport report, bool includeInfo)
        {
            var sb = new StringBuilder();
            foreach (var i in report.Issues)
            {
                if (i.Severity == SusIssueSeverity.Info && !includeInfo) continue;
                sb.Append(i.Id).Append('|').Append((int)i.Severity).Append(';');
            }
            return sb.ToString();
        }

        static SusIssueSeverity Map(SusValidationSeverity s) => s switch
        {
            SusValidationSeverity.Error => SusIssueSeverity.Error,
            SusValidationSeverity.Warning => SusIssueSeverity.Warning,
            _ => SusIssueSeverity.Info,
        };

        static Action SuggestFix(SusValidationIssue v)
        {
            if (v.Category == "Config" || v.Message.Contains("sus.config", StringComparison.OrdinalIgnoreCase))
                return () => SusInspectorWindow.OpenTab(SusInspectorTab.Settings);
            if (v.Category.Contains("Package", StringComparison.OrdinalIgnoreCase)
                || v.Message.Contains("Generated", StringComparison.OrdinalIgnoreCase)
                || v.Message.Contains(".g.cs", StringComparison.OrdinalIgnoreCase))
                return () =>
                {
                    SusPackageGenerator.GenerateAll();
                    SusInspectorWindow.OpenTab(SusInspectorTab.Compile);
                };
            if (v.Category.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                return SusSetupWizard.Open;
            return null;
        }

        static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= n) return s ?? "";
            return s.Substring(0, n - 1) + "…";
        }

        static string StableHash(string s)
        {
            unchecked
            {
                var h = 23;
                if (s != null)
                    foreach (var c in s)
                        h = h * 31 + c;
                return (h & 0xFFFF).ToString("x4");
            }
        }
    }
}
