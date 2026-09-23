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

        /// <summary>
        /// StubFolder 配下のアセットに対し、Predicate 評価時に必ず例外を投げる高優先度（Order=0）ルール。
        /// BlockedByRuleError を end-to-end で再現するために使う。
        /// </summary>
        private sealed class ThrowingHighPriorityRule : AddressRuleBase
        {
            public override int Order => 0;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal)
                        ? throw new System.InvalidOperationException("boom")
                        : false)
                    .Address(_ => "unreachable");
            }
        }

        /// <summary>
        /// StubRule と同じマッチ条件だが、Order がより大きい（優先度がより低い）版。
        /// RuleCollector はテストアセンブリ全体を対象にリフレクションでルールクラスを収集するため、Order の値は
        /// 同アセンブリ内の他のテスト専用ルールクラス（RuleCollectorTests 等）と衝突しない値を選ぶこと。
        /// </summary>
        private sealed class LowerPriorityStubRule : AddressRuleBase
        {
            public override int Order => 84210;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension);
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

            var cleared = AddressTellerService.RemoveEntriesForDeletedAssets(new[] { staleGuid }, _settings, new AddressRuleBase[] { new StubRule() });

            Assert.IsNull(_settings.FindAssetEntry(staleGuid));
            Assert.AreEqual(1, cleared.Count);
            Assert.AreEqual(staleGuid, cleared[0].Guid);
            Assert.AreEqual(_stubGroup.Name, cleared[0].GroupName);
        }

        [Test]
        public void RemoveEntriesForDeletedAssets_NothingRemoved_ReturnsEmptyList()
        {
            // 対象 GUID に対応するエントリが存在しない場合、削除は1件も発生しないため空リストを返す
            // (nullを返す・例外を投げるのではなく、空リストという「結果を握りつぶさない」規約に沿った挙動)。
            var cleared = AddressTellerService.RemoveEntriesForDeletedAssets(
                new[] { "guid-not-registered" }, _settings, new AddressRuleBase[] { new StubRule() });

            Assert.IsNotNull(cleared);
            Assert.AreEqual(0, cleared.Count);
        }

        [Test]
        public void ValidateAll_HigherPriorityRuleThrows_ReportsBothRuleErrorAndBlockedByRuleError()
        {
            // ThrowingHighPriorityRule(Order=0) が例外を投げ、LowerPriorityStubRule(Order がより大きい)
            // が候補を出す。RuleError（例外そのもの）と BlockedByRuleError（そのため書き込みを見送った結果）
            // の両方が同じ issues に現れることを確認する（原因と結果は別エントリで二重計上にならない）。
            CreatePrefab(StubAssetPath);
            var rules = new AddressRuleBase[] { new ThrowingHighPriorityRule(), new LowerPriorityStubRule() };

            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, rules);

            var forAsset = issues.Where(i => i.Context?.Path == StubAssetPath).ToList();
            Assert.IsTrue(forAsset.Any(i => i.Status == ValidationStatus.RuleError), "Expected a RuleError entry.");
            Assert.IsTrue(forAsset.Any(i => i.Status == ValidationStatus.BlockedByRuleError), "Expected a BlockedByRuleError entry.");
            Assert.AreEqual(1, forAsset.Count(i => i.Status == ValidationStatus.RuleError));
            Assert.AreEqual(1, forAsset.Count(i => i.Status == ValidationStatus.BlockedByRuleError));
        }

        [Test]
        public void ApplyAll_HigherPriorityRuleThrows_WritesNothingAndReportsBothStatuses()
        {
            CreatePrefab(StubAssetPath);
            var rules = new AddressRuleBase[] { new ThrowingHighPriorityRule(), new LowerPriorityStubRule() };

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, rules);

            Assert.IsNull(_settings.FindAssetEntry(Guid(StubAssetPath)),
                "The lower-priority rule's address must not be written while a higher-priority rule failed.");
            var forAsset = issues.Where(i => i.Context?.Path == StubAssetPath).ToList();
            Assert.IsTrue(forAsset.Any(i => i.Status == ValidationStatus.RuleError));
            Assert.IsTrue(forAsset.Any(i => i.Status == ValidationStatus.BlockedByRuleError));
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

        [Test]
        public void Explain_WithInjectedRules_WinnerPropagatesToConclusionText()
        {
            // RuleExplainService.Explain が Validate(..., out winner) の winner をそのまま
            // AssetExplanation.Winner に伝え、AddressTellerExplainWindow.DescribeConclusion が
            // それを使って正しい採用アドレスを表示することを、内部の勝者選定を経由せず end-to-end で確認する。
            CreatePrefab(StubAssetPath);

            var explanations = RuleExplainService.Explain(new[] { StubAssetPath }, _settings, new AddressRuleBase[] { new StubRule() });
            var explanation = explanations.Single(e => e.AssetPath == StubAssetPath);

            Assert.AreEqual(ValidationStatus.Ok, explanation.Validation.Status);
            Assert.AreEqual("StubGroup", explanation.Winner.GroupName);
            Assert.AreEqual("StubAsset", explanation.Winner.Address);

            var (text, cssClass) = AddressTellerExplainWindow.DescribeConclusion(
                explanation.Validation, explanation.Explanation.Resolution, explanation.Winner);

            StringAssert.Contains("StubAsset", text);
            Assert.AreEqual("at-conclusion--ok", cssClass);
        }

        [Test]
        public void Explain_HigherPriorityRuleThrows_WinnerNotAdoptedAndConclusionSaysBlocked()
        {
            // 勝者候補は存在する(LowerPriorityStubRule 由来)が、より優先度の高いルールの例外により
            // BlockedByRuleError になるケースを end-to-end で確認する。結論テキストは Blocked を明示し、
            // (adopted) を出してはならない。
            CreatePrefab(StubAssetPath);
            var rules = new AddressRuleBase[] { new ThrowingHighPriorityRule(), new LowerPriorityStubRule() };

            var explanations = RuleExplainService.Explain(new[] { StubAssetPath }, _settings, rules);
            var explanation = explanations.Single(e => e.AssetPath == StubAssetPath);

            Assert.AreEqual(ValidationStatus.BlockedByRuleError, explanation.Validation.Status);

            var (text, cssClass) = AddressTellerExplainWindow.DescribeConclusion(
                explanation.Validation, explanation.Explanation.Resolution, explanation.Winner);

            StringAssert.Contains("Blocked by rule error", text);
            Assert.AreEqual("at-conclusion--error", cssClass);
        }
    }
}
