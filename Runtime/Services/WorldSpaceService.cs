using System.Collections.Generic;
using Sharq.Core.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Binding record for a world-space UI element attached to a Transform.
    /// </summary>
    public sealed class WorldBinding
    {
        public VisualElement Element;
        public Transform Target;
        public Vector3 Offset;
        public bool ClampToScreenEdges;
    }

    /// <summary>
    /// Binds UI Toolkit elements to 3D-world Transforms.
    ///
    /// Two modes:
    ///   Variant A (default via SusApp) — true world-space panel via SusWorldSpacePanel.
    ///   Variant B (fallback)           — flat screen-space markers projected each frame with
    ///                                    WorldToScreenPoint into a dedicated <see cref="WorldMarkerLayer"/>.
    ///
    /// Both variants render UNDER the screens: variant A is a separate lower-sorted UIDocument,
    /// variant B lives in the below-screens <see cref="MarkerLayer"/>. World markers must never
    /// paint over screen UI, so they are NEVER placed in the top-most <see cref="OverlayHost"/>.
    ///
    /// Static convenience API (for Sharq-generated code):
    ///   WorldSpaceService.BindToWorld(el, target, offset);
    ///   WorldSpaceService.Unbind(el);
    ///   WorldSpaceService.GetEdgePosition(worldPos, pw, ph, margin);
    ///
    /// Instance API (for tests and manual setup):
    ///   var svc = new WorldSpaceService { MarkerLayer = layer, MainCamera = cam };
    ///   svc.Tick();
    /// </summary>
    public class WorldSpaceService
    {
        // ─── Static singleton (for Sharq-generated code) ───────────

        /// <summary>
        /// Default instance used by static convenience methods.
        /// Set automatically by <see cref="SusApp"/> / <see cref="SusBootstrap.EnsureWorldSpacePanel"/>.
        /// If null, static properties fall back to internal static fields (tests/scenarios).
        /// </summary>
        public static WorldSpaceService Default { get; set; }

        private static Camera _cameraFallback;

        // ─── Static convenience API ────────────────────────────────

        public static Camera Camera
        {
            get => Default != null ? Default.MainCamera : _cameraFallback;
            set
            {
                if (Default != null)
                    Default.MainCamera = value;
                else
                    _cameraFallback = value;
            }
        }

        public static WorldBinding BindToWorld(VisualElement element, Transform target,
            Vector3 offset = default)
        {
            if (Default != null)
                return Default.Bind(element, target, offset);

            // Standalone mode (tests, no OverlayHost configured)
            if (element == null || target == null)
                return null;

            var binding = new WorldBinding
            {
                Element = element,
                Target = target,
                Offset = offset,
            };
            _standaloneBindings.Add(binding);
            return binding;
        }

        /// <summary>
        /// Position-based binding (no Transform, uses explicit world-space Vector3).
        /// Position must be updated externally — Tick() only recalculates Transform-bound elements.
        /// </summary>
        public static void BindToWorld(VisualElement element, Vector3 worldPosition)
        {
            if (Default != null)
            {
                Default.BindPosition(element, worldPosition);
                return;
            }
            // Standalone mode
            if (element == null) return;
            _standaloneBindings.Add(new WorldBinding
            {
                Element = element,
                Target = null,
                Offset = worldPosition,
            });
        }

        public static void Unbind(VisualElement element)
        {
            if (Default != null)
            {
                Default.UnbindElement(element);
                return;
            }
            // Standalone mode
            for (int i = _standaloneBindings.Count - 1; i >= 0; i--)
            {
                if (_standaloneBindings[i].Element == element)
                {
                    _standaloneBindings.RemoveAt(i);
                    return;
                }
            }
        }

        public static WorldEdgeData GetEdgePosition(
            Vector3 worldPos, float panelWidth, float panelHeight, float margin)
        {
            if (Default != null)
                return Default.CalculateEdgePosition(worldPos, panelWidth, panelHeight, margin);

            // Fallback: use static camera + direct calculation
            var cam = _cameraFallback;
            if (cam == null)
                return new WorldEdgeData { IsOnScreen = false, IsBehindCamera = true };

            return CalculateEdgeStatic(cam, worldPos, panelWidth, panelHeight, margin);
        }

        public static void Tick()
        {
            if (Default != null)
            {
                Default.TickPositions();
                return;
            }
            // Standalone mode — clean up dead bindings (only Element gone)
            for (int i = _standaloneBindings.Count - 1; i >= 0; i--)
            {
                if (_standaloneBindings[i].Element == null)
                    _standaloneBindings.RemoveAt(i);
            }
        }

        public static int BindingCount
        {
            get
            {
                if (Default != null)
                    return Default._bindings.Count;
                return _standaloneBindings.Count;
            }
        }

        /// <summary>
        /// Unbinds and removes ALL active bindings.
        /// </summary>
        public static void ClearAll()
        {
            if (Default != null)
            {
                Default.UnbindAll();
                return;
            }
            _standaloneBindings.Clear();
        }

        // ─── Standalone state (when Default is not set, e.g. in tests) ─

        private static readonly List<WorldBinding> _standaloneBindings = new();

#if UNITY_EDITOR
        // With Domain Reload disabled these survive leaving Play Mode: Default would point at an
        // instance whose OverlayHost/MarkerLayer belong to a destroyed panel, _cameraFallback at a
        // destroyed Camera, and standalone bindings at Transforms/VisualElements from the previous
        // scene.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Default = null;
            _cameraFallback = null;
            _standaloneBindings.Clear();
        }
