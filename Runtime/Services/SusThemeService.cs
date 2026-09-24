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
        /// USS class that declares an element a SCOPED cascade root: <see cref="ResolveCascadeRoot"/>
        /// stops there instead of climbing on to <see cref="SusBootstrap.TokenCascadeRoot"/>, so a
        /// theme written through this service lands on that element and repaints its subtree only.
        /// Put it on with <see cref="MarkScopedCascadeRoot"/>.
        ///
        /// The name carries no <c>sus-</c> prefix on purpose: this belongs to the same family as
        /// <c>theme-dark</c> / <c>theme-light</c> / <c>breakpoint-*</c> — classes that say what a
        /// cascade root IS, not what a product component looks like. A <c>sus-</c> name here would
        /// also read as a product class on a host that is deliberately not the product (the
        /// storybook shell — see SusStorybookShellIsolation and its EditMode boundary test).
        /// </summary>
        public const string ScopedRootClass = "theme-scope";

        /// <summary>
        /// Declares <paramref name="root"/> a scoped cascade root — "when someone asks for the
        /// cascade root from inside here, the answer is THIS element".
        ///
        /// The case this exists for is a panel that shows a component under a theme of its own
        /// while the application around it keeps another: the storybook stage (plan
        /// ARCH-20260911-STORYBOOK-SHELL D27), a theme preview, a side-by-side compare. Handing
        /// such a subtree to <see cref="SetTheme"/> was not enough on its own, because
        /// <see cref="ResolveCascadeRoot"/> preferred the global cascade root whenever it shared a
        /// panel with the hint — so the class went on the panel root and the whole instrument
        /// repainted (card T-3400: 107 shell elements changed colour from one stage chip).
        ///
        /// Only the declaration is done here. Token sheets are NOT loaded onto the element: the
        /// normal case is a subtree that already sits under a loaded cascade and only needs a
        /// theme of its own. A detached subtree that needs the sheets too asks
        /// <see cref="SusBootstrap.EnsureTokenCascade"/> for them, as before. Idempotent.
        /// </summary>
        public static void MarkScopedCascadeRoot(VisualElement root)
        {
            if (root == null) return;
            if (!root.ClassListContains(ScopedRootClass))
                root.AddToClassList(ScopedRootClass);
        }

        /// <summary>Whether <paramref name="el"/> carries <see cref="ScopedRootClass"/>.</summary>
        public static bool IsScopedCascadeRoot(VisualElement el) =>
            el != null && el.ClassListContains(ScopedRootClass);

        /// <summary>
        /// Resolves the element that owns the design-token cascade (theme classes + L1–L5 sheets).
        /// A scoped root declared by <see cref="MarkScopedCascadeRoot"/> wins; otherwise prefer
        /// <see cref="SusBootstrap.TokenCascadeRoot"/>; never use bare <c>panel.visualTree</c>
        /// when a UIDocument content root was cascaded (sheets/classes would not match).
        /// </summary>
        public static VisualElement ResolveCascadeRoot(VisualElement hint)
        {
            // A declared scope is an ANSWER, not a hint: it is checked before the global root,
            // because the whole point of declaring it is that the global root is the wrong answer
            // here (card T-3400). Nothing changes for a tree that declares no scope — the loop
            // finds nothing and the preference below runs exactly as it did.
            for (var scope = hint; scope != null; scope = scope.parent)
            {
                if (scope.ClassListContains(ScopedRootClass))
                    return scope;
            }

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
            // The panel-wide search is skipped for a SCOPED root (card T-3400): a scope that asked
            // to repaint itself only must not reach the application's overlay host through the back
            // door. A scope with popups of its own owns an OverlayHost inside itself — which is
            // what SusBootstrap.GetOrCreateOverlay(canvas) already gives the storybook stage.
            if (overlayHost == null && !IsScopedCascadeRoot(target) && target.panel?.visualTree != null)
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

            // Trailing sheets of a source component stay last on the copy and follow the
            // source when its scope changes or is cleared (SusComponent.SetTrailingSheets).
            SusComponent.TrackTrailingCopy(from, to);
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
