using NUnit.Framework;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor.Tests
{
    public class AssetConditionTests
    {
        private static AssetContext MakeCtx(string path) =>
            new AssetContext("guid1", path, typeof(GameObject));

        [Test]
        public void And_AssetCondition_CombinesPredicatesWithShortCircuitAnd()
        {
            var a = new AssetCondition(ctx => ctx.Path.StartsWith("Assets/A/"), "InFolderA");
            var b = new AssetCondition(ctx => ctx.Extension == ".prefab", "IsPrefab");

            var combined = a.And(b);

            Assert.IsTrue(combined.Test(MakeCtx("Assets/A/Foo.prefab")));
            Assert.IsFalse(combined.Test(MakeCtx("Assets/A/Foo.png")));
            Assert.IsFalse(combined.Test(MakeCtx("Assets/B/Foo.prefab")));
        }

        [Test]
        public void And_AssetCondition_BothDescriptionsPresent_Concatenates()
        {
            var a = new AssetCondition(ctx => true, "InFolderA");
            var b = new AssetCondition(ctx => true, "IsPrefab");

            var combined = a.And(b);

            Assert.AreEqual("InFolderA AND IsPrefab", combined.Description);
        }

        [Test]
        public void And_AssetCondition_OneDescriptionNull_UsesOtherOnly()
        {
            var a = new AssetCondition(ctx => true, "InFolderA");
            var b = new AssetCondition(ctx => true);

            Assert.AreEqual("InFolderA", a.And(b).Description);
            Assert.AreEqual("InFolderA", b.And(a).Description);
        }

        [Test]
        public void And_AssetCondition_BothDescriptionsNull_ResultDescriptionIsNull()
        {
            var a = new AssetCondition(ctx => true);
            var b = new AssetCondition(ctx => true);

            Assert.IsNull(a.And(b).Description);
        }

        [Test]
        public void And_RawLambda_CombinesPredicatesAndDescription()
        {
            var a = new AssetCondition(ctx => ctx.Path.StartsWith("Assets/A/"), "InFolderA");

            var combined = a.And(ctx => ctx.Extension == ".prefab", "IsPrefab");

            Assert.IsTrue(combined.Test(MakeCtx("Assets/A/Foo.prefab")));
            Assert.IsFalse(combined.Test(MakeCtx("Assets/A/Foo.png")));
            Assert.AreEqual("InFolderA AND IsPrefab", combined.Description);
        }

        [Test]
        public void And_RawLambda_NoDescription_KeepsOriginalDescription()
        {
            var a = new AssetCondition(ctx => true, "InFolderA");

            var combined = a.And(ctx => true);

            Assert.AreEqual("InFolderA", combined.Description);
        }
    }
}
