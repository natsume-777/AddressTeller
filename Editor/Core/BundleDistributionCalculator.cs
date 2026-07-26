using System;
using System.Collections.Generic;
using System.Linq;

namespace AddressTeller
{
    /// <summary>
    /// グループの BundleMode（PackTogether/PackSeparately/PackTogetherByLabel）の正規化値。
    /// Addressables の <c>BundlePackingMode</c> に依存しない Core 側独立定義。
    /// </summary>
    public enum BundleModeKind
    {
        PackTogether,
        PackSeparately,
        PackTogetherByLabel,

        /// <summary>BundledAssetGroupSchema が付与されていない等、BundleMode が判定できないグループ。</summary>
        Unknown,
    }

    /// <summary>
    /// <see cref="BundleDistributionCalculator.Calculate"/> への入力1件分。
    /// アセットが所属するグループ名と、付与されているラベル集合。
    /// </summary>
    public readonly struct BundleAssetPlacement
    {
        public string GroupName { get; }
        public IReadOnlyCollection<string> Labels { get; }

        public BundleAssetPlacement(string groupName, IReadOnlyCollection<string> labels)
        {
            GroupName = groupName;
            Labels = labels ?? Array.Empty<string>();
        }
    }

    /// <summary>論理バンドル分布の計算結果。</summary>
    public sealed class BundleDistribution
    {
        public IReadOnlyList<LogicalBundle> Bundles { get; }

        public BundleDistribution(IReadOnlyList<LogicalBundle> bundles)
        {
            Bundles = bundles;
        }
    }

    /// <summary>
    /// 論理バンドル1件分。グループ・BundleMode・分割キー・該当アセット数を持つ。
    /// Unknown モードの場合はバンドル数として数えず、グループ内アセット数の別集計として扱う
    /// （<see cref="BundleDistributionCalculator.Calculate"/> のコメント参照）。
    /// </summary>
    public sealed class LogicalBundle
    {
        public string GroupName { get; }
        public BundleModeKind Mode { get; }
        public string SplitKey { get; }
        public int AssetCount { get; }

        public LogicalBundle(string groupName, BundleModeKind mode, string splitKey, int assetCount)
        {
            GroupName = groupName;
            Mode = mode;
            SplitKey = splitKey;
            AssetCount = assetCount;
        }
    }

    /// <summary>
    /// Predict 結果（アセット→グループ/ラベル）と各グループの BundleMode から、
    /// ビルド前の論理バンドル単位の個数・分布を概算する純粋関数群。
    /// Addressables / AssetDatabase に依存しない（<c>AddressTellerReportBuilder</c> 等の
    /// レポート生成系のビルダー群と同じ流儀）。
    /// </summary>
    public static class BundleDistributionCalculator
    {
        /// <summary>ラベル集合が空の場合に使う固定の分割キー。</summary>
        public const string NoLabelsSplitKey = "(no labels)";

        /// <summary>BundleMode が判定できないグループに使う固定の分割キー。</summary>
        public const string UnknownSplitKey = "(unknown)";

        /// <summary>正規化ラベルキーの区切り文字。</summary>
        private const string LabelKeySeparator = "|";

        /// <summary>
        /// アセット→グループ/ラベルの配置と、グループ→BundleMode から論理バンドル分布を計算する。
        /// 戻り値の Bundles は GroupName → Mode → SplitKey（いずれも Ordinal）の順で決定的に整列される。
        /// </summary>
        /// <param name="assets">アセット識別子（GUID 等）→配置。</param>
        /// <param name="groupModes">グループ名→BundleMode。未掲載のグループは Unknown として扱う。</param>
        public static BundleDistribution Calculate(
            IReadOnlyDictionary<string, BundleAssetPlacement> assets,
            IReadOnlyDictionary<string, BundleModeKind> groupModes)
        {
            // グループ名 → そのグループに属するアセットの一覧（識別子 + 配置）。
            var assetsByGroup = new Dictionary<string, List<(string AssetId, BundleAssetPlacement Placement)>>();
            foreach (var pair in assets)
            {
                if (!assetsByGroup.TryGetValue(pair.Value.GroupName, out var list))
                {
                    list = new List<(string AssetId, BundleAssetPlacement Placement)>();
                    assetsByGroup[pair.Value.GroupName] = list;
                }

                list.Add((pair.Key, pair.Value));
            }

            var bundles = new List<LogicalBundle>();

            foreach (var (groupName, groupAssets) in assetsByGroup)
            {
                var mode = groupModes.TryGetValue(groupName, out var m) ? m : BundleModeKind.Unknown;

                switch (mode)
                {
                    case BundleModeKind.PackTogether:
                        bundles.Add(new LogicalBundle(groupName, mode, "all", groupAssets.Count));
                        break;

                    case BundleModeKind.PackSeparately:
                        foreach (var (assetId, _) in groupAssets)
                            bundles.Add(new LogicalBundle(groupName, mode, assetId, 1));
                        break;

                    case BundleModeKind.PackTogetherByLabel:
                        var byLabelKey = groupAssets
                            .GroupBy(a => NormalizeLabelKey(a.Placement.Labels));
                        foreach (var group in byLabelKey)
                            bundles.Add(new LogicalBundle(groupName, mode, group.Key, group.Count()));
                        break;

                    case BundleModeKind.Unknown:
                    default:
                        // Unknown はバンドル数として数えず、グループ単位でアセット数のみを別集計する。
                        bundles.Add(new LogicalBundle(groupName, BundleModeKind.Unknown, UnknownSplitKey, groupAssets.Count));
                        break;
                }
            }

            var ordered = bundles
                .OrderBy(b => b.GroupName, StringComparer.Ordinal)
                .ThenBy(b => b.Mode)
                .ThenBy(b => b.SplitKey, StringComparer.Ordinal)
                .ToList();

            return new BundleDistribution(ordered);
        }

        /// <summary>
        /// ラベル集合を Ordinal ソート + 区切り文字で連結した正規化キーを返す。
        /// 空集合の場合は <see cref="NoLabelsSplitKey"/> を返す。
        /// Addressables 本体は区切り文字なしで連結するため順序非決定・衝突の可能性があるが、
        /// 本実装ではその挙動に合わせず正規化キーを用いる（概算であることの一部）。
        /// </summary>
        private static string NormalizeLabelKey(IReadOnlyCollection<string> labels)
        {
            if (labels == null || labels.Count == 0) return NoLabelsSplitKey;

            return string.Join(LabelKeySeparator, labels.OrderBy(l => l, StringComparer.Ordinal));
        }
    }
}
