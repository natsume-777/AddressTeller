using NUnit.Framework;

namespace Natsume777.AddressTeller.Editor.Tests
{
    public class AddressRuleBaseTests
    {
        private sealed class SampleRule : AddressRuleBase
        {
            public override int Order => 5;
            public override void Configure(IAddressRuleBuilder rules) { }
        }

        [Test]
        public void Order_ReturnsOverriddenValue()
        {
            var rule = new SampleRule();
            Assert.AreEqual(5, rule.Order);
        }
    }
}
