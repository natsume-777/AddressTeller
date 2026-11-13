using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// RemoveStaleManagedLabels を、ApplyAll / ValidateAll / BuildPredictedSnapshot の各入口で一貫して評価することと、
    /// 未使用ラベル定義の掃除（UnusedManagedLabelCleaner / メニュー）を検証する。
    /// </summary>
    public class AddressTellerStaleManagedLabelPipelineTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerStaleLabelPipelineTestTemp";
        private const string StubAssetPath = TestRootFolder + "/StubAsset.prefab";

        // ConfigFolder は評価対象から除外されるため、テストアセット配下と衝突しない別パス（実在しなくてよい）にする。
        private const string FakeConfigFolder = "Assets/_AddressTellerStaleLabelPipelineTestConfig";

        /// <summary>TestRootFolder 配下のアセットに StubGroup のアドレスと "fresh" ラベルを割り当てるルール。</summary>
        private sealed class FreshLabelRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(ctx => ctx.Path.StartsWith(TestRootFolder + "/", StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension)
                    .Label("fresh");
            }
        }

        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _stubGroup;
        private bool _originalRemoveStale;
        private bool _originalCleanup;

        [SetUp]
        public void SetUp()
        {
            LabelLedgerTestUtil.Reset();
            _originalRemoveStale = AddressTellerSettings.RemoveStaleManagedLabels;
            _originalCleanup = AddressTellerSettings.CleanupStaleEntries;
            AddressTellerSettings.RemoveStaleManagedLabels = false;
            AddressTellerSettings.CleanupStaleEntries = false;

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerStaleLabelPipelineTestSettings");
            _stubGroup = _settings.CreateGroup("StubGroup", false, false, false, null);

            if (AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.DeleteAsset(TestRootFolder);
            AssetDatabase.CreateFolder("Assets", "_AddressTellerStaleLabelPipelineTestTemp");
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = _originalRemoveStale;
            AddressTellerSettings.CleanupStaleEntries = _originalCleanup;
            LabelLedgerTestUtil.Reset();

            AssetDatabase.DeleteAsset(TestRootFolder);

            foreach (var group in _settings.groups.Where(g => g != null).ToList())
                UnityEngine.Object.DestroyImmediate(group, true);
            UnityEngine.Object.DestroyImmediate(_settings, true);
        }

        private static string CreateStubPrefabAndGetGuid()
        {
            var go = new GameObject("StubAsset");
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, StubAssetPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            return AssetDatabase.AssetPathToGUID(StubAssetPath);
        }

        /// <summary>台帳に "stale" がある状態で、スタブアセットのエントリに "stale" と手付けの "manual" を付けておく。</summary>
        private AddressableAssetEntry PrepareEntryCarryingStaleLabel()
        {
            var guid = CreateStubPrefabAndGetGuid();
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var entry = _settings.CreateOrMoveEntry(guid, _stubGroup);
            entry.SetAddress("StubAsset");
            entry.SetLabel("stale", true);
            entry.SetLabel("manual", true);
            return entry;
        }

        private static readonly AddressRuleBase[] Rules = { new FreshLabelRule() };

        // --- OFF: フルスキャン経路で通知、増分 ApplyAll では通知しない ---

        [Test]
        public void RemoveOff_ValidateAllAndBuildPredictedSnapshot_ReportNoticeForTheEntry()
        {
            var entry = PrepareEntryCarryingStaleLabel();

            var validateIssues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, Rules);
            var notice = validateIssues.SingleOrDefault(i => i.Status == ValidationStatus.StaleManagedLabelKept);
            Assert.IsNotNull(notice, "ValidateAll は StaleManagedLabelKept を1件報告するべき。");
            Assert.AreEqual(entry.guid, notice.Context?.Guid, "通知は資産を指す（Context あり）。");
            Assert.IsTrue(notice.IsOk);
            Assert.IsFalse(notice.IsBlocking);
            StringAssert.Contains("stale", notice.Message);
            StringAssert.DoesNotContain("manual", notice.Message, "台帳に無いラベルは通知対象にならない。");

            var predicted = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath }, Rules);
            Assert.AreEqual(1, predicted.Issues.Count(i => i.Status == ValidationStatus.StaleManagedLabelKept));

            // 通知のみで、エントリ自体には一切触れない。
            CollectionAssert.AreEquivalent(new[] { "stale", "manual" }, entry.labels);
        }

        [Test]
        public void RemoveOff_ApplyAll_WritesFreshLabel_KeepsStale_AndDoesNotReportNotice()
        {
            var entry = PrepareEntryCarryingStaleLabel();

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, Rules);

            CollectionAssert.AreEquivalent(new[] { "stale", "manual", "fresh" }, entry.labels);
            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.StaleManagedLabelKept),
                "通知はフルスキャン経路（ValidateAll / dry-run）の責務で、ApplyAll は報告しない。");
        }

        // --- ON: Predict の差分と Apply が一致する ---

        [Test]
        public void RemoveOn_PredictedDiffMatchesApply_AndNoNotice()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            var entry = PrepareEntryCarryingStaleLabel();

            var validateIssues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, Rules);
            Assert.IsFalse(validateIssues.Any(i => i.Status == ValidationStatus.StaleManagedLabelKept));

            var predicted = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath }, Rules);
            Assert.AreEqual(1, predicted.Diff.Changed.Count, "ラベルの除去が Changed として dry-run の差分に出る。");
            var (before, after) = predicted.Diff.Changed[0];
            CollectionAssert.AreEqual(new[] { "manual", "stale" }, before.Labels);
            CollectionAssert.AreEqual(new[] { "fresh", "manual" }, after.Labels);
            Assert.AreEqual(1, AddressTellerApplyFlow.CountEntriesLosingLabels(predicted), "確認ダイアログに出すラベル除去の件数。");

            LogAssert.Expect(LogType.Warning, new Regex("Removing stale managed label.*label='stale'"));
            AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, Rules);

            CollectionAssert.AreEquivalent(after.Labels, entry.labels);
        }

        // --- 未使用ラベル定義の掃除 ---

        [Test]
        public void FindUnused_ReturnsOnlyLedgerLabelsThatAreDefinedAndUnused()
        {
            // a: 台帳にあり・定義あり・未使用 → 対象。b: 使用中。c: 台帳にあるが定義が無い。
            // mine: 定義はあるが台帳に無い（利用者が自分で作ったもの）。
            AddressTellerLabelLedger.Record(new[] { "a", "b", "c" });
            _settings.AddLabel("a");
            _settings.AddLabel("b");
            _settings.AddLabel("mine");
            var entry = _settings.CreateOrMoveEntry(CreateStubPrefabAndGetGuid(), _stubGroup);
            entry.SetLabel("b", true);

            var unused = UnusedManagedLabelCleaner.FindUnused(_settings);

            CollectionAssert.AreEqual(new[] { "a" }, unused);
        }

        [Test]
        public void FindUnused_EmptyLedger_ReturnsEmpty()
        {
            _settings.AddLabel("mine");

            CollectionAssert.IsEmpty(UnusedManagedLabelCleaner.FindUnused(_settings));
        }

        [Test]
        public void Remove_DeletesDefinitionsAndForgetsThem_LeavingOthersAlone()
        {
            AddressTellerLabelLedger.Record(new[] { "a", "b" });
            _settings.AddLabel("a");
            _settings.AddLabel("b");
            _settings.AddLabel("mine");

            LogAssert.Expect(LogType.Warning, new Regex("Removing unused label definition 'a'"));
            UnusedManagedLabelCleaner.Remove(_settings, new[] { "a" });

            var labels = _settings.GetLabels();
            CollectionAssert.DoesNotContain(labels, "a");
            CollectionAssert.Contains(labels, "b");
            CollectionAssert.Contains(labels, "mine");
            CollectionAssert.AreEqual(new[] { "b" }, AddressTellerLabelLedger.GetAll());
        }

        [Test]
        public void LabelMenu_ConfirmFalse_RemovesNothing()
        {
            AddressTellerLabelLedger.Record(new[] { "a" });
            _settings.AddLabel("a");
            IReadOnlyList<string> asked = null;

            var removed = AddressTellerLabelMenu.Run(_settings, labels =>
            {
                asked = labels;
                return false;
            });

            CollectionAssert.AreEqual(new[] { "a" }, asked, "確認には削除対象の一覧を渡す。");
            CollectionAssert.IsEmpty(removed);
            CollectionAssert.Contains(_settings.GetLabels(), "a");
            CollectionAssert.AreEqual(new[] { "a" }, AddressTellerLabelLedger.GetAll());
        }

        [Test]
        public void LabelMenu_ConfirmTrue_RemovesUnusedManagedLabelsOnly()
        {
            AddressTellerLabelLedger.Record(new[] { "a" });
            _settings.AddLabel("a");
            _settings.AddLabel("mine");
            LogAssert.Expect(LogType.Warning, new Regex("Removing unused label definition 'a'"));
            LogAssert.Expect(LogType.Log, new Regex("Removed 1 unused label definition"));

            var removed = AddressTellerLabelMenu.Run(_settings, labels => true);

            CollectionAssert.AreEqual(new[] { "a" }, removed);
            CollectionAssert.DoesNotContain(_settings.GetLabels(), "a");
            CollectionAssert.Contains(_settings.GetLabels(), "mine");
        }

        [Test]
        public void LabelMenu_NothingToRemove_DoesNotAskForConfirmation()
        {
            _settings.AddLabel("mine");
            LogAssert.Expect(LogType.Log, new Regex("No unused labels written by AddressTeller"));
            var asked = false;

            var removed = AddressTellerLabelMenu.Run(_settings, labels =>
            {
                asked = true;
                return true;
            });

            Assert.IsFalse(asked);
            CollectionAssert.IsEmpty(removed);
        }

        // --- 除去を止める条件（stale エントリ削除と揃える） ---

        /// <summary>ラベル専用ルールが評価中に例外を投げる。勝者は止めないが、本来出るはずのラベルが評価結果に現れない。</summary>
        private sealed class LabelOnlyThrowsRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.AnyGroup()
                    .Where(ctx => throw new InvalidOperationException("boom"))
                    .Label("never");
            }
        }

        private sealed class ConfigureThrowsRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules) => throw new InvalidOperationException("configure boom");
        }

        private sealed class MissingFolderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(Match.InFolder("Assets/_AddressTellerNoSuchFolderForLabelTest")).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private void AssertStaleKeptAndFreshAdded(AddressableAssetEntry entry)
            => CollectionAssert.AreEquivalent(new[] { "stale", "manual", "fresh" }, entry.labels);

        [Test]
        public void RemoveOn_LabelOnlyRuleThrows_ApplyAndPredictKeepLabels()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            var entry = PrepareEntryCarryingStaleLabel();
            var rules = new AddressRuleBase[] { new FreshLabelRule(), new LabelOnlyThrowsRule() };

            var predicted = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath }, rules);
            CollectionAssert.AreEqual(new[] { "fresh", "manual", "stale" }, predicted.After.Entries.Single().Labels);

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, rules);

            Assert.IsTrue(issues.Any(i => i.Status == ValidationStatus.RuleError));
            AssertStaleKeptAndFreshAdded(entry);
        }

        [Test]
        public void RemoveOff_LabelOnlyRuleThrows_NoStaleNotice()
        {
            var entry = PrepareEntryCarryingStaleLabel();
            var rules = new AddressRuleBase[] { new FreshLabelRule(), new LabelOnlyThrowsRule() };

            var predicted = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath }, rules);

            Assert.IsFalse(predicted.Issues.Any(i => i.Status == ValidationStatus.StaleManagedLabelKept),
                "ルール例外があるアセットは除去の対象外なので、除去される予定の通知も出さない。");
            Assert.IsNotNull(entry);
        }

        [Test]
        public void RemoveOn_ConfigureFailure_KeepsLabels()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            var entry = PrepareEntryCarryingStaleLabel();
            var rules = new AddressRuleBase[] { new FreshLabelRule(), new ConfigureThrowsRule() };
            LogAssert.Expect(LogType.Error, new Regex(@"Configure\(\) threw"));

            AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, rules);

            AssertStaleKeptAndFreshAdded(entry);
        }

        [Test]
        public void RemoveOn_DeclaredFolderMissing_KeepsLabels()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            var entry = PrepareEntryCarryingStaleLabel();
            var rules = new AddressRuleBase[] { new FreshLabelRule(), new MissingFolderRule() };

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, rules);

            Assert.IsTrue(issues.Any(i => i.Status == ValidationStatus.DeclaredFolderMissing));
            AssertStaleKeptAndFreshAdded(entry);
        }

        // --- 管理済み集合はラン開始時の台帳で固定する ---

        private const string EnemyPath = TestRootFolder + "/EnemyAsset.prefab";
        private const string OtherPath = TestRootFolder + "/OtherAsset.prefab";

        private sealed class EnemyRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(ctx => ctx.Path == EnemyPath).Address(ctx => ctx.FileNameWithoutExtension).Label("Enemy");
            }
        }

        private sealed class OtherRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(ctx => ctx.Path == OtherPath).Address(ctx => ctx.FileNameWithoutExtension).Label("other");
            }
        }

        private static string CreatePrefab(string path)
        {
            var go = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            return AssetDatabase.AssetPathToGUID(path);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RemoveOn_LabelFirstWrittenInThisRun_IsNotRemovedFromAnotherAssetInTheSameRun(bool reverseOrder)
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            CreatePrefab(EnemyPath);
            var otherGuid = CreatePrefab(OtherPath);
            var otherEntry = _settings.CreateOrMoveEntry(otherGuid, _stubGroup);
            otherEntry.SetAddress("OtherAsset");
            otherEntry.SetLabel("Enemy", true); // 利用者が手で付けた同名ラベル。台帳は空。
            var rules = new AddressRuleBase[] { new EnemyRule(), new OtherRule() };
            var paths = reverseOrder ? new[] { OtherPath, EnemyPath } : new[] { EnemyPath, OtherPath };

            var predicted = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, paths, rules);
            CollectionAssert.Contains(predicted.After.Entries.Single(e => e.Guid == otherGuid).Labels, "Enemy");

            AddressTellerService.ApplyAll(paths, _settings, NullProgressReporter.Instance, rules);

            CollectionAssert.AreEquivalent(new[] { "Enemy", "other" }, otherEntry.labels, "同じランで台帳入りした名前は、そのランでは除去の対象にならない（走査順に依存しない）。");
            CollectionAssert.AreEqual(new[] { "Enemy", "other" }, AddressTellerLabelLedger.GetAll());

            // 次のランでは台帳にあるため、ルールが出さない Enemy は外れ、Predict も同じ結果を予測する。
            var predictedNext = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, paths, rules);
            CollectionAssert.AreEqual(new[] { "other" }, predictedNext.After.Entries.Single(e => e.Guid == otherGuid).Labels);
            LogAssert.Expect(LogType.Warning, new Regex("label='Enemy'"));

            AddressTellerService.ApplyAll(paths, _settings, NullProgressReporter.Instance, rules);

            CollectionAssert.AreEquivalent(new[] { "other" }, otherEntry.labels);
        }

        // --- 台帳へ書けない ---

        /// <summary>台帳の書き込みを失敗させたまま ApplyAll を1回走らせ、その issues を返す。</summary>
        private IReadOnlyList<ValidationResult> ApplyAllWithFailingLedgerWrite()
        {
            var original = AddressTellerLabelLedger.WriteFile;
            AddressTellerLabelLedger.WriteFile = (path, text) => throw new System.IO.IOException("disk full");
            try
            {
                return AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, Rules);
            }
            finally
            {
                AddressTellerLabelLedger.WriteFile = original;
            }
        }

        [Test]
        public void LedgerWriteFails_RemoveOn_ApplyAllReportsBlockingResultAndDoesNotThrow()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            var entry = PrepareEntryCarryingStaleLabel();
            LogAssert.Expect(LogType.Warning, new Regex("label='stale'"));

            var issues = ApplyAllWithFailingLedgerWrite();

            var failure = issues.Single(i => i.Status == ValidationStatus.LabelLedgerWriteFailed);
            Assert.IsFalse(failure.IsOk);
            Assert.IsTrue(failure.IsBlocking);
            Assert.IsNull(failure.Context);
            StringAssert.Contains("fresh", failure.Message);
            Assert.IsTrue(entry.labels.Contains("fresh"), "ラベルの書き込み自体は通常どおり行われる。");
            Assert.AreEqual(2, AddressTellerReportBuilder.DetermineApplyExitCode(issues), "ON の間はブロッキングなので CLI は終了コード 2。");
        }

        [Test]
        public void LedgerWriteFails_RemoveOff_ReportsNonBlockingNotice_AndExitCodeIsZero()
        {
            // OFF の間は台帳に依存する機能（除去）が働いていないので、書けないこと自体は通知にとどまる。
            var entry = PrepareEntryCarryingStaleLabel();

            var issues = ApplyAllWithFailingLedgerWrite();

            var failure = issues.Single(i => i.Status == ValidationStatus.LabelLedgerWriteFailed);
            Assert.IsTrue(failure.IsOk);
            Assert.IsFalse(failure.IsBlocking);
            Assert.IsNull(failure.Context);
            StringAssert.Contains("fresh", failure.Message);
            Assert.IsTrue(entry.labels.Contains("fresh"), "ラベルの書き込み自体は通常どおり行われる。");
            Assert.AreEqual(0, AddressTellerReportBuilder.DetermineApplyExitCode(issues), "OFF の間は終了コードに影響しない。");
        }

        [Test]
        public void LedgerWriteFails_SeverityIsFixedWhenTheResultIsCreated()
        {
            PrepareEntryCarryingStaleLabel();
            var issues = ApplyAllWithFailingLedgerWrite();
            var failure = issues.Single(i => i.Status == ValidationStatus.LabelLedgerWriteFailed);
            Assert.IsTrue(failure.IsOk, "テストの前提: OFF で生成された。");

            AddressTellerSettings.RemoveStaleManagedLabels = true;

            Assert.IsTrue(failure.IsOk, "生成後に設定が変わっても、結果の重大度は動かない。");
        }

        [Test]
        public void LedgerFileReadOnly_NewNameInRun_StillRemovesStale_AndReportsWriteFailure()
        {
            // 除去はラン開始時の台帳に基づくので、台帳に書けるかどうかとは独立。書けないと失うのは
            // 「今回初めて書いた名前（fresh）の記録」だけで、それは除去が減る側。
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            var entry = PrepareEntryCarryingStaleLabel();
            System.IO.File.SetAttributes(AddressTellerLabelLedger.AbsoluteFilePath, System.IO.FileAttributes.ReadOnly);
            LogAssert.Expect(LogType.Warning, new Regex("label='stale'"));

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, Rules);

            Assert.IsTrue(issues.Any(i => i.Status == ValidationStatus.LabelLedgerWriteFailed));
            CollectionAssert.AreEquivalent(new[] { "manual", "fresh" }, entry.labels);
        }

        [Test]
        public void LedgerFileReadOnly_NoNewNames_RemovalHappensAsPredicted_AndNoFailureIsReported()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            var entry = PrepareEntryCarryingStaleLabel();
            AddressTellerLabelLedger.Record(new[] { "fresh" });
            entry.SetLabel("fresh", true);
            System.IO.File.SetAttributes(AddressTellerLabelLedger.AbsoluteFilePath, System.IO.FileAttributes.ReadOnly);

            var predicted = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath }, Rules);
            var predictedLabels = predicted.After.Entries.Single().Labels;
            CollectionAssert.AreEqual(new[] { "fresh", "manual" }, predictedLabels);

            LogAssert.Expect(LogType.Warning, new Regex("label='stale'"));
            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, Rules);

            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.LabelLedgerWriteFailed), "書く必要のある新しい名前が無いので、書き込みは試みられない。");
            CollectionAssert.AreEquivalent(predictedLabels, entry.labels);
        }

        // --- Validate メニュー: 通知だけでも結果ウィンドウを開く ---

        [Test]
        public void MenuValidate_OnlyStaleManagedLabelKeptNotices_ShowsResultWindow()
        {
            PrepareEntryCarryingStaleLabel();
            var original = AddressTellerMenu.s_showResultWindow;
            IReadOnlyList<ValidationResult> shown = null;
            AddressTellerMenu.s_showResultWindow = (issues, title, unmatched) => shown = issues;
            try
            {
                LogAssert.Expect(LogType.Log, new Regex("StaleManagedLabelKept: 1 entry carries"));
                LogAssert.Expect(LogType.Log, new Regex(Regex.Escape("[AddressTeller] Validate completed: 1 notice(s) (no issues), including 1 entry with labels that would be removed")));

                AddressTellerMenu.Validate(_settings, Rules);
            }
            finally
            {
                AddressTellerMenu.s_showResultWindow = original;
            }

            Assert.IsNotNull(shown, "StaleManagedLabelKept 通知が1件以上あるなら、errorCount が0でも結果ウィンドウを開くべき。");
            Assert.AreEqual(ValidationStatus.StaleManagedLabelKept, shown.Single().Status);
        }
    }
}
