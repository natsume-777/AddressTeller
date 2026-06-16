using System;
using System.Collections.Generic;
using AddressTeller;

namespace AddressTellerSamples
{
    /// <summary>
    /// Utilities for unit-testing AddressRuleBase subclasses.
    /// Depends only on AddressTeller's public API — no Addressables project required.
    /// </summary>
    public static class RuleTestHelper
    {
        /// <summary>
        /// The value placed in <see cref="AddressRuleEntry.GroupName"/> when
        /// <see cref="IAddressRuleBuilder.GroupDefault"/> was called.
        /// Use this constant in assertions instead of a hard-coded string.
        /// </summary>
        public const string DefaultGroupSentinel = "(Default Group)";

        /// <summary>
        /// Creates a minimal <see cref="AssetContext"/> for testing.
        /// </summary>
        /// <param name="path">Asset path starting with "Assets/" (e.g. "Assets/Characters/Hero.prefab").</param>
        /// <param name="type">Asset type (e.g. typeof(GameObject)).</param>
        /// <param name="guid">Optional GUID; a stable placeholder derived from the path is used when omitted.</param>
        public static AssetContext For(string path, Type type, string guid = null)
        {
            return new AssetContext(guid ?? $"test-{Math.Abs(path.GetHashCode()):x8}", path, type);
        }

        /// <summary>
        /// Runs <see cref="AddressRuleBase.Configure"/> with a fake builder and returns
        /// all collected <see cref="AddressRuleEntry"/> objects.
        /// Evaluate entries with <c>entry.Predicate(ctx)</c>, <c>entry.AddressSelector?.Invoke(ctx)</c>,
        /// and <c>entry.LabelSelectors</c>.
        /// </summary>
        public static IReadOnlyList<AddressRuleEntry> Collect(AddressRuleBase rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            var builder = new FakeBuilder(rule.GetType().Name);
            rule.Configure(builder);
            return builder.Build();
        }

        // --- fake builder implementation ---

        private sealed class FakeBuilder : IAddressRuleBuilder
        {
            private readonly string _sourceClass;
            private readonly List<IEntryBuilder> _builders = new List<IEntryBuilder>();

            internal FakeBuilder(string sourceClass) => _sourceClass = sourceClass;

            internal IReadOnlyList<AddressRuleEntry> Build()
            {
                var result = new AddressRuleEntry[_builders.Count];
                for (int i = 0; i < _builders.Count; i++)
                    result[i] = _builders[i].Build(i);
                return result;
            }

            public IAddressRuleGroupBuilder Group(string groupName)
            {
                if (string.IsNullOrEmpty(groupName))
                    throw new ArgumentException("groupName must not be empty.", nameof(groupName));
                return AddGroup(groupName);
            }

            public IAddressRuleGroupBuilder GroupDefault() => AddGroup(DefaultGroupSentinel);

            private IAddressRuleGroupBuilder AddGroup(string groupName)
            {
                var b = new FakeGroupBuilder(groupName, _sourceClass);
                _builders.Add(b);
                return b;
            }

            public ILabelRuleBuilder AnyGroup()
            {
                var b = new FakeLabelBuilder(_sourceClass);
                _builders.Add(b);
                return b;
            }
        }

        private interface IEntryBuilder { AddressRuleEntry Build(int index); }

        private sealed class FakeGroupBuilder : IEntryBuilder, IAddressRuleGroupBuilder
        {
            private readonly string _groupName;
            private readonly string _sourceClass;
            private string _description;
            private Func<AssetContext, bool> _predicate = _ => true;
            private bool _whereSet;
            private Func<AssetContext, string> _addressSelector;
            private readonly List<Func<AssetContext, string>> _labelSelectors = new List<Func<AssetContext, string>>();

            internal FakeGroupBuilder(string groupName, string sourceClass)
            {
                _groupName = groupName;
                _sourceClass = sourceClass;
            }

            public IAddressRuleGroupBuilder Where(Func<AssetContext, bool> predicate) => SetWhere(predicate, null);
            public IAddressRuleGroupBuilder Where(Func<AssetContext, bool> predicate, string description) => SetWhere(predicate, description);
            public IAddressRuleGroupBuilder Where(AssetCondition condition) => SetWhere(condition?.Predicate, condition?.Description);

            private IAddressRuleGroupBuilder SetWhere(Func<AssetContext, bool> predicate, string description)
            {
                if (_whereSet)
                    throw new InvalidOperationException($"Where() on Group(\"{_groupName}\") can only be called once.");
                _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
                _description = description;
                _whereSet = true;
                return this;
            }

            public IAddressRuleGroupBuilder Address(Func<AssetContext, string> selector) { _addressSelector = selector; return this; }
            public IAddressRuleGroupBuilder Address(string address) { _addressSelector = _ => address; return this; }
            public IAddressRuleGroupBuilder Label(Func<AssetContext, string> selector) { _labelSelectors.Add(selector); return this; }
            public IAddressRuleGroupBuilder Label(string label) { _labelSelectors.Add(_ => label); return this; }

            public AddressRuleEntry Build(int index) =>
                new AddressRuleEntry(_groupName, _predicate, _addressSelector, _labelSelectors.AsReadOnly(), _sourceClass, _description, index);
        }

        private sealed class FakeLabelBuilder : IEntryBuilder, ILabelRuleBuilder
        {
            private readonly string _sourceClass;
            private string _description;
            private Func<AssetContext, bool> _predicate = _ => true;
            private bool _whereSet;
            private readonly List<Func<AssetContext, string>> _labelSelectors = new List<Func<AssetContext, string>>();

            internal FakeLabelBuilder(string sourceClass) => _sourceClass = sourceClass;

            public ILabelRuleBuilder Where(Func<AssetContext, bool> predicate) => SetWhere(predicate, null);
            public ILabelRuleBuilder Where(Func<AssetContext, bool> predicate, string description) => SetWhere(predicate, description);
            public ILabelRuleBuilder Where(AssetCondition condition) => SetWhere(condition?.Predicate, condition?.Description);

            private ILabelRuleBuilder SetWhere(Func<AssetContext, bool> predicate, string description)
            {
                if (_whereSet)
                    throw new InvalidOperationException("Where() on AnyGroup() can only be called once.");
                _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
                _description = description;
                _whereSet = true;
                return this;
            }

            public ILabelRuleBuilder Label(Func<AssetContext, string> selector) { _labelSelectors.Add(selector); return this; }
            public ILabelRuleBuilder Label(string label) { _labelSelectors.Add(_ => label); return this; }

            public AddressRuleEntry Build(int index) =>
                new AddressRuleEntry(null, _predicate, null, _labelSelectors.AsReadOnly(), _sourceClass, _description, index);
        }
    }
}
