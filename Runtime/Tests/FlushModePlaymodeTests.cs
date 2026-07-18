using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    public class FlushModeTests
    {
        private UIDocument _doc;
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("Test_UIDocument", typeof(UIDocument));
            _doc = go.GetComponent<UIDocument>();
            _doc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _root = _doc.rootVisualElement;
            // Give the root a non-zero size so worldBound resolves
            _root.style.width = 800;
            _root.style.height = 600;
        }

        [TearDown]
        public void TearDown()
        {
            if (_doc != null) Object.DestroyImmediate(_doc.gameObject);
        }

        [UnityTest]
        public IEnumerator PostLayout_RunsAfterLayoutPass()
        {
            var comp = new PostLayoutTestComponent();
            _root.Add(comp);

            // Allow UITK to perform initial layout
            yield return null;
            yield return null;

            Assert.IsTrue(comp.PostLayoutRan, "PostLayout effect should have run after layout");
            Assert.IsTrue(comp.WorldBoundValid, "worldBound should be non-zero when PostLayout runs");
        }

        [UnityTest]
        public IEnumerator PostLayout_DoesNotRunBeforeFirstLayout()
        {
            var comp = new PostLayoutTestComponent();

            // The effect runs immediately but fn is deferred via schedule.Execute
            Assert.IsFalse(comp.PostLayoutRan, "PostLayout should not run synchronously");
            Assert.IsFalse(comp.WorldBoundValid);

            _root.Add(comp);
            yield return null;
            yield return null;

            Assert.IsTrue(comp.PostLayoutRan);
        }

        private class PostLayoutTestComponent : SusComponent
        {
            public bool PostLayoutRan;
            public bool WorldBoundValid;

            protected override void Build()
            {
                style.width = 200;
                style.height = 100;
            }

            protected override void Mounted()
            {
                WatchEffect(() =>
                {
                    // At PostLayout time, this element should already have non-zero worldBound
                    WorldBoundValid = worldBound.width > 0 && worldBound.height > 0;
                    PostLayoutRan = true;
                }, FlushMode.PostLayout);
            }
        }
    }
}
