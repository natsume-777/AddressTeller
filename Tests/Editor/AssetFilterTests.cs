using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor.Tests
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
