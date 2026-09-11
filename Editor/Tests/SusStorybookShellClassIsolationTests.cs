using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Card T-3388: the shell wears its OWN classes, the product on the stage keeps the product's.
    ///
    /// Before this, the label service painted <c>sus-label</c> onto every Label under the cascade
    /// root, and in the storybook scene that root is the UIDocument root - shell included: 314 of
    /// 517 shell elements carried a kit/core class (report ux-reviewer 2026-09-11). The owner's
    /// requirement is that the storybook is styled apart from the kit defaults it exists to show,
    /// so a shell element must carry no product class at all - while the instance on the stage must
    /// carry exactly what it carries inside an application.
    ///
    /// The walk is driven by hand here (<c>InstallHooksRecursive</c>) because an EditMode host has
    /// no panel: nothing sends <c>AttachToPanelEvent</c> and the 64 ms scan never ticks. That is
    /// the same entry point both live channels use, so what it paints is what the live shell gets.
    /// </summary>
    public class SusStorybookShellClassIsolationTests
    {
        // Product classes still on the shell after this card, by carrier: the label service - 0;
        // SusIconElement's own constructor - 10 (card T-3403).

        private sealed class ProbeComponent : SusComponent
        {
            public readonly Label Text = new("probe");

            protected override void Build() => Add(Text);
        }

        /// <summary>
        /// The campaign's corpus, element for element: everything under the shell carrying a class
        /// of the SHELL's own namespace (<c>sus-sb*</c>) - 517 of them on the live tree, of which
        /// 314 also wore a kit/core class (report ux-reviewer 2026-09-11). A component instance
        /// mounted on the stage or in the state matrix is NOT in this set: it wears the product's
        /// names, never the shell's.
        /// </summary>
        private static List<VisualElement> ShellChrome(SusStorybookHost host) =>
            host.Query<VisualElement>().ToList()
                // A mounted instance is PRODUCT even while the shell has put a class of its own on
                // it: SusStoryMatrix adds sus-sb-matrix__item to the SusComponent itself rather
                // than to a wrapper (SusStoryMatrix.cs:382), so without this line every matrix cell
                // reads as "shell chrome wearing sus-alert". This never fired before card T-3409
                // because the default story of an EditMode host was a CORE fixture, and the core
                // fixtures wear no product classes; with the fixture package out of the product
                // selection the default story is a real kit component. The shell class ON a product
                // instance is a separate question, filed as its own card.
                .Where(e => !(e is SusComponent))
                .Where(e => e.GetClasses().Any(c => c.StartsWith(ShellPrefix)))
                .ToList();

        private const string ShellPrefix = "sus-sb";

        /// <summary>A class of the product: kit (<c>sk-</c>) or core (<c>sus-</c> but not the shell's).</summary>
        private static bool IsProductClass(string cls) =>
            !IsStateContractClass(cls)
            && (cls.StartsWith("sk-") || (cls.StartsWith("sus-") && !cls.StartsWith(ShellPrefix)));

        /// <summary>
        /// Two product prefixes on a shell element are a CONTRACT, not skin: UI Toolkit has no API
        /// for forcing a pseudo-class, so the state matrix turns a state on by putting the twin
        /// (<c>sus-state-*</c>) or visual-state (<c>sus-vs--*</c>) class on the cell that wraps the
        /// instance (SusStateTwins, SusComponent.VisualState). Stripping those would not decouple
        /// the shell from the kit - it would make the state columns lie.
        /// </summary>
        private static bool IsStateContractClass(string cls) =>
            cls.StartsWith("sus-vs--") || cls.StartsWith("sus-state-");

        private static VisualElement WalkedRootWith(SusStorybookHost host)
        {
            var root = new VisualElement();
            SusLabelClassService.Attach(root);   // exactly what SusBootstrap does to a cascade root
            root.Add(host);
            SusLabelClassService.InstallHooksRecursive(root);
            return root;
        }

        [Test]
        public void The_shell_subtree_is_a_declared_boundary_at_load()
        {
            // Nothing in this fixture registers it: the guarantee must come from the storybook
            // assembly's own load hook, or it would hold only for the one boot path that asked.
            using var host = new SusStorybookHost();

            Assert.That(SusLabelClassService.IsInExcludedSubtree(host), Is.True,
                "SusStorybookShellIsolation did not register the shell boundary at load");
            Assert.That(SusLabelClassService.IsInExcludedSubtree(host.QaCanvas), Is.False,
                "the stage canvas must stay a product island inside the excluded shell");
        }

        [Test]
        public void No_shell_label_carries_a_product_class()
        {
            using var host = new SusStorybookHost();
            WalkedRootWith(host);

            var chrome = ShellChrome(host);
            Assert.That(chrome.Count, Is.GreaterThan(100),
                "the shell has to be built for this test to mean anything");

            Assert.That(chrome.Count(e => e.ClassListContains(SusLabelClassService.LabelClass)),
                Is.EqualTo(0), "the label service is painting the shell again");

            // The residue is INTRINSIC, not injected: SusIconElement classes itself in its own
            // constructor and the env bar builds its chips out of it (10 elements, card T-3403 -
            // that one needs a decision, not a boundary). Everything else must be clean, so a new
            // injected product class anywhere on the shell fails here.
            var painted = chrome
                .Where(e => !(e is SusIconElement))
                .Where(e => e.GetClasses().Any(IsProductClass))
                .ToList();
            Assert.That(painted.Count, Is.EqualTo(0),
                "shell elements carrying an injected product class: " + string.Join(", ",
                    painted.Take(5).Select(e => string.Join(".", e.GetClasses()))));
        }

        [Test]
        public void A_shell_label_is_still_stripped_of_the_unity_theme()
        {
            // Not painting is only half the isolation: Unity's Default Theme must not step in
            // where sus-label used to be, otherwise the shell trades one foreign cascade for
            // another. Colour and font come from `.sus-sb` by inheritance.
            using var host = new SusStorybookHost();
            WalkedRootWith(host);

            var unityClassed = ShellChrome(host)
                .OfType<Label>()
                .Where(l => l.GetClasses().Any(c => c.StartsWith("unity-")))
                .ToList();

            Assert.That(unityClassed.Count, Is.EqualTo(0),
                "shell labels still carrying unity-* classes: " + unityClassed.Count);
        }

        [Test]
        public void The_component_on_the_stage_keeps_the_product_class()
        {
            using var host = new SusStorybookHost();
            var probe = new ProbeComponent();
            host.QaCanvas.Add(probe);
            var raw = new Label("raw decoration");
            host.QaCanvas.Add(raw);

            WalkedRootWith(host);

            Assert.That(probe.Text.ClassListContains(SusLabelClassService.LabelClass), Is.True,
                "the instance on display lost the class it wears inside an app");
            Assert.That(raw.ClassListContains(SusLabelClassService.LabelClass), Is.True,
                "a story's own decoration on the stage is product too (island)");
        }

        [Test]
        public void A_component_mounted_anywhere_in_the_shell_still_paints_its_own_labels()
        {
            // A SusComponent is an island by construction - zone D shows live instances, and the
            // matrix mounts one per state. Each must look like itself, not like the shell.
            using var host = new SusStorybookHost();
            var probe = new ProbeComponent();
            host.ZoneControls.Add(probe);

            WalkedRootWith(host);

            Assert.That(probe.Text.ClassListContains(SusLabelClassService.LabelClass), Is.True);
        }

        [Test]
        public void With_no_boundary_declared_the_walk_behaves_exactly_as_before()
        {
            // The change has to be additive: every other host (a shipped app, a demo project,
            // a skin project) registers nothing and must keep getting every label painted.
            var root = new VisualElement();
            var label = new Label("app text");
            root.Add(label);
            label.AddToClassList("unity-label");

            SusLabelClassService.InstallHooksRecursive(root);

            Assert.That(label.ClassListContains(SusLabelClassService.LabelClass), Is.True);
            Assert.That(label.ClassListContains("unity-label"), Is.False);
        }
    }
}
