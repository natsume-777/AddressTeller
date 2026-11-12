using NUnit.Framework;
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 別アセット間のアドレス重複検出（<see cref="DuplicateAddressDetector"/>）が、
    /// <see cref="AddressTellerService.ValidateAll(AddressableAssetSettings, IProgressReporter, System.Collections.Generic.IReadOnlyList{AddressRuleBase})"/>
    /// と <see cref="AddressTellerSnapshotService.BuildPredictedSnapshot(AddressableAssetSettings, System.Collections.Generic.IEnumerable{string}, System.Collections.Generic.IReadOnlyList{AddressRuleBase})"/>
    /// の両方の全件走査経路から実際に呼ばれることを確認する結線テスト。
    /// 検出アルゴリズム自体の網羅的なケースは <see cref="DuplicateAddressDetectorTests"/> を参照。
    /// テスト対象アセットは一時フォルダ Assets/_AddressTellerTestTemp 配下に作成する。
    /// </summary>
    public class AddressTellerDuplicateAddressIntegrationTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp";
        private const string StubFolder = TestRootFolder + "/DupAddr";
        private const string AssetPath1 = StubFolder + "/AssetOne.prefab";
        private const string AssetPath2 = StubFolder + "/AssetTwo.prefab";

        // StubFolder の外に置くことで ConstantAddressRule にマッチさせない（Skipped 資産用）。
        private const string StaleAssetPath = TestRootFolder + "/StaleAsset.prefab";

        // settings.ConfigFolder は AssetFilter.ShouldExclude でこの配下のパスを評価対象から除外するために使われる。
        // TestRootFolder と同じ値にすると、ここで作成するテストアセット自身が除外されてしまうため、
        // 衝突しない別パス（実在しなくてよい）を割り当てる。
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempConfig";

        /// <summary>StubFolder 配下の全アセットに固定アドレス "SharedAddress" を割り当てるテスト専用ルール。
        /// 複数アセットが同じアドレスに解決される「アセット間重複」を意図的に作るために使う
        /// （同一アセットへの複数ルールの衝突であるConflictingAddressとは別物）。</summary>
        private sealed class ConstantAddressRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", StringComparison.Ordinal))
                    .Address("SharedAddress");
            }
        }

        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _stubGroup;
        private AddressableAssetGroup _unmanagedGroup;
        private bool _originalCleanupSetting;

        [SetUp]
        public void SetUp()
        {
            _originalCleanupSetting = AddressTellerSettings.CleanupStaleEntries;
            AddressTellerSettings.CleanupStaleEntries = true;

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerDuplicateAddressTestSettings");
            _stubGroup = _settings.CreateGroup("StubGroup", false, false, false, null);
            // どのルールも Address() を宣言しないグループ = AddressTeller の所有外（管理外）。
            _unmanagedGroup = _settings.CreateGroup("UnmanagedGroup", false, false, false, null);

            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            AssetDatabase.CreateFolder(TestRootFolder, "DupAddr");
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanupSetting;
            AssetDatabase.DeleteAsset(TestRootFolder);
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
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ValidateAll_TwoManagedAssetsSameAddress_ReturnsErrorDuplicateAddress()
        {
            CreatePrefab(AssetPath1);
            CreatePrefab(AssetPath2);

            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, new AddressRuleBase[] { new ConstantAddressRule() });

            var dup = issues.SingleOrDefault(i => i.Status == ValidationStatus.DuplicateAddress);
            Assert.IsNotNull(dup, "両方 AddressTeller 管理の資産が同じアドレスに解決される場合、DuplicateAddress が報告されるべき。");
            Assert.IsFalse(dup.IsOk, "このランで AddressTeller 自身が書くアドレス同士の重複は Error 扱いになるべき。");
            Assert.IsTrue(dup.HasWritableDuplicate);
            StringAssert.Contains(AssetPath1, dup.Message);
            StringAssert.Contains(AssetPath2, dup.Message);
        }

        [Test]
        public void ValidateAll_NoDuplicateAddresses_ReturnsNoDuplicateAddressIssue()
        {
            CreatePrefab(AssetPath1);

            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, new AddressRuleBase[] { new ConstantAddressRule() });

            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.DuplicateAddress));
        }

        [Test]
        public void ValidateAll_UnmanagedDuplicate_ReturnsWarningNotError()
        {
            // どのルールにも触れられない管理外グループに、あらかじめ同じアドレスの2エントリを手動登録しておく
            // （実アセットである必要はない。RemoveEntriesForDeletedAssets の既存テストと同じ考え方）。
            _settings.CreateOrMoveEntry("manual-guid-1", _unmanagedGroup).SetAddress("ManualShared");
            _settings.CreateOrMoveEntry("manual-guid-2", _unmanagedGroup).SetAddress("ManualShared");

            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, Array.Empty<AddressRuleBase>());

            var dup = issues.SingleOrDefault(i => i.Status == ValidationStatus.DuplicateAddress);
            Assert.IsNotNull(dup, "管理外グループ同士の重複でも報告はされるべき（直せないので Warning）。");
            Assert.IsTrue(dup.IsOk, "AddressTeller はどちらのアドレスも書いていないため、Warning(IsOk=true) になるべき。");
            Assert.IsFalse(dup.HasWritableDuplicate);
        }

        [Test]
        public void BuildPredictedSnapshot_TwoManagedAssetsSameAddress_ReturnsErrorDuplicateAddress()
        {
            CreatePrefab(AssetPath1);
            CreatePrefab(AssetPath2);

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(
                _settings, new[] { AssetPath1, AssetPath2 }, new AddressRuleBase[] { new ConstantAddressRule() });

            var dup = result.Issues.SingleOrDefault(i => i.Status == ValidationStatus.DuplicateAddress);
            Assert.IsNotNull(dup);
            Assert.IsFalse(dup.IsOk);
            Assert.IsTrue(dup.HasWritableDuplicate);
        }

        [Test]
        public void BuildPredictedSnapshot_ManagedPlusUnmanagedDuplicate_ReturnsError()
        {
            CreatePrefab(AssetPath1);
            _settings.CreateOrMoveEntry("manual-guid-unmanaged", _unmanagedGroup).SetAddress("SharedAddress");

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(
                _settings, new[] { AssetPath1 }, new AddressRuleBase[] { new ConstantAddressRule() });

            var dup = result.Issues.SingleOrDefault(i => i.Status == ValidationStatus.DuplicateAddress);
            Assert.IsNotNull(dup, "AddressTeller 管理の資産1件 + 管理外の既存エントリ1件が同じアドレスなら報告されるべき。");
            Assert.IsFalse(dup.IsOk, "重複のうち1件でも AddressTeller がこのランで書く対象なら Error 扱い。");
            Assert.IsTrue(dup.HasWritableDuplicate);
        }

        [Test]
        public void BuildPredictedSnapshot_UnmanagedDuplicateOnly_DoesNotAffectExitCode()
        {
            _settings.CreateOrMoveEntry("manual-guid-1", _unmanagedGroup).SetAddress("ManualShared");
            _settings.CreateOrMoveEntry("manual-guid-2", _unmanagedGroup).SetAddress("ManualShared");

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(
                _settings, Array.Empty<string>(), new AddressRuleBase[] { new ConstantAddressRule() });

            var dup = result.Issues.SingleOrDefault(i => i.Status == ValidationStatus.DuplicateAddress);
            Assert.IsNotNull(dup);
            Assert.IsTrue(dup.IsOk);

            Assert.AreEqual(0, AddressTellerReportBuilder.DetermineExitCode(result),
                "管理外同士の重複だけでは差分もエラーも無いはずなので exit code は 0 のまま。");
        }

        [Test]
        public void ApplyAll_TwoManagedAssetsSameAddress_ReportsNonBlockingDuplicateAddress()
        {
            CreatePrefab(AssetPath1);
            CreatePrefab(AssetPath2);

            var issues = AddressTellerService.ApplyAll(
                new[] { AssetPath1, AssetPath2 }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new ConstantAddressRule() });

            var dup = issues.SingleOrDefault(i => i.Status == ValidationStatus.DuplicateAddress);
            Assert.IsNotNull(dup, "このランで書いたアドレス同士の重複は ApplyAll の末尾で報告されるべき。");
            Assert.IsTrue(dup.HasWritableDuplicate);
            Assert.IsFalse(dup.IsOk);
            Assert.IsFalse(dup.IsBlocking, "DuplicateAddress は書き込みを止めない。");
            StringAssert.Contains(AssetPath1, dup.Message);
            StringAssert.Contains(AssetPath2, dup.Message);

            // 非ブロッキング: 重複していても両方とも実際に書き込まれている。
            Assert.AreEqual("SharedAddress", _settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(AssetPath1))?.address);
            Assert.AreEqual("SharedAddress", _settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(AssetPath2))?.address);
        }

        [Test]
        public void ApplyAllIncremental_DuplicateAgainstAssetOutsideChangedPaths_IsReported()
        {
            // 増分適用の目的: 変更したパスに含まれない別アセットとの重複も検出する。
            CreatePrefab(AssetPath1);
            CreatePrefab(AssetPath2);
            var rules = new AddressRuleBase[] { new ConstantAddressRule() };
            AddressTellerService.ApplyAll(new[] { AssetPath2 }, _settings, NullProgressReporter.Instance, rules);

            var issues = AddressTellerService.ApplyAllIncremental(new[] { AssetPath1 }, _settings, rules);

            var dup = issues.SingleOrDefault(i => i.Status == ValidationStatus.DuplicateAddress);
            Assert.IsNotNull(dup);
            Assert.IsTrue(dup.HasWritableDuplicate);
            StringAssert.Contains(AssetPath2, dup.Message, "変更パスの外にある既存エントリも重複相手として報告されるべき。");
        }

        [Test]
        public void ApplyAll_WrittenAddressCollidesWithUnmanagedEntry_ReportsDuplicateAddress()
        {
            CreatePrefab(AssetPath1);
            _settings.CreateOrMoveEntry("manual-guid-unmanaged", _unmanagedGroup).SetAddress("SharedAddress");

            var issues = AddressTellerService.ApplyAll(
                new[] { AssetPath1 }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new ConstantAddressRule() });

            var dup = issues.SingleOrDefault(i => i.Status == ValidationStatus.DuplicateAddress);
            Assert.IsNotNull(dup);
            Assert.IsTrue(dup.HasWritableDuplicate);
        }

        [Test]
        public void ApplyAll_OnlySkippedAssetPassed_ReportsNoDuplicateAddress()
        {
            // 管理外同士の重複が既にあっても、このランがアドレスを割り当てなければ（Skipped のみ）何も報告しない。
            // StaleAssetPath は StubFolder の外にあるため ConstantAddressRule にマッチしない（Skipped）。
            CreatePrefab(StaleAssetPath);
            _settings.CreateOrMoveEntry("manual-guid-1", _unmanagedGroup).SetAddress("ManualShared");
            _settings.CreateOrMoveEntry("manual-guid-2", _unmanagedGroup).SetAddress("ManualShared");

            var issues = AddressTellerService.ApplyAll(
                new[] { StaleAssetPath }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new ConstantAddressRule() });

            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.DuplicateAddress));
        }

        [Test]
        public void DetectForWrittenGuids_NoWrittenGuids_ReturnsEmptyWithoutTouchingSettings()
        {
            // 書き込み（割り当て）0 件の早期 return を固定する。settings に触れないので null でも例外にならない。
            var results = DuplicateAddressDetector.DetectForWrittenGuids(null, Array.Empty<string>());

            Assert.IsEmpty(results);
        }

        [Test]
        public void ApplyAll_StaleEntryRemovedInSameRun_IsNotCountedAsDuplicate()
        {
            // 同じランで stale 削除されるエントリは重複に数えない（ValidateAll / BuildPredictedSnapshot と同じ見方）。
            CreatePrefab(AssetPath1);
            CreatePrefab(StaleAssetPath);
            var staleGuid = AssetDatabase.AssetPathToGUID(StaleAssetPath);
            _settings.CreateOrMoveEntry(staleGuid, _stubGroup).SetAddress("SharedAddress");

            var issues = AddressTellerService.ApplyAll(
                new[] { AssetPath1, StaleAssetPath }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new ConstantAddressRule() });

            Assert.IsNull(_settings.FindAssetEntry(staleGuid), "前提条件: stale エントリはこのランで削除されるはず。");
            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.DuplicateAddress),
                "削除済みのエントリは重複として報告されるべきではない。");
        }

        [Test]
        public void ApplyAll_CancelledAfterWriting_StillReportsDuplicateForWrittenAssets()
        {
            // キャンセルされても、そこまでに書いた分については重複を報告する。
            CreatePrefab(AssetPath1);
            CreatePrefab(AssetPath2);
            _settings.CreateOrMoveEntry("manual-guid-unmanaged", _unmanagedGroup).SetAddress("SharedAddress");

            var issues = AddressTellerService.ApplyAll(
                new[] { AssetPath1, AssetPath2 }, _settings, new CancelAfterReporter(1), new AddressRuleBase[] { new ConstantAddressRule() });

            Assert.IsNull(_settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(AssetPath2)), "前提条件: 2件目はキャンセルで未処理。");
            var dup = issues.SingleOrDefault(i => i.Status == ValidationStatus.DuplicateAddress);
            Assert.IsNotNull(dup);
            Assert.IsTrue(dup.HasWritableDuplicate);
        }

        /// <summary>指定回数の Report までは true を返し、それ以降は false（キャンセル）を返す。</summary>
        private sealed class CancelAfterReporter : IProgressReporter
        {
            private readonly int _allowedCalls;
            private int _calls;

            public CancelAfterReporter(int allowedCalls) => _allowedCalls = allowedCalls;

            public bool Report(int current, int total, string description) => ++_calls <= _allowedCalls;
        }

        [Test]
        public void ApplyAll_DuplicateUnrelatedToWrittenAddress_IsNotReported()
        {
            // 書き込んだアドレスと無関係な既存の重複（管理外同士）は、import のたびに出し続けないよう増分適用では報告しない。
            CreatePrefab(AssetPath1);
            _settings.CreateOrMoveEntry("manual-guid-1", _unmanagedGroup).SetAddress("ManualShared");
            _settings.CreateOrMoveEntry("manual-guid-2", _unmanagedGroup).SetAddress("ManualShared");

            var issues = AddressTellerService.ApplyAll(
                new[] { AssetPath1 }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new ConstantAddressRule() });

            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.DuplicateAddress));
        }

        [Test]
        public void ValidateAllAndBuildPredictedSnapshot_AgreeWhenStaleEntryWouldBeRemoved()
        {
            // ValidateAll の予測後集合と BuildPredictedSnapshot の afterMap は、
            // stale クリーンアップ（このランで削除される予定のエントリ）を同じように反映しなければならない
            // ことの回帰テスト。
            // asset A はマッチしてアドレス "SharedAddress" を得る。
            CreatePrefab(AssetPath1);

            // asset B（StaleAssetPath）は StubFolder の外にあるため ConstantAddressRule にマッチしない
            // （Skipped）。所有グループ（StubGroup）に "SharedAddress" のエントリを手動登録しておく。
            // このテストクラスは SetUp で CleanupStaleEntries を ON にしているため、この実行で削除される予定のはず。
            CreatePrefab(StaleAssetPath);
            var staleGuid = AssetDatabase.AssetPathToGUID(StaleAssetPath);
            _settings.CreateOrMoveEntry(staleGuid, _stubGroup).SetAddress("SharedAddress");

            var rules = new AddressRuleBase[] { new ConstantAddressRule() };

            var validateIssues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, rules);
            var predicted = AddressTellerSnapshotService.BuildPredictedSnapshot(
                _settings, new[] { AssetPath1, StaleAssetPath }, rules);

            // 前提条件の検証: stale エントリが実際に削除予測に載っていること。
            Assert.IsTrue(predicted.Diff.Removed.Any(e => e.Guid == staleGuid),
                "前提条件: stale エントリは Diff.Removed に現れるはず。");

            Assert.IsFalse(validateIssues.Any(i => i.Status == ValidationStatus.DuplicateAddress),
                "stale エントリはこのランで削除される予定のため、重複として報告されるべきではない（ValidateAll）。");
            Assert.IsFalse(predicted.Issues.Any(i => i.Status == ValidationStatus.DuplicateAddress),
                "同じ理由で BuildPredictedSnapshot 側も重複を報告してはならない。");
        }
    }
}
