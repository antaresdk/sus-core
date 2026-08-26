using UnityEngine;
using UnityEngine.UIElements;
using Sharq.Core;

namespace Sharq.Core.Examples
{
    /// <summary>
    /// SusKeepAlive sample — wrapper for DOM caching.
    /// All content lives inside a SusComponent (KeepAliveDemoScreen) mounted via SusBootstrap.
    /// Toggles panel visibility every second.
    ///
    /// ABOUT THE "BLINKING SQUARE":
    /// The square (purple rectangle with "KeepAlive Panel" text) appears and disappears
    /// every second — this is NOT a bug, it demonstrates SusKeepAlive:
    ///   Active=true  → panel shown (display: flex)
    ///   Active=false → panel hidden (display: none), BUT the DOM is preserved.
    /// Unlike destroy/recreate, SusKeepAlive keeps element state
    /// (text, scroll, props) while hidden. Useful for the router (keep-alive routes).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SusKeepAliveExample : MonoBehaviour
    {
        [SerializeField] private float _toggleInterval = 1f;
        private KeepAliveDemoScreen _screen;
        private float _timer;
        private UIDocument _uiDocument;

        private void Start()
        {
            _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument.panelSettings == null)
            {
                _uiDocument.panelSettings = Resources.Load<PanelSettings>("PanelSettings");
                if (_uiDocument.panelSettings == null)
                {
                    var ps = ScriptableObject.CreateInstance<PanelSettings>();
                    ps.scaleMode = PanelScaleMode.ConstantPixelSize;
                    ps.referenceResolution = new Vector2Int(1920, 1080);
                    ps.match = 0.5f;
                    _uiDocument.panelSettings = ps;
                }
            }

            SusBootstrap.ApplyDefaultTSS(_uiDocument);
            _screen = SusBootstrap.Mount<KeepAliveDemoScreen>(_uiDocument);
            SusLog.Verbose("[KeepAliveExample] Mounted KeepAliveDemoScreen via SusBootstrap.Mount");
        }

        private void Update()
        {
            if (_screen?.KeepAlive == null) return;
            _timer += Time.deltaTime;
            if (_timer >= _toggleInterval)
            {
                _timer = 0f;
                _screen.KeepAlive.Active = !_screen.KeepAlive.Active;
                SusLog.Verbose($"[KeepAlive] Active={_screen.KeepAlive.Active} " +
                    $"({(_screen.KeepAlive.Active ? "visible" : "hidden — DOM preserved")})");
            }
        }
    }

    /// <summary>
    /// SusKeepAlive demo screen — all content inside a SusComponent.
    /// </summary>
    public class KeepAliveDemoScreen : SusComponent
    {
        public SusKeepAlive KeepAlive;

        protected override void Build()
        {
            // --- Explanation shown in the scene ---
            Add(new Label("SusKeepAlive Demo")
            {
                style =
                {
                    color = Color.white, fontSize = 26, unityTextAlign = TextAnchor.MiddleCenter,
                    marginTop = 20, marginBottom = 10,
                },
            });
            Add(new Label("Purple square toggles once per second: visible ↔ hidden")
            {
                style =
                {
                    color = new Color(0.7f, 0.7f, 0.7f, 1f), fontSize = 16,
                    unityTextAlign = TextAnchor.MiddleCenter, marginBottom = 4,
                },
            });
            Add(new Label("When hidden, DOM state is preserved (not destroyed/recreated)")
            {
                style =
                {
                    color = new Color(0.5f, 0.5f, 0.5f, 1f), fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleCenter, marginBottom = 20,
                },
            });

            var panel = new VisualElement
            {
                name = "cached-panel",
                style =
                {
                    width = 300, height = 200,
                    backgroundColor = new Color(0.2f, 0.2f, 0.35f, 1f),
                    alignItems = Align.Center, justifyContent = Justify.Center,
                    marginTop = 100, marginLeft = 100,
                },
            };
            panel.Add(new Label("KeepAlive Panel")
            {
                style = { color = Color.white, fontSize = 22, unityTextAlign = TextAnchor.MiddleCenter },
            });

            KeepAlive = SusKeepAlive.Wrap(panel);
            Add(KeepAlive);
        }
    }
}
