using UnityEditor;
using Sharq.Core.Storybook;

namespace Sharq.Core.Editor.Storybook
{
    /// <summary>
    /// The editor half of "the storybook does not show engine benches" (plan
    /// ARCH-20260907-STORYBOOK-ENGINE §4.1b, decision D22, card T-3410).
    ///
    /// Why the switch lives HERE and the answer is injected. The registry is in
    /// <c>com.sharq-it.sus.core.storybook</c>, a RUNTIME assembly that ships in the player and
    /// therefore cannot touch <c>UnityEditor</c> — it can hold no preference, no menu item and no
    /// checkmark. So the pattern is the one <see cref="SusStoryRegistry.PackageStampResolver"/>
    /// already uses: the editor installs a function, the runtime only ever asks it.
    ///
    /// Who needs the switch at all: whoever is working ON the engine. The benches are what the
    /// EditMode suite drives, and looking at one by hand — a deliberately broken one especially —
    /// is how a fixture gets fixed. Everyone else must never see them, because a buyer opening the
    /// storybook and finding a tab of broken toys learns the wrong thing about the product.
    ///
    /// The preference is per-machine (<see cref="EditorPrefs"/>), not per-project: it describes
    /// who is sitting at the keyboard, not what the project is.
    /// </summary>
    [InitializeOnLoad]
    public static class SusStorybookFixturesMenu
    {
        const string Menu = "Window/SUS/Storybook/Show Engine Fixtures";
        const string Pref = "Sus.Storybook.ShowFixtures";

        static SusStorybookFixturesMenu()
        {
            Install();
        }

        /// <summary>True when the benches are listed on this machine.</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(Pref, false);
            set
            {
                EditorPrefs.SetBool(Pref, value);
                SusStoryRegistry.InvalidateVisibility();
            }
        }

        /// <summary>
        /// Wires the registry to the preference. The lambda READS the pref every time instead of
        /// capturing it, so a toggle needs no rescan — only
        /// <see cref="SusStoryRegistry.InvalidateVisibility"/>, which the setter above calls.
        /// </summary>
        static void Install()
        {
            SusStoryRegistry.FixtureVisibility = _ => EditorPrefs.GetBool(Pref, false);
        }

        [MenuItem(Menu, false, 20)]
        static void Toggle()
        {
            Enabled = !Enabled;
            SusStoryRegistry.Refresh();   // zone A redraws from the Changed event
        }

        [MenuItem(Menu, true, 20)]
        static bool ToggleValidate()
        {
            UnityEditor.Menu.SetChecked(Menu, Enabled);
            return true;
        }
    }
}
