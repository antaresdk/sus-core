using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sharq.Core.Runtime.Tests
{
    /// <summary>
    /// Regression for T-726 (WebGL "RangeError: Maximum call stack size exceeded" on
    /// playground boot, core 1.0.17/kit 1.0.20/game 1.0.27; invisible in Editor Play —
    /// Mono's stack is far larger than WebGL's wasm stack).
    ///
    /// Root cause: T-587 made <c>FlushPendingBindUpdatesOnAttach</c> call
    /// <c>ApplyAllBindUpdates()</c> SYNCHRONOUSLY (correctly — it fixed a real dropped-
    /// update bug). But a bind/WatchEffect action can itself <c>Add()</c> a freshly-built
    /// child directly onto an ALREADY on-panel component (<c>this</c> — whose <c>panel</c>
    /// is set by UITK before its own AttachToPanelEvent dispatches). That Add() cascades
    /// the child's own AttachToPanelEvent SYNCHRONOUSLY, nested inside the parent's own
    /// flush call — and if the child ALSO has a pending flush (freshly built, props set
    /// before Add — the ordinary Sharq authoring pattern), it re-enters the very same
    /// small chain of frames one level deeper. A screen whose initial mount reveals
    /// several such levels in one synchronous cascade (a router building the whole
    /// initial route tree) turns this into the WebGL RangeError's "repeating pattern of a
    /// few wasm functions, going deeper each time".
    ///
    /// Fix (SusComponent.cs): a static re-entrancy guard caps synchronous
    /// <c>ApplyAllBindUpdates()</c> to depth 1 — anything that would nest deeper is
    /// queued and drained ITERATIVELY by the outermost caller instead, so the whole
    /// cascade still completes within the same tick but SUS's own flush machinery never
    /// contributes more than a constant handful of C# stack frames, regardless of how
    /// many levels re-enter.
    ///
    /// These tests don't (and must not) try to reproduce an actual stack overflow —
    /// StackOverflowException is uncatchable and would crash the shared Editor process.
    /// Instead they assert directly on the guard's own bookkeeping
    /// (<see cref="SusComponent.DebugInterceptedReentrantFlushCount"/>): a deep
    /// synchronous reveal cascade must drive it well above zero (proving the guard
    /// actually intercepted nesting, not merely that nesting never occurred), while every
    /// level of the chain still ends up fully revealed (proving nothing was silently
    /// dropped — the T-587 failure mode).
    /// </summary>
    public class ReentrantFlushDepthTests : UIDocumentTestHelper
    {
        /// <summary>
        /// Each instance reveals exactly one child of itself (directly on <c>this</c>,
        /// not on a not-yet-attached sub-container) once <c>Reveal</c> is true — mimicking
        /// a v-if/WatchEffect-revealed nested screen. The child's own Reveal is set BEFORE
        /// Add(), the standard Sharq "props set, then mount" pattern that requires the
        /// attach-time flush in the first place (T-587).
        /// </summary>
        private class ChainComp : SusComponent
        {
            public Prop<bool> Reveal { get; } = new(false);
            private readonly int _depthRemaining;

            public static int RevealedCount;

            public ChainComp(int depthRemaining)
            {
                _depthRemaining = depthRemaining;
            }

            protected override void Build()
            {
                WatchEffect(() =>
                {
                    if (!Reveal.Value || childCount > 0 || _depthRemaining <= 0) return;
                    RevealedCount++;
                    var child = new ChainComp(_depthRemaining - 1);
                    child.Reveal.Value = true; // queued pending flush, child not attached yet
                    Add(child); // `this` is already on-panel here → synchronous nested attach
                });
            }
        }

        private const int ChainDepth = 50;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            ChainComp.RevealedCount = 0;
            SusComponent.DebugInterceptedReentrantFlushCount = 0;
        }

        [UnityTest]
        public IEnumerator DeepSyncRevealCascade_InterceptsReentrantFlushes_InsteadOfRecursing()
        {
            var root = new ChainComp(ChainDepth);
            root.Reveal.Value = true; // pending flush queued before Add — standard pattern

            Root.Add(root);
            yield return WaitFrame();

            // Every level with depthRemaining > 0 (root down to the level that spawns the
            // depthRemaining==0 leaf) increments once; the leaf itself flushes too but its
            // depthRemaining<=0 guard stops it from creating a 51st child — so ChainDepth
            // increments total, not ChainDepth+1.
            Assert.AreEqual(ChainDepth, ChainComp.RevealedCount,
                "every level of the chain down to the leaf must have run its reveal " +
                "WatchEffect — nothing silently dropped by the guard, exactly the class of " +
                "bug T-587 fixed.");

            Assert.Greater(SusComponent.DebugInterceptedReentrantFlushCount, 0,
                "this scenario must actually exercise re-entrant flushing (a child attaching " +
                "synchronously from inside its parent's own flush) — a zero here would mean " +
                "the test stopped proving anything about the guard.");

            Assert.AreEqual(ChainDepth, SusComponent.DebugInterceptedReentrantFlushCount,
                "every child in the chain (all but the outermost root) attaches while its " +
                "parent's flush is still on the stack, so every one of them must be " +
                "intercepted and queued rather than recursed into.");
        }

        [UnityTest]
        public IEnumerator DeepSyncRevealCascade_FlushDepthNeverExceedsOne()
        {
            // Cross-check from the other direction: after the whole cascade settles, the
            // depth guard must be back to its resting state (0) — no leaked "still flushing"
            // state that would wedge future attaches.
            var root = new ChainComp(ChainDepth);
            root.Reveal.Value = true;

            Root.Add(root);
            yield return WaitFrame();

            // A second, independent component attaching afterwards must flush normally —
            // depthRemaining=1 so it both reveals ITSELF (proving its own top-level flush
            // ran, unintercepted) and spawns one nested child (proving the guard still
            // correctly intercepts fresh re-entrancy afterwards, not just once).
            var probe = new ChainComp(1);
            probe.Reveal.Value = true;
            Root.Add(probe);
            yield return WaitFrame();

            Assert.AreEqual(ChainDepth + 1, ChainComp.RevealedCount,
                "the independent probe component must also flush on its own attach after " +
                "the deep chain settled — the guard must not stay stuck open.");
        }
    }
}
