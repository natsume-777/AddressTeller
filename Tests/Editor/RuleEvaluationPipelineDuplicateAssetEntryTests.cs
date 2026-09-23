using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 同一 guid が2つ以上のグループにまたがって存在する状態を、RuleEvaluationPipeline.BuildPredictedRunState
    /// を共有する ValidateAll / BuildPredictedSnapshot の両方が検出し、ルール評価そのものを止めることを検証する。
    /// この状態は公開 API では作れないため <see cref="DuplicateAssetEntryTestInjector"/> で直接注入する。
    /// </summary>
    public class RuleEvaluationPipelineDuplicateAssetEntryTests
    {
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _groupA;
        private AddressableAssetGroup _groupB;

        /// <summary>settings.groups に存在するどのアセットにもマッチしないダミールール。マッチの有無自体は本テストの対象外。</summary>
        private sealed class NoMatchRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("GroupA").Where(_ => false).Address(_ => "unreachable");
            }
        }

        [SetUp]
        public void SetUp()
        {
            _settings = AddressTellerTestSettingsFactory.CreateInMemory(
                "Assets/_AddressTellerTestTempConfig", "RuleEvaluationPipelineDuplicateAssetEntryTestSettings");
            _groupA = _settings.CreateGroup("GroupA", false, false, false, null);
            _groupB = _settings.CreateGroup("GroupB", false, false, false, null);

            _settings.CreateOrMoveEntry("guid-dup", _groupA).SetAddress("AddressA");
            DuplicateAssetEntryTestInjector.InjectDuplicateEntry(_groupB, "guid-dup", "AddressB");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_groupA, true);
            UnityEngine.Object.DestroyImmediate(_groupB, true);
            UnityEngine.Object.DestroyImmediate(_settings, true);
        }

        [Test]
        public void ValidateAll_DuplicateAssetEntry_ReturnsOnlyDuplicateAssetEntry()
        {
            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, new AddressRuleBase[] { new NoMatchRule() });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ValidationStatus.DuplicateAssetEntry, issues[0].Status);
            Assert.IsTrue(issues[0].IsBlocking);
        }

        [Test]
        public void BuildPredictedSnapshot_DuplicateAssetEntry_ReturnsOnlyDuplicateAssetEntry_AndEmptyDiff()
        {
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(
                _settings, System.Array.Empty<string>(), new AddressRuleBase[] { new NoMatchRule() });

            Assert.AreEqual(1, result.Issues.Count);
            Assert.AreEqual(ValidationStatus.DuplicateAssetEntry, result.Issues[0].Status);
            Assert.IsTrue(result.Diff.IsEmpty, "重複検出でランを止めた場合、的外れな大量差分を報告してはいけない。");
        }
    }
}
