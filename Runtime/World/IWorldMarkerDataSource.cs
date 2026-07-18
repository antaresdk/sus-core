using System;
using System.Collections.Generic;

namespace Sharq.Core.World
{
    /// <summary>
    /// Read-only snapshot of all data needed by a world-space marker.
    /// Returned by IWorldMarkerDataSource.Snapshot.
    /// </summary>
    public struct WorldMarkerData
    {
        public string UnitName;
        public int Level;
        public float HpFraction;
        public string HpText;
        public float ShieldFraction;
        public string Faction;
        public string IconName;
        public IReadOnlyList<WorldBuff> Buffs;
        public bool IsAlive;
    }

    /// <summary>
    /// Tier 3 adapter — universal data source for world-space markers.
    /// Decouples marker rendering from game architecture (MonoBehaviour, ECS, networking, etc.).
    ///
    /// Usage:
    /// <code>
    /// marker.DataSource.Value = new EcsUnitMarkerSource(entityId);
    /// // Marker calls Snapshot on init, subscribes to OnDataChanged for updates.
    /// </code>
    /// </summary>
    public interface IWorldMarkerDataSource
    {
        WorldMarkerData Snapshot { get; }
        event Action OnDataChanged;
    }
}
