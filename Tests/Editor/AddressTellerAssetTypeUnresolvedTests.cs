using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AssetDatabase が型を解決できないアセット（<see cref="ValidationStatus.AssetTypeUnresolved"/>）の報告を検証する。
    /// 分岐そのものは AssetDatabase を差し替えて固定し、ApplyAll / ValidateAll / BuildPredictedSnapshot の3経路は
    /// 実アセット（存在しないスクリプトを参照する ScriptableObject）で確認する。この再現アセットで型が解決できない
    /// 状態にならなくなった Unity では、3経路のテストは失敗して気づけるようにしている。
    /// </summary>
    public class AddressTellerAssetTypeUnresolvedTests
    {
        private const string TempRoot = "Assets/_AddressTellerTestTemp";
        private const string TestRootFolder = TempRoot + "/TypeUnresolved";
        private const string MissingScriptAssetPath = TestRootFolder + "/MissingScript.asset";
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempTypeUnresolvedConfig";

        private sealed class FolderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(Match.InFolder(TestRootFolder)).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private AddressableAssetSettings _settings;
        private bool _originalCleanupSetting;

        [SetUp]
        public void SetUp()
        {
            _originalCleanupSetting = AddressTellerSettings.CleanupStaleEntries;
            AddressTellerSettings.CleanupStaleEntries = false;
            DeclaredFolderInspector.ResetCache();

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerAssetTypeUnresolvedTestSettings");
            _settings.CreateGroup("StubGroup", false, false, false, null);

            if (!AssetDatabase.IsValidFolder(TempRoot))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder(TempRoot, "TypeUnresolved");
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanupSetting;
            DeclaredFolderInspector.ResetCache();

            // 他のテストが同じ親を使うため親ごとは消さず、自分のサブフォルダだけを消す。
            // 親が空になっていれば親も消す（テスト後に Assets 直下へ残さない）。
            AssetDatabase.DeleteAsset(TestRootFolder);
            if (AssetDatabase.IsValidFolder(TempRoot)
                && AssetDatabase.FindAssets(string.Empty, new[] { TempRoot }).Length == 0)
                AssetDatabase.DeleteAsset(TempRoot);

            if (_settings != null)
            {
                foreach (var group in _settings.groups.Where(g => g != null).ToList())
                    UnityEngine.Object.DestroyImmediate(group, true);
                UnityEngine.Object.DestroyImmediate(_settings, true);
            }
        }

        // --- 分岐の単体テスト（AssetDatabase を差し替える） ---

        [Test]
        public void BuildContext_GuidEmpty_ReturnsNullWithoutNotice()
        {
            var ctx = RuleEvaluationPipeline.BuildContext(
                "Assets/NotAnAsset.txt", p => string.Empty, p => typeof(TextAsset), p => false, out var notice);

            Assert.IsNull(ctx);
            Assert.IsNull(notice, "GUID が空のパスはアセットではないので、通知を出してはならない。");
        }

        [Test]
        public void BuildContext_TypeNull_ReturnsNullWithAssetTypeUnresolvedNotice()
        {
            var ctx = RuleEvaluationPipeline.BuildContext(
                "Assets/Broken.asset", p => "guid-broken", p => null, p => false, out var notice);

            Assert.IsNull(ctx);
            Assert.IsNotNull(notice);
            Assert.AreEqual(ValidationStatus.AssetTypeUnresolved, notice.Status);
            Assert.IsTrue(notice.IsOk, "型未解決は書き込みを止めない通知であり、IsOk=true のはず。");
            Assert.IsFalse(notice.IsBlocking);
            Assert.IsNull(notice.Context);
            StringAssert.Contains("Assets/Broken.asset", notice.Message);
        }

        [Test]
        public void BuildContext_TypeResolved_ReturnsContextWithoutNotice()
        {
            var ctx = RuleEvaluationPipeline.BuildContext(
                "Assets/A.txt", p => "guid-a", p => typeof(TextAsset), p => false, out var notice);

            Assert.IsNotNull(ctx);
            Assert.AreEqual("guid-a", ctx.Guid);
            Assert.AreEqual(typeof(TextAsset), ctx.Type);
            Assert.IsNull(notice);
        }

        [Test]
        public void BuildContext_DefaultAsset_AsksIsValidFolder()
        {
            var ctx = RuleEvaluationPipeline.BuildContext(
                "Assets/Dir", p => "guid-dir", p => typeof(DefaultAsset), p => true, out var notice);

            Assert.IsTrue(ctx.IsFolder);
            Assert.IsNull(notice);
        }

        // --- 3経路（実アセット） ---

        [Test]
        public void ApplyAll_TypeUnresolvedAsset_ReportsNotice()
        {
            var path = CreateMissingScriptAsset();

            var issues = AddressTellerService.ApplyAll(new[] { path }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new FolderRule() });

            AssertSingleNoticeFor(issues, path);
        }

        [Test]
        public void ValidateAll_TypeUnresolvedAsset_ReportsNotice()
        {
            var path = CreateMissingScriptAsset();

            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, new AddressRuleBase[] { new FolderRule() });

            AssertSingleNoticeFor(issues, path);
        }

        [Test]
        public void BuildPredictedSnapshot_TypeUnresolvedAsset_ReportsNotice()
        {
            var path = CreateMissingScriptAsset();

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { path }, new AddressRuleBase[] { new FolderRule() });

            AssertSingleNoticeFor(result.Issues, path);
        }

        [Test]
        public void Explain_TypeUnresolvedAsset_ReturnsNoticeAsValidation()
        {
            var path = CreateMissingScriptAsset();

            var results = RuleExplainService.Explain(new[] { path }, _settings, new AddressRuleBase[] { new FolderRule() });

            Assert.AreEqual(1, results.Count);
            Assert.IsFalse(results[0].IsExcluded);
            Assert.IsNull(results[0].Explanation);
            Assert.AreEqual(ValidationStatus.AssetTypeUnresolved, results[0].Validation.Status);
            StringAssert.Contains(path, results[0].Validation.Message);
        }

        private static void AssertSingleNoticeFor(System.Collections.Generic.IEnumerable<ValidationResult> issues, string path)
        {
            // ValidateAll は全アセットを走査するので、プロジェクト内の他の型未解決アセットは対象外にする。
            var notices = issues.Where(i => i.Status == ValidationStatus.AssetTypeUnresolved && i.Message.Contains(path)).ToArray();
            Assert.AreEqual(1, notices.Length);
            Assert.IsTrue(notices[0].IsOk);
            StringAssert.Contains(path, notices[0].Message);
        }

        /// <summary>
        /// 存在しないスクリプトを参照する ScriptableObject を作り、BuildContext が型未解決として扱うことを確認する。
        /// 実アセットで型が解決できない状態にならない場合は失敗にする。
        /// </summary>
        private static string CreateMissingScriptAsset()
        {
            var yaml = string.Join("\n", new[]
            {
                "%YAML 1.1",
                "%TAG !u! tag:unity3d.com,2011:",
                "--- !u!114 &11400000",
                "MonoBehaviour:",
                "  m_ObjectHideFlags: 0",
                "  m_CorrespondingSourceObject: {fileID: 0}",
                "  m_PrefabInstance: {fileID: 0}",
                "  m_PrefabAsset: {fileID: 0}",
                "  m_GameObject: {fileID: 0}",
                "  m_Enabled: 1",
                "  m_EditorHideFlags: 0",
                "  m_Script: {fileID: 11500000, guid: 0123456789abcdef0123456789abcdef, type: 3}",
                "  m_Name: MissingScript",
                "  m_EditorClassIdentifier: ",
                string.Empty,
            });
            File.WriteAllText(MissingScriptAssetPath, yaml);
            AssetDatabase.ImportAsset(MissingScriptAssetPath, ImportAssetOptions.ForceSynchronousImport);

            RuleEvaluationPipeline.BuildContext(MissingScriptAssetPath, out var notice);
            // 再現しない場合は Inconclusive ではなく失敗にする（Inconclusive は green 判定をすり抜けるため）。
            Assert.IsNotNull(notice,
                "この Unity では、存在しないスクリプトを参照する ScriptableObject でも GetMainAssetTypeAtPath が型を解決してしまう。" +
                "型未解決の再現アセットを別の種類に差し替える必要がある。");

            return MissingScriptAssetPath;
        }
    }
}
