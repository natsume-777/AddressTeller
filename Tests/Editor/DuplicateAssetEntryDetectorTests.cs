using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// DuplicateAssetEntryDetector.Detect の単体テスト。同一 guid が2つ以上のグループにまたがって
    /// 存在する状態は通常の公開 API では作れないため、<see cref="DuplicateAssetEntryTestInjector"/> で
    /// 直接注入する。
    /// </summary>
    public class DuplicateAssetEntryDetectorTests
    {
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _groupA;
        private AddressableAssetGroup _groupB;

        [SetUp]
        public void SetUp()
        {
            _settings = AddressTellerTestSettingsFactory.CreateInMemory("Assets/_AddressTellerTestTemp", "AddressTellerDuplicateAssetEntryTestSettings");
            _groupA = _settings.CreateGroup("GroupA", false, false, false, null);
            _groupB = _settings.CreateGroup("GroupB", false, false, false, null);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_groupA, true);
            UnityEngine.Object.DestroyImmediate(_groupB, true);
            UnityEngine.Object.DestroyImmediate(_settings, true);
        }

        [Test]
        public void NoDuplicates_ReturnsEmpty()
        {
            _settings.CreateOrMoveEntry("guid-a", _groupA);
            _settings.CreateOrMoveEntry("guid-b", _groupB);

            var results = DuplicateAssetEntryDetector.Detect(_settings);

            CollectionAssert.IsEmpty(results);
        }

        [Test]
        public void SameGuidInTwoGroups_ReturnsOneErrorListingBothLocations()
        {
            _settings.CreateOrMoveEntry("guid-dup", _groupA).SetAddress("AddressA");
            DuplicateAssetEntryTestInjector.InjectDuplicateEntry(_groupB, "guid-dup", "AddressB");

            var results = DuplicateAssetEntryDetector.Detect(_settings);

            Assert.AreEqual(1, results.Count);
            var issue = results[0];
            Assert.AreEqual(ValidationStatus.DuplicateAssetEntry, issue.Status);
            Assert.IsFalse(issue.IsOk);
            Assert.IsTrue(issue.IsBlocking);
            Assert.IsNull(issue.Context);
            StringAssert.Contains("guid-dup", issue.Message);
            StringAssert.Contains("GroupA", issue.Message);
            StringAssert.Contains("GroupB", issue.Message);
            StringAssert.Contains("AddressA", issue.Message);
            StringAssert.Contains("AddressB", issue.Message);
        }

        [Test]
        public void MultipleDistinctDuplicateGuids_ReturnsOnePerGuid_SortedOrdinally()
        {
            _settings.CreateOrMoveEntry("guid-z", _groupA).SetAddress("Z");
            DuplicateAssetEntryTestInjector.InjectDuplicateEntry(_groupB, "guid-z", "Z2");
            _settings.CreateOrMoveEntry("guid-a", _groupA).SetAddress("A");
            DuplicateAssetEntryTestInjector.InjectDuplicateEntry(_groupB, "guid-a", "A2");
            _settings.CreateOrMoveEntry("guid-solo", _groupA).SetAddress("Solo"); // 重複していないので対象外

            var results = DuplicateAssetEntryDetector.Detect(_settings);

            Assert.AreEqual(2, results.Count);
            StringAssert.Contains("guid-a", results[0].Message);
            StringAssert.Contains("guid-z", results[1].Message);
        }
    }
}
