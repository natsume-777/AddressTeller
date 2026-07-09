using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerApplyFlow.ExecuteApply の分岐ロジック（AutoSnapshotBeforeApplyAll によるスナップショット
    /// 保存の有無、issues の有無によるログ出力）を検証する。
    /// Run() 自体は EditorUtility.DisplayDialogComplex によるモーダルダイアログ操作を挟むため、
    /// EditMode テストからは検証できない（このテストの対象外。詳細はクラスコメント末尾を参照）。
    /// ExecuteApply の rules 注入オーバーロード（internal、コードレビュー対応で追加）を使うことで、
    /// リフレクションによるルール収集（プロジェクト内の他の AddressRuleBase 実装、例えば
    /// dev/Assets/Editor/DemoRules.cs のようなワークスペースローカルなルール）に依存せず決定的に検証する。
    /// wasCancelled=true の分岐（EditorUtility.DisplayCancelableProgressBar への実際のキャンセル操作）は
    /// ユーザーの実インタラクションに依存するため自動テストでは再現できず、対象外とする。
    /// </summary>
    public class AddressTellerApplyFlowExecuteApplyTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp/ApplyFlow";
        private const string StubAssetPath = TestRootFolder + "/StubAsset.prefab";

        // settings.ConfigFolder は AssetFilter.ShouldExclude でこの配下のパスを評価対象から除外するために使われる。
        // TestRootFolder と同じ値にすると、ここで作成するテストアセット自身が除外されてしまうため、
        // 衝突しない別パス（実在しなくてよい）を割り当てる。
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempApplyFlowConfig";

        /// <summary>StubAssetPath にファイル名をアドレスとして "StubGroup" へ割り当てるテスト専用ルール。</summary>
        private sealed class StubRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(ctx => ctx.Path.StartsWith(TestRootFolder + "/", StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>存在しないグループを要求し、AutoCreateMissingGroups=false のもとで GroupNotFound issue を発生させるルール。</summary>
        private sealed class MissingGroupRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("MissingGroupForApplyFlowTest")
                    .Where(ctx => ctx.Path.StartsWith(TestRootFolder + "/", StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private AddressableAssetSettings _settings;

        private bool _originalAutoSnapshot;
        private string _originalSnapshotFolder;
        private bool _originalAutoCreateMissingGroups;
        private string _tempSnapshotRoot;

        [SetUp]
        public void SetUp()
        {
            _originalAutoSnapshot = AddressTellerSettings.AutoSnapshotBeforeApplyAll;
            _originalSnapshotFolder = AddressTellerSettings.SnapshotFolder;
            _originalAutoCreateMissingGroups = AddressTellerSettings.AutoCreateMissingGroups;
            AddressTellerSettings.AutoCreateMissingGroups = false;
            AddressTellerSettings.AutoSnapshotBeforeApplyAll = false;

            _tempSnapshotRoot = Path.Combine(Path.GetTempPath(), "AddressTellerApplyFlowTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempSnapshotRoot);
            // SnapshotFolder には絶対パスを直接渡せる
            // (GetSnapshotFolderAbsolutePath 内の Path.Combine は第二引数が絶対パスなら第一引数を無視する)。
            AddressTellerSettings.SnapshotFolder = _tempSnapshotRoot;

            // 非永続 settings に ConfigFolder のキャッシュのみを設定する（本番設定への副作用を避ける）。
            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerApplyFlowExecuteApplyTestSettings");
            _settings.CreateGroup("StubGroup", false, false, false, null);

            if (!AssetDatabase.IsValidFolder("Assets/_AddressTellerTestTemp"))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder("Assets/_AddressTellerTestTemp", "ApplyFlow");

            CreatePrefab(StubAssetPath);
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.AutoSnapshotBeforeApplyAll = _originalAutoSnapshot;
            AddressTellerSettings.SnapshotFolder = _originalSnapshotFolder;
            AddressTellerSettings.AutoCreateMissingGroups = _originalAutoCreateMissingGroups;

            AssetDatabase.DeleteAsset(TestRootFolder);

            DestroySettings(_settings);

            if (Directory.Exists(_tempSnapshotRoot))
                Directory.Delete(_tempSnapshotRoot, true);
        }

        /// <summary>テスト用の非永続 AddressableAssetSettings を、配下の全グループも含めて破棄する。</summary>
        private static void DestroySettings(AddressableAssetSettings settings)
        {
            if (settings == null) return;

            foreach (var group in settings.groups.Where(g => g != null).ToList())
                UnityEngine.Object.DestroyImmediate(group, true);

            UnityEngine.Object.DestroyImmediate(settings, true);
        }

        private static void CreatePrefab(string path)
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // System.Guid.NewGuid()（一時フォルダ名生成）と名前が衝突しないよう、
        // AddressTellerFlagConsistencyTests に倣い GuidOf という名前にする。
        private static string GuidOf(string path) => AssetDatabase.AssetPathToGUID(path);

        [Test]
        public void ExecuteApply_AutoSnapshotEnabled_CapturesSnapshotBeforeApply()
        {
            AddressTellerSettings.AutoSnapshotBeforeApplyAll = true;
            Assert.IsNull(AddressTellerAutoSnapshotService.FindLatestAuto(), "前提: 実行前にスナップショットは存在しない。");

            AddressTellerApplyFlow.ExecuteApply(_settings, new[] { StubAssetPath }, new AddressRuleBase[] { new StubRule() });

            Assert.IsNotNull(AddressTellerAutoSnapshotService.FindLatestAuto(),
                "AutoSnapshotBeforeApplyAll=true の場合、ExecuteApply は Apply の前にスナップショットを保存するべき。");
        }

        [Test]
        public void ExecuteApply_AutoSnapshotDisabled_DoesNotCaptureSnapshot()
        {
            AddressTellerSettings.AutoSnapshotBeforeApplyAll = false;

            AddressTellerApplyFlow.ExecuteApply(_settings, new[] { StubAssetPath }, new AddressRuleBase[] { new StubRule() });

            Assert.IsNull(AddressTellerAutoSnapshotService.FindLatestAuto(),
                "AutoSnapshotBeforeApplyAll=false の場合、スナップショットは保存されないべき。");
        }

        [Test]
        public void ExecuteApply_NoIssues_LogsCompletionAndAppliesRules()
        {
            LogAssert.Expect(LogType.Log, new Regex(Regex.Escape("[AddressTeller] ApplyAll completed.")));

            AddressTellerApplyFlow.ExecuteApply(_settings, new[] { StubAssetPath }, new AddressRuleBase[] { new StubRule() });

            var entry = _settings.FindAssetEntry(GuidOf(StubAssetPath));
            Assert.IsNotNull(entry, "issues が空の場合、ルールが実際に適用されているべき（Apply が実行されたことの確認）。");
            Assert.AreEqual("StubAsset", entry.address);
        }

        [Test]
        public void ExecuteApply_WithIssues_LogsEachIssueAndSummary()
        {
            // AutoCreateMissingGroups=false（SetUp）のため、存在しないグループを要求する
            // MissingGroupRule は GroupNotFound issue を1件発生させる。
            LogAssert.Expect(LogType.Error, new Regex("GroupNotFound"));
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[AddressTeller] ApplyAll completed with 1 issue(s).")));

            AddressTellerApplyFlow.ExecuteApply(_settings, new[] { StubAssetPath }, new AddressRuleBase[] { new MissingGroupRule() });

            Assert.IsNull(_settings.FindGroup("MissingGroupForApplyFlowTest"), "AutoCreateMissingGroups=false のためグループは作成されない。");
            Assert.IsNull(_settings.FindAssetEntry(GuidOf(StubAssetPath)), "issue が発生したアセットにはエントリが作成されない。");
        }

        [Test]
        public void ExecuteApply_EmptyRulesList_CompletesWithoutApplyingAnything()
        {
            LogAssert.Expect(LogType.Log, new Regex(Regex.Escape("[AddressTeller] ApplyAll completed.")));

            Assert.DoesNotThrow(() =>
                AddressTellerApplyFlow.ExecuteApply(_settings, new[] { StubAssetPath }, Array.Empty<AddressRuleBase>()));

            Assert.IsNull(_settings.FindAssetEntry(GuidOf(StubAssetPath)), "マッチするルールがなければエントリは作成されない。");
        }

        [Test]
        public void ExecuteApply_TwoArgOverload_UsedByRun_DoesNotThrow()
        {
            // Run() から呼ばれる2引数オーバーロード（rules=null）は、リフレクションによるルール収集
            // （RuleCollector.CollectEnabledRules()）を経由する。実在しないダミーパスを渡すことで、
            // プロジェクト内に実際に存在するルール（本ワークスペースの dev/Assets/Editor/DemoRules.cs 等）
            // が実行されても評価対象がなく副作用が発生しないようにしたうえで、経路自体が例外を投げない
            // ことのみをスモークテストとして確認する（AddressTellerServiceProgressTests と同じ考え方）。
            var dummyPath = TestRootFolder + "/NonExistent.prefab";

            Assert.DoesNotThrow(() =>
                AddressTellerApplyFlow.ExecuteApply(_settings, new[] { dummyPath }));
        }
    }
}
