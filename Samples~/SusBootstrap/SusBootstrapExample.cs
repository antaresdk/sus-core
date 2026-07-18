using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Sharq.Core;

/// <summary>
/// Showcase of the entire sus-core design-token system on one screen:
/// themes, colors, fonts, sizes, breakpoints, icons.
///
/// How to run:
/// 1. Open ThemeShowcase.unity (in this folder) and press Play, OR
/// 2. Create an empty GameObject, attach this script (UIDocument is added automatically),
///    assign the ThemeShowcase StyleSheet to Showcase Style Sheet, press Play.
///
/// What is demonstrated:
/// - SusBootstrap.Mount → EventSystem + token cascade (_theme → design-tokens →
///   _font → _icon → _global) + SusBreakpointService.Attach on root.
/// - SusThemeService.SetTheme → theme as a class on root (T key to toggle).
/// - All colors/sizes/fonts come ONLY via var(--sus-*) in ThemeShowcase.uss.
/// - SusBreakpointService — reactive badge with the current breakpoint.
/// - SusIcon — icons from the registry, recolored for the theme via tint token.
///
/// Keys: T — Dark — Light.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class SusBootstrapExample : MonoBehaviour
{
    [SerializeField] private UIDocument _uiDocument;

    [Tooltip("Showcase styles (ThemeShowcase.uss). All rules use var(--sus-*).")]
    [SerializeField] private StyleSheet _showcaseStyleSheet;

    private SusTheme _theme = SusTheme.Dark;

    private void Start()
    {
        _uiDocument = GetOrCreateUIDocument();
        var root = _uiDocument.rootVisualElement;

        // Add showcase styles on root — visible to all screen descendants.
        if (_showcaseStyleSheet != null)
            root.styleSheets.Add(_showcaseStyleSheet);
        else
            Debug.LogWarning("[SusExample] Showcase StyleSheet is not assigned — " +
                             "color swatches will have no token styles. Assign ThemeShowcase.uss.");

        // Mount loads the token cascade, creates EventSystem, and attaches SusBreakpointService.
        SusBootstrap.ApplyDefaultTSS(_uiDocument);
        SusBootstrap.Mount<ThemeShowcaseScreen>(_uiDocument);

        // Portal host for tooltips/modals.
        SusBootstrap.GetOrCreateOverlay(root);

        // Theme — once at startup (class on root).
        SusThemeService.Instance.SetTheme(root, _theme);

        AddKeyHint("T — toggle theme");

        Debug.Log("[SusExample] Showcase mounted. T — toggle theme.");
    }

    private void Update()
    {
        if (_uiDocument == null) return;
        if (Input.GetKeyDown(KeyCode.T))
        {
            _theme = _theme == SusTheme.Dark ? SusTheme.Light : SusTheme.Dark;
            SusThemeService.Instance.SetTheme(_uiDocument.rootVisualElement, _theme);
            Debug.Log($"[SusExample] Theme → {_theme}");
        }
    }

    private void AddKeyHint(string text)
    {
        var hint = new Label(text)
        {
            name = "key-hint",
            pickingMode = PickingMode.Ignore
        };
        hint.style.position = Position.Absolute;
        hint.style.left = 0;
        hint.style.right = 0;
        hint.style.bottom = 0;
        hint.style.paddingTop = 8;
        hint.style.paddingBottom = 8;
        hint.style.paddingLeft = 12;
        hint.style.paddingRight = 12;
        hint.style.backgroundColor = new Color(0f, 0f, 0f, 0.65f);
        hint.style.color = new Color(0.9f, 0.9f, 0.95f);
        hint.style.fontSize = 14;
        hint.style.unityTextAlign = TextAnchor.MiddleCenter;
        hint.style.whiteSpace = WhiteSpace.Normal;
        _uiDocument.rootVisualElement.Add(hint);
    }

    private UIDocument GetOrCreateUIDocument()
    {
        var doc = _uiDocument != null ? _uiDocument : GetComponent<UIDocument>();
        if (doc.panelSettings == null)
        {
            var ps = Resources.Load<PanelSettings>("PanelSettings");
            if (ps != null) doc.panelSettings = ps;
            else doc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        }
        return doc;
    }

    /// <summary>Root showcase screen. Builds a tree from token classes.</summary>
    private class ThemeShowcaseScreen : SusComponent
    {
        private Label _bpBadge;
        private readonly List<VisualElement> _iconImages = new();

        protected override void Build()
        {
            name = "theme-showcase-screen";
            AddToClassList("showcase-root");

            BuildHeader();
            BuildColors();
            BuildTypography();
            BuildIcons();

            // Primary tint — USS var() across stylesheets does NOT re-resolve
            // for -unity-background-image-tint-color in Unity UITK.
            // C# fallback: reactively set tint based on theme.
            ApplyIconTints();
            Watch(SusThemeService.Current, (_, __) => ApplyIconTints());

            RegisterCallback<AttachToPanelEvent>(_ => WireBreakpoint());
        }

        private void ApplyIconTints()
        {
            var isDark = SusThemeService.Current.Value == SusTheme.Dark;
            var tint = new StyleColor(new Color(
                isDark ? 0.94f : 0.06f,
                isDark ? 0.94f : 0.06f,
                isDark ? 0.94f : 0.06f));
            foreach (var img in _iconImages)
            {
                if (img != null) img.style.unityBackgroundImageTintColor = tint;
            }
        }

        private void BuildHeader()
        {
            var header = new VisualElement { name = "header" };
            header.AddToClassList("showcase-header");

            var title = new Label("SUS — Design Tokens");
            title.AddToClassList("showcase-title");
            header.Add(title);

            var spacer = new VisualElement();
            spacer.AddToClassList("showcase-spacer");
            header.Add(spacer);

            _bpBadge = new Label("—");
            _bpBadge.AddToClassList("showcase-badge");
            header.Add(_bpBadge);

            var themeBtn = new Label("Theme (T)");
            themeBtn.AddToClassList("showcase-btn");
            themeBtn.RegisterCallback<ClickEvent>(_ => ToggleTheme());
            header.Add(themeBtn);

            Add(header);
        }

        private void BuildColors()
        {
            var section = Section("Colors — var(--sus-*)");
            var row = new VisualElement();
            row.AddToClassList("swatch-row");

            AddSwatch(row, "primary", "swatch--primary");
            AddSwatch(row, "secondary", "swatch--secondary");
            AddSwatch(row, "success", "swatch--success");
            AddSwatch(row, "warning", "swatch--warning");
            AddSwatch(row, "error", "swatch--error");

            section.Add(row);
            Add(section);
        }

        private void BuildTypography()
        {
            var section = Section("Typography — var(--sus-font-*)");

            AddType(section, "Hero 32", "type-hero");
            AddType(section, "Heading 24", "type-heading");
            AddType(section, "Subtitle 18", "type-subtitle");
            AddType(section, "Body 14", "type-body");
            AddType(section, "Small 12", "type-small");
            AddType(section, "Caption 10", "type-caption");

            Add(section);
        }

        private void BuildIcons()
        {
            var section = Section("Icons — SusIcon + tint token");
            var row = new VisualElement();
            row.AddToClassList("icon-row");

            // Regular — tinted with --sus-text-primary (inherited from .sus-icon-bg).
            foreach (var alias in new[] { "settings", "user", "star", "bell", "calendar", "lock" })
                row.Add(MakeIcon(alias, null));

            // Accent — override tint to --sus-primary / --sus-success.
            row.Add(MakeIcon("fire", "demo-icon--accent"));
            row.Add(MakeIcon("check", "demo-icon--success"));

            section.Add(row);
            Add(section);
        }

        // ─── helpers ─────────────────────────────────────────────

        private VisualElement Section(string titleText)
        {
            var section = new VisualElement();
            section.AddToClassList("showcase-section");
            var title = new Label(titleText);
            title.AddToClassList("showcase-section__title");
            section.Add(title);
            return section;
        }

        private void AddSwatch(VisualElement row, string label, string variantClass)
        {
            var swatch = new VisualElement();
            swatch.AddToClassList("swatch");
            swatch.AddToClassList(variantClass);
            var l = new Label(label);
            l.AddToClassList("swatch__label");
            swatch.Add(l);
            row.Add(swatch);
        }

        private void AddType(VisualElement section, string text, string typeClass)
        {
            var l = new Label(text);
            l.AddToClassList("type-sample");
            l.AddToClassList(typeClass);
            section.Add(l);
        }

        private SusIcon MakeIcon(string alias, string extraClass)
        {
            var icon = new SusIcon(alias);
            icon.AddToClassList("demo-icon");
            if (extraClass != null) icon.AddToClassList(extraClass);

            // Store image ref for C# tint updates — USS var() across
            // stylesheets does NOT re-resolve for -unity-background-image-tint-color.
            var img = icon.Q<VisualElement>(className: "sus-icon__image");
            if (img != null)
                _iconImages.Add(img);

            return icon;
        }

        private void ToggleTheme()
        {
            var root = panel?.visualTree;
            if (root == null) return;
            var next = SusThemeService.Current.Value == SusTheme.Dark ? SusTheme.Light : SusTheme.Dark;
            SusThemeService.Instance.SetTheme(root, next);
        }

        private void WireBreakpoint()
        {
            var root = panel?.visualTree;
            if (root == null) return;

            var svc = SusBreakpointService.For(root);
            UpdateBadge(svc.Current.Value);
            // Prop.Changed — reactive badge update on resize.
            svc.Current.Changed += (_, next) => UpdateBadge(next);
        }

        private void UpdateBadge(Breakpoint bp)
        {
            if (_bpBadge != null) _bpBadge.text = bp.ToString().ToLowerInvariant();
        }
    }
}
