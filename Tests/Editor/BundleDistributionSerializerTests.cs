using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// BundleDistributionSerializer の単体テスト。
    /// CSV/Markdown へのシリアライズ・エスケープ・ファイル書き込みを検証する。
    /// Addressables / AssetDatabase には依存しない（純粋なデータのみを扱う）。
    /// </summary>
    public class BundleDistributionSerializerTests
    {
        private static BundleDistribution SimpleDistribution()
        {
            return new BundleDistribution(new List<LogicalBundle>
            {
                new LogicalBundle("Characters", BundleModeKind.PackTogether, "all", 3),
                new LogicalBundle("Items", BundleModeKind.PackSeparately, "asset1", 1),
                new LogicalBundle("Localized", BundleModeKind.PackTogetherByLabel, "lang_ja", 10),
            });
        }

        private static DistributionSummary SimpleSummary()
        {
            return new DistributionSummary(3, 0, new LargestBundleInfo("Localized", BundleModeKind.PackTogetherByLabel, "lang_ja", 10));
        }

        [Test]
        public void ToCsv_NullDistributionOrSummary_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, BundleDistributionSerializer.ToCsv(null, SimpleSummary()));
            Assert.AreEqual(string.Empty, BundleDistributionSerializer.ToCsv(SimpleDistribution(), null));
        }

        [Test]
        public void ToMarkdown_NullDistributionOrSummary_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, BundleDistributionSerializer.ToMarkdown(null, SimpleSummary()));
            Assert.AreEqual(string.Empty, BundleDistributionSerializer.ToMarkdown(SimpleDistribution(), null));
        }

        [Test]
        public void ToCsv_KnownDistribution_ContainsHeaderRowsAndDisclaimer()
        {
            var csv = BundleDistributionSerializer.ToCsv(SimpleDistribution(), SimpleSummary());

            var lines = csv.Replace("\r\n", "\n").Split('\n');

            Assert.AreEqual("GroupName,Mode,SplitKey,AssetCount", lines[0]);
            Assert.AreEqual("Characters,PackTogether,all,3", lines[1]);
            Assert.AreEqual("Items,PackSeparately,asset1,1", lines[2]);
            Assert.AreEqual("Localized,PackTogetherByLabel,lang_ja,10", lines[3]);

            StringAssert.Contains("# Logical bundle count: 3", csv);
            StringAssert.Contains("# Groups with unknown BundleMode: 0", csv);
            StringAssert.Contains("# Largest consolidated bundle: Localized / lang_ja (10 assets)", csv);
            StringAssert.Contains(AddressTellerReportBuilder.BundleDistributionDisclaimer, csv);
        }

        [Test]
        public void ToCsv_EmptyBundles_StillContainsHeaderAndSummary()
        {
            var distribution = new BundleDistribution(new List<LogicalBundle>());
            var summary = new DistributionSummary(0, 0, null);

            var csv = BundleDistributionSerializer.ToCsv(distribution, summary);

            var lines = csv.Replace("\r\n", "\n").Split('\n');
            Assert.AreEqual("GroupName,Mode,SplitKey,AssetCount", lines[0]);

            StringAssert.Contains("# Logical bundle count: 0", csv);
            StringAssert.Contains("# Groups with unknown BundleMode: 0", csv);
            StringAssert.DoesNotContain("Largest consolidated bundle", csv);
            StringAssert.Contains(AddressTellerReportBuilder.BundleDistributionDisclaimer, csv);
        }

        [Test]
        public void ToCsv_ValuesContainingCommaOrQuote_AreEscaped()
        {
            var distribution = new BundleDistribution(new List<LogicalBundle>
            {
                new LogicalBundle("Group, with comma", BundleModeKind.PackTogetherByLabel, "label\"quoted\"", 2),
            });
            var summary = new DistributionSummary(1, 0, new LargestBundleInfo("Group, with comma", BundleModeKind.PackTogetherByLabel, "label\"quoted\"", 2));

            var csv = BundleDistributionSerializer.ToCsv(distribution, summary);
            var lines = csv.Replace("\r\n", "\n").Split('\n');

            Assert.AreEqual("\"Group, with comma\",PackTogetherByLabel,\"label\"\"quoted\"\"\",2", lines[1]);
        }

        [Test]
        public void ToMarkdown_KnownDistribution_ContainsSummaryTableAndDisclaimer()
        {
            var markdown = BundleDistributionSerializer.ToMarkdown(SimpleDistribution(), SimpleSummary());

            StringAssert.Contains("Logical bundle count: 3", markdown);
            StringAssert.Contains("Groups with unknown BundleMode: 0", markdown);
            StringAssert.Contains("Largest consolidated bundle: Localized / lang_ja (10 assets)", markdown);

            StringAssert.Contains("| GroupName | Mode | SplitKey | AssetCount |", markdown);
            StringAssert.Contains("| Characters | PackTogether | all | 3 |", markdown);
            StringAssert.Contains("| Items | PackSeparately | asset1 | 1 |", markdown);
            StringAssert.Contains("| Localized | PackTogetherByLabel | lang_ja | 10 |", markdown);

            StringAssert.Contains("> " + AddressTellerReportBuilder.BundleDistributionDisclaimer, markdown);
        }

        [Test]
        public void ToMarkdown_EmptyBundles_OmitsLargestBundleLine()
        {
            var distribution = new BundleDistribution(new List<LogicalBundle>());
            var summary = new DistributionSummary(0, 0, null);

            var markdown = BundleDistributionSerializer.ToMarkdown(distribution, summary);

            StringAssert.Contains("Logical bundle count: 0", markdown);
            StringAssert.DoesNotContain("Largest consolidated bundle", markdown);
            StringAssert.Contains(AddressTellerReportBuilder.BundleDistributionDisclaimer, markdown);
        }

        [Test]
        public void ToMarkdown_ValuesContainingPipe_AreEscaped()
        {
            var distribution = new BundleDistribution(new List<LogicalBundle>
            {
                new LogicalBundle("Group|With|Pipe", BundleModeKind.PackTogether, "all", 1),
            });
            var summary = new DistributionSummary(1, 0, new LargestBundleInfo("Group|With|Pipe", BundleModeKind.PackTogether, "all", 1));

            var markdown = BundleDistributionSerializer.ToMarkdown(distribution, summary);

            StringAssert.Contains("| Group\\|With\\|Pipe | PackTogether | all | 1 |", markdown);
        }

        [Test]
        public void WriteToFile_Csv_WritesFileSuccessfully()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "BundleDistributionSerializerTests_" + Guid.NewGuid());
            var path = Path.Combine(tempDir, "distribution.csv");

            try
            {
                var ok = BundleDistributionSerializer.WriteToFile(path, SimpleDistribution(), SimpleSummary(), "csv");

                Assert.IsTrue(ok);
                Assert.IsTrue(File.Exists(path));
                StringAssert.Contains("GroupName,Mode,SplitKey,AssetCount", File.ReadAllText(path));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void WriteToFile_Markdown_WritesFileSuccessfully()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "BundleDistributionSerializerTests_" + Guid.NewGuid());
            var path = Path.Combine(tempDir, "distribution.md");

            try
            {
                var ok = BundleDistributionSerializer.WriteToFile(path, SimpleDistribution(), SimpleSummary(), "markdown");

                Assert.IsTrue(ok);
                Assert.IsTrue(File.Exists(path));
                StringAssert.Contains("# Bundle Distribution", File.ReadAllText(path));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void WriteToFile_UnknownFormat_ThrowsArgumentException()
        {
            var path = Path.Combine(Path.GetTempPath(), "BundleDistributionSerializerTests_unknown.txt");

            Assert.Throws<ArgumentException>(() =>
                BundleDistributionSerializer.WriteToFile(path, SimpleDistribution(), SimpleSummary(), "yaml"));
        }

        [Test]
        public void WriteToFile_InvalidPath_ReturnsFalseAndLogsError()
        {
            var blockingFile = Path.Combine(Path.GetTempPath(), "BundleDistributionSerializerTests_blocking_" + Guid.NewGuid());
            var path = Path.Combine(blockingFile, "distribution.csv");

            try
            {
                File.WriteAllText(blockingFile, "blocking");

                LogAssert.Expect(LogType.Error, new Regex("Failed to write bundle distribution"));
                var ok = BundleDistributionSerializer.WriteToFile(path, SimpleDistribution(), SimpleSummary(), "csv");

                Assert.IsFalse(ok);
            }
            finally
            {
                if (File.Exists(blockingFile))
                    File.Delete(blockingFile);
            }
        }
    }
}
