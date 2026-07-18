using System.Collections.Generic;

namespace Sharq.Core
{
    /// <summary>
    /// Key-value map of props passed from a scoped slot to its consumer.
    /// Used internally by BuildSlot.
    /// </summary>
    public class SlotPropMap : Dictionary<string, object>
    {
    }
}
