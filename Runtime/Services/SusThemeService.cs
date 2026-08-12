using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Theme switcher — manages .theme-{name} CSS class on the cascade root
    /// VisualElement. Dark is default.
    ///
    /// Usage:
    /// <code>
    /// // Startup — once
    /// SusThemeService.Instance.SetTheme(root, SusTheme.Dark);
    ///
    /// // Toggle
    /// SusThemeService.Instance.SetTheme(root, SusTheme.Light);
    ///
    /// // Custom theme
    /// SusThemeService.Instance.SetTheme(root, new SusTheme("midnight"));
    ///
    /// // React to theme changes
    /// Watch(SusThemeService.Current, (_, next) => AdaptToTheme(next));
    /// </code>
    /// </summary>
    public class SusThemeService
    {
        private static SusThemeService s_instance;

        /// <summary>Singleton instance.</summary>
        public static SusThemeService Instance => s_instance ??= new SusThemeService();

        /// <summary>Reactive theme prop — components can Watch() it.</summary>
        public static Prop<SusTheme> Current { get; } = new Prop<SusTheme>(SusTheme.Dark);

        private SusThemeService() { }

        /// <summary>
        /// Resolves the element that owns the design-token cascade (theme classes + L1–L5 sheets).
        /// Prefer <see cref="SusBootstrap.TokenCascadeRoot"/>; never use bare <c>panel.visualTree</c>
        /// when a UIDocument content root was cascaded (sheets/classes would not match).
        /// </summary>
        public static VisualElement ResolveCascadeRoot(VisualElement hint)
        {
            var cascaded = SusBootstrap.TokenCascadeRoot;
            if (cascaded != null)
            {
                if (hint == null || hint.panel == null || cascaded.panel == hint.panel)
                    return cascaded;
            }

            if (hint == null) return null;

            for (var el = hint; el != null; el = el.parent)
            {
                if (el.ClassListContains("theme-dark") || el.ClassListContains("theme-light"))
                    return el;
            }

            var vt = hint.panel?.visualTree;
            if (vt != null)
            {
                for (int i = 0; i < vt.hierarchy.childCount; i++)
                {
                    var child = vt.hierarchy[i];
                    if (child.ClassListContains("theme-dark") || child.ClassListContains("theme-light"))
                        return child;
                    if (child.styleSheets.count > 0)
                        return child;
                }
            }

            return hint;
        }

        /// <summary>
        /// Applies a theme to the cascade root by removing all
        /// .theme-* classes and adding .theme-{name}. Idempotent.
        ///
        /// Also applies to the OverlayHost inside the root so that
        /// popups, tooltips, and modals resolve theme tokens correctly.
        /// </summary>
        public void SetTheme(VisualElement root, SusTheme theme)
        {
            if (root == null && SusBootstrap.TokenCascadeRoot == null) return;

            var target = ResolveCascadeRoot(root);
            if (target == null) return;

            ReplaceThemeClass(target, theme);

            // OverlayHost (popups, tooltips, modals) — needs theme class
            // for --thm-* / --sk-* variable resolution. Name is "overlay-host", not a USS class.
            var overlayHost = target.Q<OverlayHost>(name: OverlayHost.OverlayHostName);
            if (overlayHost == null && target.panel?.visualTree != null)
                overlayHost = target.panel.visualTree.Q<OverlayHost>(name: OverlayHost.OverlayHostName);

            if (overlayHost != null)
            {
                ReplaceThemeClass(overlayHost, theme);
                // Every open overlay child must carry the theme class too —
                // USS var() through :root chains is unreliable on reparented elements.
                ApplyThemeClassesToSubtree(overlayHost, theme);
            }

            Current.Value = theme;
        }

        /// <summary>
        /// Applies .theme-{name} from <see cref="Current"/> to one element.
        /// Also loads token USS sheets directly on the element — overlay children
        /// often need them because var() chains through :root are unreliable after
        /// reparenting to OverlayHost.
        /// </summary>
        public static void ApplyThemeClasses(VisualElement element)
        {
            if (element == null) return;
            ReplaceThemeClass(element, Current.Value);

            // Load token cascade so var(--sk-*) resolves on the element itself
            SusBootstrap.EnsureTokenCascade(element);
        }

        /// <summary>
        /// Copies companion styleSheets from source to overlay content so scoped
        /// Sharq rules still match after reparenting to OverlayHost.
        /// </summary>
        public static void CopyStyleSheets(VisualElement from, VisualElement to)
        {
            if (from == null || to == null) return;
            for (int i = 0; i < from.styleSheets.count; i++)
            {
                var sheet = from.styleSheets[i];
                if (!to.styleSheets.Contains(sheet))
                    to.styleSheets.Add(sheet);
            }
        }

        /// <summary>
        /// Removes all .theme-* classes and adds .theme-{name} on the element.
        /// </summary>
        private static void ReplaceThemeClass(VisualElement el, SusTheme theme)
        {
            // Remove old theme classes (dark, light, and any custom)
            el.RemoveFromClassList("theme-dark");
            el.RemoveFromClassList("theme-light");
            // The current prop stores the previously-active theme — remove its class too
            if (Current.Value.Name != theme.Name)
                el.RemoveFromClassList($"theme-{Current.Value.Name}");

            // Add new theme class
            el.AddToClassList(theme.CssClass);
        }

#if UNITY_EDITOR
        // With Domain Reload disabled the singleton and the static Current prop survive
        // leaving Play Mode, so Current would accumulate one set of handlers per session,
        // each closing over elements of a destroyed panel.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            Current.ClearSubscribers();
            Current.Value = SusTheme.Dark;
        }
#endif

        private static void ApplyThemeClassesToSubtree(VisualElement node, SusTheme theme)
        {
            if (node == null) return;
            ReplaceThemeClass(node, theme);

            int count = node.hierarchy.childCount;
            for (int i = 0; i < count; i++)
                ApplyThemeClassesToSubtree(node.hierarchy[i], theme);
        }
    }
}
