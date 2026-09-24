using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// T-4145 grep-judge: no EditMode fixture under this directory constructs its own
    /// <see cref="UnityEditor.EditorWindow"/> anymore. Before this card, nine fixtures across
    /// this repo and a downstream package each called
    /// <c>EditorWindow.CreateInstance&lt;EditorWindow&gt;(); Show();</c> straight in their own
    /// per-TEST <c>[SetUp]</c> — a floating native OS window popping open and closing on every
    /// single test, which the owner saw as the desktop flickering through unrelated apps
    /// (C:/Temp/sus-focus.log, 2026-09-24). All eight fixtures in this repo now go through
    /// <see cref="Sharq.Core.Editor.TestSupport.SusEditorWindowTestHost.CreateAndShow"/> instead
    /// (one window per fixture, off-screen, focus handed back — see that class's doc) — which
    /// lives in <c>Editor/TestSupport/</c>, outside the directory this test scans, so it is the
    /// one place the literal is still allowed to appear.
    ///
    /// A future fixture reaching straight for <c>EditorWindow.CreateInstance&lt;EditorWindow&gt;()</c>
    /// reintroduces the per-test floating-window flicker this card exists to kill — this test is
    /// the tripwire, so it stays a plain <c>[Test]</c> (no <c>Application.isBatchMode</c> guard):
    /// it only reads source text, no graphics device needed.
    /// </summary>
    public class SusEditorWindowTestHostUsageTests
    {
        static string ThisFilePath(
            [System.Runtime.CompilerServices.CallerFilePath] string path = null)
            => path;

        static string ThisFileDir() => Path.GetDirectoryName(ThisFilePath());

        /// <summary>Drops everything from the first <c>//</c> on each line — comments (including
        /// <c>///</c> XML doc, e.g. this very file's own class doc above) are allowed to name the
        /// pattern, only live CODE reaching for it is the tripwire.</summary>
        static string StripLineComments(string source) => string.Join("\n", source
            .Split('\n')
            .Select(line =>
            {
                var i = line.IndexOf("//", System.StringComparison.Ordinal);
                return i < 0 ? line : line.Substring(0, i);
            }));

        [Test]
        public void No_fixture_constructs_its_own_EditorWindow_outside_the_shared_host()
        {
            var dir = ThisFileDir();
            var self = Path.GetFileName(ThisFilePath());

            var offenders = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) != self)
                .Where(f =>
                {
                    var code = StripLineComments(File.ReadAllText(f));
                    return code.Contains("CreateInstance<EditorWindow>") ||
                           code.Contains("CreateInstance<UnityEditor.EditorWindow>");
                })
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToList();

            Assert.That(offenders, Is.Empty,
                "these EditMode fixtures build their own EditorWindow instead of going through " +
                "SusEditorWindowTestHost.CreateAndShow (T-4145 — one shared window per fixture, " +
                "off-screen, focus handed back): " + string.Join(", ", offenders));
        }
    }
}
