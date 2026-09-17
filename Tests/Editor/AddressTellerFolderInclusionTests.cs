using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// フォルダ資産のオプトイン評価（<see cref="IAddressRuleGroupBuilder.IncludeFolders"/> /
    /// <see cref="ILabelRuleBuilder.IncludeFolders"/>）を、実際の <see cref="AssetDatabase"/> フォルダと
    /// <see cref="AddressTellerService"/> のルール注入オーバーロードを通して検証する。
    /// AssetFilter/RuleEvaluator 単体の純粋ロジックは AssetFilterTests / RuleEvaluatorTests 側でカバーする。
    /// </summary>
    public class AddressTellerFolderInclusionTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp";
        private const string StubFolder = TestRootFolder + "/FolderInclusion";
        private const string SubFolder = StubFolder + "/Sub";

        // settings.ConfigFolder は AssetFilter.ShouldExclude でこの配下のパスを評価対象から除外するために使われる。
        // TestRootFolder と同じ値にすると、ここで作成するテストアセット自身が除外されてしまうため、
        // 衝突しない別パス（実在しなくてよい）を割り当てる。
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempConfig";

        /// <summary>SubFolder 自身にマッチし、フォルダ名をアドレスとして "folder" ラベルを付与するルール（IncludeFolders() 宣言あり）。</summary>
        private sealed class FolderOptInRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(ctx => ctx.IsFolder && ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal))
                    .IncludeFolders()
                    .Address(ctx => ctx.FileName)
                    .Label("folder");
            }
        }

        /// <summary>StubFolder 配下を対象とする通常ルール。IncludeFolders() を宣言していない。</summary>
        private sealed class NoOptInRule : AddressRuleBase
        {
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
            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerFolderInclusionTestSettings");
            _stubGroup = _settings.CreateGroup("StubGroup", false, false, false, null);

            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            if (!AssetDatabase.IsValidFolder(StubFolder))
                AssetDatabase.CreateFolder(TestRootFolder, "FolderInclusion");
            if (!AssetDatabase.IsValidFolder(SubFolder))
                AssetDatabase.CreateFolder(StubFolder, "Sub");
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanupSetting;
            AssetDatabase.DeleteAsset(TestRootFolder);
        }

        private static string Guid(string path) => AssetDatabase.AssetPathToGUID(path);

        [Test]
        public void RealFolder_WithoutIncludeFolders_IsNeverMatched()
        {
            // Match.InFolder 相当の前方一致条件が、opt-in なしにサブフォルダへマッチしないことを
            // 実際に AssetDatabase.CreateFolder で作ったフォルダに対して確認する。
            var issues = AddressTellerService.ApplyAll(
                new[] { SubFolder }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new NoOptInRule() });

            Assert.AreEqual(0, issues.Count);
            Assert.IsNull(_settings.FindAssetEntry(Guid(SubFolder)));
        }

        [Test]
        public void RealFolder_WithIncludeFolders_IsMatchedAndEntryCreated()
        {
            // 実フォルダに対して RuleEvaluationPipeline.BuildContext が IsFolder == true を
            // 正しく構築できていることを、IncludeFolders() 宣言ルールの適用結果を通じて確認する。
            var issues = AddressTellerService.ApplyAll(
                new[] { SubFolder }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new FolderOptInRule() });

            Assert.AreEqual(0, issues.Count);
            var entry = _settings.FindAssetEntry(Guid(SubFolder));
            Assert.IsNotNull(entry);
            Assert.AreEqual("Sub", entry.address);
            Assert.IsTrue(entry.labels.Contains("folder"));
        }

        [Test]
        public void ManagedGroupFolderEntry_NoRuleMatches_IsRemovedByCleanup()
        {
            // 管理グループ内にフォルダエントリが既に存在する状態を作り、
            // どのルールにもマッチしなくなった場合に CleanupStaleEntries で削除されることを確認する。
            _settings.CreateOrMoveEntry(Guid(SubFolder), _stubGroup);
            Assert.IsNotNull(_settings.FindAssetEntry(Guid(SubFolder)));

            var issues = AddressTellerService.ApplyAll(
                new[] { SubFolder }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new NoOptInRule() });

            Assert.AreEqual(0, issues.Count);
            Assert.IsNull(_settings.FindAssetEntry(Guid(SubFolder)));
        }

        [Test]
        public void ManagedGroupFolderEntry_RuleStillMatches_IsNotRemoved()
        {
            _settings.CreateOrMoveEntry(Guid(SubFolder), _stubGroup);

            var issues = AddressTellerService.ApplyAll(
                new[] { SubFolder }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new FolderOptInRule() });

            Assert.AreEqual(0, issues.Count);
            Assert.IsNotNull(_settings.FindAssetEntry(Guid(SubFolder)));
        }

        [Test]
        public void Explain_RealFolder_WithIncludeFolders_ReportsMatchedRule()
        {
            var explanations = RuleExplainService.Explain(
                new[] { SubFolder }, _settings, new AddressRuleBase[] { new FolderOptInRule() });

            var explanation = explanations.SingleOrDefault(e => e.AssetPath == SubFolder);
            Assert.IsNotNull(explanation);
            Assert.IsFalse(explanation.IsExcluded);
            Assert.AreEqual(RuleMatchOutcome.Matched, explanation.Explanation.Details[0].Outcome);
        }

        [Test]
        public void Explain_RealFolder_WithoutIncludeFolders_ReportsSkippedRule()
        {
            var explanations = RuleExplainService.Explain(
                new[] { SubFolder }, _settings, new AddressRuleBase[] { new NoOptInRule() });

            var explanation = explanations.SingleOrDefault(e => e.AssetPath == SubFolder);
            Assert.IsNotNull(explanation);
            Assert.AreEqual(RuleMatchOutcome.Skipped, explanation.Explanation.Details[0].Outcome);
        }
    }
}
