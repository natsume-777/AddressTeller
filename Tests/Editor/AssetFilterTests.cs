using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    public class AssetFilterTests
    {
        private static AssetContext Ctx(string path) =>
            new AssetContext("guid1", path, typeof(GameObject));

        [TestCase("Assets/Game/Player.prefab", false)]
        [TestCase("Assets/Game/Player.cs", true)]
        [TestCase("Assets/Game/lib.dll", true)]
        [TestCase("Assets/Game/Player.prefab.meta", true)]
        public void ExcludedExtension(string path, bool expected)
        {
            Assert.AreEqual(expected, AssetFilter.ShouldExclude(Ctx(path)));
        }

        [Test]
        public void EditorFolder_IsExcluded()
        {
            Assert.IsTrue(AssetFilter.ShouldExclude(Ctx("Assets/Game/Editor/Foo.asset")));
        }

        [Test]
        public void NormalAsset_IsNotExcluded()
        {
            Assert.IsFalse(AssetFilter.ShouldExclude(Ctx("Assets/Game/Foo.prefab")));
        }

        [Test]
        public void AddressablesConfigFolder_IsExcluded()
        {
            Assert.IsTrue(AssetFilter.ShouldExclude(
                Ctx("Assets/AddressableAssetsData/Settings.asset"),
                "Assets/AddressableAssetsData"));
        }

        [Test]
        public void AddressablesConfigFolder_NullMeansNoCheck()
        {
            Assert.IsFalse(AssetFilter.ShouldExclude(
                Ctx("Assets/AddressableAssetsData/Settings.asset"),
                null));
        }

        [Test]
        public void AddressablesConfigFolder_AdjacentFolderWithSamePrefix_IsNotExcluded()
        {
            // "/" 境界を付けずに StartsWith するだけだと、configFolder と前方一致するだけの
            // 別フォルダ（例: AddressableAssetsData_Backup）まで誤って除外してしまう。
            Assert.IsFalse(AssetFilter.ShouldExclude(
                Ctx("Assets/AddressableAssetsData_Backup/Hero.prefab"),
                "Assets/AddressableAssetsData"));
        }

        // ShouldExcludeByPath は AssetContext 構築前にパス文字列のみで判定する早期除外用。
        // ShouldExclude(context, ...) のパス部分の判定結果と一致することを確認する。

        [TestCase("Assets/Game/Player.prefab", false)]
        [TestCase("Assets/Game/Player.cs", true)]
        [TestCase("Assets/Game/lib.dll", true)]
        [TestCase("Assets/Game/Player.prefab.meta", true)]
        public void ShouldExcludeByPath_ExcludedExtension_MatchesShouldExclude(string path, bool expected)
        {
            Assert.AreEqual(expected, AssetFilter.ShouldExcludeByPath(path));
            Assert.AreEqual(expected, AssetFilter.ShouldExclude(Ctx(path)));
        }

        [Test]
        public void ShouldExcludeByPath_EditorFolder_IsExcluded()
        {
            Assert.IsTrue(AssetFilter.ShouldExcludeByPath("Assets/Game/Editor/Foo.asset"));
        }

        [Test]
        public void ShouldExcludeByPath_NormalAsset_IsNotExcluded()
        {
            Assert.IsFalse(AssetFilter.ShouldExcludeByPath("Assets/Game/Foo.prefab"));
        }

        [Test]
        public void ShouldExcludeByPath_AddressablesConfigFolder_IsExcluded()
        {
            Assert.IsTrue(AssetFilter.ShouldExcludeByPath(
                "Assets/AddressableAssetsData/Settings.asset",
                "Assets/AddressableAssetsData"));
        }

        [Test]
        public void ShouldExcludeByPath_AddressablesConfigFolder_NullMeansNoCheck()
        {
            Assert.IsFalse(AssetFilter.ShouldExcludeByPath(
                "Assets/AddressableAssetsData/Settings.asset",
                null));
        }

        [Test]
        public void ShouldExcludeByPath_AdjacentFolderWithSamePrefix_IsNotExcluded()
        {
            Assert.IsFalse(AssetFilter.ShouldExcludeByPath(
                "Assets/AddressableAssetsData_Backup/Hero.prefab",
                "Assets/AddressableAssetsData"));
        }

        [TestCase(@"Assets\Game\Player.cs")]
        [TestCase(@"Assets\Game\Editor\Foo.asset")]
        [TestCase(@"Assets\AddressableAssetsData\Settings.asset")]
        [TestCase(@"Assets\Game\Player.prefab")]
        public void ShouldExcludeByPath_BackslashPath_MatchesForwardSlashEquivalent(string backslashPath)
        {
            var forwardPath = backslashPath.Replace('\\', '/');

            Assert.AreEqual(
                AssetFilter.ShouldExcludeByPath(forwardPath, "Assets/AddressableAssetsData"),
                AssetFilter.ShouldExcludeByPath(backslashPath, "Assets/AddressableAssetsData"));

            Assert.AreEqual(
                AssetFilter.ShouldExclude(Ctx(forwardPath), "Assets/AddressableAssetsData"),
                AssetFilter.ShouldExcludeByPath(backslashPath, "Assets/AddressableAssetsData"));
        }

        [Test]
        public void ShouldExcludeByPath_NullPath_ReturnsTrueWithoutThrowing()
        {
            // path.Replace('\\','/') は path が null だと NRE になるため、安全側（除外扱い）にフォールバックする。
            Assert.DoesNotThrow(() => AssetFilter.ShouldExcludeByPath(null));
            Assert.IsTrue(AssetFilter.ShouldExcludeByPath(null));
            Assert.IsTrue(AssetFilter.ShouldExcludeByPath(null, "Assets/AddressableAssetsData"));
        }

        [Test]
        public void Folder_IsExcluded()
        {
            // フォルダ資産は拡張子を持たないため ShouldExcludeByPath では弾けない。
            // Addressable 化するとフォルダエントリになり配下を二重管理するため、型ではなく
            // IsFolder フラグで除外する。
            var ctx = new AssetContext("guid1", "Assets/Game/Characters", typeof(DefaultAsset), isFolder: true);

            Assert.IsFalse(AssetFilter.ShouldExcludeByPath(ctx.Path));
            Assert.IsTrue(AssetFilter.ShouldExclude(ctx));
        }

        [Test]
        public void ExtensionlessFile_IsNotExcluded()
        {
            // 拡張子がないだけのファイル（LICENSE など）は除外しない。除外判定はあくまで
            // IsFolder に基づくもので、拡張子の有無で代用していないことを確認する。
            var ctx = new AssetContext("guid1", "Assets/Game/LICENSE", typeof(DefaultAsset), isFolder: false);

            Assert.IsFalse(AssetFilter.ShouldExclude(ctx));
        }

        [Test]
        public void ShouldExclude_TypeOnlyExclusion_NotCaughtByPathCheck()
        {
            // AddressableAssetSettings 型は ExcludedAddressablesTypes による除外であり、
            // パス自体は通常のアセットパスなので ShouldExcludeByPath では除外されない。
            var ctx = new AssetContext("guid1", "Assets/AddressableAssetsData/AddressableAssetSettings.asset", typeof(AddressableAssetSettings));

            Assert.IsFalse(AssetFilter.ShouldExcludeByPath(ctx.Path));
            Assert.IsTrue(AssetFilter.ShouldExclude(ctx));
        }
    }
}
