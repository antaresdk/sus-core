using UnityEngine;

namespace Sharq.Core.World
{
    /// <summary>
    /// Result of edge projection for off-screen world-space markers.
    /// </summary>
    public struct WorldEdgeData
    {
        /// <summary>Screen-space position clamped to the edge, in panel-local coordinates.</summary>
        public Vector3 LocalPosition;

        /// <summary>Angle in degrees the arrow should rotate to point at the object.</summary>
        public float ArrowAngle;

        /// <summary>Which edge the marker is clamped to.</summary>
        public EdgeSide Side;

        /// <summary>Whether the tracked object is visible in the camera frustum.</summary>
        public bool IsOnScreen;

        /// <summary>Whether the object is behind the camera (z &lt; 0).</summary>
        public bool IsBehindCamera;

        /// <summary>Distance from camera to object in world units.</summary>
        public float Distance;
    }
}
