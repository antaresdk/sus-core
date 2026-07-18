using UnityEngine;

namespace Sharq.Core
{
    /// <summary>
    /// Driver MonoBehaviour for WorldSpaceService — calls Tick() every LateUpdate
    /// to keep world-space overlays in sync with 3D transforms.
    /// Created automatically by WorldSpaceService.AttachDriver().
    /// </summary>
    public class WorldSpaceDriver : MonoBehaviour
    {
        public WorldSpaceService Service;

        private void LateUpdate()
        {
            Service?.TickPositions();
        }
    }
}
