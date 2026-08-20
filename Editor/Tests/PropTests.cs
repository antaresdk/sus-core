using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    public class PropTests
    {
        [Test]
        public void Value_GetSet_WritesAndReadsValue()
        {
            var p = new Prop<int>(42);
            Assert.AreEqual(42, p.Value);
            p.Value = 99;
            Assert.AreEqual(99, p.Value);
        }

        [Test]
        public void Changed_Event_FiresOnValueChange()
        {
            var p = new Prop<string>("a");
            string old = null, cur = null;
            p.Changed += (o, c) => { old = o; cur = c; };

            p.Value = "b";

            Assert.AreEqual("a", old);
            Assert.AreEqual("b", cur);
        }

        [Test]
        public void Changed_Event_DoesNotFireOnSameValue()
        {
            var p = new Prop<int>(10);
            int fireCount = 0;
            p.Changed += (_, _) => fireCount++;

            p.Value = 10;
            p.Value = 10;

            Assert.AreEqual(0, fireCount);
        }

        [Test]
        public void ClearSubscribers_DropsHandlersButKeepsValue()
        {
            var p = new Prop<int>(1);
            int changed = 0, bindingNotified = 0;
            p.Changed += (_, _) => changed++;
            p.propertyChanged += (_, _) => bindingNotified++;

            p.ClearSubscribers();
            p.Value = 2;

            Assert.AreEqual(0, changed, "Changed must not fire after ClearSubscribers");
            Assert.AreEqual(0, bindingNotified, "propertyChanged must not fire after ClearSubscribers");
            Assert.AreEqual(2, p.Value, "the value itself must still update");
        }

        [Test]
        public void ClearSubscribers_AllowsResubscribing()
        {
            var p = new Prop<int>(1);
            p.Changed += (_, _) => Assert.Fail("handler from before the clear must not run");
            p.ClearSubscribers();

            int changed = 0;
            p.Changed += (_, _) => changed++;
            p.Value = 2;

            Assert.AreEqual(1, changed);
        }

        [Test]
        public void ImplicitOperator_ReturnsValue()
        {
            var p = new Prop<float>(3.14f);
            float val = p;
            Assert.AreEqual(3.14f, val, 0.001f);
        }

        [Test]
        public void DefaultConstructor_ReturnsDefaultForType()
        {
            Assert.AreEqual(0, new Prop<int>().Value);
            Assert.AreEqual(null, new Prop<string>().Value);
            Assert.AreEqual(false, new Prop<bool>().Value);
        }

        [Test]
        public void MultipleSubscribers_AllReceiveChanged()
        {
            var p = new Prop<int>(0);
            int a = -1, b = -1;
            p.Changed += (_, v) => a = v;
            p.Changed += (_, v) => b = v;

            p.Value = 7;

            Assert.AreEqual(7, a);
            Assert.AreEqual(7, b);
        }

        #region ForceNotify

        [Test]
        public void ForceNotify_FiresChangedWithoutValueChange()
        {
            var p = new Prop<int>(10);
            int fireCount = 0;
            int lastOld = -1, lastNew = -1;
            p.Changed += (o, n) => { fireCount++; lastOld = o; lastNew = n; };

            p.ForceNotify();

            Assert.AreEqual(1, fireCount);
            Assert.AreEqual(10, lastOld);
            Assert.AreEqual(10, lastNew);
            Assert.AreEqual(10, p.Value, "Value should not change");
        }

        [Test]
        public void Mutate_ExecutesMutateAndNotifies()
        {
            var list = new System.Collections.Generic.List<int> { 1, 2, 3 };
            var p = new Prop<System.Collections.Generic.List<int>>(list);
            bool changed = false;
            p.Changed += (_, _) => changed = true;

            p.Mutate(l => l.Add(4));

            Assert.IsTrue(changed);
            Assert.AreEqual(4, p.Value.Count);
            Assert.AreEqual(4, p.Value[3]);
            Assert.AreSame(list, p.Value, "Same reference — no copy");
        }

        [Test]
        public void Mutate_ChainCalls_AllNotify()
        {
            var p = new Prop<int>(0);
            int fireCount = 0;
            p.Changed += (_, _) => fireCount++;

            p.Mutate(v => { });
            p.Mutate(v => { });
            p.Mutate(v => { });

            Assert.AreEqual(3, fireCount);
        }

        #endregion

        #region Select

        [Test]
        public void Select_ReturnsInitialMappedValue()
        {
            var squad = new Prop<int>(5);
            var doubled = squad.Select(s => s * 2);

            Assert.AreEqual(10, doubled.Value);
        }

        [Test]
        public void Select_UpdatesWhenSourceChanges()
        {
            var squad = new Prop<int>(5);
            var doubled = squad.Select(s => s * 2);

            squad.Value = 10;

            Assert.AreEqual(20, doubled.Value);
        }

        [Test]
        public void Select_ChainSelectors()
        {
            var p = new Prop<int>(5);
            var doubled = p.Select(x => x * 2);      // 10
            var asString = doubled.Select(x => $"={x}"); // "=10"

            Assert.AreEqual("=10", asString.Value);

            p.Value = 7;
            Assert.AreEqual("=14", asString.Value);
        }

        #endregion

        #region Custom equality

        [Test]
        public void CustomEquality_FloatPrecision_IgnoresSmallDifference()
        {
            var p = new Prop<float>(1.0f, equals: (a, b) => System.Math.Abs(a - b) < 0.01f);
            int fireCount = 0;
            p.Changed += (_, _) => fireCount++;

            p.Value = 1.005f; // difference 0.005 < 0.01

            Assert.AreEqual(0, fireCount, "Should NOT fire for sub-threshold change");
        }

        [Test]
        public void CustomEquality_FloatPrecision_FiresOnLargeDifference()
        {
            var p = new Prop<float>(1.0f, equals: (a, b) => System.Math.Abs(a - b) < 0.01f);
            int fireCount = 0;
            p.Changed += (_, _) => fireCount++;

            p.Value = 1.5f; // difference 0.5 >= 0.01

            Assert.AreEqual(1, fireCount, "Should fire for above-threshold change");
        }

        [Test]
        public void CustomEquality_AlwaysFalse_FiresEveryTime()
        {
            var p = new Prop<int>(0, equals: (_, _) => false);
            int fireCount = 0;
            p.Changed += (_, _) => fireCount++;

            p.Value = 0; // same value, but equals says "not equal"

            Assert.AreEqual(1, fireCount);
        }

        [Test]
        public void CustomEquality_BackwardCompatible_NoEqualsParam()
        {
            var p = new Prop<int>(42);
            Assert.AreEqual(42, p.Value);
            p.Value = 99;
            Assert.AreEqual(99, p.Value);
        }

        #endregion

        #region AsReadonly

        [Test]
        public void AsReadonly_ReturnsWrappedValue()
        {
            var p = new Prop<string>("hello");
            var r = p.AsReadonly();

            Assert.AreEqual("hello", r.Value);
        }

        [Test]
        public void AsReadonly_ChangedEvent_MirrorsSource()
        {
            var p = new Prop<int>(1);
            var r = p.AsReadonly();
            int lastVal = -1;
            r.Changed += (_, v) => lastVal = v;

            p.Value = 42;

            Assert.AreEqual(42, lastVal);
        }

        [Test]
        public void AsReadonly_ImplicitOperator_Works()
        {
            var p = new Prop<int>(99);
            var r = p.AsReadonly();

            int val = r; // implicit conversion

            Assert.AreEqual(99, val);
        }

        #endregion

        #region Peek — T-1302/T-1206

        [Test]
        public void Peek_ReturnsCurrentValue()
        {
            var p = new Prop<int>(42);
            Assert.AreEqual(42, p.Peek());
            p.Value = 99;
            Assert.AreEqual(99, p.Peek());
        }

        [Test]
        public void Peek_DoesNotRegisterDependency()
        {
            // Mirrors the Computed<T>/ReactiveEffect tracking idiom directly against
            // DependencyTracker (internal, InternalsVisibleTo this assembly) so the test
            // doesn't need a live component/effect to prove the primitive's contract.
            var p = new Prop<int>(1);
            var registered = new System.Collections.Generic.List<IReactiveSource>();

            using (DependencyTracker.Track(src => registered.Add(src)))
            {
                _ = p.Peek();
            }

            Assert.AreEqual(0, registered.Count,
                "Peek() must not call DependencyTracker.RegisterSource — a plain Value get " +
                "would have added this Prop to the tracking scope's dependency list");
        }

        [Test]
        public void Peek_ReadThenValueWrite_DoesNotSelfInvalidate()
        {
            // The T-1302/T-1206 antipattern in miniature: a "SetParam"-shaped accumulator
            // that reads the CURRENT value to compute the next one, then writes it back —
            // all while some effect is tracking. Reading the accumulator source via Peek()
            // must not make the currently-tracking scope a dependent of this Prop, so the
            // WRITE two lines later does not re-invoke that scope's invalidation callback.
            var p = new Prop<System.Collections.Generic.List<int>>(new System.Collections.Generic.List<int> { 1 });
            var invalidations = 0;

            using (DependencyTracker.Track(src =>
                       src.SubscribeInvalidate(() => invalidations++)))
            {
                var next = new System.Collections.Generic.List<int>(p.Peek()) { 2 };
                p.Value = next; // always a new list — reference-unequal, always invalidates IF subscribed
            }

            Assert.AreEqual(0, invalidations,
                "a Peek()-based read-modify-write of the SAME Prop must not invalidate a " +
                "scope that was tracking during the read — Value would have (T-1302/T-1206)");
        }

        [Test]
        public void Value_ReadThenWrite_DoesSelfInvalidate_DemonstratesTheBugPeekFixes()
        {
            // Control case: same shape as the test above but using Value (the pre-fix
            // SusTooltip.SetParam behavior) — documents exactly what Peek() had to avoid.
            var p = new Prop<System.Collections.Generic.List<int>>(new System.Collections.Generic.List<int> { 1 });
            var invalidations = 0;

            using (DependencyTracker.Track(src =>
                       src.SubscribeInvalidate(() => invalidations++)))
            {
                var next = new System.Collections.Generic.List<int>(p.Value) { 2 }; // tracked read — subscribes
                p.Value = next; // fires the subscription just registered, reentrantly
            }

            Assert.AreEqual(1, invalidations,
                "control: Value read-then-write of the SAME Prop DOES self-invalidate when a " +
                "scope is tracking — this is the mechanism behind the 90+ iteration re-entrant " +
                "flush warning (T-1302/T-1206) before the SetParam fix");
        }

        #endregion
    }
}
