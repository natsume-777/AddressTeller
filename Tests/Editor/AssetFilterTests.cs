using NUnit.Framework;
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
    }
}
