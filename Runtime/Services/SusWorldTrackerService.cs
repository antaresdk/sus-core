using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Tracks a world-space Transform and maps it to a UI element position.
    /// </summary>
    public class WorldTracker
    {
        public Transform Target;
        public VisualElement UIElement;
        public bool HiddenBehindCamera;
        public Vector2 ScreenPosition;
        public Vector2 LastPosition;
    }

    /// <summary>
    /// Manages 3D-to-2D world-to-UI tracking for multiple targets.
    /// Updates positions each frame with sub-pixel caching.
    /// </summary>
    public class SusWorldTrackerService
    {
        public Prop<List<WorldTracker>> Trackers = new(new List<WorldTracker>());

        private Camera _camera;
        private VisualElement _rootPanel;
        private const float PositionThreshold = 0.5f;

        /// <summary>
        /// Initialize with the rendering camera and root UI panel.
        /// </summary>
        public void Init(Camera camera, VisualElement rootPanel)
        {
            _camera = camera;
            _rootPanel = rootPanel;
        }

        /// <summary>
        /// Register a world-space Transform for UI tracking.
        /// </summary>
        public void Register(Transform target, VisualElement ui)
        {
            if (target == null || ui == null) return;

            // Avoid duplicates
            foreach (var t in Trackers.Value)
            {
                if (t.Target == target) return;
            }

            ui.pickingMode = PickingMode.Ignore;

            var tracker = new WorldTracker
            {
                Target = target,
                UIElement = ui
            };

            // Rebuild list to trigger reactive update
            var list = Trackers.Value;
            list.Add(tracker);
            Trackers.Value = new List<WorldTracker>(list);
        }

        /// <summary>
        /// Remove a tracked Transform.
        /// </summary>
        public void Unregister(Transform target)
        {
            if (target == null) return;

            var list = Trackers.Value;
            list.RemoveAll(t => t.Target == target);
            Trackers.Value = new List<WorldTracker>(list);
        }

        /// <summary>
        /// Update all tracker positions. Call each frame.
        /// </summary>
        public void TickPositions()
        {
            if (_camera == null || _rootPanel?.panel == null) return;

            var panel = _rootPanel.panel;

            foreach (var tracker in Trackers.Value)
            {
                if (tracker.Target == null || tracker.UIElement == null)
                    continue;

                var screenPos = _camera.WorldToScreenPoint(tracker.Target.position);

                // Behind camera check
                if (screenPos.z < 0)
                {
                    if (!tracker.HiddenBehindCamera)
                    {
                        tracker.HiddenBehindCamera = true;
                        tracker.UIElement.style.display = DisplayStyle.None;
                    }
                    continue;
                }
                else if (tracker.HiddenBehindCamera)
                {
                    tracker.HiddenBehindCamera = false;
                    tracker.UIElement.style.display = DisplayStyle.Flex;
                }

                var panelPos = RuntimePanelUtils.ScreenToPanel(panel, screenPos);

                // Sub-pixel caching
                var delta = panelPos - tracker.LastPosition;
                if (Mathf.Abs(delta.x) < PositionThreshold &&
                    Mathf.Abs(delta.y) < PositionThreshold)
                    continue;

                tracker.LastPosition = tracker.ScreenPosition;
                tracker.ScreenPosition = panelPos;

                // GPU-friendly positioning
                tracker.UIElement.style.translate = new Translate(panelPos.x, panelPos.y, 0);
            }
        }
    }
}
