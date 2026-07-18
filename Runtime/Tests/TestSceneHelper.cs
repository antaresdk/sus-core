using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// One-shot test scene setup: Camera, Directional Light, EventSystem, UIDocument.
    /// Use when playmode tests need a full scene rather than a bare GameObject.
    ///
    /// Usage:
    /// <code>
    /// [SetUp]
    /// public void SetUp() { TestSceneHelper.Setup(); }
    /// [TearDown]
    /// public void TearDown() { TestSceneHelper.TearDown(); }
    ///
    /// [UnityTest]
    /// public IEnumerator MyTest()
    /// {
    ///     var root = TestSceneHelper.Root;
    ///     // ...
    /// }
    /// </code>
    /// </summary>
    public static class TestSceneHelper
    {
        private static GameObject _rootGo;
        private static UIDocument _doc;

        /// <summary>Scene rootVisualElement after Setup().</summary>
        public static VisualElement Root { get; private set; }

        /// <summary>UIDocument component after Setup().</summary>
        public static UIDocument Doc => _doc;

        /// <summary>
        /// Creates a full test scene:
        ///   - Camera (MainCamera tag) + Directional Light
        ///   - EventSystem (StandaloneInputModule)
        ///   - GameObject with UIDocument + PanelSettings
        /// </summary>
        public static void Setup()
        {
            // Camera
            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0, 0, -10);

            // Light
            var lightGo = new GameObject("Directional Light", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);

            // EventSystem
            var evtGo = new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));

            // UIDocument
            _rootGo = new GameObject("TestSceneUI", typeof(UIDocument));
            _doc = _rootGo.GetComponent<UIDocument>();
            _doc.panelSettings = UIDocumentTestHelper.CreateTestPanelSettings();
            Root = _doc.rootVisualElement;
        }

        /// <summary>
        /// Cleans up all scene objects. Safe to call without Setup().
        /// </summary>
        public static void TearDown()
        {
            if (Root != null)
            {
                Root.Clear();
                Root = null;
            }

            if (_rootGo != null)
            {
                Object.DestroyImmediate(_rootGo);
                _rootGo = null;
                _doc = null;
            }

            // Cleanup Camera, Light, EventSystem (find by name)
            var cam = GameObject.Find("Main Camera");
            if (cam != null) Object.DestroyImmediate(cam);

            var lt = GameObject.Find("Directional Light");
            if (lt != null) Object.DestroyImmediate(lt);

            var evt = GameObject.Find("EventSystem");
            if (evt != null) Object.DestroyImmediate(evt);
        }
    }
}
