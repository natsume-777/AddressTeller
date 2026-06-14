using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// <see cref="AddressableAssetGroup"/> の BundleMode（PackTogether/PackSeparately/PackTogetherByLabel）を
    /// <see cref="BundleModeKind"/> に正規化する薄い読み取り専用ヘルパー。
    /// </summary>
    public static class BundleModeReader
    {
        /// <summary>
        /// グループの BundleMode を読み取る。
        /// <see cref="BundledAssetGroupSchema"/> が付与されていないグループは <see cref="BundleModeKind.Unknown"/> を返す。
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
                    Debug.LogWarning($"[AddressTeller] グループ '{group.Name}' の BundleMode '{schema.BundleMode}' は未対応のため Unknown として扱います。");
                    return BundleModeKind.Unknown;
            }
        }

        /// <summary>複数グループの BundleMode をまとめて読み取り、グループ名→BundleMode の辞書を返す。</summary>
        public static IReadOnlyDictionary<string, BundleModeKind> ReadBundleModes(IEnumerable<AddressableAssetGroup> groups)
        {
            return groups
                .Where(g => g != null)
                .ToDictionary(g => g.Name, ReadBundleMode);
        }
    }
}
