using NUnit.Framework;
using Sharq.Core.Storybook;
using UnityEditor;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// The USS selector scan behind the <c>hover</c> and <c>active</c> columns of the state matrix
    /// (card T-3448, plan <c>docs-canon/plans/impl/ARCH-20260911-KIT-STATE-CONTRACT.md</c> wave 5).
    ///
    /// What these tests are FOR: <see cref="SusStateTwins.Has"/> answers "does the skin declare
    /// this twin class", and a wrong answer is INVISIBLE. "No" hides a column, and a hidden column
    /// looks exactly like a component that has no hover state — so a scan that reads nothing at
    /// all reports the same thing as a skin with no twins. That is what happened: the scan reached
    /// for the serialized field <c>m_ComplexSelectors</c>, Unity 6.3 keeps its selectors in
    /// <c>m_Tables</c> instead, and every class on every sheet came back absent. The acceptance of
    /// wave 5 (the twin codemod, card T-3039) would have been falsely red: 526 rewritten selectors
    /// and still no columns.
    ///
    /// So the scan is pinned against a KNOWN class of a LIVE sheet rather than against a fixture.
    /// A fixture would have to be built through the same internal storage the tests are here to
    /// distrust; a real imported sheet is built by the editor itself, which means these tests fail
    /// the day the storage moves again — which is the whole point.
    /// </summary>
    public class SusStateTwinsTests
    {
        // The storybook shell sheet of this very package: it is here whenever the tests are, it
        // is imported by the editor under test, and its class names are stable.
        const string CoreSheet = "Packages/com.sharq-it.sus.core/Runtime/Storybook/Storybook.uss";

        // The other half of the acceptance — the class of a shipped component in its own
        // generated sheet — is asserted in the test assembly that travels WITH that sheet, in the
        // component library's own repository. This package is the free, public one, and rule R25
        // keeps the name of a paid package out of its sources; the assertion needs no reference to
        // anything, only an asset path, so it costs nothing to keep it there.

        [TearDown]
        public void TearDown() => SusStateTwins.Reset();

        static StyleSheet Required(string path)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            Assert.That(sheet, Is.Not.Null, "the sheet under test must be imported: " + path);
            return sheet;
        }

        [Test]
        public void Mentions_answers_true_for_a_class_the_sheet_declares_and_false_for_one_it_does_not()
        {
            var sheet = Required(CoreSheet);

            Assert.That(SusStateTwins.Mentions(sheet, "sb-ctl"), Is.True,
                "declared as a selector of its own in the shell sheet");
            Assert.That(SusStateTwins.Mentions(sheet, "sb-shell__burger"), Is.True,
                "declared as the rightmost part of a descendant selector");

            Assert.That(SusStateTwins.Mentions(sheet, "sus-state-twin-absent-probe-t3448"), Is.False,
                "no sheet declares this, and a scan that says otherwise reads names it invented");
        }

        [Test]
        public void A_class_that_only_ever_appears_as_an_ANCESTOR_is_still_found()
        {
            // The Unity 6.3 storage buckets a complex selector under its RIGHTMOST simple
            // selector only, so a walk that trusted the bucket keys would miss every class used
            // purely as a context. `sb-shell--narrow` is exactly that case: 10 occurrences in the
            // shell sheet, not one of them rightmost. The twin codemod writes twins onto the
            // rightmost part, but skins are free to scope a twin behind a context class, and a
            // scan blind to contexts would hide those columns.
            var sheet = Required(CoreSheet);

            Assert.That(SusStateTwins.Mentions(sheet, "sb-shell--narrow"), Is.True,
                "ancestor-only class: reachable through the selector parts, never through a bucket key");
        }

        [Test]
        public void The_scan_names_the_storage_layout_it_read_and_never_reports_a_blind_one()
        {
            var sheet = Required(CoreSheet);
            SusStateTwins.Mentions(sheet, "sb-ctl");

            Assert.That(SusStateTwins.SelectorLayout,
                Is.EqualTo("m_ComplexSelectors").Or.EqualTo("m_Tables"),
                "the layout actually read; 'none' is the blind scan that hides every column, " +
                "'unknown' means nothing was scanned at all");
        }
    }
}
