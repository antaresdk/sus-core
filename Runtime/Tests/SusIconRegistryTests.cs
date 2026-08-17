using NUnit.Framework;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// Regression coverage for T-591: components/stories that request a Bold/Fill/etc. weight
    /// for an icon whose curated CoreIconProvider set only ships the Regular glyph must not
    /// silently lose the icon (SusToggle requests Weight=Fill for "toggle-right", SusChip/
    /// SusAlert request Weight=Fill for "x-circle" — both exist only under
    /// Resources/SusRuntime/Icons/core/regular/ in this package, confirmed live).
    /// </summary>
    public class SusIconRegistryTests
    {
        [Test]
        public void ResourcesFolderIconProvider_MissingWeightVariant_FallsBackToRegular()
        {
            var provider = new CoreIconProvider();

            // "toggle-right" ships only as Icons/core/regular/toggle-right.svg — no
            // Icons/core/fill/toggle-right-fill.svg exists in this package.
            var fillRequest = provider.Load("toggle-right", SusIconWeight.Fill);
            var regularRequest = provider.Load("toggle-right", SusIconWeight.Regular);

            Assert.IsNotNull(regularRequest, "sanity: the Regular glyph itself must resolve");
            Assert.IsNotNull(fillRequest,
                "Fill request for a Regular-only glyph must degrade to Regular, not return null");
            Assert.AreSame(regularRequest, fillRequest,
                "fallback must resolve to the exact same asset as an explicit Regular request");
        }

        [Test]
        public void ResourcesFolderIconProvider_MissingWeightVariant_XCircle_FallsBackToRegular()
        {
            var provider = new CoreIconProvider();

            // Same class of gap, second real call site (SusChip close icon / SusAlert error icon
            // both request Weight="fill" for "x-circle").
            var fillRequest = provider.Load("x-circle", SusIconWeight.Fill);

            Assert.IsNotNull(fillRequest,
                "Fill request for x-circle (Regular-only in the curated set) must degrade to Regular");
        }

        [Test]
        public void ResourcesFolderIconProvider_UnknownNameAtAnyWeight_StillReturnsNull()
        {
            var provider = new CoreIconProvider();

            // The fallback must not paper over a genuinely unknown name — only degrade the
            // *weight*, never invent a result for a name the set doesn't have at all.
            var result = provider.Load("sus-t591-definitely-not-a-real-icon-name", SusIconWeight.Fill);

            Assert.IsNull(result);
        }

        [Test]
        public void SusIconRegistry_ToggleRightFill_ResolvesViaRegistryFacade()
        {
            // End-to-end through the public facade SusIcon components actually call
            // (SusIconRegistry.Load), not just the provider directly.
            var resolved = SusIconRegistry.Load("toggle-right", SusIconWeight.Fill);

            Assert.IsNotNull(resolved,
                "SusToggle's checked-state icon (Weight=Fill) must resolve through the registry");
        }
    }
}
