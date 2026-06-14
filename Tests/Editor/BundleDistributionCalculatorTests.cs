using NUnit.Framework;
using System.Collections.Generic;

namespace Natsume777.AddressTeller.Editor.Tests
{
    /// <summary>
    /// BundleDistributionCalculator.Calculate の単体テスト。
    /// Addressables に依存しない純粋関数のため、ダミーの assets/groupModes を直接組み立てて検証する。
    /// </summary>
    public class BundleDistributionCalculatorTests
    {
        [Test]
        public void PackTogether_AllAssetsInGroup_BecomeOneBundleWithCorrectAssetCount()
        {
            var assets = new Dictionary<string, BundleAssetPlacement>
            {
                ["asset1"] = new BundleAssetPlacement("Characters", null),
                ["asset2"] = new BundleAssetPlacement("Characters", null),
                ["asset3"] = new BundleAssetPlacement("Characters", null),
            };
            var groupModes = new Dictionary<string, BundleModeKind>
            {
                ["Characters"] = BundleModeKind.PackTogether,
            };

            var distribution = BundleDistributionCalculator.Calculate(assets, groupModes);

            Assert.AreEqual(1, distribution.Bundles.Count);
            Assert.AreEqual("Characters", distribution.Bundles[0].GroupName);
            Assert.AreEqual(BundleModeKind.PackTogether, distribution.Bundles[0].Mode);
            Assert.AreEqual(3, distribution.Bundles[0].AssetCount);
        }

        [Test]
        public void PackSeparately_AssetCountEqualsBundleCount()
        {
            var assets = new Dictionary<string, BundleAssetPlacement>
            {
                ["asset1"] = new BundleAssetPlacement("Items", null),
                ["asset2"] = new BundleAssetPlacement("Items", null),
            };
            var groupModes = new Dictionary<string, BundleModeKind>
            {
                ["Items"] = BundleModeKind.PackSeparately,
            };

            var distribution = BundleDistributionCalculator.Calculate(assets, groupModes);

            Assert.AreEqual(2, distribution.Bundles.Count);
            foreach (var bundle in distribution.Bundles)
            {
                Assert.AreEqual(BundleModeKind.PackSeparately, bundle.Mode);
                Assert.AreEqual(1, bundle.AssetCount);
            }
        }

        [Test]
        public void PackTogetherByLabel_SameLabelSetRegardlessOfOrder_GroupsIntoSameBundle()
        {
            var assets = new Dictionary<string, BundleAssetPlacement>
            {
                ["asset1"] = new BundleAssetPlacement("UI", new[] { "lang_ja", "common" }),
                ["asset2"] = new BundleAssetPlacement("UI", new[] { "common", "lang_ja" }),
            };
            var groupModes = new Dictionary<string, BundleModeKind>
            {
                ["UI"] = BundleModeKind.PackTogetherByLabel,
            };

            var distribution = BundleDistributionCalculator.Calculate(assets, groupModes);

            Assert.AreEqual(1, distribution.Bundles.Count);
            Assert.AreEqual("common|lang_ja", distribution.Bundles[0].SplitKey);
            Assert.AreEqual(2, distribution.Bundles[0].AssetCount);
        }

        [Test]
        public void PackTogetherByLabel_DifferentLabelSets_BecomeSeparateBundles()
        {
            var assets = new Dictionary<string, BundleAssetPlacement>
            {
                ["asset1"] = new BundleAssetPlacement("UI", new[] { "lang_ja" }),
                ["asset2"] = new BundleAssetPlacement("UI", new[] { "lang_en" }),
            };
            var groupModes = new Dictionary<string, BundleModeKind>
            {
                ["UI"] = BundleModeKind.PackTogetherByLabel,
            };

            var distribution = BundleDistributionCalculator.Calculate(assets, groupModes);

            Assert.AreEqual(2, distribution.Bundles.Count);
            CollectionAssert.AreEqual(
                new[] { "lang_en", "lang_ja" },
                new[] { distribution.Bundles[0].SplitKey, distribution.Bundles[1].SplitKey });
        }

