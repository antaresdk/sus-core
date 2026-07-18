using System;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Central hub for Sharq compile notifications, publisher-agnostic.
    /// Both generation contours publish here — <see cref="SharqFileImporter"/>
    /// (project contour, Assets/) and <see cref="SharqBatchCompiler"/> (package
    /// contour, sharq.gen.json) — so subscribers like UssHotReloadService listen
    /// in exactly one place and automatically cover every contour.
    /// </summary>
    public static class SharqCompileEvents
    {
        /// <summary>
        /// USS artifacts for a component were (re)generated.
        /// className: e.g. "SusButton"; ussPaths: absolute paths to the written .uss files.
        /// </summary>
        public static event Action<string, string[]> OnUssGenerated;

        /// <summary>
        /// All generated artifacts for a component were removed
        /// (source .sharq deleted or renamed). className: e.g. "SusButton".
        /// </summary>
        public static event Action<string> OnUssDeleted;

        /// <summary>
        /// The &lt;template&gt; section of a .sharq file changed (but &lt;script&gt; did not).
        /// className: e.g. "SusButton"; templateXml: raw &lt;template&gt; body (inner XML,
        /// not including the &lt;template&gt; wrapper tag).
        /// Subscribed by TemplateHotReloadService to attempt in-place tree rebuild via
        /// SharqTemplateInterpreter without domain reload.
        /// </summary>
        public static event Action<string, string> OnTemplateChanged;

        internal static void RaiseUssGenerated(string className, string[] ussPaths)
        {
            if (string.IsNullOrEmpty(className) || ussPaths == null || ussPaths.Length == 0) return;
            OnUssGenerated?.Invoke(className, ussPaths);
        }

        internal static void RaiseUssDeleted(string className)
        {
            if (string.IsNullOrEmpty(className)) return;
            OnUssDeleted?.Invoke(className);
        }

        internal static void RaiseTemplateChanged(string className, string templateXml)
        {
            if (string.IsNullOrEmpty(className)) return;
            OnTemplateChanged?.Invoke(className, templateXml);
        }
    }
}
