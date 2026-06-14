using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// 論理バンドル分布（<see cref="BundleDistribution"/>）の算出と、表示用の集計をまとめる。
    /// <see cref="Build"/> は Addressables 統合（スナップショット・グループ設定）に依存し、
    /// <see cref="Summarize"/> は <see cref="BundleDistribution"/> のみを扱う純粋関数。
    /// </summary>
    public static class BundleDistributionSummarizer
    {
        /// <summary>
        /// 適用後スナップショットと現在の <see cref="AddressableAssetSettings"/> から論理バンドル分布を算出する。
        /// 呼び出し側で例外を捕捉すること（<see cref="AddressTellerReportBuilder.Build(DryRunResult, AddressableAssetSettings)"/> 参照）。
        /// </summary>
        public static BundleDistribution Build(AddressTellerSnapshot after, AddressableAssetSettings settings)
        {
            var placements = after.Entries.ToDictionary(
                e => e.Guid,
                e => new BundleAssetPlacement(e.GroupName, e.Labels));

            var groupModes = BundleModeReader.ReadBundleModes(settings.groups);

            return BundleDistributionCalculator.Calculate(placements, groupModes);
        }

        /// <summary>
        /// <see cref="BundleDistribution"/> から表示用の集計値を計算する純粋関数。
        /// </summary>
        public static DistributionSummary Summarize(BundleDistribution distribution)
        {
            var totalLogicalBundleCount = distribution.Bundles.Count(b => b.Mode != BundleModeKind.Unknown);
            var unknownGroupCount = distribution.Bundles.Count(b => b.Mode == BundleModeKind.Unknown);

            LargestBundleInfo largest = null;
            foreach (var bundle in distribution.Bundles)
            {
                if (largest == null
                    || bundle.AssetCount > largest.AssetCount
                    || (bundle.AssetCount == largest.AssetCount && IsEarlier(bundle, largest)))
                {
                    largest = new LargestBundleInfo(bundle.GroupName, bundle.Mode, bundle.SplitKey, bundle.AssetCount);
                }
            }

            return new DistributionSummary(totalLogicalBundleCount, unknownGroupCount, largest);
        }

        /// <summary>
        /// AssetCount が同値の場合に、<see cref="BundleDistributionCalculator.Calculate"/> の整列順
        /// （GroupName → Mode → SplitKey の Ordinal 順）に合わせて先になる方を選ぶための比較。
        /// </summary>
        private static bool IsEarlier(LogicalBundle candidate, LargestBundleInfo current)
        {
            var groupComparison = string.Compare(candidate.GroupName, current.GroupName, StringComparison.Ordinal);
            if (groupComparison != 0) return groupComparison < 0;

            if (candidate.Mode != current.Mode) return candidate.Mode < current.Mode;

            return string.Compare(candidate.SplitKey, current.SplitKey, StringComparison.Ordinal) < 0;
        }
    }

    /// <summary>論理バンドル分布の表示用集計結果。</summary>
    public sealed class DistributionSummary
    {
        public int TotalLogicalBundleCount { get; }
        public int UnknownGroupCount { get; }

        /// <summary>AssetCount が最大の論理バンドル。<see cref="BundleDistribution.Bundles"/> が0件の場合は null。</summary>
        public LargestBundleInfo LargestBundle { get; }

        public DistributionSummary(int totalLogicalBundleCount, int unknownGroupCount, LargestBundleInfo largestBundle)
        {
            TotalLogicalBundleCount = totalLogicalBundleCount;
            UnknownGroupCount = unknownGroupCount;
            LargestBundle = largestBundle;
        }
    }

    /// <summary>最大集約バンドルの表示に必要な情報。</summary>
    public sealed class LargestBundleInfo
    {
        public string GroupName { get; }
        public BundleModeKind Mode { get; }
        public string SplitKey { get; }
        public int AssetCount { get; }

        public LargestBundleInfo(string groupName, BundleModeKind mode, string splitKey, int assetCount)
        {
            GroupName = groupName;
            Mode = mode;
            SplitKey = splitKey;
            AssetCount = assetCount;
        }
    }
}
