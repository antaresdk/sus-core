using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook.Controls
{
    /// <summary>
    /// Card T-3374 (UX pool POOL-01 U-031): a shell element that UI Toolkit renders OUTSIDE the
    /// shell subtree. The list a <see cref="DropdownField"/> drops open is not a child of the field:
    /// <see cref="GenericDropdownMenu"/> puts its root on the document root, beside the shell, so the
    /// shell sheet and its <c>--sb-*</c> names never reached it - the list rendered as Unity's bare
    /// widget, a white window whose labels inherited the shell's light ink and vanished.
    ///
    /// Dressing it as a PORTAL gives that root what the shell subtree has by inheritance: the
    /// shell's sheets, <see cref="PortalClass"/> (the token set, §4.2) and the shell's theme class.
    /// Nothing is moved and no <c>.style</c> is written (R53/R120): position, closing and picking
    /// stay Unity's, the look comes from <c>Storybook.uss</c> rules anchored on
    /// <see cref="PortalClass"/>.
    /// </summary>
    internal static class SusShellPortal
    {
        /// <summary>Class of a dressed portal root; carries the shell's token set.</summary>
        public const string PortalClass = "sb-portal";

        /// <summary>Class of the shell root (<see cref="SusStorybookHost"/>).</summary>
        public const string ShellClass = "sb-shell";

        /// <summary>The shell's light-theme class, mirrored onto a portal.</summary>
        public const string LightThemeClass = Env.SusBuiltinEnvAxes.ShellThemeAxis.LightClass;

        /// <summary>Every dropped-open list root currently under <paramref name="root"/>.</summary>
        public static IEnumerable<VisualElement> Menus(VisualElement root)
            => root.Query(className: GenericDropdownMenu.ussClassName).ToList();

        /// <summary>
        /// Dresses every list root under <paramref name="root"/> that is not in
        /// <paramref name="before"/> and not dressed yet, against the shell that holds
        /// <paramref name="insideShell"/>. Returns how many it dressed.
        /// </summary>
        public static int DressNewMenus(VisualElement root, ICollection<VisualElement> before, VisualElement insideShell)
        {
            var dressed = 0;
            foreach (var menu in Menus(root))
            {
                if (before.Contains(menu) || menu.ClassListContains(PortalClass)) continue;
                Dress(menu, insideShell);
                dressed++;
            }
            return dressed;
        }

        /// <summary>
        /// Puts the shell's sheets, <see cref="PortalClass"/> and the shell's theme class on
        /// <paramref name="portal"/>. Outside any shell only the class is added.
        /// </summary>
        public static void Dress(VisualElement portal, VisualElement insideShell)
        {
            portal.AddToClassList(PortalClass);
            var shell = ShellOf(insideShell);
            if (shell == null) return;

            for (var i = 0; i < shell.styleSheets.count; i++)
            {
                var sheet = shell.styleSheets[i];
                if (!portal.styleSheets.Contains(sheet)) portal.styleSheets.Add(sheet);
            }
            portal.EnableInClassList(LightThemeClass, shell.ClassListContains(LightThemeClass));
        }

        /// <summary>The nearest ancestor-or-self carrying <see cref="ShellClass"/>, or null.</summary>
        public static VisualElement ShellOf(VisualElement element)
        {
            for (var e = element; e != null; e = e.hierarchy.parent)
                if (e.ClassListContains(ShellClass)) return e;
            return null;
        }
    }
}
