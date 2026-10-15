using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Sharq.Core.Storybook.Controls;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Card T-3374, UX pool POOL-01 U-031: the list a zone D dropdown drops open lives on the
    /// document root, outside the shell, and rendered as a white window with invisible labels.
    /// The control dresses that root as a shell portal; these tests pin what "dressed" means.
    /// </summary>
    public class SusShellPortalTests
    {
        readonly List<Object> _owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _owned) Object.DestroyImmediate(o);
            _owned.Clear();
        }

        VisualElement Shell(out VisualElement inside, out StyleSheet sheet, bool light)
        {
            sheet = ScriptableObject.CreateInstance<StyleSheet>();
            _owned.Add(sheet);
            var shell = new VisualElement();
            shell.AddToClassList(SusShellPortal.ShellClass);
            shell.EnableInClassList(SusShellPortal.LightThemeClass, light);
            shell.styleSheets.Add(sheet);
            var zoneD = new VisualElement();
            inside = new DropdownField();
            zoneD.Add(inside);
            shell.Add(zoneD);
            return shell;
        }

        static VisualElement Menu()
        {
            var menu = new VisualElement();
            menu.AddToClassList(GenericDropdownMenu.ussClassName);
            return menu;
        }

        [Test]
        public void Dress_Gives_The_Portal_The_Shell_Sheets_Token_Class_And_Theme()
        {
            Shell(out var field, out var sheet, light: true);
            var menu = Menu();

            SusShellPortal.Dress(menu, field);

            Assert.IsTrue(menu.ClassListContains(SusShellPortal.PortalClass), "the token set rides on .sb-portal");
            Assert.IsTrue(menu.styleSheets.Contains(sheet), "the shell sheet carries the rules anchored on .sb-portal");
            Assert.IsTrue(menu.ClassListContains(SusShellPortal.LightThemeClass), "the shell's theme is mirrored");
        }

        [Test]
        public void Dress_Dark_Shell_Leaves_No_Light_Class()
        {
            Shell(out var field, out _, light: false);
            var menu = Menu();
            menu.AddToClassList(SusShellPortal.LightThemeClass);

            SusShellPortal.Dress(menu, field);

            Assert.IsFalse(menu.ClassListContains(SusShellPortal.LightThemeClass));
        }

        [Test]
        public void Dress_Twice_Adds_The_Sheet_Once()
        {
            Shell(out var field, out _, light: false);
            var menu = Menu();

            SusShellPortal.Dress(menu, field);
            SusShellPortal.Dress(menu, field);

            Assert.AreEqual(1, menu.styleSheets.count);
        }

        [Test]
        public void Dress_Outside_A_Shell_Adds_Only_The_Class()
        {
            var menu = Menu();

            SusShellPortal.Dress(menu, new DropdownField());

            Assert.IsTrue(menu.ClassListContains(SusShellPortal.PortalClass));
            Assert.AreEqual(0, menu.styleSheets.count);
        }

        [Test]
        public void DressNewMenus_Dresses_Only_The_List_That_Appeared_After_The_Gesture()
        {
            var shell = Shell(out var field, out _, light: false);
            var root = new VisualElement();
            root.Add(shell);
            var old = Menu();
            root.Add(old);
            var before = new HashSet<VisualElement>(SusShellPortal.Menus(root));

            var opened = Menu();
            root.Add(opened);

            Assert.AreEqual(1, SusShellPortal.DressNewMenus(root, before, field));
            Assert.IsTrue(opened.ClassListContains(SusShellPortal.PortalClass));
            Assert.IsFalse(old.ClassListContains(SusShellPortal.PortalClass), "a list open before the gesture is not ours");
            Assert.AreEqual(0, SusShellPortal.DressNewMenus(root, before, field), "a dressed list is not dressed again");
        }
    }
}
