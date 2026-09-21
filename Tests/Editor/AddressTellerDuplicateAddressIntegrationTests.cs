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
        public void ApplyAll_ManagedDuplicate_NeverReportsDuplicateAddress()
        {
            // 設計6（Postprocessor 経由の増分適用では重複検出を行わない。毎 import のプロジェクト全体走査は
            // 常設指示9 に反するため）の回帰テスト。ApplyAll は唯一の書き込み系エントリポイントであり、
            // DuplicateAddressDetector を一切呼ばない。将来ここへ重複検出が足された場合の回帰を検出する。
            CreatePrefab(AssetPath1);
            CreatePrefab(AssetPath2);

            var issues = AddressTellerService.ApplyAll(
                new[] { AssetPath1, AssetPath2 }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new ConstantAddressRule() });

            Assert.IsFalse(issues.Any(i => i.Status == ValidationStatus.DuplicateAddress),
                "ApplyAll は DuplicateAddressDetector を呼んではならない（増分適用経路のため）。");
        }

        [Test]
        public void ValidateAllAndBuildPredictedSnapshot_AgreeWhenStaleEntryWouldBeRemoved()
        {
            // H-2 回帰テスト: ValidateAll の予測後集合と BuildPredictedSnapshot の afterMap は、
            // stale クリーンアップ（このランで削除される予定のエントリ）を同じように反映しなければならない。
            // asset A はマッチしてアドレス "SharedAddress" を得る。
            CreatePrefab(AssetPath1);

            // asset B（StaleAssetPath）は StubFolder の外にあるため ConstantAddressRule にマッチしない
            // （Skipped）。所有グループ（StubGroup）に "SharedAddress" のエントリを手動登録しておく。
            // CleanupStaleEntries（既定 ON）により、この実行で削除される予定のはず。
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
