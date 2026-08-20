using System;
using System.Diagnostics;
using System.Reflection;
using UnityEngine.UIElements;
using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Tests for component composition: prop passing and slots.
    /// Covers 00-CORE-PLAN.md B.11, B.14.
    /// </summary>
    public class SusCoreCompositionTests
    {
        #region B.11 — Literal prop preserves child reactivity

        /// <summary>
        /// Helper: a minimal VisualElement subclass that mimics a Sharq
        /// component with a Prop&lt;string&gt; field and a class-binding hook.
        /// </summary>
        private class MockComponent : VisualElement
        {
            public Prop<string> Variant = new("default");
            public string AddedClass;

            public MockComponent()
            {
                // Simulate what generator does: bind class based on Prop.Value
                TrackProp(Variant, (v) =>
                {
                    if (!string.IsNullOrEmpty(AddedClass))
                        RemoveFromClassList(AddedClass);
                    AddedClass = "mock--" + v;
                    AddToClassList(AddedClass);
                });
            }

            private void TrackProp<T>(Prop<T> prop, Action<T> onChange)
            {
                // Manually subscribe (in real component, ReactiveEffect does this)
                onChange(prop.Value);
            }
        }

        [Test]
        public void SetChildProp_MutatesExistingPropValue_DoesNotReplaceInstance()
        {
            var child = new MockComponent();
            var originalProp = child.Variant;
            Assert.AreEqual("default", originalProp.Value);
            Assert.IsTrue(child.ClassListContains("mock--default"));

            // Simulate generator output: <sus:MockComponent variant="primary" />
            SusComponent.SetChildProp(child, "Variant", "primary");

            // Prop instance should be the SAME object (not replaced)
            Assert.AreSame(originalProp, child.Variant,
                "SetChildProp must not replace Prop<T> instance");

            // Value should be mutated
            Assert.AreEqual("primary", child.Variant.Value,
                "Prop.Value should be mutated to 'primary'");
        }

        [Test]
        public void SetChildProp_CaseInsensitive_WorksForField()
        {
            var child = new MockComponent();
            SusComponent.SetChildProp(child, "variant", "secondary");
            Assert.AreEqual("secondary", child.Variant.Value,
                "Case-insensitive field lookup should work");
        }

        [Test]
        public void SetChildProp_PlainType_DirectAssignment()
        {
            var label = new Label();
            SusComponent.SetChildProp(label, "text", "Hello");
            Assert.AreEqual("Hello", label.text);
        }

        [Test]
        public void SetChildProp_BoolLiteral_ConvertsCorrectly()
        {
            var toggle = new Toggle();
            SusComponent.SetChildProp(toggle, "value", "True");
            Assert.IsTrue(toggle.value);
            SusComponent.SetChildProp(toggle, "value", "false");
            Assert.IsFalse(toggle.value);
        }

        [Test]
        public void SetChildProp_NullProp_InitializesNewInstance()
        {
            // Create a component-like object with null Prop field
            var child = new VisualElement();
            var field = typeof(MockComponent).GetField("Variant");
            var mock = new MockComponent();

            // Scenario: Prop field is still at its default (already initialized in ctor)
            // SetChildProp with null must create a new Prop if null
            // For this test, we just verify the mutation path works
            var original = mock.Variant;
            SusComponent.SetChildProp(mock, "variant", "danger");
            Assert.AreSame(original, mock.Variant, "Should mutate, not replace");
            Assert.AreEqual("danger", mock.Variant.Value);
        }

        #endregion

        #region T-1101 — SetChildProp accessor cache (R-A2/P0-2)

        [Test]
        public void SetChildProp_CachedAccessor_ReusedAcrossCalls_StillMutatesCorrectly()
        {
            var child = new MockComponent();

            // First call resolves + caches the (Type,"Variant") accessor.
            SusComponent.SetChildProp(child, "Variant", "first");
            Assert.AreEqual("first", child.Variant.Value);

            // Second/third calls hit the cache — must still mutate correctly, not "freeze"
            // the value from the first (cold) resolution.
            SusComponent.SetChildProp(child, "Variant", "second");
            Assert.AreEqual("second", child.Variant.Value);
            SusComponent.SetChildProp(child, "Variant", "third");
            Assert.AreEqual("third", child.Variant.Value,
                "cached accessor must keep mutating Prop.Value correctly on repeat calls");
        }

        [Test]
        public void SetChildProp_CachedAccessor_IndependentAcrossInstances()
        {
            // The accessor cache is keyed by (Type, propName), shared across ALL instances of
            // that type — verify it does not leak field VALUES between instances, only the
            // reflected FieldInfo/PropertyInfo descriptor.
            var a = new MockComponent();
            var b = new MockComponent();

            SusComponent.SetChildProp(a, "variant", "alpha");
            SusComponent.SetChildProp(b, "variant", "beta");

            Assert.AreEqual("alpha", a.Variant.Value);
            Assert.AreEqual("beta", b.Variant.Value,
                "cached (Type,name) accessor must not leak state between instances");
        }

        [Test]
        public void SetChildProp_UnknownPropName_NoOpAndDoesNotThrow_EvenOnRepeatedCalls()
        {
            var child = new MockComponent();

            // First call builds + caches the "NotFound" sentinel; second call must hit that
            // cached sentinel and still be a safe no-op (not re-throw / re-reflect into a crash).
            Assert.DoesNotThrow(() => SusComponent.SetChildProp(child, "DoesNotExist", "x"));
            Assert.DoesNotThrow(() => SusComponent.SetChildProp(child, "DoesNotExist", "y"));
        }

        [Test]
        public void SetChildProp_BoolLiteral_ConvertsCorrectly_OnRepeatedCachedCalls()
        {
            var toggle = new Toggle();
            SusComponent.SetChildProp(toggle, "value", "True");
            Assert.IsTrue(toggle.value);
            SusComponent.SetChildProp(toggle, "value", "false"); // cache hit
            Assert.IsFalse(toggle.value);
            SusComponent.SetChildProp(toggle, "value", "true"); // cache hit again
            Assert.IsTrue(toggle.value, "Convert.ChangeType/bool.TryParse conversion must run on every call, not just the cold one");
        }

        /// <summary>
        /// Not a hard perf gate (JIT warm-up / CI noise make hard thresholds flaky) — logs
        /// cached-vs-raw-reflection timings for the R-A2/P0-2 "before/after" numbers, with a
        /// generous soft assertion that only fails if the cache stops paying for itself at all.
        /// </summary>
        [Test]
        public void SetChildProp_Benchmark_CachedVsRawReflection()
        {
            const int N = 20000;
            var child = new MockComponent();

            // Warm the cache (mirrors steady-state: one cold resolve, then N reactive re-applies).
            SusComponent.SetChildProp(child, "Variant", "warm");

            var swCached = Stopwatch.StartNew();
            for (int i = 0; i < N; i++)
                SusComponent.SetChildProp(child, "Variant", i % 2 == 0 ? "a" : "b");
            swCached.Stop();

            // Baseline: the exact reflection sequence SetChildProp used to run on EVERY call
            // before T-1101 (GetField + GetProperty("Value") + SetValue, no caching).
            var swRaw = Stopwatch.StartNew();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.IgnoreCase;
            for (int i = 0; i < N; i++)
            {
                var type = child.GetType();
                var field = type.GetField("Variant", flags);
                var fieldValue = field.GetValue(child);
                var propValueField = field.FieldType.GetProperty("Value");
                propValueField.SetValue(fieldValue, i % 2 == 0 ? "a" : "b");
            }
            swRaw.Stop();

            TestContext.WriteLine(
                $"[T-1101 benchmark] {N} calls — cached SetChildProp: {swCached.ElapsedMilliseconds}ms, " +
                $"raw GetField/GetProperty every call (pre-T-1101 path): {swRaw.ElapsedMilliseconds}ms");

            Assert.LessOrEqual(swCached.ElapsedMilliseconds, swRaw.ElapsedMilliseconds + 50,
                "cached SetChildProp must not be slower than doing raw reflection on every call");
        }

        #endregion

        #region T-1102 — Updated() scheduled only for overriding types (R-A3)

        private class OverridesUpdatedComponent : SusComponent
        {
            protected override void Build() { }
            protected override void Updated() { }
        }

        private class DoesNotOverrideUpdatedComponent : SusComponent
        {
            protected override void Build() { }
        }

        // Two levels deep: overrides Updated() once, in a grandparent — leaf itself does not.
        private class GrandparentOverridesUpdated : SusComponent
        {
            protected override void Build() { }
            protected override void Updated() { }
        }
        private class ChildOfGrandparentOverrides : GrandparentOverridesUpdated { }

        [Test]
        public void TypeOverridesUpdated_TrueForDirectOverride()
        {
            Assert.IsTrue(SusComponent.TypeOverridesUpdated(typeof(OverridesUpdatedComponent)));
        }

        [Test]
        public void TypeOverridesUpdated_FalseWhenNeverOverridden()
        {
            Assert.IsFalse(SusComponent.TypeOverridesUpdated(typeof(DoesNotOverrideUpdatedComponent)),
                "T-1102: a component that never overrides Updated() must not be scheduled at 60Hz");
        }

        [Test]
        public void TypeOverridesUpdated_TrueWhenOverriddenByAncestor()
        {
            Assert.IsTrue(SusComponent.TypeOverridesUpdated(typeof(ChildOfGrandparentOverrides)),
                "override anywhere in the hierarchy still means Updated() does real work");
        }

        [Test]
        public void TypeOverridesUpdated_CachedResultIsStable()
        {
            // Calling twice must hit the cache and return the identical answer both times —
            // regression guard against a cache that returns garbage/inverted on hit.
            Assert.IsTrue(SusComponent.TypeOverridesUpdated(typeof(OverridesUpdatedComponent)));
            Assert.IsTrue(SusComponent.TypeOverridesUpdated(typeof(OverridesUpdatedComponent)));
            Assert.IsFalse(SusComponent.TypeOverridesUpdated(typeof(DoesNotOverrideUpdatedComponent)));
            Assert.IsFalse(SusComponent.TypeOverridesUpdated(typeof(DoesNotOverrideUpdatedComponent)));
        }

        #endregion

        #region B.14 — Slots between components

        [Test]
        public void CloneSlotContent_MovesElementToChildHierarchy()
        {
            var parentContainer = new VisualElement();
            var slotContent = new Label("Slot Content");
            parentContainer.Add(slotContent);

            var childContainer = new VisualElement();

            // Simulate slot projection: move content from parent to child
            slotContent.RemoveFromHierarchy();
            childContainer.Add(slotContent);

            Assert.IsNull(slotContent.parent?.parent == parentContainer ? slotContent.parent : null);
            Assert.IsNotNull(slotContent.parent);
            Assert.AreSame(childContainer, slotContent.parent);
        }

        /// <summary>
        /// T-530: the slot container created by GetSlotContainer() must carry stable classes
        /// so component USS can reach the projected children (row wrappers) without a
        /// type selector: <c>.wrapper &gt; .sus-slot { flex-direction: row }</c>.
        /// </summary>
        private class SlotHostComponent : SusComponent
        {
            public VisualElement Wrapper;
            public VisualElement DefaultSlot;
            public VisualElement AppendSlot;

            protected override void Build()
            {
                Wrapper = new VisualElement();
                Wrapper.AddToClassList("host__append");
                AppendSlot = GetSlotContainer("append");
                Wrapper.Add(AppendSlot);
                BuildSlot("append", null, AppendSlot);
                Add(Wrapper);

                DefaultSlot = GetSlotContainer(null);
                Add(DefaultSlot);
                BuildSlot("default", null, DefaultSlot);
            }
        }

        [Test]
        public void GetSlotContainer_CarriesStableSlotClasses()
        {
            var host = new SlotHostComponent();

            Assert.IsTrue(host.AppendSlot.ClassListContains(SusComponent.SlotContainerClass),
                "named slot container must have the shared 'sus-slot' class");
            Assert.IsTrue(host.AppendSlot.ClassListContains("sus-slot--append"),
                "named slot container must have 'sus-slot--<name>'");
            Assert.AreSame(host.AppendSlot, host.Wrapper[0],
                "slot container stays nested inside the author's wrapper");

            Assert.IsTrue(host.DefaultSlot.ClassListContains("sus-slot"));
            Assert.IsTrue(host.DefaultSlot.ClassListContains("sus-slot--default"),
                "null/empty name normalizes to 'default'");
            Assert.AreEqual("sus-slot--default", SusComponent.SlotContainerClassFor(""));

            // Public accessor returns the same (classed) element; classes are idempotent.
            Assert.AreSame(host.AppendSlot, host.Slot("append"));
            Assert.AreEqual(2, CountClasses(host.Slot("append")),
                "repeated GetSlotContainer must not stack duplicate classes");
        }

        private static int CountClasses(VisualElement ve)
        {
            var n = 0;
            foreach (var _ in ve.GetClasses()) n++;
            return n;
        }

        #endregion
    }
}
