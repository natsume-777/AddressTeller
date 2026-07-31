using System;
using System.Collections.Generic;

namespace AddressTeller
{
    /// <summary>
    /// One group-scoped rule assembled by Configure().
    /// </summary>
    public sealed class AddressRuleEntry
    {
        /// <summary>Name of the group this rule targets, or null for a label-only rule from AnyGroup().</summary>
        public string GroupName { get; }

        /// <summary>Condition that determines whether this rule applies to a given asset.</summary>
        public Func<AssetContext, bool> Predicate { get; }

        /// <summary>If null, this rule does not assign an address.</summary>
        public Func<AssetContext, string> AddressSelector { get; }

        /// <summary>Label selectors registered for this rule.</summary>
        public IReadOnlyList<Func<AssetContext, string>> LabelSelectors { get; }

        /// <summary>Name of the AddressRuleBase subclass that defined this entry.</summary>
        public string SourceClass { get; }

        /// <summary>Description passed to Where. If null, falls back to an index-based display.</summary>
        public string Description { get; }

        /// <summary>Order (zero-based) in which Group() was called within Configure(). Used in error messages.</summary>
        public int RuleIndex { get; }

        /// <summary>Creates an AddressRuleEntry. See the properties above for each parameter's meaning.</summary>
        public AddressRuleEntry(
            string groupName,
            Func<AssetContext, bool> predicate,
            Func<AssetContext, string> addressSelector,
            IReadOnlyList<Func<AssetContext, string>> labelSelectors,
            string sourceClass = null,
            string description = null,
            int ruleIndex = 0)
        {
            // groupName は AnyGroup() 由来のラベル専用エントリでは null を許容する。
            GroupName = groupName;
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            AddressSelector = addressSelector;
            LabelSelectors = labelSelectors ?? Array.Empty<Func<AssetContext, string>>();
            SourceClass = sourceClass;
            Description = description;
            RuleIndex = ruleIndex;
        }

        /// <summary>
        /// Builds an identifier string for error messages from SourceClass / Description / RuleIndex.
        /// </summary>
        public static string DescribeSource(string sourceClass, string description, int ruleIndex)
        {
            return sourceClass != null
                ? (description != null ? $"{sourceClass} > \"{description}\"" : $"{sourceClass}[{ruleIndex}]")
                : (description ?? $"Rule[{ruleIndex}]");
        }
    }
}
