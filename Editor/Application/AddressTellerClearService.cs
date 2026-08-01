using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor
{
    /// <summary>Scope of entries targeted by a clear operation.</summary>
    public enum ClearScope
    {
        /// <summary>Targets only entries in groups managed by AddressTeller (groups referenced as GroupName by any rule).</summary>
        Managed = 0,

        /// <summary>Targets every Addressable entry.</summary>
        All = 1,
    }

    /// <summary>Information about a single entry removed by a clear operation. Used for logging, snapshot descriptions, etc.</summary>
    public readonly struct ClearedEntry
    {
        /// <summary>GUID of the removed entry's asset.</summary>
        public string Guid { get; }

        /// <summary>Address the entry had before removal.</summary>
        public string Address { get; }

        /// <summary>Name of the group the entry belonged to.</summary>
        public string GroupName { get; }

        /// <summary>Labels the entry had before removal.</summary>
        public IReadOnlyList<string> Labels { get; }

        /// <summary>Creates a ClearedEntry from the removed entry's pre-removal state.</summary>
        public ClearedEntry(string guid, string address, string groupName, IReadOnlyList<string> labels)
        {
            Guid = guid;
            Address = address;
            GroupName = groupName;
            Labels = labels;
        }
    }

    /// <summary>
    /// Bulk-removes every Addressables entry (or every entry managed by AddressTeller).
    /// This is an intentional exception to the default safe-by-default policy (asset-level ownership
    /// checks, off by default), aimed at pre-release package initial setup scenarios.
    /// Callers are expected to take a snapshot before removal and log the return value individually at
    /// Warning level or above.
    /// </summary>
    public static class AddressTellerClearService
    {
        /// <summary>
        /// Removes the target entries from <paramref name="settings"/>.
        /// When <paramref name="scope"/> is <see cref="ClearScope.Managed"/>, <paramref name="managedGroups"/>
        /// is required (throws <see cref="ArgumentNullException"/> if null).
        /// Returns information about each removed entry as <see cref="ClearedEntry"/>, sorted by
        /// GroupName then Guid (both Ordinal ascending).
        /// This method itself does not log anything (that is the caller's responsibility).
        /// </summary>
        /// <remarks>
        /// Null contract for this argument: since this method is a public API entry point that consumers
        /// call explicitly, it throws <see cref="ArgumentNullException"/> even when <paramref name="settings"/>
        /// is null (the same policy as Restore/RestoreExactWithRemoval/Diff on
        /// <see cref="AddressTellerSnapshotService"/>).
        /// </remarks>
        public static IReadOnlyList<ClearedEntry> Clear(
            AddressableAssetSettings settings,
            ClearScope scope,
            IReadOnlyCollection<string> managedGroups = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (scope == ClearScope.Managed && managedGroups == null)
                throw new ArgumentNullException(nameof(managedGroups), "managedGroups must be provided when scope is ClearScope.Managed.");

            var targets = new List<(string Guid, string Address, string GroupName, IReadOnlyList<string> Labels)>();

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                if (scope == ClearScope.Managed && !managedGroups.Contains(group.Name)) continue;

                foreach (var entry in group.entries)
                {
                    targets.Add((
                        entry.guid,
                        entry.address,
                        group.Name,
                        entry.labels.OrderBy(l => l, StringComparer.Ordinal).ToList()));
                }
            }

            var ordered = targets
                .OrderBy(t => t.GroupName, StringComparer.Ordinal)
                .ThenBy(t => t.Guid, StringComparer.Ordinal)
                .ToList();

            var cleared = new List<ClearedEntry>(ordered.Count);
            foreach (var target in ordered)
            {
                cleared.Add(new ClearedEntry(target.Guid, target.Address, target.GroupName, target.Labels));
                settings.RemoveAssetEntry(target.Guid);
            }

            return cleared;
        }
    }
}
