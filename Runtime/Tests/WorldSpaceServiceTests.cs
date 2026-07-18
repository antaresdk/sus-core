using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    public class WorldSpaceServiceTests
    {
        private GameObject _camGo;
        private Camera _cam;
        private GameObject _docGo;
        private UIDocument _doc;
        private OverlayHost _host;
        private WorldSpaceService _svc;

        [SetUp]
        public void SetUp()
        {
            _camGo = new GameObject("TestCamera", typeof(Camera));
            _cam = _camGo.GetComponent<Camera>();

            _docGo = new GameObject("TestUIDoc", typeof(UIDocument));
            _doc = _docGo.GetComponent<UIDocument>();
            _doc.panelSettings = UIDocumentTestHelper.CreateTestPanelSettings();
            _host = new OverlayHost();
            _doc.rootVisualElement.Add(_host);

            _svc = new WorldSpaceService
            {
                OverlayHost = _host,
                MainCamera = _cam,
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (_svc != null)
            {
                for (int i = _svc.Count - 1; i >= 0; i--)
                    _svc.UnbindTarget(null);
            }
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            if (_docGo != null) Object.DestroyImmediate(_docGo);
        }

        // W6.1
        [UnityTest]
        public IEnumerator BindToWorld_SetsPosition_AfterTick()
        {
            var targetGo = new GameObject("Target");
            targetGo.transform.position = new Vector3(0, 2, 5);
            _cam.transform.position = Vector3.zero;
            _cam.transform.rotation = Quaternion.identity;

            var el = new VisualElement { style = { width = 50, height = 20 } };
            _doc.rootVisualElement.Add(el);

            _svc.Bind(el, targetGo.transform);
            Assert.AreEqual(1, _svc.Count);

            yield return new WaitForEndOfFrame();
            _svc.TickPositions();

            // Position was set — left/top should be finite numbers, not NaN
            Assert.IsFalse(float.IsNaN(el.resolvedStyle.left));
            Assert.IsFalse(float.IsNaN(el.resolvedStyle.top));

            Object.DestroyImmediate(targetGo);
        }

        // W6.2
        [UnityTest]
        public IEnumerator MultipleElements_TickUpdatesAll()
        {
            var t1 = new GameObject("T1");
            var t2 = new GameObject("T2");
            t1.transform.position = new Vector3(-1, 1, 5);
            t2.transform.position = new Vector3(2, 0, 5);
            _cam.transform.position = Vector3.zero;
            _cam.transform.rotation = Quaternion.identity;

            var el1 = new VisualElement { style = { width = 40, height = 10 } };
            var el2 = new VisualElement { style = { width = 40, height = 10 } };
            _doc.rootVisualElement.Add(el1);
            _doc.rootVisualElement.Add(el2);

            _svc.Bind(el1, t1.transform);
            _svc.Bind(el2, t2.transform);
            Assert.AreEqual(2, _svc.Count);

            yield return new WaitForEndOfFrame();
            _svc.TickPositions();

            Assert.AreEqual(2, _svc.Count);
            Assert.AreEqual(DisplayStyle.Flex, el1.style.display.value);
            Assert.AreEqual(DisplayStyle.Flex, el2.style.display.value);

            Object.DestroyImmediate(t1);
            Object.DestroyImmediate(t2);
        }

        // W6.4
        [UnityTest]
        public IEnumerator BehindCamera_HidesElement()
        {
            var targetGo = new GameObject("Target");
            targetGo.transform.position = new Vector3(0, 0, -5);
            _cam.transform.position = Vector3.zero;
            _cam.transform.rotation = Quaternion.identity;

            var el = new VisualElement { style = { width = 30, height = 10 } };
            _host.Add(el);

            _svc.Bind(el, targetGo.transform);

            yield return new WaitForEndOfFrame();
            _svc.TickPositions();

            // Service sets display inline — check style, not resolvedStyle
            Assert.AreEqual(DisplayStyle.None, el.style.display.value,
                "Element should be hidden when target is behind camera");

            Object.DestroyImmediate(targetGo);
        }

        // ─── Edge cases ──────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator BindToWorld_NullTarget_ReturnsNull()
        {
            var el = new VisualElement();
            var result = _svc.Bind(el, null);
            Assert.IsNull(result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BindToWorld_NullElement_ReturnsNull()
        {
            var go = new GameObject("T");
            go.transform.position = Vector3.forward * 5;

            var result = _svc.Bind(null, go.transform);
            Assert.IsNull(result);

            Object.DestroyImmediate(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Tick_NoBindings_ExitsEarly()
        {
            Assert.AreEqual(0, _svc.Count);
            _svc.TickPositions();
            Assert.AreEqual(0, _svc.Count);
            yield return null;
        }
    }
}
