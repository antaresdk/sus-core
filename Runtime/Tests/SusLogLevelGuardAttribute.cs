using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

// T-576: this assembly's own tests (SusLogTests) already exercise SusLog.Level directly, so the
// guard covers itself too — see the class doc below for why that matters.
[assembly: Sharq.Core.Runtime.Tests.SusLogLevelGuardAttribute]

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// T-576: <see cref="SusLog"/>.Level is a process-wide static gate shared by every test
    /// assembly in a single PlayMode run (Unity Test Runner executes all selected assemblies in
    /// one domain/AppDomain, so static state is genuinely shared across package boundaries).
    /// Any test or Storybook story that raises/lowers it and restores it on a deferred signal
    /// (e.g. a story's cleanup running on <c>VisualElement.DetachFromPanelEvent</c>, which can
    /// fire a frame later than NUnit's own TearDown/AfterTest) can leak the change into whatever
    /// test happens to run next — order-dependently. That is the exact class of flake documented
    /// in T-576: two full-suite runs with byte-identical code between them produced different
    /// pass/fail results on the same two warning-assertion tests.
    ///
    /// Apply <c>[assembly: SusLogLevelGuard]</c> in every test assembly that has warning-
    /// dependent assertions (<c>LogAssert.Expect(LogType.Warning, ...)</c> or similar) so no
    /// single test's outcome depends on ambient state a prior, unrelated test (possibly in a
    /// DIFFERENT package's test assembly, sharing the same PlayMode run) left behind. This wraps
    /// every test in the assembly it's declared on — resets to the canonical default (Warn) both
    /// BEFORE and AFTER each test, so a leak can neither arrive from an earlier test nor escape
    /// to a later one. Individual tests that need a non-default level for their own assertion
    /// (e.g. <c>SusModalContractTests.Contract_StateAudit_VerboseOnly</c>) still set/restore it
    /// themselves inside their own try/finally — this guard is a backstop, not a replacement for
    /// that local discipline.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class SusLogLevelGuardAttribute : Attribute, ITestAction
    {
        public void BeforeTest(ITest test) => SusLog.ResetForTests(SusLogLevel.Warn, defineFloor: false);

        public void AfterTest(ITest test) => SusLog.ResetForTests(SusLogLevel.Warn, defineFloor: false);

        public ActionTargets Targets => ActionTargets.Test;
    }
}
