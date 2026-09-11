using NUnit.Framework;
using Sharq.Core.Storybook;
using Sharq.Core.Storybook.Controls;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// The three shapes of <c>[SusDependsOn]</c> the kit/game corpus actually needs (T-3080).
    /// Before this card the attribute knew exactly one of them — "prop X has value V" — and the
    /// corpus had ZERO declarations, so the two shapes below had nowhere to live:
    ///
    /// • OR — <c>SusTextfield.ClearIcon</c> works while <c>Clearable</c> OR <c>PersistentClear</c>
    ///   holds; two plain attributes would have AND'ed and dimmed a working control.
    /// • NEGATION — <c>SusHudUnitCard.Icon</c> / <c>SusBattleToken.Icon</c> paint only while
    ///   <c>ImageSrc</c> is EMPTY (icon is the portrait fallback), and
    ///   <c>SusEquipmentSlot.SlotIcon</c> only while the slot holds no item.
    ///
    /// The condition is judged on the LIVE component, so each case is asserted in both directions.
    /// </summary>
    public class SusDependencyShapesTests
    {
        /// <summary>Carries one prop of every dependency shape found in kit/game (T-3080).</summary>
        sealed class DependencyShapesFixture : SusComponent
        {
            public Prop<bool> Closable = new(false);
            public Prop<bool> Clearable = new(false);
            public Prop<bool> PersistentClear = new(false);
            public Prop<string> ImageSrc = new("");
            public Prop<string> Phase = new("idle");

            /// <summary>Chip shape: the append glyph is replaced by the close button.</summary>
            [SusDependsOn(nameof(Closable), "false")]
            public Prop<string> AppendIcon = new("caret");

            /// <summary>Textfield shape: either switch is enough.</summary>
            [SusDependsOn(nameof(Clearable), Group = "clear")]
            [SusDependsOn(nameof(PersistentClear), Group = "clear")]
            public Prop<string> ClearIcon = new("x");

            /// <summary>Unit-card shape: the glyph is the fallback for a missing portrait.</summary>
            [SusDependsOn(nameof(ImageSrc), Negate = true)]
            public Prop<string> Icon = new("user");

            /// <summary>Both shapes at once: a fallback glyph that a terminal phase replaces.</summary>
            [SusDependsOn(nameof(ImageSrc), Negate = true)]
            [SusDependsOn(nameof(Phase), "completed", Negate = true)]
            public Prop<string> PhaseIcon = new("bolt");

            protected override void Build() { }
        }

        static SusPropInfo Prop(SusComponent c, string name)
        {
            var props = c.DescribeProps();
            for (int i = 0; i < props.Count; i++)
                if (props[i].Name == name) return props[i];
            Assert.Fail("no prop " + name);
            return null;
        }

        [Test]
        public void AValueCondition_HoldsOnlyForThatValue()
        {
            var c = new DependencyShapesFixture();
            var icon = Prop(c, "AppendIcon");

            Assert.That(c.DescribeDependency(icon), Is.EqualTo("Closable = false"));
            Assert.That(c.IsDependencySatisfied(icon), Is.True, "not closable → the glyph shows");

            c.Closable.Value = true;
            Assert.That(c.IsDependencySatisfied(icon), Is.False, "the close button took the slot");
        }

        [Test]
        public void AnOrGroup_HoldsWhenAnyMemberHolds_AndSaysSoInOneLine()
        {
            var c = new DependencyShapesFixture();
            var clear = Prop(c, "ClearIcon");

            Assert.That(c.DescribeDependency(clear), Is.EqualTo("Clearable or PersistentClear"));
            Assert.That(c.IsDependencySatisfied(clear), Is.False, "neither switch is on");

            c.PersistentClear.Value = true;
            Assert.That(c.IsDependencySatisfied(clear), Is.True,
                "PersistentClear alone shows the clear button — an AND would have lied here");

            c.PersistentClear.Value = false;
            c.Clearable.Value = true;
            Assert.That(c.IsDependencySatisfied(clear), Is.True);
        }

        [Test]
        public void ANegatedCondition_HoldsWhileTheOtherPropIsEmpty()
        {
            var c = new DependencyShapesFixture();
            var icon = Prop(c, "Icon");

            Assert.That(c.DescribeDependency(icon), Is.EqualTo("ImageSrc empty"));
            Assert.That(c.IsDependencySatisfied(icon), Is.True, "no portrait → the glyph is what paints");

            c.ImageSrc.Value = "portrait/knight";
            Assert.That(c.IsDependencySatisfied(icon), Is.False, "the portrait covers the glyph");
        }

        [Test]
        public void SeveralUngroupedConditions_StillAnd()
        {
            var c = new DependencyShapesFixture();
            var icon = Prop(c, "PhaseIcon");

            Assert.That(c.DescribeDependency(icon), Is.EqualTo("ImageSrc empty & Phase ≠ completed"));
            Assert.That(c.IsDependencySatisfied(icon), Is.True);

            c.Phase.Value = "completed";
            Assert.That(c.IsDependencySatisfied(icon), Is.False, "one failed condition is enough");

            c.Phase.Value = "idle";
            c.ImageSrc.Value = "portrait/knight";
            Assert.That(c.IsDependencySatisfied(icon), Is.False);
        }

        [Test]
        public void ThePanelDimsAndLabelsTheRow_ForEveryShape()
        {
            var component = new DependencyShapesFixture();
            var panel = new SusControlPanel(component, "Shapes", new SusStoryContext(null, component, null));
            try
            {
                var clear = panel.Find("ClearIcon");
                Assert.That(clear.IsActive, Is.False);
                Assert.That(clear.DependencyNote, Does.Contain("Clearable or PersistentClear"));
                Assert.That(clear.ClassListContains("sb-ctl--inert"), Is.True);

                // Driving one member of the OR-group through the panel revives the row.
                Assert.That(panel.Find("Clearable").SetFromString("true"), Is.True);
                Assert.That(clear.IsActive, Is.True);
                Assert.That(clear.ClassListContains("sb-ctl--inert"), Is.False);

                var icon = panel.Find("Icon");
                Assert.That(icon.IsActive, Is.True, "no portrait yet");
                Assert.That(icon.DependencyNote, Does.Contain("ImageSrc empty"));

                Assert.That(panel.Find("ImageSrc").SetFromString("portrait/knight"), Is.True);
                Assert.That(icon.IsActive, Is.False, "the portrait wins, so the glyph control is inert");
            }
            finally
            {
                panel.Dispose();
            }
        }
    }
}
