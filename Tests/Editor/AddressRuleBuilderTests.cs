using NUnit.Framework;
using System;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    public class AddressRuleBuilderTests
    {
        private static AssetContext MakeCtx(string path) =>
            new AssetContext("guid1", path, typeof(GameObject));

        [Test]
        public void Group_Where_Address_ProducesOneEntry()
        {
            var builder = new AddressRuleBuilderImpl();
            builder.Group("Characters")
                .Where(ctx => ctx.Path.StartsWith("Assets/Characters/"))
                .Address(ctx => ctx.FileNameWithoutExtension);

            Assert.AreEqual(1, builder.Entries.Count);
            var entry = builder.Entries[0];
            Assert.AreEqual("Characters", entry.GroupName);
            Assert.IsNotNull(entry.Predicate);
            Assert.IsNotNull(entry.AddressSelector);
        }

        [Test]
        public void MultipleGroups_AccumulateEntries()
        {
            var builder = new AddressRuleBuilderImpl();
            builder.Group("A").Address("addr-a");
            builder.Group("B").Address("addr-b");

            Assert.AreEqual(2, builder.Entries.Count);
            Assert.AreEqual("A", builder.Entries[0].GroupName);
            Assert.AreEqual("B", builder.Entries[1].GroupName);
        }

        [Test]
        public void NoLabel_IsValid()
        {
            var builder = new AddressRuleBuilderImpl();
            builder.Group("G").Address("addr");

            var entry = builder.Entries[0];
            Assert.AreEqual(0, entry.LabelSelectors.Count);
        }

        [Test]
        public void MultipleLabels_Accumulate()
        {
            var builder = new AddressRuleBuilderImpl();
            builder.Group("G")
                .Address("addr")
                .Label("label-a")
                .Label(ctx => ctx.FileNameWithoutExtension);

            var entry = builder.Entries[0];
            Assert.AreEqual(2, entry.LabelSelectors.Count);
        }

        [Test]
        public void NoAddress_AddressSelectorIsNull()
        {
            var builder = new AddressRuleBuilderImpl();
            builder.Group("G").Label("lbl");

            Assert.IsNull(builder.Entries[0].AddressSelector);
        }

        [Test]
        public void Predicate_DefaultMatchesAll()
        {
            var builder = new AddressRuleBuilderImpl();
            builder.Group("G").Address("addr");

            var entry = builder.Entries[0];
            Assert.IsTrue(entry.Predicate(MakeCtx("Assets/Anything/Foo.prefab")));
        }

        [Test]
        public void Where_FiltersCorrectly()
        {
            var builder = new AddressRuleBuilderImpl();
            builder.Group("G")
                .Where(ctx => ctx.Path.StartsWith("Assets/A/"))
                .Address("addr");

            var entry = builder.Entries[0];
            Assert.IsTrue(entry.Predicate(MakeCtx("Assets/A/Foo.prefab")));
            Assert.IsFalse(entry.Predicate(MakeCtx("Assets/B/Foo.prefab")));
        }

        [Test]
        public void Address_StaticString_ReturnsConstant()
        {
            var builder = new AddressRuleBuilderImpl();
            builder.Group("G").Address("my-address");

            var entry = builder.Entries[0];
            Assert.AreEqual("my-address", entry.AddressSelector(MakeCtx("Assets/Foo.prefab")));
        }

        [Test]
        public void Group_ThrowsOnEmptyName()
        {
            var builder = new AddressRuleBuilderImpl();
            Assert.Throws<ArgumentException>(() => builder.Group(""));
        }

        [Test]
        public void Where_CalledTwice_Throws()
        {
            var builder = new AddressRuleBuilderImpl();
            var group = builder.Group("G").Where(ctx => true);

            Assert.Throws<InvalidOperationException>(() => group.Where(ctx => false));
        }

        [Test]
        public void Where_CalledTwice_WithDescriptionOverload_Throws()
        {
            var builder = new AddressRuleBuilderImpl();
            var group = builder.Group("G").Where(ctx => true, "first");

            Assert.Throws<InvalidOperationException>(() => group.Where(ctx => false, "second"));
        }

        [Test]
        public void Where_WithAssetCondition_UsesPredicateAndDescription()
        {
            var condition = new AssetCondition(ctx => ctx.Path.StartsWith("Assets/A/"), "InFolderA");

            var builder = new AddressRuleBuilderImpl();
            builder.Group("G")
                .Where(condition)
                .Address("addr");

            var entry = builder.Entries[0];
            Assert.AreEqual("InFolderA", entry.Description);
            Assert.IsTrue(entry.Predicate(MakeCtx("Assets/A/Foo.prefab")));
            Assert.IsFalse(entry.Predicate(MakeCtx("Assets/B/Foo.prefab")));
        }

        [Test]
        public void Where_WithAssetCondition_CalledTwice_Throws()
        {
            var condition = new AssetCondition(ctx => true, "cond");

            var builder = new AddressRuleBuilderImpl();
            var group = builder.Group("G").Where(condition);

            Assert.Throws<InvalidOperationException>(() => group.Where(condition));
        }

        [Test]
        public void Where_WithAssetCondition_NoDescription_DescriptionIsNull()
        {
            var condition = new AssetCondition(ctx => true);

            var builder = new AddressRuleBuilderImpl();
            builder.Group("G")
                .Where(condition)
                .Address("addr");

            Assert.IsNull(builder.Entries[0].Description);
        }
    }
}
