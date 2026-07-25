using NUnit.Framework;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// ルールクラスの Configure() 自体が例外を送出した場合に、他のルールの評価・書き込みをブロックせず
    /// ValidationStatus.RuleConfigureFailed として issues に反映されること（ApplyAll/ValidateAll/
    /// BuildPredictedSnapshot）と、issues を返さない呼び出し側（RemoveEntriesForDeletedAssets/Explain）
    /// でも例外が漏れずスキップされることを検証する。
    /// 破壊的操作である ClearAll（AddressTellerMenu）が、managedGroups を信頼できない状態のまま
    /// 実行されず中止されることも合わせて検証する。
    /// </summary>
    public class AddressTellerRuleConfigureFailureTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp/RuleConfigureFailure";
        private const string StubAssetPath = TestRootFolder + "/StubAsset.prefab";
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempRuleConfigureFailureConfig";

        /// <summary>正常に動作する対照用ルール。BrokenRule と同時に評価しても影響を受けないことを確認する。</summary>
        private sealed class WorkingRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(ctx => ctx.Path.StartsWith(TestRootFolder + "/", System.StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>
        /// BrokenRule と同じグループ("BrokenGroup")を宣言するが、Configure() 自体は正常に完了する対照用ルール。
        /// このルールは StubAssetPath にはマッチしない条件にしてある(hasConfigureFailures 検証用に、
        /// StubAssetPath を「どのルールにもマッチしなくなった(Skipped)」状態にするため)。
        /// </summary>
        private sealed class OtherRuleSharingBrokenGroup : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("BrokenGroup")
                    .Where(ctx => false)
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>
        /// Where() を1グループにつき2回呼び出し、パッケージ自身の仕様（2回目の呼び出しは
        /// InvalidOperationException）により Configure() 自体が例外を送出するルール。
        /// 利用者が最も踏みやすいミスの典型例として再現する。
        /// </summary>
        private sealed class BrokenRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                var group = rules.Group("BrokenGroup").Where(ctx => true);
                group.Where(ctx => true);
            }
        }

        private AddressableAssetSettings _settings;
        private bool _originalCleanupSetting;

        [SetUp]
        public void SetUp()
        {
            _originalCleanupSetting = AddressTellerSettings.CleanupStaleEntries;

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerRuleConfigureFailureTestSettings");
            _settings.CreateGroup("StubGroup", false, false, false, null);

            if (!AssetDatabase.IsValidFolder("Assets/_AddressTellerTestTemp"))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder("Assets/_AddressTellerTestTemp", "RuleConfigureFailure");

            CreatePrefab(StubAssetPath);
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanupSetting;

            AssetDatabase.DeleteAsset(TestRootFolder);
            DestroySettings(_settings);
        }

        private static void DestroySettings(AddressableAssetSettings settings)
        {
            if (settings == null) return;

            foreach (var group in settings.groups.Where(g => g != null).ToList())
                Object.DestroyImmediate(group, true);

            Object.DestroyImmediate(settings, true);
        }

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

        private static string GuidOf(string path) => AssetDatabase.AssetPathToGUID(path);

        [Test]
        public void ApplyAll_OneRuleThrowsInConfigure_OtherRuleStillAppliesAndIssueReported()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("BrokenRule.Configure() threw")));

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance,
                new AddressRuleBase[] { new BrokenRule(), new WorkingRule() });

            Assert.IsTrue(issues.Any(i => i.Status == ValidationStatus.RuleConfigureFailed),
                "Configure() の例外は RuleConfigureFailed として issues に反映されるべき。");

            var entry = _settings.FindAssetEntry(GuidOf(StubAssetPath));
            Assert.IsNotNull(entry, "壊れていないルール(WorkingRule)は引き続き正常に適用されるべき。");
            Assert.AreEqual("StubAsset", entry.address);
        }

        [Test]
        public void ApplyAll_ConfigureFailure_DoesNotRemoveEntryOfOtherRuleSharingSameGroup()
        {
            // BrokenRule が担当するはずだった "BrokenGroup" を OtherRuleSharingBrokenGroup も
            // 宣言している(同じグループを複数ルールで分担する構成)の再現。BrokenRule.Configure() が
            // 例外を送出しても、managedGroups には "BrokenGroup" が(OtherRuleSharingBrokenGroup 由来で)
            // 残り続けるため、誤って StubAssetPath が「どのルールにもマッチしなくなった(Skipped)」ことを
            // 理由に削除されてはならない。
            AddressTellerSettings.CleanupStaleEntries = true;
            var brokenGroup = _settings.CreateGroup("BrokenGroup", false, false, false, null);
            var existingEntry = _settings.CreateOrMoveEntry(GuidOf(StubAssetPath), brokenGroup);
            existingEntry.SetAddress("StubAsset");

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("BrokenRule.Configure() threw")));
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("Skipping stale entry cleanup for this run")));

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance,
                new AddressRuleBase[] { new BrokenRule(), new OtherRuleSharingBrokenGroup() });

            Assert.IsTrue(issues.Any(i => i.Status == ValidationStatus.RuleConfigureFailed));

            var entry = _settings.FindAssetEntry(GuidOf(StubAssetPath));
            Assert.IsNotNull(entry, "他のルールの Configure() 失敗を理由に、無関係なルールが担当していたエントリを削除してはいけない。");
            Assert.AreEqual("StubAsset", entry.address);
        }

        [Test]
        public void ValidateAll_OneRuleThrowsInConfigure_ReportsRuleConfigureFailed()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("BrokenRule.Configure() threw")));

            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance,
                new AddressRuleBase[] { new BrokenRule(), new WorkingRule() });

            Assert.IsTrue(issues.Any(i => i.Status == ValidationStatus.RuleConfigureFailed));
        }

        [Test]
        public void BuildPredictedSnapshot_OneRuleThrowsInConfigure_ReportsIssueWithoutThrowing()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("BrokenRule.Configure() threw")));

            DryRunResult result = default;
            Assert.DoesNotThrow(() =>
                result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath },
                    new AddressRuleBase[] { new BrokenRule(), new WorkingRule() }));

            Assert.IsTrue(result.Issues.Any(i => i.Status == ValidationStatus.RuleConfigureFailed));
        }

        [Test]
        public void RemoveEntriesForDeletedAssets_OneRuleThrowsInConfigure_DoesNotThrow()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("BrokenRule.Configure() threw")));

            Assert.DoesNotThrow(() =>
                AddressTellerService.RemoveEntriesForDeletedAssets(new[] { "some-deleted-guid-rule-configure-failure" }, _settings,
                    new AddressRuleBase[] { new BrokenRule(), new WorkingRule() }));
        }

        [Test]
        public void RemoveEntriesForDeletedAssets_ConfigureFailure_DoesNotRemoveEntryOfOtherRuleSharingSameGroup()
        {
            // 同一グループを複数ルールで分担している場合の再現(ApplyAll と同じ理由)。
            // 削除追従(資産削除時のクリーンアップ)でも managedGroups が信頼できない実行では
            // 削除してはならない。
            AddressTellerSettings.CleanupStaleEntries = true;
            var brokenGroup = _settings.CreateGroup("BrokenGroup", false, false, false, null);
            var guid = GuidOf(StubAssetPath);
            var existingEntry = _settings.CreateOrMoveEntry(guid, brokenGroup);
            existingEntry.SetAddress("StubAsset");

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("BrokenRule.Configure() threw")));
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("Skipping stale entry cleanup for this run")));

            AddressTellerService.RemoveEntriesForDeletedAssets(new[] { guid }, _settings,
                new AddressRuleBase[] { new BrokenRule(), new OtherRuleSharingBrokenGroup() });

            Assert.IsNotNull(_settings.FindAssetEntry(guid),
                "他のルールの Configure() 失敗を理由に、無関係なルールが担当していたエントリを削除してはいけない。");
        }

        [Test]
        public void Explain_OneRuleThrowsInConfigure_DoesNotThrow()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("BrokenRule.Configure() threw")));

            Assert.DoesNotThrow(() =>
                RuleExplainService.Explain(new[] { StubAssetPath }, _settings,
                    new AddressRuleBase[] { new BrokenRule(), new WorkingRule() }));
        }

        [Test]
        public void Explain_OneRuleThrowsInConfigure_ReturnsConfigureFailure()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("BrokenRule.Configure() threw")));

            RuleExplainService.Explain(new[] { StubAssetPath }, _settings,
                new AddressRuleBase[] { new BrokenRule(), new WorkingRule() },
                out var configureFailures);

            Assert.AreEqual(1, configureFailures.Count,
                "Configure() の例外は out 引数経由で呼び出し側に返されるべき。");
            Assert.AreEqual(ValidationStatus.RuleConfigureFailed, configureFailures[0].Status);
        }

        [Test]
        public void ClearAll_OneRuleThrowsInConfigure_AbortsWithoutRemovingEntries()
        {
            // ClearCLI の scope=managed 中止(setup.ConfigureFailures.Count > 0 で exit code 3)と対称の
            // 安全対策。対話メニュー版(AddressTellerMenu.ClearAll)でも、ルールの Configure() が1件でも
            // 失敗していれば managedGroups(所有権判定)を信頼できないため、確認ダイアログを出す前に
            // 中止し既存エントリを一切削除してはいけない。
            var stubGroup = _settings.FindGroup("StubGroup");
            var guid = GuidOf(StubAssetPath);
            var existingEntry = _settings.CreateOrMoveEntry(guid, stubGroup);
            existingEntry.SetAddress("StubAsset");

            string notifiedMessage = null;
            var originalNotify = AddressTellerMenu.s_notifyClearAborted;
            AddressTellerMenu.s_notifyClearAborted = message => notifiedMessage = message;
            try
            {
                LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("BrokenRule.Configure() threw")));
                LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[AddressTeller] Clear All aborted:")));

                AddressTellerMenu.ClearAll(_settings, new AddressRuleBase[] { new BrokenRule(), new WorkingRule() });

                Assert.IsNotNull(notifiedMessage,
                    "ルール構成エラー時、s_notifyClearAborted 経由でユーザーに通知されるべき。");
                Assert.IsNotNull(_settings.FindAssetEntry(guid),
                    "ルール構成が壊れている状態のまま Clear All が既存エントリを削除してはいけない。");
            }
            finally
            {
                AddressTellerMenu.s_notifyClearAborted = originalNotify;
            }
        }
    }
}
