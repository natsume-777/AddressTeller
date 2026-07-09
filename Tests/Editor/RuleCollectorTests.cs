using NUnit.Framework;
using System;
using System.Linq;
using System.Reflection;

namespace AddressTeller.Editor.Tests
{
    public class RuleCollectorTests
    {
        // テスト用スタブ（このアセンブリ内にのみ存在）
        private sealed class LowPriorityRule : AddressRuleBase
        {
            public override int Order => 10;
            public override void Configure(IAddressRuleBuilder rules) { }
        }

        // 引数なしコンストラクタが例外を投げるルール型。
        // Activator.CreateInstance がそのまま例外を伝播しないことを確認するためのスタブ。
        private sealed class ThrowingConstructorRule : AddressRuleBase
        {
            public ThrowingConstructorRule() => throw new InvalidOperationException("constructor boom");
            public override int Order => 20;
            public override void Configure(IAddressRuleBuilder rules) { }
        }

        // AddressRuleBase を継承したオープンジェネリック型。
        // IsAbstract / GetConstructor のチェックは通過するが、Activator.CreateInstance は
        // ArgumentException（open generic type には対応していない）を投げる。
        private sealed class OpenGenericRule<T> : AddressRuleBase
        {
            public override int Order => 30;
            public override void Configure(IAddressRuleBuilder rules) { }
        }

        private sealed class HighPriorityRule : AddressRuleBase
        {
            public override int Order => -5;
            public override void Configure(IAddressRuleBuilder rules) { }
        }

