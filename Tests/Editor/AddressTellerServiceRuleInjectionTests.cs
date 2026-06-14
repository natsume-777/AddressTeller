using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerService.ApplyAll/ValidateAll/RemoveEntriesForDeletedAssets と
    /// RuleExplainService.Explain の「ルール注入」オーバーロードが、
    /// リフレクションによるルール収集に依存せず動作することを検証する。
    /// テスト対象アセットは一時フォルダ Assets/_AddressTellerTestTemp 配下に作成する。
    /// </summary>
    public class AddressTellerServiceRuleInjectionTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp";
        private const string StubFolder = TestRootFolder + "/RuleInjection";
        private const string StubAssetPath = StubFolder + "/StubAsset.prefab";

        // settings.ConfigFolder は AssetFilter.ShouldExclude でこの配下のパスを評価対象から除外するために使われる。
        // TestRootFolder と同じ値にすると、ここで作成するテストアセット自身が除外されてしまうため、
        // 衝突しない別パス（実在しなくてよい）を割り当てる。
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempConfig";

        /// <summary>StubFolder 配下のアセットに、ファイル名をアドレスとして "stub" ラベルを "StubGroup" へ付与するテスト専用ルール。</summary>
        private sealed class StubRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension)
                    .Label("stub");
            }
        }

        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _stubGroup;
        private bool _originalCleanupSetting;

        [SetUp]
        public void SetUp()
        {
            _originalCleanupSetting = AddressTellerSettings.CleanupStaleEntries;
            AddressTellerSettings.CleanupStaleEntries = true;

            // 非永続 settings に ConfigFolder のキャッシュのみを設定する（本番設定への副作用を避ける）。
            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerServiceRuleInjectionTestSettings");
            _stubGroup = _settings.CreateGroup("StubGroup", false, false, false, null);

            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            AssetDatabase.CreateFolder(TestRootFolder, "RuleInjection");
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanupSetting;
            AssetDatabase.DeleteAsset(TestRootFolder);
        }

        private static string Guid(string path) => AssetDatabase.AssetPathToGUID(path);

        private static void CreatePrefab(string path)
        {
            var go = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ApplyAll_WithInjectedRules_AppliesAddressAndLabel()
        {
            CreatePrefab(StubAssetPath);

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new StubRule() });

            Assert.AreEqual(0, issues.Count);
            var entry = _settings.FindAssetEntry(Guid(StubAssetPath));
            Assert.IsNotNull(entry);
            Assert.AreEqual("StubAsset", entry.address);
            Assert.IsTrue(entry.labels.Contains("stub"));
        }

        [Test]
        public void ValidateAll_WithInjectedRules_ReturnsNoIssuesForMatchingAsset()
        {
            CreatePrefab(StubAssetPath);

            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, new AddressRuleBase[] { new StubRule() });

            Assert.IsFalse(issues.Any(i => i.Context?.Path == StubAssetPath));
        }

        [Test]
        public void RemoveEntriesForDeletedAssets_WithInjectedRules_RemovesEntryInManagedGroup()
        {
            const string staleGuid = "stale-guid-rule-injection";
            _settings.CreateOrMoveEntry(staleGuid, _stubGroup);

            AddressTellerService.RemoveEntriesForDeletedAssets(new[] { staleGuid }, _settings, new AddressRuleBase[] { new StubRule() });

            Assert.IsNull(_settings.FindAssetEntry(staleGuid));
        }

        [Test]
        public void Explain_WithInjectedRules_ReportsMatchedRule()
        {
            CreatePrefab(StubAssetPath);

            var explanations = RuleExplainService.Explain(new[] { StubAssetPath }, _settings, new AddressRuleBase[] { new StubRule() });

            var explanation = explanations.SingleOrDefault(e => e.AssetPath == StubAssetPath);
            Assert.IsNotNull(explanation);
            Assert.IsFalse(explanation.IsExcluded);
            Assert.AreEqual(1, explanation.Explanation.Details.Count);
            Assert.AreEqual(RuleMatchOutcome.Matched, explanation.Explanation.Details[0].Outcome);
            Assert.AreEqual("StubAsset", explanation.Explanation.Details[0].ProducedAddress);
        }
    }
}
