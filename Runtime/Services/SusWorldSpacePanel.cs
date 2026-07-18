using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Variant A: True world-space UI panel. Renders UI elements in 3D space
    /// via a separate UIDocument with PanelSettings.renderMode = WorldSpace.
    ///
    /// Elements are positioned at world transforms with billboarding (always
    /// face camera) and distance-based scaling for readability.
    ///
    /// Usage:
    ///   1. Create a GameObject with UIDocument + this component.
    ///   2. Assign a PanelSettings asset with renderMode = WorldSpace.
    ///   3. Call AttachElement(el, target) to bind UI to a 3D object.
    ///   4. LateUpdate automatically updates positions, rotation, and scale.
    ///
    /// Integration with WorldSpaceService (W7.4):
    ///   worldSpaceService.UseWorldSpacePanel(panel);  // switches to variant A
    ///   worldSpaceService.UseScreenSpacePanel();       // back to variant B
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("SUS/World Space Panel")]
    public class SusWorldSpacePanel : MonoBehaviour
    {
        [Header("Camera")]
        [Tooltip("Camera to billboard towards and measure distance from. Null = Camera.main.")]
        public Camera TargetCamera;

        [Header("Billboarding")]
        [Tooltip("Always face the camera.")]
        public bool EnableBillboard = true;

        [Header("Distance Scaling")]
        [Tooltip("Enable distance-based scaling for consistent readability.")]
        public bool EnableDistanceScaling = true;

        [Tooltip("World distance at which the element renders at 1x scale.")]
        public float BaseDistance = 10f;

        [Tooltip("Minimum scale multiplier (prevents elements becoming invisible at long range).")]
        public float MinScale = 0.5f;

        [Tooltip("Maximum scale multiplier (prevents elements blowing up when camera is close).")]
        public float MaxScale = 2f;

        [Header("Debug")]
        [Tooltip("Log attachment/detachment events.")]
        public bool VerboseLogging;

        private UIDocument _document;
        private VisualElement _root;
        private readonly Dictionary<VisualElement, Attachment> _attachments = new();

        private sealed class Attachment
        {
            public VisualElement Container;
            public Transform Target;
            public Vector3 Offset;
        }

        /// <summary>Root VisualElement of the world-space panel.</summary>
        public VisualElement Root => _root;

        /// <summary>Number of active attachments.</summary>
        public int Count => _attachments.Count;

        /// <summary>
        /// Event: fired when the panel successfully initialises (UIDocument + PanelSettings confirmed).
        /// Subscribe to know when it's safe to call AttachElement.
        /// </summary>
        public event System.Action OnReady;

        // ─── Unity Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _root = _document.rootVisualElement;

            if (_root == null)
                Debug.LogError("[SusWorldSpacePanel] UIDocument has no rootVisualElement. " +
                               "Make sure PanelSettings is assigned and renderMode = WorldSpace.", this);
        }

        private void Start()
        {
            if (TargetCamera == null)
                TargetCamera = Camera.main;

            if (_root != null && TargetCamera != null)
                OnReady?.Invoke();
        }

        private void LateUpdate()
        {
            if (TargetCamera == null || _attachments.Count == 0) return;

            var deadTargets = new List<VisualElement>();

            foreach (var kvp in _attachments)
            {
                var el = kvp.Key;
                var a = kvp.Value;

                if (a.Target == null)
                {
                    deadTargets.Add(el);
                    continue;
                }

                var worldPos = a.Target.position + a.Offset;

                // ── W7.2: 3D world position ──
                a.Container.style.translate = new Translate(worldPos.x, worldPos.y);

                // ── W7.3: Billboard (always face camera) ──
                if (EnableBillboard)
#pragma warning disable CS0618
                    a.Container.transform.rotation = TargetCamera.transform.rotation;
#pragma warning restore CS0618

                // ── W7.3: Distance-based scaling ──
                if (EnableDistanceScaling)
                {
                    float dist = Vector3.Distance(TargetCamera.transform.position, worldPos);
                    float scale = Mathf.Clamp(BaseDistance / Mathf.Max(dist, 0.01f), MinScale, MaxScale);
                    a.Container.style.scale = new Scale(Vector3.one * scale);
                }
                else
                {
                    a.Container.style.scale = new Scale(Vector3.one);
                }
            }

            // Cleanup dead targets
            foreach (var el in deadTargets)
                DetachElement(el);
        }

        private void OnDestroy()
        {
            foreach (var a in _attachments.Values)
                a.Container?.RemoveFromHierarchy();
            _attachments.Clear();
        }

        // ─── W7.2: Attach / Detach ───────────────────────────────────────────

        /// <summary>
        /// Attach a UI element to a world-space Transform.
        /// The element is placed inside a container positioned at the target's
        /// world position + offset. Billboard and scaling are applied each frame
        /// in LateUpdate.
        /// </summary>
        public void AttachElement(VisualElement element, Transform target, Vector3 offset = default)
        {
            if (element == null)
            {
                if (VerboseLogging) Debug.LogWarning("[SusWorldSpacePanel] AttachElement: element is null.");
                return;
            }
            if (target == null)
            {
                if (VerboseLogging) Debug.LogWarning("[SusWorldSpacePanel] AttachElement: target is null.");
                return;
            }
            if (_root == null)
            {
                Debug.LogError("[SusWorldSpacePanel] AttachElement: panel not initialised (no rootVisualElement).");
                return;
            }

            // Already attached → detach first
            if (_attachments.TryGetValue(element, out var old))
            {
                if (VerboseLogging) Debug.Log("[SusWorldSpacePanel] Re-attaching element, detaching previous.");
                old.Container.RemoveFromHierarchy();
            }

            var container = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute },
            };
            container.Add(element);

            var worldPos = target.position + offset;
            container.style.translate = new Translate(worldPos.x, worldPos.y);

            _root.Add(container);

            _attachments[element] = new Attachment
            {
                Container = container,
                Target = target,
                Offset = offset,
            };

            if (VerboseLogging)
                Debug.Log($"[SusWorldSpacePanel] Attached '{element.name}' to '{target.name}' at {worldPos}");
        }

        /// <summary>
        /// Detach a previously attached element.
        /// </summary>
        public void DetachElement(VisualElement element)
        {
            if (element == null || !_attachments.TryGetValue(element, out var attachment))
                return;

            attachment.Container.RemoveFromHierarchy();
            _attachments.Remove(element);

            if (VerboseLogging)
                Debug.Log($"[SusWorldSpacePanel] Detached '{element.name}'");
        }

        /// <summary>
        /// Detach all elements bound to a given Transform.
        /// </summary>
        public void DetachTarget(Transform target)
        {
            if (target == null) return;

            var toRemove = new List<VisualElement>();
            foreach (var kvp in _attachments)
                if (kvp.Value.Target == target)
                    toRemove.Add(kvp.Key);

            foreach (var el in toRemove)
                DetachElement(el);
        }

        /// <summary>Detach all elements.</summary>
        public void DetachAll()
        {
            foreach (var a in _attachments.Values)
                a.Container?.RemoveFromHierarchy();
            _attachments.Clear();
        }
    }
}
