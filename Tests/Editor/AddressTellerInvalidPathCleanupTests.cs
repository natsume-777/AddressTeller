using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Presets;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 管理対象グループ内の「パスが Addressables のエントリとして構造的に無効なエントリ」（旧バージョンの
    /// AddressTeller が作成した残骸等を想定）が、CleanupStaleEntries 有効時に AddressTellerService.ApplyAll /
    /// AddressTellerSnapshotService.BuildPredictedSnapshot の両方で一貫して掃除されることを検証する。
    ///
    /// 「無効」の再現には .preset アセット（AssetFilter.ExcludedExtensions に含まれる拡張子）を実際に
    /// プロジェクト内に作成して使う。GUID が実際に解決できる実アセットでありながら、パス自体は
    /// Addressables のエントリとして無効というケースを、本番のプロジェクト設定ファイルを一切汚さずに
    /// 再現できるため。実在しないアセットの GUID（AssetPath が空文字になるケース）は、このクリーンアップの
    /// 対象外であることを別途検証する。
    /// </summary>
    public class AddressTellerInvalidPathCleanupTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp";
        private const string StubFolder = TestRootFolder + "/InvalidPathCleanup";
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempInvalidPathConfig";

        // StubFolder 配下には基本的に実アセットを置かないが、rule が実際にマッチするかどうかは
        // この掃除自体の対象判定には関係ない。ルールが Group(...) を宣言していること自体で
        // managedGroups に含まれることだけが重要。
        private sealed class ManagedGroupRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("ManagedGroup")
                    .Where(ctx => false)
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _managedGroup;
        private AddressableAssetGroup _otherGroup;
        private bool _originalCleanupSetting;

        [SetUp]
        public void SetUp()
        {
            _originalCleanupSetting = AddressTellerSettings.CleanupStaleEntries;

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerInvalidPathCleanupTestSettings");
            _managedGroup = _settings.CreateGroup("ManagedGroup", false, false, false, null);
            _otherGroup = _settings.CreateGroup("OtherGroup", false, false, false, null);

            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            if (!AssetDatabase.IsValidFolder(StubFolder))
                AssetDatabase.CreateFolder(TestRootFolder, "InvalidPathCleanup");
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanupSetting;

            if (AssetDatabase.IsValidFolder(StubFolder))
                AssetDatabase.DeleteAsset(StubFolder);
        }

        /// <summary>CreateInvalidExtensionAsset 専用の Preset ターゲット。ScriptableObject はどんな環境
        /// （レンダーパイプライン差異等）でも確実に Preset 化できるため、これ専用の空の型を用意している。</summary>
        private sealed class PresetProbeAsset : ScriptableObject
        {
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

        /// <summary>
        /// 実際にプロジェクト内へ .preset アセットを作成する。GUID は実際に解決できるが、拡張子が
        /// AssetFilter.ExcludedExtensions に含まれるため、パスとしては Addressables エントリとして無効になる
        /// （「GUID は引けるが構造的に無効」を、本番のプロジェクト設定を汚さずに再現するためのヘルパー）。
        /// </summary>
        private static void CreateInvalidExtensionAsset(string path)
        {
            var probe = ScriptableObject.CreateInstance<PresetProbeAsset>();
            try
            {
                var preset = new Preset(probe);
                AssetDatabase.CreateAsset(preset, path);
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        [Test]
        public void ApplyAll_ValidPathEntryInManagedGroup_IsNotRemoved()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var validAssetPath = StubFolder + "/ValidAsset.prefab";
            CreatePrefab(validAssetPath);
            _settings.CreateOrMoveEntry(Guid(validAssetPath), _managedGroup).SetAddress("ValidAddress");
            var rules = new AddressRuleBase[] { new ManagedGroupRule() };

            AddressTellerService.ApplyAll(System.Array.Empty<string>(), _settings, NullProgressReporter.Instance, rules);

            Assert.IsNotNull(_settings.FindAssetEntry(Guid(validAssetPath)),
                "An entry whose path is a normal, valid Addressables path must never be removed by this cleanup.");
        }

        [Test]
        public void ApplyAll_UnresolvableGuidEntryInManagedGroup_IsNotRemoved()
        {
            // "guid-unresolvable" はプロジェクト内のどのアセットにも対応しないため AssetPath が空文字になる。
            // LFS未取得・ブランチ切替中・パッケージ未導入等、資産が一時的に解決できないだけの状況を
            // 誤って「パス無効」として削除してしまわないことを確認する。
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-unresolvable", _managedGroup).SetAddress("StaleAddress");
            var rules = new AddressRuleBase[] { new ManagedGroupRule() };

            AddressTellerService.ApplyAll(System.Array.Empty<string>(), _settings, NullProgressReporter.Instance, rules);

            Assert.IsNotNull(_settings.FindAssetEntry("guid-unresolvable"),
                "An entry whose GUID cannot currently be resolved to a path must be left alone by this cleanup " +
                "(deletion follow-up for genuinely deleted assets is a separate code path).");
        }

        [Test]
        public void ApplyAll_StructurallyInvalidPathEntryInManagedGroup_CleanupOn_IsRemoved()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var invalidAssetPath = StubFolder + "/Invalid.preset";
            CreateInvalidExtensionAsset(invalidAssetPath);
            var guid = Guid(invalidAssetPath);
            _settings.CreateOrMoveEntry(guid, _managedGroup).SetAddress("StaleAddress");
            var rules = new AddressRuleBase[] { new ManagedGroupRule() };

            AddressTellerService.ApplyAll(System.Array.Empty<string>(), _settings, NullProgressReporter.Instance, rules);

            Assert.IsNull(_settings.FindAssetEntry(guid),
                "An entry whose path resolves but is structurally invalid for an Addressables entry (excluded extension) should be removed from a managed group.");
        }

        [Test]
        public void ApplyAll_ConfigFolderAdjacentEntryInManagedGroup_CleanupOn_IsRemoved()
        {
            // AssetFilter.IsPathValidForAddressablesEntry は ConfigFolder を境界なしの前方一致で判定する
            // （Addressables 本体に揃えた挙動）。そのため、ConfigFolder と名前の文字列が前方一致するだけで
            // 実際にはその配下ではない隣接フォルダ（例: FakeConfigFolder + "Backup"）も無効パス扱いになる。
            AddressTellerSettings.CleanupStaleEntries = true;
            const string adjacentFolder = FakeConfigFolder + "Backup";
            if (!AssetDatabase.IsValidFolder(adjacentFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTempInvalidPathConfigBackup");

            try
            {
                var assetPath = adjacentFolder + "/Adjacent.prefab";
                CreatePrefab(assetPath);
                var guid = Guid(assetPath);
                _settings.CreateOrMoveEntry(guid, _managedGroup).SetAddress("StaleAddress");
                var rules = new AddressRuleBase[] { new ManagedGroupRule() };

                AddressTellerService.ApplyAll(System.Array.Empty<string>(), _settings, NullProgressReporter.Instance, rules);

                Assert.IsNull(_settings.FindAssetEntry(guid),
                    "An entry under a folder that merely shares a name prefix with ConfigFolder (not its actual contents) " +
                    "should be treated as invalid and removed, matching Addressables' own boundary-less ConfigFolder exclusion.");
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(adjacentFolder))
                    AssetDatabase.DeleteAsset(adjacentFolder);
            }
        }

        [Test]
        public void BuildPredictedSnapshot_ConfigFolderAdjacentEntryInManagedGroup_AppearsInDiffRemoved()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            const string adjacentFolder = FakeConfigFolder + "Backup";
            if (!AssetDatabase.IsValidFolder(adjacentFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTempInvalidPathConfigBackup");

            try
            {
                var assetPath = adjacentFolder + "/AdjacentDryRun.prefab";
                CreatePrefab(assetPath);
                var guid = Guid(assetPath);
                _settings.CreateOrMoveEntry(guid, _managedGroup).SetAddress("StaleAddress");
                var rules = new AddressRuleBase[] { new ManagedGroupRule() };

                var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, System.Array.Empty<string>(), rules);

                var removed = result.Diff.Removed.SingleOrDefault(e => e.Guid == guid);
                Assert.IsNotNull(removed, "The dry-run prediction must match what ApplyAll would actually remove for a ConfigFolder-adjacent path.");
                // dry-run は副作用ゼロ。
                Assert.IsNotNull(_settings.FindAssetEntry(guid));
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(adjacentFolder))
                    AssetDatabase.DeleteAsset(adjacentFolder);
            }
        }

        [Test]
        public void ApplyAll_StructurallyInvalidPathEntryInUnmanagedGroup_CleanupOn_IsKept()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var invalidAssetPath = StubFolder + "/InvalidUnmanaged.preset";
            CreateInvalidExtensionAsset(invalidAssetPath);
            var guid = Guid(invalidAssetPath);
            // OtherGroup はどのルールからも参照されていないため managedGroups には含まれない。
            _settings.CreateOrMoveEntry(guid, _otherGroup).SetAddress("StaleAddress");
            var rules = new AddressRuleBase[] { new ManagedGroupRule() };

            AddressTellerService.ApplyAll(System.Array.Empty<string>(), _settings, NullProgressReporter.Instance, rules);

            Assert.IsNotNull(_settings.FindAssetEntry(guid),
                "Entries in a group AddressTeller does not manage must never be touched.");
        }

        [Test]
        public void ApplyAll_StructurallyInvalidPathEntryInManagedGroup_CleanupOff_IsKept()
        {
            AddressTellerSettings.CleanupStaleEntries = false;
            var invalidAssetPath = StubFolder + "/InvalidCleanupOff.preset";
            CreateInvalidExtensionAsset(invalidAssetPath);
            var guid = Guid(invalidAssetPath);
            _settings.CreateOrMoveEntry(guid, _managedGroup).SetAddress("StaleAddress");
            var rules = new AddressRuleBase[] { new ManagedGroupRule() };

            AddressTellerService.ApplyAll(System.Array.Empty<string>(), _settings, NullProgressReporter.Instance, rules);

            Assert.IsNotNull(_settings.FindAssetEntry(guid),
                "CleanupStaleEntries=false must keep the entry, matching the existing 'no rule matches' cleanup behavior.");
        }

        [Test]
        public void BuildPredictedSnapshot_StructurallyInvalidPathEntryInManagedGroup_AppearsInDiffRemoved()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var invalidAssetPath = StubFolder + "/InvalidDryRun.preset";
            CreateInvalidExtensionAsset(invalidAssetPath);
            var guid = Guid(invalidAssetPath);
            _settings.CreateOrMoveEntry(guid, _managedGroup).SetAddress("StaleAddress");
            var rules = new AddressRuleBase[] { new ManagedGroupRule() };

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, System.Array.Empty<string>(), rules);

            var removed = result.Diff.Removed.SingleOrDefault(e => e.Guid == guid);
            Assert.IsNotNull(removed, "The dry-run prediction must match what ApplyAll would actually remove.");
            // dry-run は副作用ゼロ。
            Assert.IsNotNull(_settings.FindAssetEntry(guid));
        }

        [Test]
        public void BuildPredictedSnapshot_ValidPathEntryInManagedGroup_DoesNotAppearInDiff()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var validAssetPath = StubFolder + "/ValidDryRun.prefab";
            CreatePrefab(validAssetPath);
            var guid = Guid(validAssetPath);
            _settings.CreateOrMoveEntry(guid, _managedGroup).SetAddress("ValidAddress");
            var rules = new AddressRuleBase[] { new ManagedGroupRule() };

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, System.Array.Empty<string>(), rules);

            Assert.IsFalse(result.Diff.Removed.Any(e => e.Guid == guid));
        }

        [Test]
        public void BuildPredictedSnapshot_UnresolvableGuidEntryInManagedGroup_DoesNotAppearInDiff()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-unresolvable", _managedGroup).SetAddress("StaleAddress");
            var rules = new AddressRuleBase[] { new ManagedGroupRule() };

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, System.Array.Empty<string>(), rules);

            Assert.IsFalse(result.Diff.Removed.Any(e => e.Guid == "guid-unresolvable"));
        }

        [Test]
        public void BuildPredictedSnapshot_StructurallyInvalidPathEntryInUnmanagedGroup_DoesNotAppearInDiff()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var invalidAssetPath = StubFolder + "/InvalidUnmanagedDryRun.preset";
            CreateInvalidExtensionAsset(invalidAssetPath);
            var guid = Guid(invalidAssetPath);
            _settings.CreateOrMoveEntry(guid, _otherGroup).SetAddress("StaleAddress");
            var rules = new AddressRuleBase[] { new ManagedGroupRule() };

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, System.Array.Empty<string>(), rules);

            Assert.IsFalse(result.Diff.Removed.Any(e => e.Guid == guid));
        }

        [Test]
        public void BuildPredictedSnapshot_CleanupOff_StructurallyInvalidPathEntryDoesNotAppearInDiff()
        {
            AddressTellerSettings.CleanupStaleEntries = false;
            var invalidAssetPath = StubFolder + "/InvalidCleanupOffDryRun.preset";
            CreateInvalidExtensionAsset(invalidAssetPath);
            var guid = Guid(invalidAssetPath);
            _settings.CreateOrMoveEntry(guid, _managedGroup).SetAddress("StaleAddress");
            var rules = new AddressRuleBase[] { new ManagedGroupRule() };

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, System.Array.Empty<string>(), rules);

            Assert.IsFalse(result.Diff.Removed.Any(e => e.Guid == guid));
        }
    }
}
