using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Inline diagnostics for .sharq files.
    /// Runs after parsing — validates template against best practices.
    /// Replaces a full Roslyn Analyzer for Phase 5.
    /// </summary>
    internal static class SharqValidator
    {
        private static readonly Regex VForPattern = new(
            @"v-for\s*=\s*""[^""]*""",
            RegexOptions.Compiled);

        private static readonly Regex KeyPattern = new(
            @":key\s*=",
            RegexOptions.Compiled);

        public static List<ValidationMessage> Validate(SharqFileModel model)
        {
            var messages = new List<ValidationMessage>();
            if (model == null) return messages;

            if (!SusConfig.Instance.EnableValidation) return messages;

            var template = model.TemplateXml;
            if (string.IsNullOrEmpty(template)) return messages;

            // Missing :key on v-for
            if (SusConfig.Instance.StrictVForKey)
            {
                var forMatches = VForPattern.Matches(template);
                foreach (Match m in forMatches)
                {
                    // Extract the full v-for tag context
                    var forExpr = m.Value;
                    var afterExpr = template.Substring(
                        Math.Min(m.Index + m.Length, template.Length));
                    var tagEnd = afterExpr.IndexOf('>');
                    var tagContext = tagEnd >= 0
                        ? template.Substring(m.Index, m.Length + tagEnd + 1)
                        : forExpr;

                    // Check if :key is in the same tag
                    var hasKey = afterExpr.Length > 0 && afterExpr.Substring(0, Math.Min(tagEnd, afterExpr.Length)).Contains(":key");
                    if (!hasKey)
                    {
                        messages.Add(new ValidationMessage
                        {
                            Severity = ValidationSeverity.Warning,
                            Message = $"v-for without :key — elements will use index-based keys",
                            Context = forExpr.Trim()
                        });
                    }
                }
            }

            // Detect unused script fields (heuristic: fields not used in template)
            if (!string.IsNullOrEmpty(model.ScriptBody))
            {
                var fieldPattern = new Regex(
                    @"public\s+(?<type>[\w<>]+)\s+(?<name>\w+)\s*[=;]",
                    RegexOptions.Compiled);

                foreach (Match m in fieldPattern.Matches(model.ScriptBody))
                {
                    var fieldName = m.Groups["name"].Value;
                    // Skip Prop<T> fields, methods
                    if (m.Groups["type"].Value.StartsWith("Prop<")) continue;

                    // Check if field name appears in template
                    if (!template.Contains(fieldName))
                    {
                        messages.Add(new ValidationMessage
                        {
                            Severity = ValidationSeverity.Info,
                            Message = $"Field '{fieldName}' not referenced in template — consider removing or using [CreateProperty]",
                            Context = fieldName
                        });
                    }
                }
            }

            return messages;
        }

        public static void LogMessages(string sharqName, List<ValidationMessage> messages)
        {
            foreach (var msg in messages)
            {
                var prefix = msg.Severity switch
                {
                    ValidationSeverity.Error => "[Sharq/ERROR]",
                    ValidationSeverity.Warning => "[Sharq/WARN]",
                    _ => "[Sharq/INFO]"
                };

                if (msg.Severity == ValidationSeverity.Warning)
                    UnityEngine.Debug.LogWarning($"{prefix} {sharqName}: {msg.Message} ({msg.Context})");
                else if (msg.Severity == ValidationSeverity.Error)
                    UnityEngine.Debug.LogError($"{prefix} {sharqName}: {msg.Message} ({msg.Context})");
                else
                    UnityEngine.Debug.Log($"{prefix} {sharqName}: {msg.Message}");
            }
        }
    }

    internal class ValidationMessage
    {
        public ValidationSeverity Severity;
        public string Message;
        public string Context;
    }

    internal enum ValidationSeverity
    {
        Info,
        Warning,
        Error
    }
}
