using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Computes the logical bundle distribution (<see cref="BundleDistribution"/>) and its
    /// display-oriented aggregate.
    /// <see cref="Build"/> depends on Addressables integration (snapshot, group settings), while
    /// <see cref="Summarize"/> is a pure function that only handles a <see cref="BundleDistribution"/>.
    /// </summary>
    public static class BundleDistributionSummarizer
    {
        /// <summary>
        /// Computes the logical bundle distribution from the post-apply snapshot and the current
        /// <see cref="AddressableAssetSettings"/>. Callers must catch exceptions (the internal report
        /// builder used by <c>CheckCLI</c>/<c>ApplyAllCLI</c>/<c>ApplyWithValidateCLI</c> does so).
        /// <paramref name="warnings"/> receives any group-name-duplication warnings detected by
        /// <see cref="BundleModeReader.ReadBundleModes"/> (may be empty). This method does not log
        /// directly, so the caller can decide whether to log them.
        /// </summary>
        public static BundleDistribution Build(AddressTellerSnapshot after, AddressableAssetSettings settings, out IReadOnlyList<string> warnings)
        {
            var placements = after.Entries.ToDictionary(
                e => e.Guid,
                e => new BundleAssetPlacement(e.GroupName, e.Labels));

            var groupModes = BundleModeReader.ReadBundleModes(settings.groups, out warnings);

            return BundleDistributionCalculator.Calculate(placements, groupModes);
        }

        /// <summary>
        /// Pure function that computes display-oriented aggregate values from a <see cref="BundleDistribution"/>.
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

    /// <summary>Display-oriented aggregate result for the logical bundle distribution.</summary>
    public sealed class DistributionSummary
    {
        /// <summary>Total number of logical bundles, excluding Unknown.</summary>
        public int TotalLogicalBundleCount { get; }

        /// <summary>Number of groups whose BundleMode could not be determined (Unknown).</summary>
        public int UnknownGroupCount { get; }

        /// <summary>The logical bundle with the largest AssetCount. Null if <see cref="BundleDistribution.Bundles"/> is empty.</summary>
        public LargestBundleInfo LargestBundle { get; }

        /// <summary>Creates a DistributionSummary from precomputed aggregate values.</summary>
        public DistributionSummary(int totalLogicalBundleCount, int unknownGroupCount, LargestBundleInfo largestBundle)
        {
            TotalLogicalBundleCount = totalLogicalBundleCount;
            UnknownGroupCount = unknownGroupCount;
            LargestBundle = largestBundle;
        }
    }

    /// <summary>Information needed to display the largest consolidated bundle.</summary>
    public sealed class LargestBundleInfo
    {
        /// <summary>Name of the group this bundle belongs to.</summary>
        public string GroupName { get; }

        /// <summary>The group's BundleMode.</summary>
        public BundleModeKind Mode { get; }

        /// <summary>
        /// Key identifying the split within the group (see <see cref="LogicalBundle.SplitKey"/> for the
        /// possible fixed values).
        /// </summary>
        public string SplitKey { get; }

        /// <summary>Number of assets in this bundle.</summary>
        public int AssetCount { get; }

        /// <summary>Creates a LargestBundleInfo describing a single logical bundle.</summary>
        public LargestBundleInfo(string groupName, BundleModeKind mode, string splitKey, int assetCount)
        {
            GroupName = groupName;
            Mode = mode;
            SplitKey = splitKey;
            AssetCount = assetCount;
        }
    }
}
