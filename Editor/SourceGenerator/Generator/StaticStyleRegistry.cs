using System.Collections.Generic;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Deduplicating registry of inline <c>style="…"</c> strings → USS class names
    /// (<c>sharq-{Component}-sN</c>) for one <see cref="BuildMethodGenerator"/> run.
    /// </summary>
    internal sealed class StaticStyleRegistry
    {
        private int _styleCounter;
        private readonly Dictionary<string, string> _generatedStyles = new();

        /// <summary>
        /// Maps raw inline style strings to generated USS class names.
        /// Key: "font-size: 24px; color: white;", Value: "sharq-ComponentName-s0".
        /// </summary>
        internal IReadOnlyDictionary<string, string> Styles => _generatedStyles;

        internal void Clear()
        {
            _styleCounter = 0;
            _generatedStyles.Clear();
        }

        /// <summary>
        /// Registers an inline style string for USS generation.
        /// Returns a deduplicated class name (e.g. "sharq-MyComponent-s0"), or null if empty.
        /// Multiple elements with identical styles share one class.
        /// </summary>
        internal string Register(string componentName, string styleStr)
        {
            if (string.IsNullOrEmpty(styleStr)) return null;

            var normalized = styleStr.Trim();
            if (string.IsNullOrEmpty(normalized)) return null;

            if (!_generatedStyles.TryGetValue(normalized, out var styleClass))
            {
                styleClass = $"sharq-{componentName}-s{_styleCounter++}";
                _generatedStyles[normalized] = styleClass;
            }
            return styleClass;
        }
    }
}
