using NUnit.Framework;
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Presets;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// stale 削除の件数上限（<see cref="AddressTellerSettings.MaxStaleRemovalsPerRun"/>）の挙動を検証する。
    /// 無マッチ判定に基づく削除と無効パスエントリの掃除は、ラン末尾にまとめて実行され、合計が上限を超えれば
    /// 全件見送られる。公開 API の ApplyAll（Postprocessor の増分適用・CLI を含む）に適用し、メニューの確認
    /// ダイアログ経路が使う ApplyAllConfirmed にだけ適用しない。
    /// </summary>
    public class AddressTellerStaleRemovalLimitTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp";
        private const string StubFolder = TestRootFolder + "/StaleRemovalLimit";
        private const string KeeperPath = StubFolder + "/Keeper.prefab";
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempStaleRemovalLimitConfig";

        /// <summary>ManagedGroup を所有グループとして宣言するが、何にもマッチしないルール（そのグループの既存エントリは全て stale になる）。</summary>
        private sealed class MatchNothingRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("ManagedGroup")
                    .Where(ctx => false)
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>Keeper.prefab だけを KeeperGroup に登録するルール（上限超過時も書き込みは止まらないことの確認用）。</summary>
        private sealed class KeeperRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("KeeperGroup")
                    .Where(ctx => ctx.Path == KeeperPath)
                    .Address(ctx => "KeeperAddress");
            }
        }

        /// <summary>CreateInvalidExtensionAsset 専用の Preset ターゲット。</summary>
        private sealed class PresetProbeAsset : ScriptableObject
        {
        }

        /// <summary>指定回数の Report までは true を返し、それ以降は false（キャンセル）を返す。</summary>
        private sealed class CancelAfterReporter : IProgressReporter
        {
            private readonly int _allowedCalls;
            private int _calls;

            public CancelAfterReporter(int allowedCalls) => _allowedCalls = allowedCalls;

            public bool Report(int current, int total, string description) => ++_calls <= _allowedCalls;
        }

        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _managedGroup;
        private AddressableAssetGroup _keeperGroup;
        private bool _originalCleanup;
        private int _originalLimit;

        [SetUp]
        public void SetUp()
        {
            _originalCleanup = AddressTellerSettings.CleanupStaleEntries;
            _originalLimit = AddressTellerSettings.MaxStaleRemovalsPerRun;

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerStaleRemovalLimitTestSettings");
            _managedGroup = _settings.CreateGroup("ManagedGroup", false, false, false, null);
            _keeperGroup = _settings.CreateGroup("KeeperGroup", false, false, false, null);

            if (AssetDatabase.IsValidFolder(StubFolder))
                AssetDatabase.DeleteAsset(StubFolder);
            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            AssetDatabase.CreateFolder(TestRootFolder, "StaleRemovalLimit");
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanup;
            AddressTellerSettings.MaxStaleRemovalsPerRun = _originalLimit;

            // SetUp が（無ければ）作る親の TestRootFolder ごと消す。StubFolder だけを消すと空の親フォルダと
            // その .meta がプロジェクトに残る（他のテストクラスの TearDown も TestRootFolder ごと消している）。
            if (AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.DeleteAsset(TestRootFolder);

            UnityEngine.Object.DestroyImmediate(_managedGroup, true);
            UnityEngine.Object.DestroyImmediate(_keeperGroup, true);
            UnityEngine.Object.DestroyImmediate(_settings, true);
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
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void CreateInvalidExtensionAsset(string path)
        {
            var probe = ScriptableObject.CreateInstance<PresetProbeAsset>();
            try
            {
                AssetDatabase.CreateAsset(new Preset(probe), path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// ManagedGroup に登録済みで、どのルールにもマッチしない（＝stale な）実プレハブを count 件作り、そのパスを返す。
        /// </summary>
        private string[] CreateStaleEntries(int count)
        {
            var paths = new string[count];
            for (var i = 0; i < count; i++)
            {
                paths[i] = $"{StubFolder}/Stale{i}.prefab";
                CreatePrefab(paths[i]);
                _settings.CreateOrMoveEntry(GuidOf(paths[i]), _managedGroup).SetAddress($"StaleAddress{i}");
            }
            return paths;
        }

        private bool EntryExists(string path) => _settings.FindAssetEntry(GuidOf(path)) != null;

        private static readonly AddressRuleBase[] s_rules = { new MatchNothingRule() };

        // --- 上限ちょうど／超過 ---

        [Test]
        public void NonInteractive_RemovalsEqualToLimit_RemovesAll()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 3;
            var paths = CreateStaleEntries(3);

            var issues = AddressTellerService.ApplyAll(paths, _settings, NullProgressReporter.Instance, s_rules);

            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded));
            Assert.IsFalse(paths.Any(EntryExists), "件数が上限ちょうどなら全件削除される。");
        }

        [Test]
        public void NonInteractive_RemovalsExceedLimit_RemovesNoneAndReportsBlockingIssue()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 2;
            var paths = CreateStaleEntries(3);

            var issues = AddressTellerService.ApplyAll(paths, _settings, NullProgressReporter.Instance, s_rules);

            Assert.IsTrue(paths.All(EntryExists), "上限を超えたら一部だけ削除せず、全件見送る。");
            var exceeded = issues.Single(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded);
            Assert.IsFalse(exceeded.IsOk);
            Assert.IsTrue(exceeded.IsBlocking);
            Assert.IsNull(exceeded.Context);
        }

        [Test]
        public void NonInteractive_LimitExceeded_MessageHasCountLimitAndFirstPaths()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 1;
            var paths = CreateStaleEntries(7);

            var issues = AddressTellerService.ApplyAll(paths, _settings, NullProgressReporter.Instance, s_rules);

            var message = issues.Single(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded).Message;
            StringAssert.Contains("7", message);
            StringAssert.Contains("limit of 1", message);
            // 先頭は Ordinal 順の 5 件（Stale0〜Stale4）。それ以降は省略され「...」で示される。
            for (var i = 0; i < 5; i++)
                StringAssert.Contains(paths[i], message);
            StringAssert.DoesNotContain(paths[5], message);
            StringAssert.Contains("...", message);
        }

        [Test]
        public void NonInteractive_LimitZero_IsUnlimited()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 0;
            var paths = CreateStaleEntries(4);

            var issues = AddressTellerService.ApplyAll(paths, _settings, NullProgressReporter.Instance, s_rules);

            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded));
            Assert.IsFalse(paths.Any(EntryExists));
        }

        [Test]
        public void NonInteractive_LimitExceeded_StillWritesAddressesForMatchedAssets()
        {
            // 見送るのは削除だけ。マッチしたアセットのアドレス書き込みは通常どおり行われる。
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 1;
            CreatePrefab(KeeperPath);
            var stalePaths = CreateStaleEntries(2);
            var rules = new AddressRuleBase[] { new MatchNothingRule(), new KeeperRule() };

            var issues = AddressTellerService.ApplyAll(
                stalePaths.Concat(new[] { KeeperPath }).ToArray(), _settings, NullProgressReporter.Instance, rules);

            Assert.IsTrue(issues.Any(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded));
            var keeperEntry = _settings.FindAssetEntry(GuidOf(KeeperPath));
            Assert.IsNotNull(keeperEntry);
            Assert.AreEqual("KeeperAddress", keeperEntry.address);
            Assert.AreEqual("KeeperGroup", keeperEntry.parentGroup.Name);
        }

        // --- 経路ごとの適用範囲 ---

        [Test]
        public void ApplyAllConfirmed_RemovalsExceedLimit_IgnoresLimitAndRemovesAll()
        {
            // メニュー経路（AddressTellerApplyFlow.ExecuteApply）が呼ぶ無制限のオーバーロード。
            // 確認ダイアログが削除件数を見せた上での実行なので、上限は適用しない。
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 1;
            var paths = CreateStaleEntries(3);

            var issues = AddressTellerService.ApplyAllConfirmed(paths, _settings, NullProgressReporter.Instance, s_rules);

            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded));
            Assert.IsFalse(paths.Any(EntryExists));
        }

        // --- 停止中は上限超過を報告しない ---

        /// <summary>Configure() が例外を送出するルール（そのラン全体の stale 削除が止まる）。</summary>
        private sealed class BrokenConfigureRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
                => throw new InvalidOperationException("broken on purpose");
        }

        /// <summary>実在しないフォルダを宣言するルール（DeclaredFolderMissing でそのラン全体の stale 削除が止まる）。</summary>
        private sealed class MissingFolderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("ManagedGroup")
                    .Where(Match.InFolder(TestRootFolder + "/DoesNotExistStaleRemovalLimit"))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>述語が例外を投げるルール（他にマッチが無いと、Skipped かつ resolution.Errors &gt; 0 になる）。</summary>
        private sealed class ThrowingPredicateRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("ManagedGroup")
                    .Where(ctx => { throw new InvalidOperationException("predicate boom"); })
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        [Test]
        public void ApplyAll_ConfigureFailure_SuspendsCleanupAndReportsNoLimitIssue()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 1;
            var paths = CreateStaleEntries(3);
            var rules = new AddressRuleBase[] { new MatchNothingRule(), new BrokenConfigureRule() };

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("BrokenConfigureRule.Configure\\(\\) threw"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Skipping stale entry cleanup"));

            var issues = AddressTellerService.ApplyAll(paths, _settings, NullProgressReporter.Instance, rules);

            Assert.IsTrue(paths.All(EntryExists));
            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded),
                "停止中は削除予定を作らないので、上限超過は報告されない。");
        }

        [Test]
        public void ApplyAll_DeclaredFolderMissing_SuspendsCleanupAndReportsNoLimitIssue()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 1;
            var paths = CreateStaleEntries(3);
            var rules = new AddressRuleBase[] { new MatchNothingRule(), new MissingFolderRule() };

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Skipping stale entry cleanup"));

            var issues = AddressTellerService.ApplyAll(paths, _settings, NullProgressReporter.Instance, rules);

            Assert.IsTrue(paths.All(EntryExists));
            Assert.IsTrue(issues.Any(i => i.Status == ValidationStatus.DeclaredFolderMissing));
            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded));
        }

        // --- ルール例外があるアセットのエントリは削除しない ---

        [Test]
        public void ApplyAll_PredicateThrowsAndNothingElseMatches_KeepsEntry()
        {
            // Skipped かつ resolution.Errors > 0（全ルールが正常評価された上でのマッチ 0 件、とは言えない）。
            AddressTellerSettings.CleanupStaleEntries = true;
            var paths = CreateStaleEntries(1);
            var rules = new AddressRuleBase[] { new ThrowingPredicateRule() };

            var issues = AddressTellerService.ApplyAll(paths, _settings, NullProgressReporter.Instance, rules);

            Assert.IsTrue(issues.Any(i => i.Status == ValidationStatus.RuleError));
            Assert.IsTrue(EntryExists(paths[0]), "ルール例外があるアセットのエントリは、無マッチ扱いで削除してはならない。");
        }

        [Test]
        public void ApplyAllIncremental_PredicateThrowsAndNothingElseMatches_KeepsEntry()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var paths = CreateStaleEntries(1);
            var rules = new AddressRuleBase[] { new ThrowingPredicateRule() };

            var issues = AddressTellerService.ApplyAllIncremental(paths, _settings, rules);

            Assert.IsTrue(issues.Any(i => i.Status == ValidationStatus.RuleError));
            Assert.IsTrue(EntryExists(paths[0]));
        }

        // --- CLI の終了コードとレポート ---

        [Test]
        public void ExitWithReport_LimitExceeded_ExitsWithCode2AndReportListsIssueWithoutRemovedDrift()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 1;
            var paths = CreateStaleEntries(3);
            var reportPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AddressTellerStaleLimitReport_{Guid.NewGuid():N}.json");
            var originalExit = AddressTellerMenu.s_exitCli;
            int? exitCode = null;
            AddressTellerMenu.s_exitCli = code => exitCode = code;

            try
            {
                // CLI と同じ手順: 先に dry-run を作り、続けて ApplyAll を実行して結果をレポート化する。
                var dryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, paths, s_rules);
                Assert.AreEqual(3, dryRun.Diff.Removed.Count, "前提: dry-run は 3 件の削除を予測する。");
                var applyIssues = AddressTellerService.ApplyAll(paths, _settings, NullProgressReporter.Instance, s_rules);
                Assert.IsTrue(applyIssues.Any(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded));

                Assert.IsTrue(AddressTellerCliArgs.TryParse(new[] { "-addressTellerReport", reportPath }, out var cliArgs, out _));
                AddressTellerMenu.ExitWithReport(dryRun, applyIssues, cliArgs, _settings);

                Assert.AreEqual(2, exitCode);
                var report = JsonUtility.FromJson<AddressTellerReport>(System.IO.File.ReadAllText(reportPath));
                Assert.IsTrue(report.Issues.Any(i => i.Status == nameof(ValidationStatus.StaleRemovalLimitExceeded)),
                    "実行時にしか出ない blocking issue がレポートの Issues に載る。");
                Assert.IsFalse(report.Drift.Any(d => d.ChangeType == "Removed"), "見送った削除は Removed drift に載せない。");
                Assert.AreEqual(0, report.Summary.Removed);
                Assert.AreEqual(2, report.Summary.ExitCode);
            }
            finally
            {
                AddressTellerMenu.s_exitCli = originalExit;
                if (System.IO.File.Exists(reportPath)) System.IO.File.Delete(reportPath);
            }
        }

        [Test]
        public void Incremental_RemovalsExceedLimit_RemovesNoneAndReportsIssue()
        {
            // Postprocessor が呼ぶ増分適用。
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 2;
            var paths = CreateStaleEntries(3);

            var issues = AddressTellerService.ApplyAllIncremental(paths, _settings, s_rules);

            Assert.IsTrue(paths.All(EntryExists));
            Assert.AreEqual(1, issues.Count(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded));
        }

        [Test]
        public void Incremental_RemovalsWithinLimit_RemovesAll()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 3;
            var paths = CreateStaleEntries(3);

            var issues = AddressTellerService.ApplyAllIncremental(paths, _settings, s_rules);

            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded));
            Assert.IsFalse(paths.Any(EntryExists));
        }

        // --- キャンセル ---

        [Test]
        public void ApplyAll_Cancelled_RemovesNothing()
        {
            // 1件目を処理（stale と判定）した後で2件目の手前でキャンセルする。削除はラン末尾に回すため、
            // 1件目の stale エントリも含めて何も削除されない。
            AddressTellerSettings.CleanupStaleEntries = true;
            var paths = CreateStaleEntries(3);

            AddressTellerService.ApplyAll(paths, _settings, new CancelAfterReporter(allowedCalls: 1), s_rules);

            Assert.IsTrue(paths.All(EntryExists), "キャンセル時は削除を一切行わない。");
        }

        [Test]
        public void ApplyAll_Cancelled_DoesNotRemoveInvalidPathEntries()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var invalidPath = StubFolder + "/Invalid.preset";
            CreateInvalidExtensionAsset(invalidPath);
            _settings.CreateOrMoveEntry(GuidOf(invalidPath), _managedGroup).SetAddress("InvalidAddress");
            var paths = CreateStaleEntries(1);

            AddressTellerService.ApplyAll(paths, _settings, new CancelAfterReporter(allowedCalls: 0), s_rules);

            Assert.IsTrue(EntryExists(invalidPath), "無効パスの掃除もキャンセル時は行わない。");
        }

        // --- 無効パス掃除の合流 ---

        [Test]
        public void NonInteractive_InvalidPathEntriesCountTowardLimit()
        {
            // 無マッチ1件 + 無効パス1件 = 2件。上限1を超えるので、両方とも見送られる。
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 1;
            var invalidPath = StubFolder + "/Invalid.preset";
            CreateInvalidExtensionAsset(invalidPath);
            _settings.CreateOrMoveEntry(GuidOf(invalidPath), _managedGroup).SetAddress("InvalidAddress");
            var stalePaths = CreateStaleEntries(1);

            var issues = AddressTellerService.ApplyAll(stalePaths, _settings, NullProgressReporter.Instance, s_rules);

            Assert.IsTrue(EntryExists(invalidPath));
            Assert.IsTrue(EntryExists(stalePaths[0]));
            Assert.AreEqual(1, issues.Count(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded));
        }

        [Test]
        public void NonInteractive_InvalidPathEntryAlone_WithinLimit_IsRemoved()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 1;
            var invalidPath = StubFolder + "/Invalid.preset";
            CreateInvalidExtensionAsset(invalidPath);
            _settings.CreateOrMoveEntry(GuidOf(invalidPath), _managedGroup).SetAddress("InvalidAddress");

            var issues = AddressTellerService.ApplyAll(Array.Empty<string>(), _settings, NullProgressReporter.Instance, s_rules);

            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded));
            Assert.IsFalse(EntryExists(invalidPath));
        }

        // --- Cleanup OFF ---

        [Test]
        public void NonInteractive_CleanupOff_RemovesNothingAndReportsNoLimitIssue()
        {
            AddressTellerSettings.CleanupStaleEntries = false;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 1;
            var paths = CreateStaleEntries(3);

            var issues = AddressTellerService.ApplyAll(paths, _settings, NullProgressReporter.Instance, s_rules);

            Assert.IsTrue(paths.All(EntryExists));
            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded),
                "削除が無効なら、上限超過は発生し得ない。");
        }

        [Test]
        public void Predict_CleanupOff_StillReportsUnmatchedEntryKept()
        {
            // 上限を設定していても、Cleanup OFF の dry-run は従来どおり UnmatchedEntryKept で通知する。
            AddressTellerSettings.CleanupStaleEntries = false;
            AddressTellerSettings.MaxStaleRemovalsPerRun = 1;
            var paths = CreateStaleEntries(2);

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, paths, s_rules);

            Assert.AreEqual(2, result.Issues.Count(i => i.Status == ValidationStatus.UnmatchedEntryKept));
            Assert.IsFalse(result.Issues.Any(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded));
        }

        // --- 設定値 ---

        [Test]
        public void MaxStaleRemovalsPerRun_DefaultIsZero()
        {
            Assert.AreEqual(0, new AddressTellerSettingsAsset.Data()._maxStaleRemovalsPerRun);
        }

        [Test]
        public void MaxStaleRemovalsPerRun_NegativeValueIsStoredAsZero()
        {
            AddressTellerSettings.MaxStaleRemovalsPerRun = -5;

            Assert.AreEqual(0, AddressTellerSettings.MaxStaleRemovalsPerRun);
        }

        [Test]
        public void MaxStaleRemovalsPerRun_PositiveValueRoundTrips()
        {
            AddressTellerSettings.MaxStaleRemovalsPerRun = 25;

            Assert.AreEqual(25, AddressTellerSettings.MaxStaleRemovalsPerRun);
        }

        [TestCase(0, 0)]
        [TestCase(7, 7)]
        [TestCase(-1, 0)]
        [TestCase(int.MinValue, 0)]
        public void NormalizeMaxStaleRemovalsPerRun_ClampsNegativeToZero(int input, int expected)
        {
            Assert.AreEqual(expected, AddressTellerSettingsAsset.NormalizeMaxStaleRemovalsPerRun(input, warnIfChanged: false));
        }
    }
}
