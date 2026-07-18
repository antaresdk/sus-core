using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// World-space marker CONTAINER (framework primitive, sus-core).
    /// Binds a screen-space UI element to a 3D <see cref="Target"/> and repositions it every
    /// tick: expanded while the target is on-screen, clamped to the screen edge as a POI marker
    /// when off-screen (see <see cref="WorldSpaceService.GetEdgePosition"/>).
    ///
    /// This is the generic container only — it holds NO unit content (name/HP/faction/icon).
    /// Downstream UI content is added as a child:
    /// <code>
    /// var marker = new SusWorldMarker();
    /// marker.Target.Value = unit.transform;
    /// marker.Add(contentElement);
    /// </code>
    /// </summary>
    public class SusWorldMarker : VisualElement
    {
        /// <summary>3D transform the marker follows. Reactive.</summary>
        public Prop<Transform> Target { get; } = new(null);

        /// <summary>World-space offset added to the target position. Reactive.</summary>
        public Prop<Vector3> WorldOffset { get; } = new(new Vector3(0f, 2f, 0f));

        /// <summary>Camera used for projection. Falls back to <see cref="WorldSpaceService.Camera"/>. Reactive.</summary>
        public Prop<Camera> Camera { get; } = new(null);

        /// <summary>Reposition interval in milliseconds. Reactive.</summary>
        public Prop<int> UpdateRate { get; } = new(16);

        /// <summary>Raised when the marker is clicked.</summary>
        public event Action OnClick;

        private IVisualElementScheduledItem _ticker;

        public SusWorldMarker()
        {
            AddToClassList("sus-world-marker");
            style.position = Position.Absolute;

            Target.Changed += (_, __) => Rebind();
            WorldOffset.Changed += (_, __) => Rebind();
            UpdateRate.Changed += (_, rate) => RestartTicker(rate);
            Camera.Changed += (_, cam) => { if (cam != null) WorldSpaceService.Camera = cam; };

            RegisterCallback<AttachToPanelEvent>(_ => OnAttach());
            RegisterCallback<DetachFromPanelEvent>(_ => OnDetach());
            RegisterCallback<ClickEvent>(_ => OnClick?.Invoke());
        }

        private void OnAttach()
        {
            Rebind();
            RestartTicker(UpdateRate.Value);
        }

        private void OnDetach()
        {
            _ticker?.Pause();
            _ticker = null;
            WorldSpaceService.Unbind(this);
        }

        private void RestartTicker(int rate)
        {
            _ticker?.Pause();
            if (panel != null)
                _ticker = schedule.Execute(UpdatePosition).Every(Mathf.Max(1, rate));
        }

        private void Rebind()
        {
            if (panel == null) return;
            WorldSpaceService.Unbind(this);
            if (Target.Value != null)
                WorldSpaceService.BindToWorld(this, Target.Value, WorldOffset.Value);
        }

        private void UpdatePosition()
        {
            var cam = Camera.Value ?? WorldSpaceService.Camera;
            if (cam == null || Target.Value == null) return;

            var worldPos = Target.Value.position + WorldOffset.Value;

            float panelW = panel?.visualTree?.resolvedStyle.width ?? Screen.width;
            float panelH = panel?.visualTree?.resolvedStyle.height ?? Screen.height;
            if (panelW <= 0) panelW = Screen.width;
            if (panelH <= 0) panelH = Screen.height;

            var edge = WorldSpaceService.GetEdgePosition(worldPos, panelW, panelH, 0f);
            style.translate = new Translate(edge.LocalPosition.x, edge.LocalPosition.y);
        }
    }
}
