using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using AddressTeller;

namespace AddressTellerSamples
{
    [TestFixture]
    public class ExampleRuleTest
    {
        // An inline rule that mirrors BasicRules/ExampleRules.cs.
        // Replace this with your own AddressRuleBase subclass.
        private sealed class LocalExampleRules : AddressRuleBase
        {
            public override int Order => 0;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("MyGroup")
                    .Where(ctx => ctx.Path.StartsWith("Assets/Demo/Characters/")
                               && ctx.Type == typeof(GameObject))
                    .Address(ctx => ctx.FileNameWithoutExtension)
                    .Label("character")
                    .Label("humanoid");

                rules.Group("MyGroup")
                    .Where(ctx => ctx.Path.StartsWith("Assets/Demo/Items/")
                               && ctx.Type == typeof(GameObject))
                    .Address(ctx => ctx.FileNameWithoutExtension)
                    .Label("item");
            }
        }

        private LocalExampleRules _rule;

        [SetUp]
        public void SetUp() => _rule = new LocalExampleRules();

        [Test]
        public void Collect_Returns_TwoEntries()
        {
            var entries = RuleTestHelper.Collect(_rule);
            Assert.AreEqual(2, entries.Count);
        }

        [Test]
        public void CharacterPrefab_MatchesFirstEntry_WithExpectedGroupAndAddress()
        {
            var ctx = RuleTestHelper.For("Assets/Demo/Characters/Hero.prefab", typeof(GameObject));
            var entries = RuleTestHelper.Collect(_rule);

            var matched = FindFirst(entries, ctx);
            Assert.IsNotNull(matched, "No entry matched the character prefab.");
            Assert.AreEqual("MyGroup", matched.GroupName);
            Assert.AreEqual("Hero", matched.AddressSelector?.Invoke(ctx));
        }

        [Test]
        public void CharacterPrefab_HasLabels_CharacterAndHumanoid()
        {
            var ctx = RuleTestHelper.For("Assets/Demo/Characters/Hero.prefab", typeof(GameObject));
            var entries = RuleTestHelper.Collect(_rule);
            var matched = FindFirst(entries, ctx);

            Assert.IsNotNull(matched);
            var labels = EvalLabels(matched, ctx);
            CollectionAssert.Contains(labels, "character");
            CollectionAssert.Contains(labels, "humanoid");
        }

        [Test]
        public void ItemPrefab_MatchesSecondEntry_WithItemLabel()
        {
            var ctx = RuleTestHelper.For("Assets/Demo/Items/Sword.prefab", typeof(GameObject));
            var entries = RuleTestHelper.Collect(_rule);

            var matched = FindFirst(entries, ctx);
            Assert.IsNotNull(matched, "No entry matched the item prefab.");
            CollectionAssert.Contains(EvalLabels(matched, ctx), "item");
        }

        [Test]
        public void Texture_DoesNotMatchAnyEntry()
        {
            var ctx = RuleTestHelper.For("Assets/Demo/Characters/HeroTex.png", typeof(Texture2D));
            var entries = RuleTestHelper.Collect(_rule);

            Assert.IsNull(FindFirst(entries, ctx), "A texture should not match LocalExampleRules.");
        }

        [Test]
        public void For_WithExplicitGuid_UsesProvidedGuid()
        {
            var ctx = RuleTestHelper.For("Assets/Demo/Characters/Hero.prefab", typeof(GameObject), "explicit-guid");
            Assert.AreEqual("explicit-guid", ctx.Guid);
        }

        private static AddressRuleEntry FindFirst(IReadOnlyList<AddressRuleEntry> entries, AssetContext ctx)
        {
            foreach (var e in entries)
                if (e.Predicate(ctx)) return e;
            return null;
        }

        private static List<string> EvalLabels(AddressRuleEntry entry, AssetContext ctx)
        {
            var result = new List<string>();
            foreach (var selector in entry.LabelSelectors)
                result.Add(selector(ctx));
            return result;
        }
    }
}
