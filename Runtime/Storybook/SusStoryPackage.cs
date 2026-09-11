using System;
using System.Collections.Generic;

namespace Sharq.Core.Storybook
{
    /// <summary>Package id + version as resolved for one assembly.</summary>
    public readonly struct SusStoryPackageStamp
    {
        public SusStoryPackageStamp(string packageId, string version)
        {
            PackageId = packageId;
            Version = version;
        }

        /// <summary>Full package id (<c>com.sharq-it.sus.core</c>), or null when unknown.</summary>
        public string PackageId { get; }

        /// <summary>Package version (<c>1.0.29</c>), or null when unknown.</summary>
        public string Version { get; }

        public bool IsEmpty => string.IsNullOrEmpty(PackageId) && string.IsNullOrEmpty(Version);
    }

    /// <summary>One group header of zone A and the stories under it.</summary>
    public sealed class SusStoryGroup
    {
        internal SusStoryGroup(string id, IReadOnlyList<SusStoryEntry> stories)
        {
            Id = id;
            Stories = stories;
        }

        /// <summary>Group segment of the id as written (<c>atoms</c>).</summary>
        public string Id { get; }

        /// <summary>Stories of this group, already ordered.</summary>
        public IReadOnlyList<SusStoryEntry> Stories { get; }
    }

    /// <summary>
    /// One package tab of zone A. A package with zero groups is NOT an error and NOT hidden —
    /// it is the "loaded, but declared no stories" case the mock-up shows as a dashed plate.
    /// </summary>
    public sealed class SusStoryPackage
    {
        internal SusStoryPackage(
            string key,
            string packageId,
            string version,
            IReadOnlyList<SusStoryGroup> groups,
            SusStoryPackageKind kind = SusStoryPackageKind.Product)
        {
            Key = key;
            PackageId = packageId;
            Version = version;
            Groups = groups;
            Kind = kind;
        }

        /// <summary>Short key = first id segment and the tab label (<c>kit</c>).</summary>
        public string Key { get; }

        /// <summary>Full package id shown on the empty plate; falls back to a derived guess.</summary>
        public string PackageId { get; }

        /// <summary>Version string shown at the bottom of zone A, or empty when unresolved.</summary>
        public string Version { get; }

        /// <summary>Groups in display order; empty for a package that declared no stories.</summary>
        public IReadOnlyList<SusStoryGroup> Groups { get; }

        /// <summary>
        /// Product or engine fixture (plan §4.1b, D21, card T-3409). A package is a fixture only
        /// when EVERY assembly that contributed to it said so: one product mark on the key is
        /// enough to make the tab real, because a bench cannot quietly hide a shipping package.
        /// </summary>
        public SusStoryPackageKind Kind { get; }

        /// <summary>True when this package is a test bench and is therefore not listed.</summary>
        public bool IsFixture => Kind == SusStoryPackageKind.Fixture;

        /// <summary>True when this package contributes no stories at all.</summary>
        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < Groups.Count; i++)
                    if (Groups[i].Stories.Count > 0) return false;
                return true;
            }
        }

        /// <summary>Total number of stories under this tab.</summary>
        public int StoryCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Groups.Count; i++) n += Groups[i].Stories.Count;
                return n;
            }
        }

        public override string ToString() => Key + " (" + StoryCount + " stories)";
    }
}
