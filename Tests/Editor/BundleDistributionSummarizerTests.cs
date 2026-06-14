using NUnit.Framework;
using System.Collections.Generic;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// BundleDistributionSummarizer.Summarize の単体テスト。
    /// BundleDistribution（純粋なデータ）を直接組み立てて検証する。
    /// </summary>
    public class BundleDistributionSummarizerTests
    {
        [Test]
        public void EmptyDistribution_ReturnsZeroCountsAndNullLargestBundle()
        {
            var distribution = new BundleDistribution(new List<LogicalBundle>());

            var summary = BundleDistributionSummarizer.Summarize(distribution);

            Assert.AreEqual(0, summary.TotalLogicalBundleCount);
            Assert.AreEqual(0, summary.UnknownGroupCount);
            Assert.IsNull(summary.LargestBundle);
        }

        [Test]
        public void UnknownBundles_ExcludedFromTotalAndCountedSeparately()
        {
            var distribution = new BundleDistribution(new List<LogicalBundle>
            {
                new LogicalBundle("A", BundleModeKind.PackTogether, "all", 3),
                new LogicalBundle("B", BundleModeKind.Unknown, BundleDistributionCalculator.UnknownSplitKey, 5),
                new LogicalBundle("C", BundleModeKind.Unknown, BundleDistributionCalculator.UnknownSplitKey, 2),
            });

            var summary = BundleDistributionSummarizer.Summarize(distribution);

            Assert.AreEqual(1, summary.TotalLogicalBundleCount);
            Assert.AreEqual(2, summary.UnknownGroupCount);
        }

        [Test]
        public void LargestBundle_SelectsBundleWithMaxAssetCount()
        {
            var distribution = new BundleDistribution(new List<LogicalBundle>
            {
                new LogicalBundle("A", BundleModeKind.PackTogether, "all", 3),
                new LogicalBundle("B", BundleModeKind.PackSeparately, "asset1", 1),
                new LogicalBundle("C", BundleModeKind.PackTogetherByLabel, "lang_ja", 10),
            });

            var summary = BundleDistributionSummarizer.Summarize(distribution);

            Assert.IsNotNull(summary.LargestBundle);
            Assert.AreEqual("C", summary.LargestBundle.GroupName);
            Assert.AreEqual("lang_ja", summary.LargestBundle.SplitKey);
            Assert.AreEqual(10, summary.LargestBundle.AssetCount);
        }

        [Test]
        public void LargestBundle_TieBrokenByGroupNameThenModeThenSplitKeyOrdinal()
        {
            // 同じ AssetCount(5)を持つバンドルが複数ある場合、BundleDistributionCalculator.Calculateと同じ
            // GroupName→Mode→SplitKeyのOrdinal順で先になる方を選ぶ。
            var distribution = new BundleDistribution(new List<LogicalBundle>
            {
                new LogicalBundle("Z", BundleModeKind.PackTogether, "all", 5),
                new LogicalBundle("A", BundleModeKind.PackTogether, "all", 5),
                new LogicalBundle("A", BundleModeKind.PackSeparately, "z_asset", 5),
                new LogicalBundle("A", BundleModeKind.PackSeparately, "a_asset", 5),
            });

            var summary = BundleDistributionSummarizer.Summarize(distribution);

            Assert.IsNotNull(summary.LargestBundle);
            Assert.AreEqual("A", summary.LargestBundle.GroupName);
            Assert.AreEqual(BundleModeKind.PackTogether, summary.LargestBundle.Mode);
            Assert.AreEqual("all", summary.LargestBundle.SplitKey);
            Assert.AreEqual(5, summary.LargestBundle.AssetCount);
        }

        [Test]
        public void LargestBundle_SameGroupAndSplitKey_TieBrokenByModeOrdinal()
        {
            // GroupName・SplitKeyが同じでもModeが異なる場合は、Modeのenum順（小さい方）を優先する。
            var distribution = new BundleDistribution(new List<LogicalBundle>
            {
                new LogicalBundle("A", BundleModeKind.PackSeparately, "x", 5),
                new LogicalBundle("A", BundleModeKind.PackTogether, "x", 5),
            });

            var summary = BundleDistributionSummarizer.Summarize(distribution);

            Assert.IsNotNull(summary.LargestBundle);
            Assert.AreEqual(BundleModeKind.PackTogether, summary.LargestBundle.Mode);
        }

        [Test]
        public void LargestBundle_UnknownBundlesAreStillConsideredForMaxAssetCount()
        {
            // LargestBundleはUnknown除外ロジックの影響を受けず、AssetCountのみで判定する。
            var distribution = new BundleDistribution(new List<LogicalBundle>
            {
                new LogicalBundle("A", BundleModeKind.PackTogether, "all", 2),
                new LogicalBundle("B", BundleModeKind.Unknown, BundleDistributionCalculator.UnknownSplitKey, 100),
            });

            var summary = BundleDistributionSummarizer.Summarize(distribution);

            Assert.IsNotNull(summary.LargestBundle);
            Assert.AreEqual("B", summary.LargestBundle.GroupName);
            Assert.AreEqual(100, summary.LargestBundle.AssetCount);
        }
    }
}
