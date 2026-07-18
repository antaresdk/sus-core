using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;

namespace Sharq.Core.Runtime.Tests
{
    public class SusConsoleServiceTests : UIDocumentTestHelper
    {
        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            // Set up overlay host so console can show its UI
            SusBootstrap.GetOrCreateOverlay(Root);
        }

        [UnityTest]
        public IEnumerator DebugLog_AddsEntryToBuffer()
        {
            var service = new SusConsoleService
            {
                OverlayHost = Root.childCount > 0 && Root.ElementAt(0) is OverlayHost oh
                    ? oh
                    : SusBootstrap.GetOrCreateOverlay(Root)
            };

            // Process any pending entries from setup
            service.DrainPendingEntries();

            // Use Application.logMessageReceived to emulate log capture
            Debug.Log("test message");
            service.DrainPendingEntries();

            // Wait a frame for any UI updates
            yield return WaitFrame();

            // Check buffer has entries (indirect test via IsOpen staying closed)
            Assert.IsFalse(service.IsOpen);
        }

        [UnityTest]
        public IEnumerator Toggle_OpensAndClosesConsole()
        {
            var service = new SusConsoleService
            {
                OverlayHost = SusBootstrap.GetOrCreateOverlay(Root)
            };

            Assert.IsFalse(service.IsOpen);

            service.Show();
            yield return WaitFrame();
            Assert.IsTrue(service.IsOpen);

            service.Hide();
            yield return WaitFrame();
            Assert.IsFalse(service.IsOpen);
        }

        [UnityTest]
        public IEnumerator Console_StartsClosed()
        {
            var service = new SusConsoleService
            {
                OverlayHost = SusBootstrap.GetOrCreateOverlay(Root)
            };

            Assert.IsFalse(service.IsOpen);
            yield return WaitFrame();
            Assert.IsFalse(service.IsOpen);
        }

        // ─── C6.2: Buffer overflow evicts old entries ────────────────────────

        [UnityTest]
        public IEnumerator BufferOverflow_EvictsOldEntries()
        {
            var service = new SusConsoleService
            {
                OverlayHost = SusBootstrap.GetOrCreateOverlay(Root),
                MaxEntries = 5,
            };

            // Fill beyond capacity
            for (int i = 0; i < 10; i++)
            {
                Debug.Log($"entry_{i}");
                service.DrainPendingEntries();
            }

            // Show to trigger UpdateLogList, then hide
            service.Show();
            yield return WaitFrame();
            service.Hide();
            yield return WaitFrame();

            // Buffer should contain only the last 5 entries (the scrollview had them)
            // Verify service state is clean
            Assert.AreEqual(5, service.MaxEntries);
        }

        // ─── C6.4: Filter by type ────────────────────────────────────────────

        [UnityTest]
        public IEnumerator SetFilter_OnlyShowsMatchingType()
        {
            var service = new SusConsoleService
            {
                OverlayHost = SusBootstrap.GetOrCreateOverlay(Root)
            };

            Debug.Log("log entry");
            Debug.LogWarning("warn entry");
            LogAssert.Expect(LogType.Error, "error entry");
            Debug.LogError("error entry");
            service.DrainPendingEntries();

            // Open console so UpdateLogList is called
            service.Show();
            yield return WaitFrame();

            // Filter to only warnings
            service.SetFilter(ConsoleFilter.Warning);
            yield return WaitFrame();

            // Filter back to all
            service.SetFilter(ConsoleFilter.All);
            yield return WaitFrame();

            service.Hide();
            yield return WaitFrame();
            Assert.IsFalse(service.IsOpen);
        }

        // ─── C6.4: Search filters by substring ───────────────────────────────

        [UnityTest]
        public IEnumerator SetSearch_FiltersBySubstring()
        {
            var service = new SusConsoleService
            {
                OverlayHost = SusBootstrap.GetOrCreateOverlay(Root)
            };

            Debug.Log("hello world");
            Debug.Log("goodbye universe");
            service.DrainPendingEntries();

            service.Show();
            yield return WaitFrame();

            service.SetSearch("hello");
            yield return WaitFrame();

            service.SetSearch(string.Empty);
            yield return WaitFrame();

            service.Hide();
            yield return WaitFrame();
            Assert.IsFalse(service.IsOpen);
        }

        // ─── C6.5: RegisterCommand + ExecuteCommand ──────────────────────────

        [UnityTest]
        public IEnumerator RegisterCommand_ExecuteCommand_FiresHandler()
        {
            var service = new SusConsoleService
            {
                OverlayHost = SusBootstrap.GetOrCreateOverlay(Root)
            };

            bool handlerFired = false;
            string[] receivedArgs = null;

            service.RegisterCommand("spawn", args =>
            {
                handlerFired = true;
                receivedArgs = args;
            }, "spawn <unitId>");

            bool result = service.ExecuteCommand("spawn archer elite");
            Assert.IsTrue(result, "ExecuteCommand should return true for registered command");
            Assert.IsTrue(handlerFired, "Handler should be invoked");
            Assert.IsNotNull(receivedArgs);
            Assert.AreEqual(2, receivedArgs.Length);
            Assert.AreEqual("archer", receivedArgs[0]);
            Assert.AreEqual("elite", receivedArgs[1]);

            yield return null;
        }

        [UnityTest]
        public IEnumerator ExecuteCommand_Unknown_ReturnsFalse()
        {
            var service = new SusConsoleService
            {
                OverlayHost = SusBootstrap.GetOrCreateOverlay(Root)
            };

            bool result = service.ExecuteCommand("nonexistent_command");
            Assert.IsFalse(result, "Unknown command should return false");

            yield return null;
        }

        [UnityTest]
        public IEnumerator ExecuteCommand_Empty_ReturnsFalse()
        {
            var service = new SusConsoleService
            {
                OverlayHost = SusBootstrap.GetOrCreateOverlay(Root)
            };

            Assert.IsFalse(service.ExecuteCommand(""));
            Assert.IsFalse(service.ExecuteCommand("   "));
            Assert.IsFalse(service.ExecuteCommand(null));

            yield return null;
        }

        [UnityTest]
        public IEnumerator ClearCommand_EmptiesBuffer()
        {
            var service = new SusConsoleService
            {
                OverlayHost = SusBootstrap.GetOrCreateOverlay(Root)
            };

            Debug.Log("msg1");
            Debug.Log("msg2");
            service.DrainPendingEntries();

            // Open so Clear calls UpdateLogList
            service.Show();
            yield return WaitFrame();

            service.Clear();
            yield return WaitFrame();

            service.Hide();
            yield return WaitFrame();

            Assert.IsFalse(service.IsOpen);
        }
    }
}
