using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// DuplicateAddressDetector.Detect の単体テスト。guid はテストアセットとして実在させる必要はない
    /// （AssetDatabase.GUIDToAssetPath が解決できない guid は「(path could not be resolved)」として
    /// メッセージに含まれるだけで、検出ロジック自体には影響しない）。
    /// </summary>
    public class DuplicateAddressDetectorTests
    {
        [Test]
        public void TwoManagedAssets_SameAddress_ReturnsErrorWithHasWritableDuplicateTrue()
        {
            var afterAddresses = new Dictionary<string, string>
            {
                ["guid-a"] = "Shared",
                ["guid-b"] = "Shared",
            };
            var writtenGuids = new HashSet<string> { "guid-a", "guid-b" };

            var results = DuplicateAddressDetector.Detect(afterAddresses, writtenGuids);

            Assert.AreEqual(1, results.Count);
            var issue = results[0];
            Assert.AreEqual(ValidationStatus.DuplicateAddress, issue.Status);
            Assert.IsTrue(issue.HasWritableDuplicate);
            Assert.IsFalse(issue.IsOk, "両方ともこのランで AddressTeller が書く対象のため Error(IsOk=false)。");
            Assert.IsNull(issue.Context, "重複は複数アセットにまたがるため、単一アセットに紐づく Context は持たない。");
        }

        [Test]
        public void TwoUnmanagedAssets_SameAddress_ReturnsWarningWithHasWritableDuplicateFalse()
        {
            var afterAddresses = new Dictionary<string, string>
            {
                ["guid-a"] = "Shared",
                ["guid-b"] = "Shared",
            };
            var writtenGuids = new HashSet<string>(); // このランで AddressTeller はどちらも書かない

            var results = DuplicateAddressDetector.Detect(afterAddresses, writtenGuids);

            Assert.AreEqual(1, results.Count);
            var issue = results[0];
            Assert.AreEqual(ValidationStatus.DuplicateAddress, issue.Status);
            Assert.IsFalse(issue.HasWritableDuplicate);
            Assert.IsTrue(issue.IsOk, "AddressTeller はどちらのアドレスも書いていないため直しようがなく、Warning(IsOk=true)。");
        }

        [Test]
        public void OneManagedOneUnmanaged_SameAddress_ReturnsError()
        {
            var afterAddresses = new Dictionary<string, string>
            {
                ["guid-managed"] = "Shared",
                ["guid-unmanaged"] = "Shared",
            };
            var writtenGuids = new HashSet<string> { "guid-managed" };

            var results = DuplicateAddressDetector.Detect(afterAddresses, writtenGuids);

            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].HasWritableDuplicate);
            Assert.IsFalse(results[0].IsOk);
        }

        [Test]
        public void NoDuplicates_ReturnsEmpty()
        {
            var afterAddresses = new Dictionary<string, string>
            {
                ["guid-a"] = "AddressA",
                ["guid-b"] = "AddressB",
            };
            var writtenGuids = new HashSet<string> { "guid-a", "guid-b" };

            var results = DuplicateAddressDetector.Detect(afterAddresses, writtenGuids);

            CollectionAssert.IsEmpty(results);
        }

        [Test]
        public void ThreeOrMoreDuplicates_ReturnsSingleIssue()
        {
            var afterAddresses = new Dictionary<string, string>
            {
                ["guid-a"] = "Shared",
                ["guid-b"] = "Shared",
                ["guid-c"] = "Shared",
            };
            var writtenGuids = new HashSet<string> { "guid-a", "guid-b", "guid-c" };

            var results = DuplicateAddressDetector.Detect(afterAddresses, writtenGuids);

            Assert.AreEqual(1, results.Count, "3件の重複でも issue は1件にまとまる。");
            Assert.IsTrue(results[0].Message.Contains("guid-a"));
            Assert.IsTrue(results[0].Message.Contains("guid-b"));
            Assert.IsTrue(results[0].Message.Contains("guid-c"));
        }

        [Test]
        public void MultipleDistinctDuplicateGroups_ReturnsOneIssuePerAddress_SortedOrdinally()
        {
            var afterAddresses = new Dictionary<string, string>
            {
                ["guid-z1"] = "Zebra",
                ["guid-z2"] = "Zebra",
                ["guid-a1"] = "Apple",
                ["guid-a2"] = "Apple",
                ["guid-solo"] = "Solo", // 重複していないので対象外
            };
            var writtenGuids = new HashSet<string> { "guid-z1", "guid-z2", "guid-a1", "guid-a2" };

            var results = DuplicateAddressDetector.Detect(afterAddresses, writtenGuids);

            Assert.AreEqual(2, results.Count);
            // Ordinal 順で "Apple" < "Zebra" のため、この順で決定的に並ぶ。
            StringAssert.Contains("Apple", results[0].Message);
            StringAssert.Contains("Zebra", results[1].Message);
        }

        [Test]
        public void EmptyOrNullAddress_ExcludedFromDetection()
        {
            var afterAddresses = new Dictionary<string, string>
            {
                ["guid-a"] = "",
                ["guid-b"] = "",
                ["guid-c"] = null,
                ["guid-d"] = null,
            };
            var writtenGuids = new HashSet<string>();

            var results = DuplicateAddressDetector.Detect(afterAddresses, writtenGuids);

            CollectionAssert.IsEmpty(results);
        }
    }
}
