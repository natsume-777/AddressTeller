using System;
using System.Collections.Generic;

namespace AddressTeller
{
    internal sealed class AddressRuleBuilderImpl : IAddressRuleBuilder
    {
        private readonly string _sourceClass;
        private readonly List<AddressRuleGroupBuilder> _groupBuilders = new List<AddressRuleGroupBuilder>();

        public AddressRuleBuilderImpl(string sourceClass = null)
        {
            _sourceClass = sourceClass;
        }

        public IReadOnlyList<AddressRuleEntry> Entries
        {
            get
            {
                var result = new AddressRuleEntry[_groupBuilders.Count];
                for (int i = 0; i < _groupBuilders.Count; i++)
                    result[i] = _groupBuilders[i].Build(i);
                return result;
            }
        }

        public IAddressRuleGroupBuilder Group(string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) throw new ArgumentException("groupName must not be empty.", nameof(groupName));
            var builder = new AddressRuleGroupBuilder(groupName, _sourceClass);
            _groupBuilders.Add(builder);
            return builder;
        }
    }

    internal sealed class AddressRuleGroupBuilder : IAddressRuleGroupBuilder
    {
        private readonly string _groupName;
        private readonly string _sourceClass;
        private string _description;
        private Func<AssetContext, bool> _predicate = _ => true;
        private bool _whereSet;
        private Func<AssetContext, string> _addressSelector;
        private readonly List<Func<AssetContext, string>> _labelSelectors = new List<Func<AssetContext, string>>();

        internal AddressRuleGroupBuilder(string groupName, string sourceClass)
        {
            _groupName = groupName;
            _sourceClass = sourceClass;
        }

        public IAddressRuleGroupBuilder Where(Func<AssetContext, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            ThrowIfWhereAlreadySet();
            _predicate = predicate;
            _whereSet = true;
            return this;
        }

        public IAddressRuleGroupBuilder Where(Func<AssetContext, bool> predicate, string description)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            ThrowIfWhereAlreadySet();
            _predicate = predicate;
            _description = description;
            _whereSet = true;
            return this;
        }

        public IAddressRuleGroupBuilder Where(AssetCondition condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            return Where(condition.Predicate, condition.Description);
        }

        private void ThrowIfWhereAlreadySet()
        {
            if (_whereSet)
                throw new InvalidOperationException(
                    $"Group(\"{_groupName}\") の Where() は1回しか呼び出せません。" +
                    "複数の条件は1つのラムダ式に && でまとめてください。");
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

        internal AddressRuleEntry Build(int index)
        {
            return new AddressRuleEntry(_groupName, _predicate, _addressSelector, _labelSelectors.AsReadOnly(), _sourceClass, _description, index);
        }
    }
}
