using NUnit.Framework;

namespace Sharq.Core.Editor.Tests
{
    public class DependencyTrackerTests
    {
        [Test]
        public void Track_CollectsSourcesReadInsideFunction()
        {
            var p1 = new Prop<int>(10);
            var p2 = new Prop<string>("hello");
            var collected = new System.Collections.Generic.List<IReactiveSource>();

            using (DependencyTracker.Track(src => collected.Add(src)))
            {
                _ = p1.Value;
                _ = p2.Value;
            }

            Assert.AreEqual(2, collected.Count);
            Assert.Contains(p1, collected);
            Assert.Contains(p2, collected);
        }

        [Test]
        public void Track_DoesNotCollectWhenNotReadingProps()
        {
            var collected = new System.Collections.Generic.List<IReactiveSource>();

            using (DependencyTracker.Track(src => collected.Add(src)))
            {
                _ = 42;
            }

            Assert.AreEqual(0, collected.Count);
        }

        [Test]
        public void NestedTracking_DoesNotLeakOuterCollector()
        {
            var outerSources = new System.Collections.Generic.List<IReactiveSource>();
            var innerSources = new System.Collections.Generic.List<IReactiveSource>();
            var p1 = new Prop<int>(1);
            var p2 = new Prop<int>(2);

            using (DependencyTracker.Track(src => outerSources.Add(src)))
            {
                _ = p1.Value;

                using (DependencyTracker.Track(src => innerSources.Add(src)))
                {
                    _ = p2.Value;
                }
            }

            Assert.AreEqual(1, innerSources.Count);
            Assert.AreEqual(1, outerSources.Count);
            Assert.Contains(p1, outerSources);
            Assert.False(outerSources.Contains(p2), "Outer should not receive p2 from nested scope");
        }

        [Test]
        public void Computed_RegistersAsDependency()
        {
            var p = new Prop<int>(5);
            var c = new Computed<int>(() => p.Value * 2);
            var collected = new System.Collections.Generic.List<IReactiveSource>();

            using (DependencyTracker.Track(src => collected.Add(src)))
            {
                _ = c.Value;
            }

            Assert.AreEqual(1, collected.Count);
            Assert.Contains(c as IReactiveSource, collected);
        }

        #region Untracked

        [Test]
        public void Untracked_DoesNotCollectDependencies()
        {
            var p = new Prop<int>(42);
            var collected = new System.Collections.Generic.List<IReactiveSource>();

            using (DependencyTracker.Track(src => collected.Add(src)))
            {
                var val = DependencyTracker.Untracked(() => p.Value);
                Assert.AreEqual(42, val);
            }

            Assert.AreEqual(0, collected.Count, "Untracked read should not register dependency");
        }

        [Test]
        public void Untracked_RestoresTrackingAfterBlock()
        {
            var p1 = new Prop<int>(1);
            var p2 = new Prop<int>(2);
            var collected = new System.Collections.Generic.List<IReactiveSource>();

            using (DependencyTracker.Track(src => collected.Add(src)))
            {
                _ = p1.Value; // tracked (1)

                var untrackedVal = DependencyTracker.Untracked(() => p2.Value);
                Assert.AreEqual(2, untrackedVal);

                _ = p1.Value; // tracked again (2)
            }

            Assert.AreEqual(2, collected.Count, "p2 should NOT be tracked, p1 should be tracked twice");
            Assert.False(collected.Contains(p2), "Untracked source must not appear in collection");
        }

        [Test]
        public void Untracked_Void_DoesNotTrack()
        {
            var p = new Prop<int>(99);
            var collected = new System.Collections.Generic.List<IReactiveSource>();

            using (DependencyTracker.Track(src => collected.Add(src)))
            {
                DependencyTracker.Untracked(() => { _ = p.Value; });
            }

            Assert.AreEqual(0, collected.Count);
        }

        [Test]
        public void Untracked_Nested_WorksCorrectly()
        {
            var p = new Prop<int>(10);
            var collected = new System.Collections.Generic.List<IReactiveSource>();

            using (DependencyTracker.Track(src => collected.Add(src)))
            {
                DependencyTracker.Untracked(() =>
                {
                    DependencyTracker.Untracked(() => { _ = p.Value; });
                });
            }

            Assert.AreEqual(0, collected.Count);
        }

        #endregion
    }
}
