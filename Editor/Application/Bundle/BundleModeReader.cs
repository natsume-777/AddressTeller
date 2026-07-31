using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Thin read-only helper that normalizes an <see cref="AddressableAssetGroup"/>'s BundleMode
    /// (PackTogether/PackSeparately/PackTogetherByLabel) into a <see cref="BundleModeKind"/>.
    /// </summary>
    public static class BundleModeReader
    {
        /// <summary>
        /// Reads a group's BundleMode.
        /// Returns <see cref="BundleModeKind.Unknown"/> for a group with no <see cref="BundledAssetGroupSchema"/> attached.
        /// </summary>
        public static BundleModeKind ReadBundleMode(AddressableAssetGroup group)
        {
            var schema = group?.GetSchema<BundledAssetGroupSchema>();
            if (schema == null) return BundleModeKind.Unknown;

            switch (schema.BundleMode)
            {
                case BundledAssetGroupSchema.BundlePackingMode.PackTogether:
                    return BundleModeKind.PackTogether;
                case BundledAssetGroupSchema.BundlePackingMode.PackSeparately:
                    return BundleModeKind.PackSeparately;
                case BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel:
                    return BundleModeKind.PackTogetherByLabel;
                default:
                    // 将来 Addressables 側に新しい BundlePackingMode が追加された場合に、
                    // 例外を投げず Unknown に倒しつつ見落とさないよう警告を出す。
                    Debug.LogWarning($"[AddressTeller] BundleMode '{schema.BundleMode}' for group '{group.Name}' is not supported and will be treated as Unknown.");
                    return BundleModeKind.Unknown;
            }
        }

        /// <summary>
        /// Reads the BundleMode for multiple groups at once and returns a group name -> BundleMode
        /// dictionary. Since group names are not guaranteed unique in Addressables (the UI enforces
        /// uniqueness, but direct API manipulation, asset duplication, placement in another folder, etc.
        /// can produce duplicates), this does not use ToDictionary (which throws on a duplicate key);
        /// instead, when a duplicate is found, the first group found is used and processing continues.
        /// Warnings for duplicates are not logged directly; they are returned via
        /// <paramref name="warnings"/> so the caller (report, ResultWindow, etc.) can decide whether to
        /// log them, avoiding duplicate logging per call site. At most one warning is returned per
        /// duplicated group name (three or more duplicates still produce a single warning).
        /// </summary>
        public static IReadOnlyDictionary<string, BundleModeKind> ReadBundleModes(IEnumerable<AddressableAssetGroup> groups, out IReadOnlyList<string> warnings)
        {
            var result = new Dictionary<string, BundleModeKind>();
            var warningList = new List<string>();
            var warnedGroupNames = new HashSet<string>();

            foreach (var group in groups.Where(g => g != null))
            {
                if (result.ContainsKey(group.Name))
                {
                    if (warnedGroupNames.Add(group.Name))
                        warningList.Add($"Multiple groups are named '{group.Name}'. The first one found will be used for bundle distribution calculation.");
                    continue;
                }

                result.Add(group.Name, ReadBundleMode(group));
            }

            warnings = warningList;
            return result;
        }
    }
}
