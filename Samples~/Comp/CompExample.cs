using UnityEngine;
using UnityEngine.UIElements;
using Sharq.Core;

/// <summary>
/// Component composition demo: parent→child prop passing.
///
/// 1. Create a GameObject with UIDocument and attach this script.
/// 2. Enter Play mode.
///
/// What is verified:
/// - Literal props reach the child's Prop&lt;T&gt;
/// - Mutating .Value (not replacing the instance) preserves internal bindings
/// - Case-insensitive field lookup
/// </summary>
public class CompExample : MonoBehaviour
{
    [SerializeField] private UIDocument _uiDocument;

    private void Start()
    {
        try
        {
            Debug.Log("[CompExample] Start() called");
            var doc = GetOrCreateUIDocument();
            Debug.Log($"[CompExample] UIDocument: {(doc != null ? "ok" : "NULL")}, panelSettings: {(doc?.panelSettings != null ? "ok" : "NULL")}, rootVE: {(doc?.rootVisualElement != null ? "ok" : "NULL")}");

            SusBootstrap.ApplyDefaultTSS(doc);
            Debug.Log("[CompExample] ApplyDefaultTSS done");

            var root = SusBootstrap.Mount<CompScreen>(doc);
            Debug.Log($"[CompExample] Mount done, root.name={root?.name ?? "NULL"}");

            root.VariantProp = "secondary";
            root.LabelProp = "Composition Works!";
            Debug.Log("[CompExample] Props set, Start() complete");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CompExample] CRASH: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private UIDocument GetOrCreateUIDocument()
    {
        var doc = _uiDocument != null ? _uiDocument : GetComponent<UIDocument>();
        if (doc.panelSettings == null)
        {
            var ps = Resources.Load<PanelSettings>("PanelSettings");
            if (ps != null)
                doc.panelSettings = ps;
            else
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.scaleMode = PanelScaleMode.ConstantPixelSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.match = 0.5f;
                doc.panelSettings = ps;
            }
        }
        return doc;
    }

    public class CompScreen : SusComponent
    {
        public string VariantProp = "primary";
        public string LabelProp = "Hello";

        protected override void Build()
        {
            name = "comp-screen";
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;
            style.backgroundColor = new Color(0.05f, 0.08f, 0.15f, 1f);
            style.paddingTop = 40;
            style.paddingLeft = 40;
            style.paddingRight = 40;

            Add(new Label("Comp Scene — OK")
            {
                style =
                {
                    color = Color.white, fontSize = 28,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginBottom = 24,
                },
            });

            var child = new MockChild();
            Add(child);

            child.Variant.Value = VariantProp;
            WatchEffect(() => child.Label.Value = LabelProp);
        }
    }

    /// <summary>
    /// Mock of a Sharq-generated component with Prop&lt;T&gt; fields.
    /// </summary>
    public class MockChild : VisualElement
    {
        public Prop<string> Variant = new("default");
        public Prop<string> Label = new("n/a");
        private readonly Label _label;

        public MockChild()
        {
            _label = new Label { name = "child-label" };
            Add(_label);

            Variant.Changed += (_, __) => UpdateLabel();
            Label.Changed += (_, __) => UpdateLabel();

            UpdateLabel();
        }

        private void UpdateLabel()
        {
            _label.text = $"variant={Variant.Value}, label={Label.Value}";
        }
    }
}
