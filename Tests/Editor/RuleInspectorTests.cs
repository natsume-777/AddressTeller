using System;
using NUnit.Framework;
using UnityEngine;
using AddressTeller.Testing;

namespace AddressTeller.Editor.Tests
{
    public class RuleInspectorTests
    {
        private sealed class TwoEntryRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("Characters")
                    .Where(ctx => ctx.Path.StartsWith("Assets/Characters/"))
                    .Address(ctx => ctx.FileNameWithoutExtension)
                    .Label("character");

                rules.AnyGroup()
                    .Where(ctx => ctx.Path.Contains("/UI/"))
                    .Label("ui");
            }
        }

        private sealed class GroupDefaultRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.GroupDefault().Address("addr");
            }
        }

        private sealed class ConfigureThrowsRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                throw new InvalidOperationException("boom");
            }
        }

        private sealed class WhereCalledTwiceRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                var group = rules.Group("G").Where(ctx => true);
                group.Where(ctx => false);
            }
        }

        private sealed class AddressCalledTwiceRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                var group = rules.Group("G").Address("first");
                group.Address("second");
            }
        }

        private sealed class EmptyRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                // 何も登録しない
            }
        }

        [Test]
        public void Collect_NullRule_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => RuleInspector.Collect(null));
        }

        [Test]
        public void Collect_ReturnsAllEntriesFromConfigure()
        {
            var entries = RuleInspector.Collect(new TwoEntryRule());

            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual("Characters", entries[0].GroupName);
            Assert.IsNull(entries[1].GroupName);
        }

        [Test]
        public void Collect_EvaluatesPredicateAndSelectorsAsExpected()
        {
            var entries = RuleInspector.Collect(new TwoEntryRule());
            var ctx = new AssetContext("guid1", "Assets/Characters/Hero.prefab", typeof(GameObject));

            var entry = entries[0];
            Assert.IsTrue(entry.Predicate(ctx));
            Assert.AreEqual("Hero", entry.AddressSelector(ctx));
            Assert.AreEqual("character", entry.LabelSelectors[0](ctx));
        }

        [Test]
        public void Collect_GroupDefaultRule_GroupNameIsUnresolvedDefaultGroup()
        {
            var entries = RuleInspector.Collect(new GroupDefaultRule());

            Assert.AreEqual(1, entries.Count);
            Assert.IsTrue(RuleInspector.IsUnresolvedDefaultGroup(entries[0].GroupName));
        }

        [Test]
        public void IsUnresolvedDefaultGroup_NonSentinelGroupName_ReturnsFalse()
        {
            var entries = RuleInspector.Collect(new TwoEntryRule());

            Assert.IsFalse(RuleInspector.IsUnresolvedDefaultGroup(entries[0].GroupName));
        }

        [Test]
        public void Collect_ConfigureThrows_PropagatesException()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => RuleInspector.Collect(new ConfigureThrowsRule()));
            Assert.AreEqual("boom", ex.Message);
        }

        [Test]
        public void Collect_WhereCalledTwiceOnSameGroup_PropagatesInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => RuleInspector.Collect(new WhereCalledTwiceRule()));
        }

        [Test]
        public void Collect_AddressCalledTwiceOnSameGroup_PropagatesInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => RuleInspector.Collect(new AddressCalledTwiceRule()));
        }

        [Test]
        public void Collect_EntrySourceClass_IsRuleTypeName()
        {
            var entries = RuleInspector.Collect(new TwoEntryRule());

            Assert.AreEqual(nameof(TwoEntryRule), entries[0].SourceClass);
            Assert.AreEqual(nameof(TwoEntryRule), entries[1].SourceClass);
        }

        [Test]
        public void Collect_RuleIndex_IsZeroBasedSequence()
        {
            var entries = RuleInspector.Collect(new TwoEntryRule());

            Assert.AreEqual(0, entries[0].RuleIndex);
            Assert.AreEqual(1, entries[1].RuleIndex);
        }

        [Test]
        public void Collect_NoRulesRegistered_ReturnsEmptyListNotNull()
        {
            var entries = RuleInspector.Collect(new EmptyRule());

            Assert.IsNotNull(entries);
            Assert.AreEqual(0, entries.Count);
        }

        [Test]
        public void IsUnresolvedDefaultGroup_NullGroupName_ReturnsFalse()
        {
            Assert.IsFalse(RuleInspector.IsUnresolvedDefaultGroup(null));
        }

        [Test]
        public void Collect_CalledTwiceOnSameRuleInstance_DoesNotAccumulateEntries()
        {
            var rule = new TwoEntryRule();
            RuleInspector.Collect(rule);
            var entries = RuleInspector.Collect(rule);

            Assert.AreEqual(2, entries.Count);
        }

        [Test]
        public void DisplayGroupName_SentinelGroupName_ReturnsDefaultGroupPlaceholder()
        {
            var entries = RuleInspector.Collect(new GroupDefaultRule());

            Assert.AreEqual("(Default Group)", RuleInspector.DisplayGroupName(entries[0].GroupName));
        }

        [Test]
        public void DisplayGroupName_NonSentinelGroupName_ReturnsUnchanged()
        {
            Assert.AreEqual("Characters", RuleInspector.DisplayGroupName("Characters"));
        }

        [Test]
        public void DisplayGroupName_NullGroupName_ReturnsNull()
        {
            Assert.IsNull(RuleInspector.DisplayGroupName(null));
        }

        [Test]
        public void DisplayGroupName_GroupLiterallyNamedDefaultGroupPlaceholder_ReturnsUnchanged()
        {
            // 「(Default Group)」という名前のグループがプロジェクトに実在していても、
            // DisplayGroupName は表示用の素通しであり、未解決センチネルとの区別はしない。
            Assert.AreEqual("(Default Group)", RuleInspector.DisplayGroupName("(Default Group)"));
        }

        [Test]
        public void IsUnresolvedDefaultGroup_GroupLiterallyNamedDefaultGroupPlaceholder_ReturnsFalse()
        {
            Assert.IsFalse(RuleInspector.IsUnresolvedDefaultGroup("(Default Group)"));
        }
    }
}
