using System;
using System.Collections.Generic;
using System.Linq;

namespace AddressTeller
{
    /// <summary>
    /// Normalized value for a group's BundleMode (PackTogether/PackSeparately/PackTogetherByLabel).
    /// Defined independently in Core, without depending on Addressables' <c>BundlePackingMode</c>.
    /// </summary>
    public enum BundleModeKind
    {
        /// <summary>All assets in the group are packed into a single bundle.</summary>
        PackTogether,

        /// <summary>Each asset in the group is packed into its own separate bundle.</summary>
        PackSeparately,

        /// <summary>Assets in the group are packed into one bundle per distinct label set.</summary>
        PackTogetherByLabel,

        /// <summary>The group's BundleMode could not be determined, e.g. no BundledAssetGroupSchema is attached.</summary>
        Unknown,
    }

    /// <summary>
    /// One input entry for <see cref="BundleDistributionCalculator.Calculate"/>.
    /// The group an asset belongs to, and the set of labels assigned to it.
    /// </summary>
    public readonly struct BundleAssetPlacement
    {
        /// <summary>Name of the group the asset belongs to.</summary>
        public string GroupName { get; }

        /// <summary>Labels assigned to the asset.</summary>
        public IReadOnlyCollection<string> Labels { get; }

        /// <summary>Creates a BundleAssetPlacement for a single asset. A null <paramref name="labels"/> is treated as empty.</summary>
        public BundleAssetPlacement(string groupName, IReadOnlyCollection<string> labels)
        {
            GroupName = groupName;
            Labels = labels ?? Array.Empty<string>();
        }
    }

    /// <summary>Result of computing the logical bundle distribution.</summary>
    public sealed class BundleDistribution
    {
        /// <summary>The computed logical bundles.</summary>
        public IReadOnlyList<LogicalBundle> Bundles { get; }

        /// <summary>Creates a BundleDistribution wrapping the given computed bundles.</summary>
        public BundleDistribution(IReadOnlyList<LogicalBundle> bundles)
        {
            Bundles = bundles;
        }
    }

    /// <summary>
    /// One logical bundle: group, BundleMode, split key, and the number of assets it contains.
    /// For the Unknown mode this is not counted as a bundle; it is instead a separate per-group asset
    /// count (see the remarks on <see cref="BundleDistributionCalculator.Calculate"/>).
    /// </summary>
    public sealed class LogicalBundle
    {
        /// <summary>Name of the group this bundle belongs to.</summary>
        public string GroupName { get; }

        /// <summary>The group's BundleMode.</summary>
        public BundleModeKind Mode { get; }

        /// <summary>
        /// Key identifying the split within the group (e.g. "all", an asset id, or a label key). May also
        /// be the fixed strings <see cref="BundleDistributionCalculator.NoLabelsSplitKey"/> or
        /// <see cref="BundleDistributionCalculator.UnknownSplitKey"/>.
        /// </summary>
        public string SplitKey { get; }

        /// <summary>Number of assets in this bundle.</summary>
        public int AssetCount { get; }

        /// <summary>Creates a LogicalBundle describing one group/mode/split-key combination and its asset count.</summary>
        public LogicalBundle(string groupName, BundleModeKind mode, string splitKey, int assetCount)
        {
            GroupName = groupName;
            Mode = mode;
            SplitKey = splitKey;
            AssetCount = assetCount;
        }
    }

    /// <summary>
    /// Pure functions that estimate the pre-build logical bundle count/distribution from a Predict
    /// result (asset -> group/labels) and each group's BundleMode.
    /// Does not depend on Addressables / AssetDatabase (the same approach used by the report-generating
    /// builders such as <c>AddressTellerReportBuilder</c>).
    /// </summary>
    public static class BundleDistributionCalculator
    {
        /// <summary>Fixed split key used when the label set is empty.</summary>
        public const string NoLabelsSplitKey = "(no labels)";

        /// <summary>Fixed split key used for a group whose BundleMode could not be determined.</summary>
        public const string UnknownSplitKey = "(unknown)";

        /// <summary>Separator character used in the normalized label key.</summary>
        private const string LabelKeySeparator = "|";

        /// <summary>
        /// Computes the logical bundle distribution from an asset -> group/label placement map and a
        /// group -> BundleMode map.
        /// The returned Bundles are sorted deterministically by GroupName, then Mode, then SplitKey
        /// (all Ordinal).
        /// </summary>
        /// <param name="assets">Asset identifier (e.g. GUID) -> placement.</param>
        /// <param name="groupModes">Group name -> BundleMode. Groups not listed are treated as Unknown.</param>
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
