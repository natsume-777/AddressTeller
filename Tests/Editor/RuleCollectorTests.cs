using NUnit.Framework;
using System.Linq;
using System.Reflection;

namespace Natsume777.AddressTeller.Editor.Tests
{
    public class RuleCollectorTests
    {
        // テスト用スタブ（このアセンブリ内にのみ存在）
        private sealed class LowPriorityRule : AddressRuleBase
        {
            public override int Order => 10;
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
    }
}