#endif

        /// <summary>
        /// Dedicated screen-space host for the variant-B flat-marker fallback — a
        /// <see cref="WorldMarkerLayer"/> inserted UNDER the screens (see
        /// <see cref="SusBootstrap.GetOrCreateWorldMarkerLayer"/>). This is the correct host:
        /// world markers belong to 3D objects and must render below screen UI (a menu/HUD covers
        /// them). Wired automatically by <see cref="SusApp"/> / <see cref="SusBootstrap.EnsureWorldSpacePanel"/>.
        /// </summary>
        public VisualElement MarkerLayer { get; set; }

        /// <summary>
        /// Legacy screen-space host. Kept only for backward compatibility and unit tests.
        /// Prefer <see cref="MarkerLayer"/>: the OverlayHost is always the TOP-most layer
        /// (popups/modals), so binding world markers here would paint them OVER screens.
        /// Used only when <see cref="MarkerLayer"/> is not set.
        /// </summary>
        public OverlayHost OverlayHost { get; set; }

        /// <summary>
        /// Resolved screen-space host for variant B: the dedicated below-screens
        /// <see cref="MarkerLayer"/> when set, else the legacy <see cref="OverlayHost"/>.
        /// </summary>
        private VisualElement ScreenHost => MarkerLayer ?? (VisualElement)OverlayHost;

        /// <summary>Adds a projected marker to the screen-space host (variant B).</summary>
        private void AddMarker(VisualElement element)
        {
            if (element == null) return;
            if (MarkerLayer != null)
                MarkerLayer.Add(element);
            else
                OverlayHost?.AddToOverlay(element, OverlayCategory.World);
        }

        /// <summary>Removes a projected marker from the screen-space host (variant B).</summary>
        private void RemoveMarker(VisualElement element)
        {
            if (element == null) return;
            if (MarkerLayer != null)
            {
                if (element.parent == MarkerLayer)
                    MarkerLayer.Remove(element);
            }
            else
            {
                OverlayHost?.RemoveFromOverlay(element);
            }
        }

        /// <summary>
        /// Instance camera for world-to-screen projection.
        /// Static alias: WorldSpaceService.Camera (delegates here when Default is set).
        /// </summary>
        public Camera MainCamera { get; set; }

        /// <summary>
        /// Optional world-space panel (variant A). When set, BindToWorld/Unbind
        /// delegate to this panel instead of the screen-space OverlayHost.
        /// Tick() becomes a no-op (the panel has its own LateUpdate).
        /// </summary>
        public SusWorldSpacePanel WorldSpacePanel { get; set; }

        /// <summary>
        /// True when using variant A (world-space panel), false for variant B (screen-space markers).
        /// </summary>
        public bool IsWorldSpaceMode => WorldSpacePanel != null;

        internal readonly List<WorldBinding> _bindings = new();
        private readonly List<WorldBinding> _toRemove = new();

        /// <summary>
        /// Returns the number of active bindings.
        /// </summary>
        public int Count => _bindings.Count;

        /// <summary>
        /// Minimum interval between Tick() updates in milliseconds (0 = every frame).
        /// </summary>
        public float UpdateIntervalMs { get; set; } = 0f;

        private float _lastTickTime;

        // ─── Instance Bind / Unbind ────────────────────────────────

        /// <summary>
        /// Instance method: binds an element to a world-space Transform.
        /// Static alias: WorldSpaceService.BindToWorld(el, target, offset)
        /// </summary>
        public WorldBinding Bind(VisualElement element, Transform target,
            Vector3 offset = default)
        {
            return BindToWorldInternal(element, target, offset);
        }

        private WorldBinding BindToWorldInternal(VisualElement element, Transform target, Vector3 offset)
        {
            if (element == null || target == null)
                return null;

            if (WorldSpacePanel != null)
            {
                WorldSpacePanel.AttachElement(element, target, offset);
                var wb = new WorldBinding { Element = element, Target = target, Offset = offset };
                _bindings.Add(wb);
                return wb;
            }

            if (ScreenHost == null)
                return null;

            element.style.position = Position.Absolute;
            element.pickingMode = PickingMode.Ignore;

            var binding = new WorldBinding
            {
                Element = element,
                Target = target,
                Offset = offset,
                ClampToScreenEdges = false,
            };

            _bindings.Add(binding);
            AddMarker(element);

            return binding;
        }

        /// <summary>
        /// Instance method: unbinds and removes the element.
        /// </summary>
        public void UnbindElement(VisualElement element)
        {
            if (element == null) return;

            for (int i = _bindings.Count - 1; i >= 0; i--)
            {
                if (_bindings[i].Element == element)
                {
                    if (WorldSpacePanel != null)
                        WorldSpacePanel.DetachElement(element);
                    else
                        RemoveMarker(element);

                    _bindings.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// Unbinds all elements tied to a given Transform.
        /// </summary>
        public void UnbindTarget(Transform target)
        {
            if (target == null) return;
            for (int i = _bindings.Count - 1; i >= 0; i--)
            {
                if (_bindings[i].Target == target)
                    UnbindElement(_bindings[i].Element);
            }
        }

        /// <summary>
        /// Unbinds and removes ALL active bindings from this instance.
        /// </summary>
        public void UnbindAll()
        {
            for (int i = _bindings.Count - 1; i >= 0; i--)
                UnbindElement(_bindings[i].Element);
        }

        /// <summary>
        /// Position-based binding: adds element to the screen-space marker layer for rendering,
        /// but uses explicit screen-space positioning (no Transform-driven Tick).
        /// The caller is responsible for setting left/top based on GetEdgePosition.
        /// </summary>
        public void BindPosition(VisualElement element, Vector3 worldPosition)
        {
            if (element == null || ScreenHost == null) return;

            element.style.position = Position.Absolute;
            element.pickingMode = PickingMode.Ignore;

            // Use a null-target binding for lifecycle tracking (Unbind will clean it up).
            // Tick() skips null-target entries during cleanup.
            var binding = new WorldBinding
            {
                Element = element,
                Target = null, // position-based, no Transform
                Offset = worldPosition,
            };
            _bindings.Add(binding);
            AddMarker(element);
        }

        // ─── Instance Tick ─────────────────────────────────────────

        public void TickPositions()
        {
            if (_bindings.Count == 0) return;

            if (WorldSpacePanel != null)
            {
                _toRemove.Clear();
                for (int i = 0; i < _bindings.Count; i++)
                {
                    if (_bindings[i].Element == null)
                        _toRemove.Add(_bindings[i]);
                }
                foreach (var dead in _toRemove)
                {
                    WorldSpacePanel.DetachElement(dead.Element);
                    _bindings.Remove(dead);
                }
                return;
            }

            if (MainCamera == null || ScreenHost?.panel == null) return;

            if (UpdateIntervalMs > 0f)
            {
                float now = Time.unscaledTime * 1000f;
                if (now - _lastTickTime < UpdateIntervalMs) return;
                _lastTickTime = now;
            }

            var panel = ScreenHost.panel;
            float pw = panel.visualTree.resolvedStyle.width;
            float ph = panel.visualTree.resolvedStyle.height;

            _toRemove.Clear();

            for (int i = 0; i < _bindings.Count; i++)
            {
                var b = _bindings[i];

                // Auto-remove only if element is gone from hierarchy
                if (b.Element == null)
                {
                    _toRemove.Add(b);
                    continue;
                }

                // Position-based bindings (no Transform): skip Tick update,
                // caller manages position via GetEdgePosition
                if (b.Target == null)
                    continue;

                var worldPos = b.Target.position + b.Offset;
                var screenPos = MainCamera.WorldToScreenPoint(worldPos);

                if (screenPos.z < 0)
                {
                    b.Element.style.display = DisplayStyle.None;
                    continue;
                }

                b.Element.style.display = DisplayStyle.Flex;

                var panelPos = RuntimePanelUtils.ScreenToPanel(panel,
                    new Vector2(screenPos.x, Screen.height - screenPos.y));

                float elW = b.Element.resolvedStyle.width;
                float elH = b.Element.resolvedStyle.height;
                float x = panelPos.x - elW / 2f;
                float y = panelPos.y - elH;

                if (b.ClampToScreenEdges)
                {
                    x = Mathf.Clamp(x, 4f, pw - elW - 4f);
                    y = Mathf.Clamp(y, 4f, ph - elH - 4f);
                }

                b.Element.style.left = x;
                b.Element.style.top = y;
            }

            foreach (var dead in _toRemove)
            {
                RemoveMarker(dead.Element);
                _bindings.Remove(dead);
            }
        }

        // ─── Edge Projection (SusWorldMarker) ──────────────────────

        public WorldEdgeData CalculateEdgePosition(
            Vector3 worldPos, float panelWidth, float panelHeight, float margin)
        {
            if (MainCamera == null)
            {
                return new WorldEdgeData { IsOnScreen = false, IsBehindCamera = true };
            }

            var screenPos = MainCamera.WorldToScreenPoint(worldPos);
            var distance = Vector3.Distance(MainCamera.transform.position, worldPos);

            if (screenPos.z < 0f)
            {
                screenPos.x = Screen.width - screenPos.x;
                screenPos.y = Screen.height - screenPos.y;
                screenPos.z = -screenPos.z;

                var clampedBehind = ClampToEdge(
                    new Vector2(screenPos.x, Screen.height - screenPos.y),
                    panelWidth, panelHeight, margin);

                return new WorldEdgeData
                {
                    LocalPosition = clampedBehind.Position,
                    ArrowAngle = clampedBehind.Angle,
                    Side = clampedBehind.Side,
                    IsOnScreen = false,
                    IsBehindCamera = true,
                    Distance = distance
                };
            }

            float panelX = screenPos.x;
            float panelY = Screen.height - screenPos.y;

            bool isOnScreen = panelX >= margin && panelX <= panelWidth - margin &&
                              panelY >= margin && panelY <= panelHeight - margin;

            if (isOnScreen)
            {
                return new WorldEdgeData
                {
                    LocalPosition = new Vector3(panelX, panelY, 0),
                    Side = EdgeSide.None,
                    IsOnScreen = true,
                    IsBehindCamera = false,
                    Distance = distance
                };
            }

            var clamped = ClampToEdge(new Vector2(panelX, panelY), panelWidth, panelHeight, margin);

            return new WorldEdgeData
            {
                LocalPosition = clamped.Position,
                ArrowAngle = clamped.Angle,
                Side = clamped.Side,
                IsOnScreen = false,
                IsBehindCamera = false,
                Distance = distance
            };
        }

        // ─── Helpers ───────────────────────────────────────────────

        public WorldSpaceDriver AttachDriver()
        {
            var go = new GameObject("__WorldSpaceDriver__");
            go.hideFlags = HideFlags.HideAndDontSave;
            var driver = go.AddComponent<WorldSpaceDriver>();
            driver.Service = this;
            return driver;
        }

        public void UseWorldSpacePanel(SusWorldSpacePanel panel)
        {
            WorldSpacePanel = panel;
            if (MainCamera != null && panel.TargetCamera == null)
                panel.TargetCamera = MainCamera;
        }

        public void UseScreenSpacePanel()
        {
            if (WorldSpacePanel != null)
            {
                WorldSpacePanel.DetachAll();
                WorldSpacePanel = null;
            }
        }

        private static (Vector3 Position, float Angle, EdgeSide Side) ClampToEdge(
            Vector2 screenPos, float panelW, float panelH, float margin)
        {
            float cx = panelW / 2f;
            float cy = panelH / 2f;
            float dx = screenPos.x - cx;
            float dy = screenPos.y - cy;

            float normDx = Mathf.Abs(dx / (panelW / 2f));
            float normDy = Mathf.Abs(dy / (panelH / 2f));

            EdgeSide side;
            float x, y;

            if (normDx > normDy)
            {
                if (dx > 0) { side = EdgeSide.Right; x = panelW - margin; }
                else { side = EdgeSide.Left; x = margin; }
                y = Mathf.Clamp(screenPos.y, margin, panelH - margin);
            }
            else
            {
                if (dy > 0) { side = EdgeSide.Bottom; y = panelH - margin; }
                else { side = EdgeSide.Top; y = margin; }
                x = Mathf.Clamp(screenPos.x, margin, panelW - margin);
            }

            float angle = Mathf.Atan2(screenPos.y - y, screenPos.x - x) * Mathf.Rad2Deg;

            return (new Vector3(x, y, 0), angle, side);
        }

        /// <summary>
        /// Static fallback for GetEdgePosition when Default is not set.
        /// Used by tests that set static Camera directly.
        /// </summary>
        private static WorldEdgeData CalculateEdgeStatic(
            Camera cam, Vector3 worldPos, float panelWidth, float panelHeight, float margin)
        {
            if (cam == null)
                return new WorldEdgeData { IsOnScreen = false, IsBehindCamera = true };

            var screenPos = cam.WorldToScreenPoint(worldPos);
            var distance = Vector3.Distance(cam.transform.position, worldPos);

            if (screenPos.z < 0f)
            {
                screenPos.x = Screen.width - screenPos.x;
                screenPos.y = Screen.height - screenPos.y;
                screenPos.z = -screenPos.z;

                var clampedBehind = ClampToEdge(
                    new Vector2(screenPos.x, Screen.height - screenPos.y),
                    panelWidth, panelHeight, margin);

                return new WorldEdgeData
                {
                    LocalPosition = clampedBehind.Position,
                    ArrowAngle = clampedBehind.Angle,
                    Side = clampedBehind.Side,
                    IsOnScreen = false,
                    IsBehindCamera = true,
                    Distance = distance
                };
            }

            float panelX = screenPos.x;
            float panelY = Screen.height - screenPos.y;

            bool isOnScreen = panelX >= margin && panelX <= panelWidth - margin &&
                              panelY >= margin && panelY <= panelHeight - margin;

            if (isOnScreen)
            {
                return new WorldEdgeData
                {
                    LocalPosition = new Vector3(panelX, panelY, 0),
                    Side = EdgeSide.None,
                    IsOnScreen = true,
                    IsBehindCamera = false,
                    Distance = distance
                };
            }

            var clamped = ClampToEdge(new Vector2(panelX, panelY), panelWidth, panelHeight, margin);

            return new WorldEdgeData
            {
                LocalPosition = clamped.Position,
                ArrowAngle = clamped.Angle,
                Side = clamped.Side,
                IsOnScreen = false,
                IsBehindCamera = false,
                Distance = distance
            };
        }
    }
}
