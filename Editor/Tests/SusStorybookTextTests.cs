using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Sharq.Core.Storybook;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// POOL-01 R3 (card T-3372): the storybook's panel falls back to a face with Cyrillic, and the
    /// project's PanelSettings asset is not what gets changed.
    /// </summary>
    public class SusStorybookTextTests
    {
        PanelSettings _source;
        PanelSettings _copy;

        [SetUp]
        public void SetUp() => _source = ScriptableObject.CreateInstance<PanelSettings>();

        [TearDown]
        public void TearDown()
        {
            SusStorybookText.Release(_copy);
            Object.DestroyImmediate(_source);
        }

        [Test]
        public void TheCopy_FallsBackToAFaceThatHasCyrillic()
        {
            _copy = SusStorybookText.WithGlyphFallback(_source);

            Assert.IsNotNull(_copy);
            Assert.AreNotSame(_source, _copy, "the document mounts into a copy");
            var text = _copy.textSettings;
            Assert.IsNotNull(text);
            Assert.IsNotNull(text.fallbackFontAssets);
            Assert.AreEqual(SusStorybookText.GlyphFallbackFonts.Length, text.fallbackFontAssets.Count);
            // A Russian greeting plus the letters a Latin-only face loses first (Zhe, Yo).
            foreach (var c in "\u041f\u0440\u0438\u0432\u0435\u0442\u0416\u0401\u0451")
            {
                Assert.IsTrue(text.fallbackFontAssets[0].HasCharacter(c, false, true),
                    "the fallback face draws U+" + ((int)c).ToString("X4"));
            }
        }

        [Test]
        public void TheSourceAsset_IsLeftAsItWas()
        {
            _copy = SusStorybookText.WithGlyphFallback(_source);

            Assert.IsNull(_source.textSettings, "the project's PanelSettings is not written");
        }

        [Test]
        public void ProjectTextSettings_AreCopied_NotEdited()
        {
            var own = ScriptableObject.CreateInstance<PanelTextSettings>();
            _source.textSettings = own;
            try
            {
                _copy = SusStorybookText.WithGlyphFallback(_source);

                Assert.AreNotSame(own, _copy.textSettings);
                Assert.IsTrue(own.fallbackFontAssets == null || own.fallbackFontAssets.Count == 0,
                    "the project's text settings keep their own fallback list");
                Assert.AreEqual(1, _copy.textSettings.fallbackFontAssets.Count);
            }
            finally
            {
                _source.textSettings = null;
                Object.DestroyImmediate(own);
            }
        }
    }
}