        [Test]
        public void PackTogetherByLabel_AssetWithNoLabels_UsesNoLabelsSplitKey()
        {
            var assets = new Dictionary<string, BundleAssetPlacement>
            {
                ["asset1"] = new BundleAssetPlacement("UI", null),
                ["asset2"] = new BundleAssetPlacement("UI", new string[0]),
            };
            var groupModes = new Dictionary<string, BundleModeKind>
            {
                ["UI"] = BundleModeKind.PackTogetherByLabel,
            };

            var distribution = BundleDistributionCalculator.Calculate(assets, groupModes);

            Assert.AreEqual(1, distribution.Bundles.Count);
            Assert.AreEqual(BundleDistributionCalculator.NoLabelsSplitKey, distribution.Bundles[0].SplitKey);
            Assert.AreEqual(2, distribution.Bundles[0].AssetCount);
        }

        [Test]
        public void GroupNotInGroupModes_TreatedAsUnknown()
        {
            var assets = new Dictionary<string, BundleAssetPlacement>
            {
                ["asset1"] = new BundleAssetPlacement("Misc", null),
                ["asset2"] = new BundleAssetPlacement("Misc", null),
            };
            var groupModes = new Dictionary<string, BundleModeKind>();

            var distribution = BundleDistributionCalculator.Calculate(assets, groupModes);

            Assert.AreEqual(1, distribution.Bundles.Count);
            Assert.AreEqual(BundleModeKind.Unknown, distribution.Bundles[0].Mode);
            Assert.AreEqual(BundleDistributionCalculator.UnknownSplitKey, distribution.Bundles[0].SplitKey);
            Assert.AreEqual(2, distribution.Bundles[0].AssetCount);
        }

        [Test]
        public void GroupExplicitlyUnknown_TreatedSameAsMissingEntry()
        {
            var assets = new Dictionary<string, BundleAssetPlacement>
            {
                ["asset1"] = new BundleAssetPlacement("Misc", null),
            };
            var groupModes = new Dictionary<string, BundleModeKind>
            {
                ["Misc"] = BundleModeKind.Unknown,
            };

            var distribution = BundleDistributionCalculator.Calculate(assets, groupModes);

            Assert.AreEqual(1, distribution.Bundles.Count);
            Assert.AreEqual(BundleModeKind.Unknown, distribution.Bundles[0].Mode);
            Assert.AreEqual(BundleDistributionCalculator.UnknownSplitKey, distribution.Bundles[0].SplitKey);
        }

        [Test]
        public void Bundles_AreOrderedByGroupNameThenModeThenSplitKey()
        {
            var assets = new Dictionary<string, BundleAssetPlacement>
            {
                // Zグループ: PackSeparatelyで2バンドル（SplitKeyはアセットID）
                ["z2"] = new BundleAssetPlacement("Z", null),
                ["z1"] = new BundleAssetPlacement("Z", null),
                // Aグループ: PackTogetherByLabelで2バンドル
                ["a1"] = new BundleAssetPlacement("A", new[] { "label2" }),
                ["a2"] = new BundleAssetPlacement("A", new[] { "label1" }),
            };
            var groupModes = new Dictionary<string, BundleModeKind>
            {
                ["Z"] = BundleModeKind.PackSeparately,
                ["A"] = BundleModeKind.PackTogetherByLabel,
            };

            var distribution = BundleDistributionCalculator.Calculate(assets, groupModes);

            Assert.AreEqual(4, distribution.Bundles.Count);

            // GroupNameのOrdinal順: "A" < "Z"
            Assert.AreEqual("A", distribution.Bundles[0].GroupName);
            Assert.AreEqual("A", distribution.Bundles[1].GroupName);
            Assert.AreEqual("Z", distribution.Bundles[2].GroupName);
            Assert.AreEqual("Z", distribution.Bundles[3].GroupName);

            // Aグループ内はSplitKeyのOrdinal順: "label1" < "label2"
            Assert.AreEqual("label1", distribution.Bundles[0].SplitKey);
            Assert.AreEqual("label2", distribution.Bundles[1].SplitKey);

            // Zグループ内はSplitKey(アセットID)のOrdinal順: "z1" < "z2"
            Assert.AreEqual("z1", distribution.Bundles[2].SplitKey);
            Assert.AreEqual("z2", distribution.Bundles[3].SplitKey);
        }

        [Test]
        public void EmptyAssets_ReturnsEmptyBundles()
        {
            var assets = new Dictionary<string, BundleAssetPlacement>();
            var groupModes = new Dictionary<string, BundleModeKind>();

            var distribution = BundleDistributionCalculator.Calculate(assets, groupModes);

            Assert.AreEqual(0, distribution.Bundles.Count);
        }
    }
}
