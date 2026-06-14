using NUnit.Framework;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    public class NamingTests
    {
        private static AssetContext MakeCtx(string path) =>
            new AssetContext("guid1", path, typeof(GameObject));

        [Test]
        public void FileName_ReturnsFileNameWithExtension()
        {
            var ctx = MakeCtx("Assets/Game/Characters/Player.prefab");

            Assert.AreEqual("Player.prefab", Naming.FileName()(ctx));
        }

        [Test]
        public void FileName_NoExtension_ReturnsFileNameAsIs()
        {
            var ctx = MakeCtx("Assets/Game/Player");

            Assert.AreEqual("Player", Naming.FileName()(ctx));
        }

        [Test]
        public void FileNameWithoutExtension_ReturnsNameWithoutExtension()
        {
            var ctx = MakeCtx("Assets/Game/Characters/Player.prefab");

            Assert.AreEqual("Player", Naming.FileNameWithoutExtension()(ctx));
        }

        [Test]
        public void FileNameWithoutExtension_NoExtension_ReturnsFileNameAsIs()
        {
            var ctx = MakeCtx("Assets/Game/Player");

            Assert.AreEqual("Player", Naming.FileNameWithoutExtension()(ctx));
        }

        [Test]
        public void ParentFolderName_NestedPath_ReturnsImmediateParentFolderName()
        {
            var ctx = MakeCtx("Assets/Game/Characters/Player.prefab");

            Assert.AreEqual("Characters", Naming.ParentFolderName()(ctx));
        }

        [Test]
        public void ParentFolderName_AssetDirectlyUnderAssets_ReturnsAssets()
        {
            var ctx = MakeCtx("Assets/Player.prefab");

            Assert.AreEqual("Assets", Naming.ParentFolderName()(ctx));
        }

        [Test]
        public void RelativePath_AssetInSubfolder_DelegatesToRelativePathFrom()
        {
            var ctx = MakeCtx("Assets/Game/Sub/Player.prefab");

            Assert.AreEqual(ctx.RelativePathFrom("Assets/Game"), Naming.RelativePath("Assets/Game")(ctx));
            Assert.AreEqual("Sub/Player.prefab", Naming.RelativePath("Assets/Game")(ctx));
        }

        [Test]
        public void RelativePath_AssetNotInFolder_ReturnsOriginalPath()
        {
            var ctx = MakeCtx("Assets/Other/Player.prefab");

            Assert.AreEqual("Assets/Other/Player.prefab", Naming.RelativePath("Assets/Game")(ctx));
        }

        [Test]
        public void Address_AcceptsNamingFileNameWithoutExtension()
        {
            var builder = new AddressRuleBuilderImpl();
            builder.Group("G")
                .Where(ctx => true)
                .Address(Naming.FileNameWithoutExtension());

            var entry = builder.Entries[0];
            var ctx = MakeCtx("Assets/Game/Player.prefab");
            Assert.AreEqual("Player", entry.AddressSelector(ctx));
        }
    }
}
