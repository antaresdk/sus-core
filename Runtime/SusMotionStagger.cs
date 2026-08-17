using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>Staggered play of the same motion factory across children.</summary>
    public static class SusMotionStagger
    {
        /// <summary>
        /// Play the same factory on each child with <paramref name="delayStepS"/> between starts.
        /// Factory must return a fresh <see cref="SusMotion"/> for that child (not yet played).
        /// </summary>
        public static SusMotionHandle Children(
            VisualElement parent,
            Func<VisualElement, SusMotion> factory,
            float delayStepS = 0.04f,
            SusRestoreMode restore = SusRestoreMode.KeywordNull)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            var handles = new List<SusMotionHandle>(parent.childCount);
            int pending = 0;
            bool allStarted = false;
            bool playing = true;

            void OnOneComplete()
            {
                pending--;
                if (allStarted && pending <= 0)
                    playing = false;
            }

            int index = 0;
            foreach (var child in parent.Children())
            {
                var motion = factory(child);
                if (motion == null) continue;

                float delay = index * Mathf.Max(0f, delayStepS);
                motion.Delay(delay).Restore(restore);
                pending++;
                handles.Add(motion.Play(OnOneComplete));
                index++;
            }

            allStarted = true;
            if (pending <= 0)
                playing = false;

            return new SusMotionHandle(
                () => playing,
                applyRestore =>
                {
                    playing = false;
                    foreach (var h in handles)
                        h.Stop(applyRestore);
                });
        }
    }
}
