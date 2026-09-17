using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// RuleEvaluationPipeline.BuildSetup が settings.ConfigFolder をフォワードスラッシュへ正規化することを
    /// 検証する。settings.ConfigFolder は Windows ではバックスラッシュ区切り（例: "Assets\AddressableAssetsData"）
    /// で返ることがあり、正規化しないと AssetFilter の除外判定・無効パスエントリの掃除の双方で
    /// ConfigFolder 配下のパスがフォワードスラッシュのアセットパスと一致しなくなる。
    /// </summary>
    public class RuleEvaluationPipelineConfigFolderTests
    {
        private const string BackslashConfigFolder = @"Assets\AddressableAssetsData";
        private const string ForwardSlashConfigFolder = "Assets/AddressableAssetsData";

        private AddressableAssetSettings _settings;

        [TearDown]
        public void TearDown()
        {
            if (_settings != null)
                UnityEngine.Object.DestroyImmediate(_settings, true);
        }

        [Test]
        public void BuildSetup_BackslashConfigFolder_IsNormalizedToForwardSlash()
        {
            _settings = AddressTellerTestSettingsFactory.CreateInMemory(BackslashConfigFolder, "RuleEvaluationPipelineConfigFolderTestSettings");

            var setup = RuleEvaluationPipeline.BuildSetup(_settings, System.Array.Empty<AddressRuleBase>());

            Assert.AreEqual(ForwardSlashConfigFolder, setup.ConfigFolder);
        }

        [Test]
        public void BuildSetup_BackslashConfigFolder_ExcludesConfigFolderAssetsViaAssetFilter()
        {
            // BuildSetup が正規化を怠ると、ConfigFolder 配下のフォワードスラッシュ区切りのアセットパスが
            // AssetFilter.ShouldExcludeByPath の前方一致判定にかからず、除外されなくなってしまう。
            _settings = AddressTellerTestSettingsFactory.CreateInMemory(BackslashConfigFolder, "RuleEvaluationPipelineConfigFolderTestSettings2");

            var setup = RuleEvaluationPipeline.BuildSetup(_settings, System.Array.Empty<AddressRuleBase>());

            Assert.IsTrue(AssetFilter.ShouldExcludeByPath(
                ForwardSlashConfigFolder + "/AddressableAssetSettings.asset", setup.ConfigFolder));
        }

        /// <summary>ManagedGroups に "ManagedGroup" を含めるためだけの最小のルール。Where は常に false。</summary>
        private sealed class DummyManagedGroupRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("ManagedGroup").Where(ctx => false).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        [Test]
        public void BuildSetup_BackslashConfigFolder_InvalidPathSweepRemovesConfigFolderEntry()
        {
            // AddressTellerApplier.FindInvalidPathManagedEntries（無効パスエントリの掃除）が
            // BuildSetup の正規化済み ConfigFolder を正しく使って、ConfigFolder 配下の実アセットの
            // エントリを検出できることを確認する（掃除経路での正規化の効果を end-to-end で見るテスト）。
            const string fakeConfigFolderForward = "Assets/_AddressTellerTestTempCfgSweep";
            var fakeConfigFolderBackslash = fakeConfigFolderForward.Replace('/', '\\');

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(fakeConfigFolderBackslash, "RuleEvaluationPipelineConfigFolderTestSettings3");
            var group = _settings.CreateGroup("ManagedGroup", false, false, false, null);

            if (!AssetDatabase.IsValidFolder(fakeConfigFolderForward))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTempCfgSweep");

            try
            {
                var assetPath = fakeConfigFolderForward + "/Probe.prefab";
                var go = new GameObject("Probe");
                try
                {
                    PrefabUtility.SaveAsPrefabAsset(go, assetPath);
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }

                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                _settings.CreateOrMoveEntry(guid, group);

                var setup = RuleEvaluationPipeline.BuildSetup(_settings, new AddressRuleBase[] { new DummyManagedGroupRule() });
                var invalidEntries = AddressTellerApplier.FindInvalidPathManagedEntries(_settings, setup.ManagedGroups, setup.ConfigFolder);

                Assert.IsTrue(invalidEntries.Any(e => e.guid == guid),
                    "An entry under the (backslash-declared) ConfigFolder should be detected as invalid once the ConfigFolder is normalized.");
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(fakeConfigFolderForward))
                    AssetDatabase.DeleteAsset(fakeConfigFolderForward);
            }
        }
    }
}
