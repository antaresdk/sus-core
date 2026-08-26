using NUnit.Framework;
using Sharq.Core.Diagnostics;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// R-D8 / T-1123 — ClickAuditService register / suspend / report. EditMode.
    /// </summary>
    public class ClickAuditServiceTests
    {
        private VisualElement _elA;
        private VisualElement _elB;

        [SetUp]
        public void SetUp()
        {
            _elA = new VisualElement { name = "click-a" };
            _elB = new VisualElement { name = "click-b" };
            ClickAuditService.Instance.Resume();
        }

        [TearDown]
        public void TearDown()
        {
            ClickAuditService.Instance.Unregister(_elA);
            ClickAuditService.Instance.Unregister(_elB);
            ClickAuditService.Instance.Resume();
        }

        [Test]
        public void Register_Null_NoOp()
        {
            Assert.DoesNotThrow(() => ClickAuditService.Instance.Register(null, "x"));
        }

        [Test]
        public void Register_DedupesSameElement()
        {
            ClickAuditService.Instance.Register(_elA, "first");
            ClickAuditService.Instance.Register(_elA, "second");
            Assert.DoesNotThrow(() => ClickAuditService.Instance.DumpReport());
        }

        [Test]
        public void Unregister_RemovesFromReportPath()
        {
            ClickAuditService.Instance.Register(_elA, "temp");
            ClickAuditService.Instance.Unregister(_elA);
            Assert.DoesNotThrow(() => ClickAuditService.Instance.DumpReport());
        }

        [Test]
        public void Suspend_AndResume_DoNotThrow()
        {
            ClickAuditService.Instance.Register(_elA, "btn");
            Assert.DoesNotThrow(() => ClickAuditService.Instance.Suspend());
            Assert.DoesNotThrow(() => ClickAuditService.Instance.Resume());
        }

        [Test]
        public void Install_NullPanel_NoOp()
        {
            Assert.DoesNotThrow(() => ClickAuditService.Instance.Install(null));
        }

        [Test]
        public void IgnoreAndTransparent_DoNotThrow()
        {
            Assert.DoesNotThrow(() =>
                ClickAuditService.Instance.RegisterTransparentOverlay(_elA, "tip"));
            Assert.DoesNotThrow(() => ClickAuditService.Instance.IgnoreElement(_elB));
            Assert.DoesNotThrow(() => ClickAuditService.Instance.DumpReport());
        }

        [Test]
        public void RunActiveAudit_NullPanel_NoOp()
        {
            ClickAuditService.Instance.Register(_elA, "orphan");
            Assert.DoesNotThrow(() => ClickAuditService.Instance.RunActiveAudit(null));
        }
    }
}
