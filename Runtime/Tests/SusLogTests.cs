using NUnit.Framework;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>Gate tests for <see cref="SusLog"/> (T-330 / ARCH-SUS-LOG).</summary>
    public class SusLogTests
    {
        [TearDown]
        public void TearDown()
        {
            SusLog.ResetForTests(SusLogLevel.Warn, defineFloor: false);
        }

        [Test]
        public void DefaultLevel_IsWarn()
        {
            SusLog.ResetForTests(SusLogLevel.Warn);
            Assert.AreEqual(SusLogLevel.Warn, SusLog.Level);
            Assert.IsTrue(SusLog.IsEnabled(SusLogLevel.Error));
            Assert.IsTrue(SusLog.IsEnabled(SusLogLevel.Warn));
            Assert.IsFalse(SusLog.IsEnabled(SusLogLevel.Info));
            Assert.IsFalse(SusLog.IsVerbose);
        }

        [Test]
        public void Level_Filters_InfoAndVerbose()
        {
            SusLog.ResetForTests(SusLogLevel.Warn);
            Assert.IsTrue(SusLog.IsEnabled(SusLogLevel.Error));
            Assert.IsTrue(SusLog.IsEnabled(SusLogLevel.Warn));
            Assert.IsFalse(SusLog.IsEnabled(SusLogLevel.Info));
            Assert.IsFalse(SusLog.IsEnabled(SusLogLevel.Verbose));
        }

        [Test]
        public void Error_AlwaysEnabled_EvenAtErrorLevel()
        {
            SusLog.ResetForTests(SusLogLevel.Error);
            Assert.IsTrue(SusLog.IsEnabled(SusLogLevel.Error));
            Assert.IsFalse(SusLog.IsEnabled(SusLogLevel.Warn));
            Assert.IsFalse(SusLog.IsEnabled(SusLogLevel.Info));
            Assert.IsFalse(SusLog.IsVerbose);
        }

        [Test]
        public void VerboseLevel_EnablesAll()
        {
            SusLog.ResetForTests(SusLogLevel.Verbose);
            Assert.IsTrue(SusLog.IsVerbose);
            Assert.IsTrue(SusLog.IsEnabled(SusLogLevel.Error));
            Assert.IsTrue(SusLog.IsEnabled(SusLogLevel.Warn));
            Assert.IsTrue(SusLog.IsEnabled(SusLogLevel.Info));
            Assert.IsTrue(SusLog.IsEnabled(SusLogLevel.Verbose));
        }

        [Test]
        public void DefineFloor_CannotLowerBelowVerbose()
        {
            SusLog.ResetForTests(SusLogLevel.Verbose, defineFloor: true);
            SusLog.Level = SusLogLevel.Warn;
            Assert.AreEqual(SusLogLevel.Verbose, SusLog.Level);
            Assert.IsTrue(SusLog.IsVerbose);
        }

        [Test]
        public void TryParseLevel_CaseInsensitive()
        {
            Assert.IsTrue(SusLog.TryParseLevel("verbose", out var v));
            Assert.AreEqual(SusLogLevel.Verbose, v);
            Assert.IsTrue(SusLog.TryParseLevel("WARN", out var w));
            Assert.AreEqual(SusLogLevel.Warn, w);
            Assert.IsFalse(SusLog.TryParseLevel("nope", out _));
        }

        [Test]
        public void UseLogLevel_SetsSusLogLevel()
        {
            SusLog.ResetForTests(SusLogLevel.Warn);
            var app = SusApp.Create(new VisualElement())
                .UseLogLevel(SusLogLevel.Info);
            Assert.AreSame(app, app.UseLogLevel(SusLogLevel.Info));
            Assert.AreEqual(SusLogLevel.Info, SusLog.Level);
        }
    }
}
