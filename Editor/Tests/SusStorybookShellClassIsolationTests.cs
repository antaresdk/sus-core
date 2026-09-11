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
        /// of the SHELL's own namespace (<c>sb-*</c>) - 517 of them on the live tree, of which
        /// 314 also wore a kit/core class (report ux-reviewer 2026-09-11). A component instance
        /// mounted on the stage or in the state matrix is NOT in this set: it wears the product's
        /// names, never the shell's.
        /// </summary>
        private static List<VisualElement> ShellChrome(SusStorybookHost host) =>
            host.Query<VisualElement>().ToList()
                // No carve-out for mounted instances (card T-3421): the shell no longer puts a
                // class of its own on a SusComponent at all - the state matrix wraps each cell's
                // instance in a box of the shell's own, and the instance inside wears the
                // product's names only. Until T-3421 this selection had to drop every
                // SusComponent, because sb-matrix__item sat on the instance itself and every
                // matrix cell read as "shell chrome wearing sus-alert" (it first fired at T-3409,
                // when the default story of an EditMode host stopped being a core fixture).
                .Where(e => e.GetClasses().Any(c => c.StartsWith(ShellPrefix)))
                .ToList();

        private const string ShellPrefix = "sb-";

        /// <summary>
        /// A class of the product: kit (<c>sk-</c>) or core (<c>sus-</c>). Card T-3387 is what
        /// makes this one line instead of two: while the shell still shared the product's own
        /// <c>sus-</c> prefix, every test of "is this a product class" had to carve the shell
        /// back out of that space by hand. With the shell on <c>sb-</c> the two namespaces no
        /// longer overlap.
        /// </summary>
        private static bool IsProductClass(string cls) =>
            !IsStateContractClass(cls) && (cls.StartsWith("sk-") || cls.StartsWith("sus-"));

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
        public void No_product_instance_carries_a_shell_class()
        {
            // The OTHER direction of the same boundary (card T-3421). The shell may mount an
            // instance wherever it likes - the stage, zone D, a cell of the state matrix - but it
            // dresses a box of its OWN around it. What a buyer mounts in an application and what
            // the stand shows must carry the same class list, or the stand stops being evidence.
            //
            // The witness that made this a card: SusStoryMatrix put sb-matrix__item on the
            // SusComponent itself, and 16 matrix cells came out as sus-alert...sb-matrix__item
            // (card T-3409, when the default story of an EditMode host stopped being a core
            // fixture and became a real kit component).
            using var host = new SusStorybookHost();
            WalkedRootWith(host);

            var mounted = host.Query<SusComponent>().ToList();
            Assert.That(mounted.Count, Is.GreaterThan(0),
                "no instance is mounted - this test would pass on an empty shell");

            var dressed = mounted
                .Where(e => e.GetClasses().Any(c => c.StartsWith(ShellPrefix)))
                .ToList();
            Assert.That(dressed.Count, Is.EqualTo(0),
                "product instances wearing a class of the shell: " + string.Join(", ",
                    dressed.Take(5).Select(e => string.Join(".", e.GetClasses()))));
        }

        [Test]
        public void A_shell_label_is_still_stripped_of_the_unity_theme()
        {
            // Not painting is only half the isolation: Unity's Default Theme must not step in
            // where sus-label used to be, otherwise the shell trades one foreign cascade for
            // another. Colour and font come from `.sb-shell` by inheritance.
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
