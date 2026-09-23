using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor
{
    /// <summary>Scope of entries targeted by a clear operation.</summary>
    public enum ClearScope
    {
        /// <summary>Targets only entries in groups AddressTeller owns (groups where a rule declares Address()).</summary>
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

            var targets = new List<(AddressableAssetEntry Entry, string Guid, string Address, string GroupName, IReadOnlyList<string> Labels)>();

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                if (scope == ClearScope.Managed && !managedGroups.Contains(group.Name)) continue;

                foreach (var entry in group.entries)
                {
                    targets.Add((
                        entry,
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
                // settings.RemoveAssetEntry(guid) は内部で FindAssetEntry(guid)（settings.groups を先頭から
                // 探して最初に見つかった1件を返す）を経由するため、同一 guid が複数グループに存在する状態では
                // 意図しない側（このループで実際に列挙した target.Entry とは限らない）を削除しうる。
                // ここでは列挙時に確定させたエントリ自身を、それが属するグループから直接取り除くことで、
                // どのエントリを消すかの曖昧さを排除する。呼び出し元（ClearAll/ClearCLICore）は
                // DuplicateAssetEntryDetector で事前にこの状態自体を弾いているため通常は到達しないが、
                // このメソッド単体（Clear）は防御的にエントリ単位で安全な削除にしておく。
                target.Entry.parentGroup.RemoveAssetEntry(target.Entry);
            }

            return cleared;
        }
    }
}
