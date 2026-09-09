using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// The rule the storybook control panel stands on (T-3096): "nobody reads this prop"
    /// (<see cref="SusPropInfo.Dead"/>) is an answer only AFTER <c>Mounted()</c> has run.
    ///
    /// The kadr kit-button.png (T-3041) printed "dead props: Text · PrependIcon · AppendIcon ·
    /// Icon" under a SusButton that renders all four: those are exactly the props whose readers
    /// live in <c>Mounted()</c>, which the constructor DEFERS by a frame, while the thirteen props
    /// the template's <c>:class</c> bindings read in <c>Build()</c> looked alive. Nothing was wrong
    /// with the button — the observation was taken one frame too early. Hence
    /// <see cref="SusComponent.IsMounted"/> / <see cref="SusComponent.MountCompleted"/>, which
    /// zone D now waits for.
    /// </summary>
    public class MountedPropObservationTests : UIDocumentTestHelper
    {
        sealed class LateReaderComp : SusComponent
        {
            /// <summary>Read by a Build()-time binding — alive from the constructor on.</summary>
            public Prop<bool> Active = new(false);

            /// <summary>Read only from Mounted(), exactly like SusButton.Text.</summary>
            public Prop<string> Caption = new("");

            public readonly Label Label = new();

            protected override void Build()
            {
                BindClass(this, "is-active", () => Active.Value);
                Add(Label);
            }

            protected override void Mounted()
            {
                Watch(Caption, (_, text) => Label.text = text);
                Label.text = Caption.Value;
            }
        }

        static SusPropInfo Prop(SusComponent c, string name)
        {
            var props = c.DescribeProps();
            for (int i = 0; i < props.Count; i++)
                if (props[i].Name == name) return props[i];
            Assert.Fail("no prop " + name);
            return null;
        }

        [UnityTest]
        public IEnumerator APropReadOnlyFromMounted_LooksDeadUntilTheComponentMounts()
        {
            var comp = new LateReaderComp();

            Assert.That(comp.IsMounted, Is.False, "the constructor defers Mounted() by a frame");
            Assert.That(Prop(comp, "Caption").Dead, Is.True,
                "before the mount there is nothing to observe — this is the trap, not a defect");
            Assert.That(Prop(comp, "Active").Dead, Is.False,
                "Build() bindings read their props immediately, which is why only SOME props looked dead");

            Root.Add(comp);
            yield return null;
            yield return null;

            Assert.That(comp.IsMounted, Is.True, "IsMounted is what makes the moment falsifiable");
            Assert.That(Prop(comp, "Caption").Dead, Is.False,
                "the Mounted() watcher is a reader: the prop was never dead");
        }

        [UnityTest]
        public IEnumerator MountCompleted_FiresOnceAfterMounted()
        {
            var comp = new LateReaderComp();
            int fired = 0;
            bool mountedWhenRaised = false;
            comp.MountCompleted += () => { fired++; mountedWhenRaised = comp.IsMounted; };

            Root.Add(comp);
            yield return null;
            yield return null;

            Assert.That(fired, Is.EqualTo(1));
            Assert.That(mountedWhenRaised, Is.True, "handlers must see the finished state");

            comp.RemoveFromHierarchy();
            Root.Add(comp);
            yield return null;

            Assert.That(fired, Is.EqualTo(1), "one-shot: a re-attach is not a second mount");
        }
    }
}
