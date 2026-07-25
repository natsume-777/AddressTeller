using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// BundleModeReader.ReadBundleModes の単体テスト。
    /// 実際の AddressableAssetGroup（非永続）を組み立てて検証する。
    /// </summary>
    public class BundleModeReaderTests
    {
        private AddressableAssetSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = AddressTellerTestSettingsFactory.CreateInMemory("Assets/_AddressTellerTestTemp", "BundleModeReaderTestSettings");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var group in _settings.groups.Where(g => g != null).ToList())
                UnityEngine.Object.DestroyImmediate(group, true);
            UnityEngine.Object.DestroyImmediate(_settings, true);
        }

        [Test]
        public void ReadBundleModes_DuplicateGroupName_ReturnsWarningAndUsesFirstGroup_WithoutThrowing()
        {
            // Addressables はグループ名の一意性を保証しないため、groups.ToDictionary(g => g.Name, ...)
            // は重複時に例外を投げてしまう。重複があっても処理を継続し、警告は戻り値（warnings）として
            // 返されること（ログへ直書きしないこと）を検証する。
            var groupA = _settings.CreateGroup("Duplicate_TemporaryNameA", false, false, false, null);
            var groupB = _settings.CreateGroup("Duplicate_TemporaryNameB", false, false, false, null);
            groupA.Name = "Duplicate";
            groupB.Name = "Duplicate";

            IReadOnlyDictionary<string, BundleModeKind> result = null;
            IReadOnlyList<string> warnings = null;
            Assert.DoesNotThrow(() => result = BundleModeReader.ReadBundleModes(new[] { groupA, groupB }, out warnings));

            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(result.ContainsKey("Duplicate"));
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("Duplicate", warnings[0]);
        }

        [Test]
        public void ReadBundleModes_ThreeGroupsSharingName_ReturnsSingleWarning()
        {
            // 同一グループ名が3件以上重複していても、警告はグループ名につき1件にまとめられるべき。
            var groupA = _settings.CreateGroup("Triple_TemporaryNameA", false, false, false, null);
            var groupB = _settings.CreateGroup("Triple_TemporaryNameB", false, false, false, null);
            var groupC = _settings.CreateGroup("Triple_TemporaryNameC", false, false, false, null);
            groupA.Name = "Triple";
            groupB.Name = "Triple";
            groupC.Name = "Triple";

            BundleModeReader.ReadBundleModes(new[] { groupA, groupB, groupC }, out var warnings);

            Assert.AreEqual(1, warnings.Count);
        }

        [Test]
        public void ReadBundleModes_NullGroup_IsSkipped()
        {
            var group = _settings.CreateGroup("SingleGroup", false, false, false, null);

            var result = BundleModeReader.ReadBundleModes(new[] { group, null }, out var warnings);

            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(result.ContainsKey("SingleGroup"));
            Assert.AreEqual(0, warnings.Count);
        }
    }
}
