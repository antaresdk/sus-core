using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    public class SusWorldSpacePanelTests
    {
        private GameObject _camGo;
        private Camera _cam;
        private GameObject _panelGo;
        private SusWorldSpacePanel _panel;
        private WorldSpaceService _svc;

        [SetUp]
        public void SetUp()
        {
            _camGo = new GameObject("TestCamera", typeof(Camera));
            _cam = _camGo.GetComponent<Camera>();
            _cam.transform.position = Vector3.zero;

            _panelGo = new GameObject("WSPanel", typeof(UIDocument), typeof(SusWorldSpacePanel));
            _panel = _panelGo.GetComponent<SusWorldSpacePanel>();
            _panel.TargetCamera = _cam;

            _svc = new WorldSpaceService { MainCamera = _cam };
        }

        [TearDown]
        public void TearDown()
        {
            if (_svc != null && _svc.IsWorldSpaceMode)
                _svc.UseScreenSpacePanel();
            if (_panelGo != null) Object.DestroyImmediate(_panelGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
        }

        // ─── W7.2: Attach / Detach / Re-attach ───────────────────────────────

        [UnityTest]
        public IEnumerator AttachElement_AddsToPanel()
        {
            var target = new GameObject("Target");
            target.transform.position = new Vector3(1, 2, 10);

            var el = new VisualElement { style = { width = 40, height = 10 } };
            _panel.AttachElement(el, target.transform);
            Assert.AreEqual(1, _panel.Count);
            yield return null;
            Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator DetachElement_RemovesFromPanel()
        {
            var target = new GameObject("Target");
            target.transform.position = Vector3.forward * 5;
            var el = new VisualElement();
            _panel.AttachElement(el, target.transform);
            Assert.AreEqual(1, _panel.Count);
            _panel.DetachElement(el);
            Assert.AreEqual(0, _panel.Count);
            yield return null;
            Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator AttachElement_NullTarget_DoesNotThrow()
        {
            var el = new VisualElement();
            _panel.AttachElement(el, null);
            Assert.AreEqual(0, _panel.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AttachElement_NullElement_DoesNotThrow()
        {
            var target = new GameObject("T");
            _panel.AttachElement(null, target.transform);
            Assert.AreEqual(0, _panel.Count);
            yield return null;
            Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator Reattach_SameElement_DoesNotDuplicate()
        {
            var t1 = new GameObject("T1");
            var t2 = new GameObject("T2");
            t1.transform.position = new Vector3(1, 0, 10);
            t2.transform.position = new Vector3(2, 0, 10);

            var el = new VisualElement();
            _panel.AttachElement(el, t1.transform);
            _panel.AttachElement(el, t2.transform);
            Assert.AreEqual(1, _panel.Count, "Re-attach should not duplicate");
            yield return null;
            Object.DestroyImmediate(t1);
            Object.DestroyImmediate(t2);
        }

        // ─── DetachTarget / DetachAll ────────────────────────────────────────

        [UnityTest]
        public IEnumerator DetachTarget_RemovesAllForTransform()
        {
            var target = new GameObject("Target");
            target.transform.position = Vector3.forward * 5;
            _panel.AttachElement(new VisualElement(), target.transform);
            _panel.AttachElement(new VisualElement(), target.transform);
            Assert.AreEqual(2, _panel.Count);
            _panel.DetachTarget(target.transform);
            Assert.AreEqual(0, _panel.Count);
            yield return null;
            Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator DetachAll_ClearsEverything()
        {
            var t1 = new GameObject("T1");
            var t2 = new GameObject("T2");
            t1.transform.position = new Vector3(1, 0, 10);
            t2.transform.position = new Vector3(2, 0, 10);
            _panel.AttachElement(new VisualElement(), t1.transform);
            _panel.AttachElement(new VisualElement(), t2.transform);
            Assert.AreEqual(2, _panel.Count);
            _panel.DetachAll();
            Assert.AreEqual(0, _panel.Count);
            yield return null;
            Object.DestroyImmediate(t1);
            Object.DestroyImmediate(t2);
        }

        // ─── W7.3: Dead target auto-cleanup ──────────────────────────────────

        [UnityTest]
        public IEnumerator DeadTarget_AutoRemoved_OnLateUpdate()
        {
            var target = new GameObject("Target");
            target.transform.position = Vector3.forward * 5;
            var el = new VisualElement();
            _panel.AttachElement(el, target.transform);
            Assert.AreEqual(1, _panel.Count);

            Object.DestroyImmediate(target);
            yield return new WaitForEndOfFrame();
            Assert.AreEqual(0, _panel.Count, "Dead target should be auto-detached");
        }

        // ─── W7.3: Billboard / Distance Scale smoke ──────────────────────────

        [UnityTest]
        public IEnumerator Billboard_DoesNotThrow()
        {
            _panel.EnableBillboard = true;
            _panel.EnableDistanceScaling = false;
            var target = new GameObject("Target");
            target.transform.position = new Vector3(5, 0, 10);
            _panel.AttachElement(new VisualElement(), target.transform);
            yield return new WaitForEndOfFrame();
            Assert.AreEqual(1, _panel.Count);
            Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator DistanceScale_DoesNotThrow()
        {
            _panel.EnableBillboard = false;
            _panel.EnableDistanceScaling = true;
            _panel.BaseDistance = 10f;
            var target = new GameObject("Target");
            target.transform.position = new Vector3(0, 0, 20);
            _panel.AttachElement(new VisualElement(), target.transform);
            yield return new WaitForEndOfFrame();
            Assert.AreEqual(1, _panel.Count);
            Object.DestroyImmediate(target);
        }

        // ─── W7.4: Mode toggle via WorldSpaceService ─────────────────────────

        [UnityTest]
        public IEnumerator UseWorldSpacePanel_SetsMode()
        {
            Assert.IsFalse(_svc.IsWorldSpaceMode);
            _svc.UseWorldSpacePanel(_panel);
            Assert.IsTrue(_svc.IsWorldSpaceMode);
            Assert.AreSame(_panel, _svc.WorldSpacePanel);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UseScreenSpacePanel_ClearsMode()
        {
            _svc.UseWorldSpacePanel(_panel);
            _svc.UseScreenSpacePanel();
            Assert.IsFalse(_svc.IsWorldSpaceMode);
            Assert.IsNull(_svc.WorldSpacePanel);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BindToWorld_InWorldSpaceMode_DelegatesToPanel()
        {
            _svc.UseWorldSpacePanel(_panel);
            var target = new GameObject("Target");
            target.transform.position = Vector3.forward * 5;
            var el = new VisualElement();
            var binding = _svc.Bind(el, target.transform);
            Assert.IsNotNull(binding);
            Assert.AreEqual(1, _panel.Count);
            Assert.AreEqual(1, _svc.Count);
            yield return null;
            Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator Tick_InWorldSpaceMode_IsNoOp()
        {
            _svc.UseWorldSpacePanel(_panel);
            var target = new GameObject("Target");
            target.transform.position = Vector3.forward * 5;
            _svc.Bind(new VisualElement(), target.transform);
            yield return new WaitForEndOfFrame();
            _svc.TickPositions();
            Assert.AreEqual(1, _panel.Count);
            Object.DestroyImmediate(target);
        }
    }
}
