using NUnit.Framework;
using Sharq.Core.Diagnostics;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// R-D8 / T-1123 — ScreenAudit dumps / hotkey install. EditMode (no throw + idempotent).
    /// </summary>
    public class ScreenAuditTests
    {
        [Test]
        public void LayoutDump_NullRoot_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ScreenAudit.LayoutDump(null));
        }

        [Test]
        public void LayoutDump_SimpleTree_DoesNotThrow()
        {
            var root = new VisualElement { name = "audit-root" };
            root.Add(new Label("hello"));
            Assert.DoesNotThrow(() => ScreenAudit.LayoutDump(root, maxDepth: 4));
        }

        [Test]
        public void PickableLayerAudit_DoesNotThrow()
        {
            var root = new VisualElement();
            var btn = new Button { text = "go", pickingMode = PickingMode.Position };
            root.Add(btn);
            Assert.DoesNotThrow(() => ScreenAudit.PickableLayerAudit(root));
        }

        [Test]
        public void FullPropsDump_EmptyTree_DoesNotThrow()
        {
            var root = new VisualElement();
            root.Add(new Label("plain"));
            Assert.DoesNotThrow(() => ScreenAudit.FullPropsDump(root));
        }

        [Test]
        public void InstallHotkey_Null_NoOp_ThenInstallIfNeeded_Idempotent()
        {
            Assert.DoesNotThrow(() => ScreenAudit.InstallHotkey(null));

            var root = new VisualElement { focusable = true };
            Assert.DoesNotThrow(() => ScreenAudit.InstallIfNeeded(root));
            Assert.DoesNotThrow(() => ScreenAudit.InstallIfNeeded(root));
            Assert.DoesNotThrow(() => ScreenAudit.InstallHotkey(root));
        }
    }
}
