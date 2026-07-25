using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace AddressTeller.Editor
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
                    Debug.LogWarning($"[AddressTeller] BundleMode '{schema.BundleMode}' for group '{group.Name}' is not supported and will be treated as Unknown.");
                    return BundleModeKind.Unknown;
            }
        }

        /// <summary>
        /// 複数グループの BundleMode をまとめて読み取り、グループ名→BundleMode の辞書を返す。
        /// グループ名は Addressables 上で一意性が保証されていない（UI からは一意性が強制されるが、
        /// API 直接操作・アセット複製・別フォルダ配置等では重複しうる）ため、ToDictionary（重複キーで例外）は使わず、
        /// 重複を検出した場合は最初に見つかったグループを採用して処理を継続する。重複が見つかった場合の警告は
        /// ログへ直書きせず <paramref name="warnings"/> として返す（呼び出し元がレポート・ResultWindow 等、
        /// 経路ごとに重複してログ出力しないようにするため）。同一グループ名についての警告は1件のみ返す
        /// （3件以上重複していても警告は1回にまとめる）。
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