        private sealed class DefaultOrderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules) { }
        }

        // DefaultOrderRule と Order(0) が重複するテスト用スタブ
        private sealed class AnotherDefaultOrderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules) { }
        }

        private static Assembly ThisAssembly => typeof(RuleCollectorTests).Assembly;

        [Test]
        public void CollectRules_OrdersAscending()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });

            for (int i = 1; i < rules.Count; i++)
                Assert.LessOrEqual(rules[i - 1].Order, rules[i].Order,
                    $"rules[{i - 1}].Order={rules[i - 1].Order} > rules[{i}].Order={rules[i].Order}");
        }

        [Test]
        public void CollectRules_ContainsAllConcreteSubclasses()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });

            Assert.IsTrue(rules.Any(r => r is LowPriorityRule));
            Assert.IsTrue(rules.Any(r => r is HighPriorityRule));
            Assert.IsTrue(rules.Any(r => r is DefaultOrderRule));
        }

        [Test]
        public void CollectRules_EmptyAssemblies_ReturnsEmpty()
        {
            var rules = RuleCollector.CollectRules(new Assembly[0]);
            Assert.AreEqual(0, rules.Count);
        }

        [Test]
        public void CollectRules_AbstractBaseClass_NotCollected()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });

            Assert.IsFalse(rules.Any(r => r.GetType() == typeof(AddressRuleBase)));
        }

        [Test]
        public void CollectRules_ConstructorThrows_SkipsTypeButCollectsOthers()
        {
            // ThrowingConstructorRule のインスタンス化で例外が発生しても、収集全体が中断せず
            // 他のルール型は引き続き収集されることを確認する。
            Assert.DoesNotThrow(() => RuleCollector.CollectRules(new[] { ThisAssembly }));

            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });

            Assert.IsFalse(rules.Any(r => r is ThrowingConstructorRule));
            Assert.IsTrue(rules.Any(r => r is LowPriorityRule));
            Assert.IsTrue(rules.Any(r => r is HighPriorityRule));
        }

        [Test]
        public void CollectRules_OpenGenericType_SkippedWithoutThrowing()
        {
            // オープンジェネリック型は IsAbstract / GetConstructor のチェックは通過するが
            // Activator.CreateInstance が ArgumentException を投げる。例外が外へ漏れず、
            // 該当型のみスキップされ、他のルールは収集されることを確認する。
            Assert.DoesNotThrow(() => RuleCollector.CollectRules(new[] { ThisAssembly }));

            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });

            Assert.IsFalse(rules.Any(r =>
                r.GetType().IsGenericType && r.GetType().GetGenericTypeDefinition() == typeof(OpenGenericRule<>)));
            Assert.IsTrue(rules.Any(r => r is LowPriorityRule));
        }

        [Test]
        public void CollectRules_NoArg_ExcludesTestAssembly()
        {
            var rules = RuleCollector.CollectRules();

            Assert.IsFalse(rules.Any(r => r is DefaultOrderRule));
            Assert.IsFalse(rules.Any(r => r is AnotherDefaultOrderRule));
            Assert.IsFalse(rules.Any(r => r is LowPriorityRule));
            Assert.IsFalse(rules.Any(r => r is HighPriorityRule));
        }

        [Test]
        public void CollectRules_SameOrder_SortedByFullNameOrdinal()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });

            // DefaultOrderRule と AnotherDefaultOrderRule は両方 Order=0。
            // Order が同値の場合は型のフルネーム（Ordinal）で決定的にソートされる。
            var sameOrderRules = rules.Where(r => r.Order == 0).ToList();
            for (int i = 1; i < sameOrderRules.Count; i++)
            {
                var prevName = sameOrderRules[i - 1].GetType().FullName;
                var currName = sameOrderRules[i].GetType().FullName;
                Assert.LessOrEqual(string.CompareOrdinal(prevName, currName), 0,
                    $"{prevName} should sort before-or-equal {currName} (Ordinal)");
            }

            var defaultIndex = sameOrderRules.FindIndex(r => r is DefaultOrderRule);
            var anotherIndex = sameOrderRules.FindIndex(r => r is AnotherDefaultOrderRule);
            Assert.AreNotEqual(-1, defaultIndex);
            Assert.AreNotEqual(-1, anotherIndex);

            var expectedFirst = string.CompareOrdinal(typeof(DefaultOrderRule).FullName, typeof(AnotherDefaultOrderRule).FullName) <= 0
                ? defaultIndex
                : anotherIndex;
            var expectedSecond = expectedFirst == defaultIndex ? anotherIndex : defaultIndex;
            Assert.Less(expectedFirst, expectedSecond);
        }

        [Test]
        public void FindDuplicateOrders_SameOrder_GroupedTogether()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });

            var duplicates = RuleCollector.FindDuplicateOrders(rules).ToList();

            var group = duplicates.SingleOrDefault(g => g.Key == 0);
            Assert.IsNotNull(group);
            Assert.IsTrue(group.Any(r => r is DefaultOrderRule));
            Assert.IsTrue(group.Any(r => r is AnotherDefaultOrderRule));
        }

        [Test]
        public void FindDuplicateOrders_UniqueOrder_NotIncluded()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });

            var duplicates = RuleCollector.FindDuplicateOrders(rules);

            Assert.IsFalse(duplicates.Any(g => g.Key == -5 || g.Key == 10));
        }

        [Test]
        public void CollectEnabledRules_ExcludesDisabledClassByFullName()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });
            var disabled = new[] { typeof(LowPriorityRule).FullName };

            var enabled = RuleCollector.CollectEnabledRules(rules, disabled);

            Assert.IsFalse(enabled.Any(r => r is LowPriorityRule));
            Assert.IsTrue(enabled.Any(r => r is HighPriorityRule));
            Assert.IsTrue(enabled.Any(r => r is DefaultOrderRule));
        }

        [Test]
        public void CollectEnabledRules_KeepsOrderAscending()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });
            var disabled = new[] { typeof(DefaultOrderRule).FullName };

            var enabled = RuleCollector.CollectEnabledRules(rules, disabled);

            for (int i = 1; i < enabled.Count; i++)
                Assert.LessOrEqual(enabled[i - 1].Order, enabled[i].Order,
                    $"enabled[{i - 1}].Order={enabled[i - 1].Order} > enabled[{i}].Order={enabled[i].Order}");
        }

        [Test]
        public void CollectEnabledRules_UnknownTypeNameInDisabledList_HasNoEffect()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });
            var disabled = new[] { "Some.Nonexistent.Namespace.NoSuchRule" };

            var enabled = RuleCollector.CollectEnabledRules(rules, disabled);

            Assert.AreEqual(rules.Count, enabled.Count);
        }

        [Test]
        public void CollectEnabledRules_EmptyDisabledList_ReturnsAllRules()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });

            var enabled = RuleCollector.CollectEnabledRules(rules, new string[0]);

            Assert.AreEqual(rules.Count, enabled.Count);
        }

        // --- TryCollectEnabledRules（CLI -addressTellerDisableRules の和集合計算） ---

        [Test]
        public void TryCollectEnabledRules_NoAdditional_SameAsCollectEnabledRules()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });
            var persisted = new[] { typeof(LowPriorityRule).FullName };

            var ok = RuleCollector.TryCollectEnabledRules(rules, persisted, System.Array.Empty<string>(), out var enabled, out var unknown);

            Assert.IsTrue(ok);
            Assert.IsEmpty(unknown);
            CollectionAssert.AreEqual(RuleCollector.CollectEnabledRules(rules, persisted), enabled);
        }

        [Test]
        public void TryCollectEnabledRules_AdditionalKnownName_ExcludesThatRule()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });
            var additional = new[] { typeof(HighPriorityRule).FullName };

            var ok = RuleCollector.TryCollectEnabledRules(rules, System.Array.Empty<string>(), additional, out var enabled, out var unknown);

            Assert.IsTrue(ok);
            Assert.IsEmpty(unknown);
            Assert.IsFalse(enabled.Any(r => r is HighPriorityRule));
            Assert.IsTrue(enabled.Any(r => r is LowPriorityRule));
        }

        [Test]
        public void TryCollectEnabledRules_PersistedAndAdditional_UnionApplied()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });
            var persisted = new[] { typeof(LowPriorityRule).FullName };
            var additional = new[] { typeof(HighPriorityRule).FullName };

            var ok = RuleCollector.TryCollectEnabledRules(rules, persisted, additional, out var enabled, out var unknown);

            Assert.IsTrue(ok);
            Assert.IsEmpty(unknown);
            Assert.IsFalse(enabled.Any(r => r is LowPriorityRule));
            Assert.IsFalse(enabled.Any(r => r is HighPriorityRule));
            Assert.IsTrue(enabled.Any(r => r is DefaultOrderRule));
        }

        [Test]
        public void TryCollectEnabledRules_DuplicateBetweenPersistedAndAdditional_NoError()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });
            var name = typeof(LowPriorityRule).FullName;

            var ok = RuleCollector.TryCollectEnabledRules(rules, new[] { name }, new[] { name }, out var enabled, out var unknown);

            Assert.IsTrue(ok);
            Assert.IsEmpty(unknown);
            Assert.IsFalse(enabled.Any(r => r is LowPriorityRule));
        }

        [Test]
        public void TryCollectEnabledRules_UnknownAdditionalName_ReturnsFalseWithUnknownName()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });
            var additional = new[] { "Some.Nonexistent.Namespace.NoSuchRule" };

            var ok = RuleCollector.TryCollectEnabledRules(rules, System.Array.Empty<string>(), additional, out var enabled, out var unknown);

            Assert.IsFalse(ok);
            Assert.IsNull(enabled);
            CollectionAssert.AreEqual(additional, unknown);
        }

        [Test]
        public void TryCollectEnabledRules_MixedKnownAndUnknownAdditionalNames_ReturnsOnlyUnknown()
        {
            var rules = RuleCollector.CollectRules(new[] { ThisAssembly });
            var additional = new[] { typeof(LowPriorityRule).FullName, "Some.Nonexistent.Namespace.NoSuchRule" };

            var ok = RuleCollector.TryCollectEnabledRules(rules, System.Array.Empty<string>(), additional, out var enabled, out var unknown);

            Assert.IsFalse(ok);
            Assert.IsNull(enabled);
            CollectionAssert.AreEqual(new[] { "Some.Nonexistent.Namespace.NoSuchRule" }, unknown);
        }
    }
}
