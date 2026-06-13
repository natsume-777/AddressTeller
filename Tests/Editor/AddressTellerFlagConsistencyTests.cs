using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettings の各フラグ（AutoCreateMissingGroups / CleanupStaleEntries /
    /// DisabledRuleClassNames）が、ApplyAll/ValidateAll/Explain/BuildPredictedSnapshot の各エントリポイントで
    /// 一貫した分岐結果を返すことを固定する回帰テスト。
    ///
    /// CLI（ApplyAllCLI/ApplyWithValidateCLI/CheckCLI）は引数なしの Service/Snapshot メソッドへ委譲するだけで
    /// AddressTellerSettings.* を直接読まないため、フラグ評価そのものはここでの委譲先（Service側）テストでカバーする。
    /// </summary>
    public class AddressTellerFlagConsistencyTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp";
        private const string StubFolder = TestRootFolder + "/FlagConsistency";
        private const string StubAssetPath = StubFolder + "/StubAsset.prefab";
        private const string OtherAssetPath = StubFolder + "/OtherAsset.prefab";

        // settings.ConfigFolder は AssetFilter.ShouldExclude でこの配下のパスを評価対象から除外するために使われる。
        // TestRootFolder と同じ値にすると、ここで作成するテストアセット自身が除外されてしまうため、
        // 衝突しない別パス（実在しなくてよい）を割り当てる。
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempFlagConsistencyConfig";

        /// <summary>StubFolder 配下のアセットに、ファイル名をアドレスとして "MissingGroup"（存在しない）を割り当てるルール。</summary>
        private sealed class MissingGroupRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("MissingGroup")
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>StubFolder 配下のアセットに "disabled" ラベルを付与する、無効化テスト用ルール。</summary>
        private sealed class DisabledLabelRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension)
                    .Label("disabled");
            }
        }

        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _stubGroup;

        private bool _originalAutoCreate;
        private bool _originalCleanup;
        private List<string> _originalDisabled;

        [SetUp]
        public void SetUp()
        {
            _originalAutoCreate = AddressTellerSettings.AutoCreateMissingGroups;
            _originalCleanup = AddressTellerSettings.CleanupStaleEntries;
            _originalDisabled = AddressTellerSettings.DisabledRuleClassNames.ToList();

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerFlagConsistencyTestSettings");
            _stubGroup = _settings.CreateGroup("StubGroup", false, false, false, null);

            // 前回テストのTearDownが残骸を残していた場合に備え、クリーンな状態から始める。
            if (AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.DeleteAsset(TestRootFolder);

            AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            AssetDatabase.CreateFolder(TestRootFolder, "FlagConsistency");
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.AutoCreateMissingGroups = _originalAutoCreate;
            AddressTellerSettings.CleanupStaleEntries = _originalCleanup;

            foreach (var name in AddressTellerSettings.DisabledRuleClassNames.ToList())
                if (!_originalDisabled.Contains(name))
                    AddressTellerSettings.SetRuleEnabled(name, true);

            foreach (var name in _originalDisabled)
                if (!AddressTellerSettings.DisabledRuleClassNames.Contains(name))
                    AddressTellerSettings.SetRuleEnabled(name, false);

            AssetDatabase.DeleteAsset(TestRootFolder);

            DestroySettings(_settings);
        }

        /// <summary>テスト用の非永続 AddressableAssetSettings を、配下の全グループも含めて破棄する。</summary>
        private static void DestroySettings(AddressableAssetSettings settings)
        {
            if (settings == null)
                return;

            foreach (var group in settings.groups.Where(g => g != null).ToList())
                Object.DestroyImmediate(group, true);

            Object.DestroyImmediate(settings, true);
        }

        private static string GuidOf(string path) => AssetDatabase.AssetPathToGUID(path);

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

        // --- 1. AutoCreateMissingGroups = false: Apply/Predict 共に GroupNotFound ---

        [Test]
        public void AutoCreate_Off_ApplyAndPredict_BothGroupNotFound()
        {
            AddressTellerSettings.AutoCreateMissingGroups = false;

            CreatePrefab(StubAssetPath);
            var rules = new AddressRuleBase[] { new MissingGroupRule() };

            var applyIssues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, rules);
            var applyIssue = applyIssues.SingleOrDefault(i => i.Context?.Guid == GuidOf(StubAssetPath));
            Assert.IsNotNull(applyIssue, "AutoCreate OFF では GroupNotFound が issues に含まれるべき。");
            Assert.AreEqual(ValidationStatus.GroupNotFound, applyIssue.Status);

            var predicted = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath }, rules);
            var predictIssue = predicted.Issues.SingleOrDefault(i => i.Context?.Guid == GuidOf(StubAssetPath));
            Assert.IsNotNull(predictIssue, "AutoCreate OFF では Predict 側も GroupNotFound が issues に含まれるべき。");
            Assert.AreEqual(ValidationStatus.GroupNotFound, predictIssue.Status);

            // 副作用なし。
            Assert.IsNull(_settings.FindGroup("MissingGroup"));
            Assert.IsNull(_settings.FindAssetEntry(GuidOf(StubAssetPath)));
        }

        // --- 2. AutoCreateMissingGroups = true: Apply はグループ作成、Predict は GroupWillBeCreated ---

        [Test]
        public void AutoCreate_On_ApplyGroupCreated_PredictGroupWillBeCreated()
        {
            AddressTellerSettings.AutoCreateMissingGroups = true;

            CreatePrefab(StubAssetPath);
            var rules = new AddressRuleBase[] { new MissingGroupRule() };

            var applyIssues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, rules);
            var applyIssue = applyIssues.SingleOrDefault(i => i.Context?.Guid == GuidOf(StubAssetPath));
            Assert.IsNotNull(applyIssue, "AutoCreate ON では GroupWillBeCreated（作成結果の提示）が issues に含まれるべき。");
            Assert.AreEqual(ValidationStatus.GroupWillBeCreated, applyIssue.Status);
            Assert.IsTrue(applyIssue.IsOk);

            // Apply は実際にグループを作成してエントリを書き込む。
            Assert.IsNotNull(_settings.FindGroup("MissingGroup"));
            var entry = _settings.FindAssetEntry(GuidOf(StubAssetPath));
            Assert.IsNotNull(entry);
            Assert.AreEqual("MissingGroup", entry.parentGroup.Name);

            // 同じフラグ値で Predict（dry-run、別の非永続 settings相当の状況=作成前）も GroupWillBeCreated を返すことの固定。
            // 検証用に作成前の状態を別途用意する。
            var predictSettings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerFlagConsistencyPredictSettings");
            try
            {
                var predicted = AddressTellerSnapshotService.BuildPredictedSnapshot(predictSettings, new[] { StubAssetPath }, rules);
                CollectionAssert.AreEqual(new[] { "MissingGroup" }, predicted.GroupsToCreate);
                Assert.IsFalse(predicted.Issues.Any(i => i.Context?.Guid == GuidOf(StubAssetPath)),
                    "GroupWillBeCreated は IsOk=true のため issues には積まれない。");
                // dry-run は副作用ゼロ。
                Assert.IsNull(predictSettings.FindGroup("MissingGroup"));
            }
            finally
            {
                DestroySettings(predictSettings);
            }
        }

        // --- 3. AutoCreateMissingGroups = true: ValidateAll も非破壊で GroupWillBeCreated ---

        [Test]
        public void AutoCreate_On_ValidateGroupWillBeCreated()
        {
            AddressTellerSettings.AutoCreateMissingGroups = true;

            CreatePrefab(StubAssetPath);
            var rules = new AddressRuleBase[] { new MissingGroupRule() };

            var validateIssues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, rules);
            var issue = validateIssues.SingleOrDefault(i => i.Context?.Guid == GuidOf(StubAssetPath));

            Assert.IsNotNull(issue, "AutoCreate ON では ValidateAll も GroupWillBeCreated を情報提供として返すべき。");
            Assert.AreEqual(ValidationStatus.GroupWillBeCreated, issue.Status);
            Assert.IsTrue(issue.IsOk);

            // ValidateAll は非破壊。
            Assert.IsNull(_settings.FindGroup("MissingGroup"));
            Assert.IsNull(_settings.FindAssetEntry(GuidOf(StubAssetPath)));
        }

        // --- 4. AutoCreateMissingGroups = true: RuleExplainService.Explain も BuildSetup 経由で GroupWillBeCreated ---

        [Test]
        public void AutoCreate_On_ExplainValidationGroupWillBeCreated()
        {
            AddressTellerSettings.AutoCreateMissingGroups = true;

            CreatePrefab(StubAssetPath);
            var rules = new AddressRuleBase[] { new MissingGroupRule() };

            var explanations = RuleExplainService.Explain(new[] { StubAssetPath }, _settings, rules);
            var explanation = explanations.SingleOrDefault(e => e.AssetPath == StubAssetPath);

            Assert.IsNotNull(explanation);
            Assert.IsFalse(explanation.IsExcluded);
            Assert.IsNotNull(explanation.Validation);
            Assert.AreEqual(ValidationStatus.GroupWillBeCreated, explanation.Validation.Status);
            Assert.IsTrue(explanation.Validation.IsOk);

            // Explain も非破壊。
            Assert.IsNull(_settings.FindGroup("MissingGroup"));
        }

        // --- 5/6. CleanupStaleEntries: managed グループ内の孤児エントリの保持/削除 ---

        [Test]
        public void Cleanup_Off_StaleEntryKept()
        {
            AddressTellerSettings.CleanupStaleEntries = false;

            // NoMatchRule（常にfalse）を渡すため OtherAsset.prefab は Skipped になる。
            // あらかじめ StubGroup（managed）に登録しておき、孤児エントリとする。
            CreatePrefab(OtherAssetPath);
            var staleGuid = GuidOf(OtherAssetPath);
            _settings.CreateOrMoveEntry(staleGuid, _stubGroup).SetAddress("StaleAddress");

            // どのアセットにもマッチしない NoMatchRule を渡すことで、OtherAsset.prefab を Skipped（孤児）にする。
            var rules = new AddressRuleBase[] { new NoMatchRule() };

            AddressTellerService.ApplyAll(new[] { OtherAssetPath }, _settings, NullProgressReporter.Instance, rules);

            var entry = _settings.FindAssetEntry(staleGuid);
            Assert.IsNotNull(entry, "CleanupStaleEntries=false の場合、孤児エントリは保持されるべき。");
            Assert.AreEqual("StaleAddress", entry.address);
        }

        [Test]
        public void Cleanup_On_StaleEntryRemoved()
        {
            AddressTellerSettings.CleanupStaleEntries = true;

            CreatePrefab(OtherAssetPath);
            var staleGuid = GuidOf(OtherAssetPath);
            _settings.CreateOrMoveEntry(staleGuid, _stubGroup).SetAddress("StaleAddress");

            var rules = new AddressRuleBase[] { new NoMatchRule() };

            AddressTellerService.ApplyAll(new[] { OtherAssetPath }, _settings, NullProgressReporter.Instance, rules);

            Assert.IsNull(_settings.FindAssetEntry(staleGuid), "CleanupStaleEntries=true の場合、孤児エントリは削除されるべき。");
        }

        /// <summary>どのアセットにもマッチしないルール。StubGroup を managedGroups に含めつつ、孤児エントリを作るために使う。</summary>
        private sealed class NoMatchRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(ctx => false)
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        // --- 7. DisabledRuleClassNames: 無効化したルールが Service 評価に波及しないこと ---

        [Test]
        public void DisabledRule_ExcludedFromEnabledRules_AndAbsentFromServiceResult()
        {
            CreatePrefab(StubAssetPath);

            var allRules = new AddressRuleBase[] { new DisabledLabelRule() };
            var className = typeof(DisabledLabelRule).FullName;

            // 無効化前: ラベルが付与される。
            AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance,
                RuleCollector.CollectEnabledRules(allRules, AddressTellerSettings.DisabledRuleClassNames));

            var entryBefore = _settings.FindAssetEntry(GuidOf(StubAssetPath));
            Assert.IsNotNull(entryBefore);
            Assert.IsTrue(entryBefore.labels.Contains("disabled"), "無効化前は DisabledLabelRule が適用され 'disabled' ラベルが付くべき。");

            // ルールを無効化。
            AddressTellerSettings.SetRuleEnabled(className, false);

            var enabledRules = RuleCollector.CollectEnabledRules(allRules, AddressTellerSettings.DisabledRuleClassNames);
            Assert.IsFalse(enabledRules.Any(r => r.GetType() == typeof(DisabledLabelRule)),
                "無効化したルール型は CollectEnabledRules の結果から除外されるべき。");

            // 無効化後に Explain しても DisabledLabelRule の評価詳細が現れない（enabledRules が空のため Details も空）。
            var explanations = RuleExplainService.Explain(new[] { StubAssetPath }, _settings, enabledRules);
            var explanation = explanations.SingleOrDefault(e => e.AssetPath == StubAssetPath);
            Assert.IsNotNull(explanation);
            Assert.AreEqual(0, explanation.Explanation.Details.Count,
                "無効化したルールしか存在しない場合、評価詳細は空になるべき。");

            // Service 結果への波及確認: 無効化後に ApplyAll しても新規ラベルは増えない。
            AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, enabledRules);
            var entryAfter = _settings.FindAssetEntry(GuidOf(StubAssetPath));
            Assert.IsTrue(entryAfter.labels.Contains("disabled"),
                "ラベルは加算のみで剥がされないため、無効化前に付与済みの 'disabled' は残る。");
        }
    }
}
