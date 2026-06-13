using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSnapshotService.BuildPredictedSnapshot (dry-run) の統合テスト。
    /// ルール一覧を明示的に指定するオーバーロードに対し、このアセンブリ内で定義した
    /// スタブルール（<see cref="StubRule"/>）を渡すことで、dev プロジェクトの実ルール・実アセットへの
    /// 依存を排除する。テスト対象アセットは一時フォルダ Assets/_AddressTellerTestTemp 配下に作成する。
    /// </summary>
    public class AddressTellerSnapshotServiceBuildPredictedSnapshotTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp";
        private const string StubFolder = TestRootFolder + "/Stub";
        private const string StubAssetPath = StubFolder + "/StubAsset.prefab";
        private const string OtherAssetPath = TestRootFolder + "/OtherAsset.prefab";

        // settings.ConfigFolder は AssetFilter.ShouldExclude でこの配下のパスを評価対象から除外するために使われる。
        // TestRootFolder と同じ値にすると、ここで作成するテストアセット自身が除外されてしまうため、
        // 衝突しない別パス（実在しなくてよい）を割り当てる。
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempConfig";

        /// <summary>
        /// StubFolder 配下のアセットに、ファイル名をアドレスとして "alpha"+"beta" ラベルを
        /// "StubGroup" グループへ付与するテスト専用ルール。
        /// </summary>
        private sealed class StubRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension)
                    .Label("alpha")
                    .Label("beta");
            }
        }

        /// <summary>StubFolder 配下のアセットを、存在しない "MissingGroup" へ向けるテスト専用ルール。</summary>
        private sealed class MissingGroupRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("MissingGroup")
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _stubGroup;
        private bool _originalCleanupSetting;
        private bool _originalAutoCreateSetting;

        [SetUp]
        public void SetUp()
        {
            _originalCleanupSetting = AddressTellerSettings.CleanupStaleEntries;
            AddressTellerSettings.CleanupStaleEntries = true;

            _originalAutoCreateSetting = AddressTellerSettings.AutoCreateMissingGroups;

            // BuildPredictedSnapshot は settings.ConfigFolder を参照するため、
            // 永続化されていない settings（AssetPath 未確定）では例外になる。
            // isPersisted=true でディスク上に .asset を作成すると本番の Addressables 設定に副作用が
            // 残るため、非永続 settings に ConfigFolder のキャッシュのみを設定するヘルパーを使う。
            // ConfigFolder には FakeConfigFolder（TestRootFolder とは別パス）を渡す。
            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerBuildPredictedSnapshotTestSettings");
            _stubGroup = _settings.CreateGroup("StubGroup", false, false, false, null);

            // 上記ヘルパーは isPersisted=false のためディスク上にフォルダを作成しない。
            // CreatePrefab 等で TestRootFolder 配下にアセットを置くため、ここで親フォルダから順に作成する。
            if (!AssetDatabase.IsValidFolder("Assets/_AddressTellerTestTemp"))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            AssetDatabase.CreateFolder(TestRootFolder, "Stub");
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanupSetting;
            AddressTellerSettings.AutoCreateMissingGroups = _originalAutoCreateSetting;

            // SetUp で作成した一時アセット・フォルダはフォルダ削除でまとめて消す。
            AssetDatabase.DeleteAsset(TestRootFolder);
        }

        private static string Guid(string path) => AssetDatabase.AssetPathToGUID(path);

        /// <summary>指定パスに最小限の Prefab アセットを作成する。</summary>
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
        public void NewlyMatchedAsset_AppearsInDiffAdded()
        {
            CreatePrefab(StubAssetPath);

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath }, new AddressRuleBase[] { new StubRule() });

            var added = result.Diff.Added.SingleOrDefault(e => e.Guid == Guid(StubAssetPath));
            Assert.IsNotNull(added, "StubAsset.prefab はまだ StubGroup に存在しないため Diff.Added に現れるべき。");
            Assert.AreEqual("StubAsset", added.Address);
            Assert.AreEqual("StubGroup", added.GroupName);
            CollectionAssert.AreEqual(new[] { "alpha", "beta" }, added.Labels);
        }

        [Test]
        public void StaleManagedEntry_AppearsInDiffRemoved()
        {
            // StubFolder 外のアセットは StubRule のどの述語にもマッチしないが、
            // あらかじめ StubGroup（管理対象）に登録しておくことでクリーンアップ対象にする。
            CreatePrefab(OtherAssetPath);
            var otherGuid = Guid(OtherAssetPath);
            _settings.CreateOrMoveEntry(otherGuid, _stubGroup).SetAddress("StaleAddress");

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { OtherAssetPath }, new AddressRuleBase[] { new StubRule() });

            var removed = result.Diff.Removed.SingleOrDefault(e => e.Guid == otherGuid);
            Assert.IsNotNull(removed, "管理対象グループに残留したエントリは CleanupStaleEntries により Diff.Removed に現れるべき。");
        }

        [Test]
        public void AddressChangedAsset_AppearsInDiffChanged()
        {
            CreatePrefab(StubAssetPath);
            var stubGuid = Guid(StubAssetPath);
            _settings.CreateOrMoveEntry(stubGuid, _stubGroup).SetAddress("OldAddress");

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath }, new AddressRuleBase[] { new StubRule() });

            var changed = result.Diff.Changed.SingleOrDefault(c => c.After.Guid == stubGuid);
            Assert.IsNotNull(changed.After, "アドレスが変化する場合は Diff.Changed に現れるべき。");
            Assert.AreEqual("OldAddress", changed.Before.Address);
            Assert.AreEqual("StubAsset", changed.After.Address);
        }

        [Test]
        public void UnchangedAsset_DoesNotAppearInDiff()
        {
            CreatePrefab(StubAssetPath);
            var stubGuid = Guid(StubAssetPath);
            var entry = _settings.CreateOrMoveEntry(stubGuid, _stubGroup);
            entry.SetAddress("StubAsset");
            _settings.AddLabel("alpha");
            _settings.AddLabel("beta");
            entry.SetLabel("alpha", true);
            entry.SetLabel("beta", true);

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath }, new AddressRuleBase[] { new StubRule() });

            Assert.IsFalse(result.Diff.Added.Any(e => e.Guid == stubGuid));
            Assert.IsFalse(result.Diff.Removed.Any(e => e.Guid == stubGuid));
            Assert.IsFalse(result.Diff.Changed.Any(c => c.After.Guid == stubGuid));
        }

        [Test]
        public void GroupNotFound_AppearsInDryRunResultIssues()
        {
            // StubGroup を削除して「StubRule が参照するグループが存在しない」状態を作る。
            _settings.RemoveGroup(_stubGroup);
            _stubGroup = null;

            CreatePrefab(StubAssetPath);

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath }, new AddressRuleBase[] { new StubRule() });

            var issue = result.Issues.SingleOrDefault(i => i.Context.Guid == Guid(StubAssetPath));
            Assert.IsNotNull(issue, "StubGroup が存在しない場合、GroupNotFound が Issues に含まれるべき。");
            Assert.AreEqual(ValidationStatus.GroupNotFound, issue.Status);
        }

        [Test]
        public void AutoCreateOn_MissingGroup_GroupsToCreate_ContainsGroupName_WithoutSideEffect()
        {
            AddressTellerSettings.AutoCreateMissingGroups = true;

            CreatePrefab(StubAssetPath);

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath }, new AddressRuleBase[] { new MissingGroupRule() });

            CollectionAssert.AreEqual(new[] { "MissingGroup" }, result.GroupsToCreate);

            // dry-run は副作用ゼロ。MissingGroup は実際には作成されない。
            Assert.IsNull(_settings.FindGroup("MissingGroup"));

            // IsOk=true（情報提供）のため、issues には積まれない。
            Assert.IsFalse(result.Issues.Any(i => i.Context.Guid == Guid(StubAssetPath)));
        }

        [Test]
        public void AutoCreateOff_MissingGroup_GroupsToCreate_IsEmpty()
        {
            AddressTellerSettings.AutoCreateMissingGroups = false;

            CreatePrefab(StubAssetPath);

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath }, new AddressRuleBase[] { new MissingGroupRule() });

            CollectionAssert.IsEmpty(result.GroupsToCreate);

            var issue = result.Issues.SingleOrDefault(i => i.Context.Guid == Guid(StubAssetPath));
            Assert.IsNotNull(issue, "AutoCreate OFF の場合、GroupNotFound が Issues に含まれるべき。");
            Assert.AreEqual(ValidationStatus.GroupNotFound, issue.Status);
        }

        [Test]
        public void AutoCreateOn_MultipleAssetsTargetingSameMissingGroup_GroupsToCreate_IsDeduplicated()
        {
            AddressTellerSettings.AutoCreateMissingGroups = true;

            CreatePrefab(StubAssetPath);
            const string secondAssetPath = StubFolder + "/StubAsset2.prefab";
            CreatePrefab(secondAssetPath);

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(
                _settings, new[] { StubAssetPath, secondAssetPath }, new AddressRuleBase[] { new MissingGroupRule() });

            CollectionAssert.AreEqual(new[] { "MissingGroup" }, result.GroupsToCreate);
            Assert.IsNull(_settings.FindGroup("MissingGroup"));
        }
    }
}
