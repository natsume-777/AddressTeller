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

        // A rule that misuses the builder by calling Where() twice on the same group.
        // Configure() has no explicit throw of its own — the exception is thrown by the builder
        // object returned by Group(), and Configure() simply lets it propagate.
        private sealed class WhereCalledTwiceRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                var group = rules.Group("MyGroup").Where(ctx => true);
                group.Where(ctx => false);
            }
        }

        // A rule that misuses the builder by calling Address() twice on the same group. Same shape as
        // WhereCalledTwiceRule above, but for the "one address per rule" constraint.
        private sealed class AddressCalledTwiceRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                var group = rules.Group("MyGroup").Address("first");
                group.Address("second");
            }
        }

        // A rule that targets the project's default Addressables group instead of a named group.
        // The resulting entry's GroupName carries an unresolved sentinel until the real evaluation
        // pipeline resolves it, so check it with RuleTestHelper.IsUnresolvedDefaultGroup() rather
        // than comparing against a hard-coded string.
        private sealed class GroupDefaultRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.GroupDefault()
                    .Where(ctx => ctx.Path.StartsWith("Assets/Demo/Config/"))
                    .Address(ctx => ctx.FileNameWithoutExtension);
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

        [Test]
        public void Collect_WhenWhereCalledTwiceOnSameGroup_PropagatesInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => RuleTestHelper.Collect(new WhereCalledTwiceRule()));
        }

        [Test]
        public void Collect_WhenAddressCalledTwiceOnSameGroup_PropagatesInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => RuleTestHelper.Collect(new AddressCalledTwiceRule()));
        }

        [Test]
        public void ConfigAsset_MatchesGroupDefaultRule_GroupNameIsUnresolvedDefaultGroup()
        {
            var ctx = RuleTestHelper.For("Assets/Demo/Config/Settings.asset", typeof(ScriptableObject));
            var entries = RuleTestHelper.Collect(new GroupDefaultRule());

            var matched = FindFirst(entries, ctx);
            Assert.IsNotNull(matched, "No entry matched the config asset.");
            // Demonstrates the convention of always routing a group name through DisplayGroupName()
            // when printing it, since the raw sentinel used by GroupDefault() is not itself legible.
            Assert.IsTrue(RuleTestHelper.IsUnresolvedDefaultGroup(matched.GroupName),
                $"Expected the unresolved GroupDefault() sentinel, but got '{RuleTestHelper.DisplayGroupName(matched.GroupName)}'.");
            Assert.AreEqual("Settings", matched.AddressSelector?.Invoke(ctx));
        }

        // ctx.IsFolder かつ entry.IncludesFolders が false のエントリは Predicate を呼ばずスキップする。
        // 本番の評価パイプライン（RuleEvaluator）と同じ判定であり、IncludeFolders() を宣言していない
        // ルールの Predicate がフォルダ用に書かれていない前提を壊さないようにするため。
        private static AddressRuleEntry FindFirst(IReadOnlyList<AddressRuleEntry> entries, AssetContext ctx)
        {
            foreach (var e in entries)
            {
                if (ctx.IsFolder && !e.IncludesFolders) continue;
                if (e.Predicate(ctx)) return e;
            }
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
