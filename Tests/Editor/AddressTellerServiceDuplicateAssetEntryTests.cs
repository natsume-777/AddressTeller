using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 同一 guid が2つ以上のグループにまたがって存在する状態で、ApplyAll / RemoveEntriesForDeletedAssets が
    /// 何も書き込まずに中止すること、Explain は止まらないことを検証する。
    /// この状態は公開 API では作れないため <see cref="DuplicateAssetEntryTestInjector"/> で直接注入する。
    /// </summary>
    public class AddressTellerServiceDuplicateAssetEntryTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp";
        private const string StubFolder = TestRootFolder + "/DuplicateAssetEntry";
        private const string StubAssetPath = StubFolder + "/StubAsset.prefab";
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempConfig";

        /// <summary>StubFolder 配下のアセットにファイル名をアドレスとして付与するテスト専用ルール。</summary>
        private sealed class StubRule : AddressRuleBase
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
        private AddressableAssetGroup _otherGroup;

        [SetUp]
        public void SetUp()
        {
            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerServiceDuplicateAssetEntryTestSettings");
            _stubGroup = _settings.CreateGroup("StubGroup", false, false, false, null);
            _otherGroup = _settings.CreateGroup("OtherGroup", false, false, false, null);

            // ターゲットアセットとは無関係な guid に対して重複状態を作る
            // （「重複があればこのランは一切何も評価・書き込みしない」ことを、ターゲットアセットへの影響で確認するため）。
            _settings.CreateOrMoveEntry("guid-dup", _stubGroup).SetAddress("AddressA");
            DuplicateAssetEntryTestInjector.InjectDuplicateEntry(_otherGroup, "guid-dup", "AddressB");

            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            AssetDatabase.CreateFolder(TestRootFolder, "DuplicateAssetEntry");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestRootFolder);

            Object.DestroyImmediate(_stubGroup, true);
            Object.DestroyImmediate(_otherGroup, true);
            Object.DestroyImmediate(_settings, true);
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

        [Test]
        public void ApplyAll_DuplicateAssetEntry_WritesNothingAndReturnsDuplicateOnly()
        {
            CreatePrefab(StubAssetPath);

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, new AddressRuleBase[] { new StubRule() });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ValidationStatus.DuplicateAssetEntry, issues[0].Status);
            Assert.IsNull(_settings.FindAssetEntry(Guid(StubAssetPath)),
                "重複が検出された実行では、他の無関係なアセットへの書き込みも一切行われてはいけない。");
        }

        [Test]
        public void RemoveEntriesForDeletedAssets_DuplicateAssetEntry_LogsErrorAndRemovesNothing()
        {
            const string staleGuid = "stale-guid-duplicate-entry-test";
            _settings.CreateOrMoveEntry(staleGuid, _stubGroup);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("guid-dup"));

            var cleared = AddressTellerService.RemoveEntriesForDeletedAssets(new[] { staleGuid }, _settings, new AddressRuleBase[] { new StubRule() });

            Assert.AreEqual(0, cleared.Count);
            Assert.IsNotNull(_settings.FindAssetEntry(staleGuid), "重複が検出された実行では、無関係な削除追従も一切行われてはいけない。");
        }

        [Test]
        public void Explain_DuplicateAssetEntry_DoesNotStop()
        {
            // Explain は読み取り専用の per-asset 診断ツールであり、重複検出の対象外
            // （ValidationStatus.DuplicateAssetEntry の XML doc 参照）。
            CreatePrefab(StubAssetPath);

            var explanations = RuleExplainService.Explain(new[] { StubAssetPath }, _settings, new AddressRuleBase[] { new StubRule() });

            var explanation = explanations.SingleOrDefault(e => e.AssetPath == StubAssetPath);
            Assert.IsNotNull(explanation);
            Assert.AreEqual(ValidationStatus.Ok, explanation.Validation.Status);
        }
    }
}
