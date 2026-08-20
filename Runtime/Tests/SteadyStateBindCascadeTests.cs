using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// Regression for T-1204 — steady-state bind-cascade desync.
    ///
    /// Root cause (see <see cref="SusComponent.ApplyAllBindUpdates"/> remarks): a WatchEffect
    /// that writes a derived Prop, read by a BindText/BindClass on the SAME component instance,
    /// shares that instance's single <c>_bindScheduleItem</c>. When the WatchEffect runs AS that
    /// item's own scheduled callback and its side effect (the derived Prop write) tries to
    /// re-arm the SAME item reentrantly via <c>ScheduleBindUpdate</c>, UITK's scheduler silently
    /// drops the re-arm — the same failure class T-587 fixed for the attach-time flush, but not
    /// for this steady-state invalidation cascade. Symptom: the derived Prop's value is correct
    /// (direct read), but the bound Label.text / CSS class is stuck one generation behind
    /// FOREVER, even after many real frames with nothing left to schedule.
    ///
    /// Live repro this mirrors: sus-game BattleUnitWorldBar.Mounted()'s WatchEffect(ApplyVisual)
    /// sets HpText.Value; Build()'s BindText(HpCounterLabel, () =&gt; HpText.Value) reads it on
    /// the same instance (BattleUnitWorldBarContractTests, T-1115/T-1204).
    /// </summary>
    public class SteadyStateBindCascadeTests : UIDocumentTestHelper
    {
        /// <summary>
        /// Source -&gt; WatchEffect (derives Text/Flag) -&gt; BindText/BindClass, all on ONE
        /// instance — exactly the "derived display prop" idiom flagged as at-risk across
        /// kit/game in T-1204.
        /// </summary>
        private class DerivedDisplayComp : SusComponent
        {
            public Prop<int> Source { get; } = new(0);
            private readonly Prop<string> _text = new("");
            private readonly Prop<bool> _flag = new(false);
            public Label Label { get; } = new Label { name = "derived-label" };
            public VisualElement Chip { get; } = new VisualElement { name = "derived-chip" };

            protected override void Build()
            {
                BindText(Label, () => _text.Value);
                BindClass(Chip, "is-hot", () => _flag.Value);
                Add(Label);
                Add(Chip);
            }

            protected override void Mounted()
            {
                WatchEffect(() =>
                {
                    var v = Source.Value;
                    _text.Value = v.ToString();
                    _flag.Value = v >= 10;
                });
            }
        }

        /// <summary>
        /// Poll instead of a fixed frame count: <c>Mounted()</c> itself is deferred one
        /// scheduler tick past <c>Build()</c>/attach (see <see cref="SusComponent"/> ctor,
        /// "Defer Mounted to next frame"), so even the FIRST render of a Mounted()-driven
        /// WatchEffect can take more than a single <c>yield return null</c> to land — matching
        /// the polling idiom BattleUnitWorldBarContractTests.WaitUntilText uses for the live
        /// T-1204 repro. The important thing this proves is convergence, not a stuck-forever
        /// desync — without the fix, this loop exhausts every iteration and the assert below
        /// (outside the loop) fails.
        /// </summary>
        static IEnumerator WaitUntilText(Label label, string expected, int maxFrames = 30)
        {
            for (var i = 0; i < maxFrames && label.text != expected; i++)
                yield return null;
        }

        static IEnumerator WaitUntilClass(VisualElement el, string className, bool present, int maxFrames = 30)
        {
            for (var i = 0; i < maxFrames && el.ClassListContains(className) != present; i++)
                yield return null;
        }

        [UnityTest]
        public IEnumerator WatchEffect_DerivedProp_BindText_SameInstance_UpdatesOnFirstChange()
        {
            var comp = new DerivedDisplayComp();
            Root.Add(comp);
            yield return WaitUntilText(comp.Label, "0");
            Assert.AreEqual("0", comp.Label.text, "precondition: initial Mounted() run applies");

            comp.Source.Value = 7;
            yield return WaitUntilText(comp.Label, "7");

            Assert.AreEqual("7", comp.Label.text,
                "T-1204: BindText on the SAME instance as the WatchEffect that feeds it must " +
                "reflect the FIRST steady-state change, not stay stuck on the initial render.");
        }

        [UnityTest]
        public IEnumerator WatchEffect_DerivedProp_BindClass_SameInstance_UpdatesOnFirstChange()
        {
            var comp = new DerivedDisplayComp();
            Root.Add(comp);
            yield return WaitUntilText(comp.Label, "0"); // Mounted() has run once steady state reached
            Assert.IsFalse(comp.Chip.ClassListContains("is-hot"), "precondition: starts cold");

            comp.Source.Value = 12;
            yield return WaitUntilClass(comp.Chip, "is-hot", true);

            Assert.IsTrue(comp.Chip.ClassListContains("is-hot"),
                "T-1204: BindClass on the SAME instance as the WatchEffect that feeds it must " +
                "reflect the FIRST steady-state change.");
        }

        [UnityTest]
        public IEnumerator WatchEffect_DerivedProp_BindText_SameInstance_SurvivesMultipleGenerations()
        {
            // Guards against a fix that only unblocks the FIRST cascade (e.g. a one-shot
            // synchronous drain that doesn't loop over newly re-queued actions) but re-breaks
            // on the second real change.
            var comp = new DerivedDisplayComp();
            Root.Add(comp);
            yield return WaitUntilText(comp.Label, "0");

            comp.Source.Value = 1;
            yield return WaitUntilText(comp.Label, "1");
            Assert.AreEqual("1", comp.Label.text, "generation 1 must apply");

            comp.Source.Value = 2;
            yield return WaitUntilText(comp.Label, "2");
            Assert.AreEqual("2", comp.Label.text, "generation 2 must apply");

            comp.Source.Value = 3;
            yield return WaitUntilText(comp.Label, "3");
            Assert.AreEqual("3", comp.Label.text, "generation 3 must apply");
        }
    }
}
