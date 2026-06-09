using System;
using System.Collections.Generic;

namespace Natsume777.AddressTeller
{
    public sealed class AddressRuleBuilderImpl : IAddressRuleBuilder
    {
        private readonly List<AddressRuleGroupBuilder> _groupBuilders = new List<AddressRuleGroupBuilder>();

        public IReadOnlyList<AddressRuleEntry> Entries
        {
            get
            {
                var result = new AddressRuleEntry[_groupBuilders.Count];
                for (int i = 0; i < _groupBuilders.Count; i++)
                    result[i] = _groupBuilders[i].Build();
                return result;
            }
        }

        public IAddressRuleGroupBuilder Group(string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) throw new ArgumentException("groupName must not be empty.", nameof(groupName));
            var builder = new AddressRuleGroupBuilder(groupName);
            _groupBuilders.Add(builder);
            return builder;
        }
    }

    internal sealed class AddressRuleGroupBuilder : IAddressRuleGroupBuilder
    {
        private readonly string _groupName;
        private Func<AssetContext, bool> _predicate = _ => true;
        private Func<AssetContext, string> _addressSelector;
        private readonly List<Func<AssetContext, string>> _labelSelectors = new List<Func<AssetContext, string>>();

        internal AddressRuleGroupBuilder(string groupName)
        {
            _groupName = groupName;
        }

        public IAddressRuleGroupBuilder Where(Func<AssetContext, bool> predicate)
        {
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            return this;
        }

        public IAddressRuleGroupBuilder Address(Func<AssetContext, string> selector)
        {
            _addressSelector = selector ?? throw new ArgumentNullException(nameof(selector));
            return this;
        }

        public IAddressRuleGroupBuilder Address(string address)
        {
            if (string.IsNullOrEmpty(address)) throw new ArgumentException("address must not be empty.", nameof(address));
            _addressSelector = _ => address;
            return this;
        }

        public IAddressRuleGroupBuilder Label(Func<AssetContext, string> selector)
        {
            _labelSelectors.Add(selector ?? throw new ArgumentNullException(nameof(selector)));
            return this;
        }

        public IAddressRuleGroupBuilder Label(string label)
        {
            if (string.IsNullOrEmpty(label)) throw new ArgumentException("label must not be empty.", nameof(label));
            _labelSelectors.Add(_ => label);
            return this;
        }

        internal AddressRuleEntry Build() =>
            new AddressRuleEntry(_groupName, _predicate, _addressSelector, _labelSelectors.AsReadOnly());
    }
}
