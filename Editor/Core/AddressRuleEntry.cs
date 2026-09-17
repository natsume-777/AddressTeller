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

        /// <summary>
        /// True when this rule opted in to seeing folder assets (via IncludeFolders() on the builder).
        /// Not part of the public constructor: it is only ever set by AddressRuleBuilderImpl, which is
        /// the sole producer of entries that rule evaluation consumes. Consumers that build their own
        /// evaluation loop over collected entries (e.g. unit-testing helpers) must check this before
        /// invoking Predicate for a folder AssetContext, to match the production evaluator's behavior:
        /// a rule that has not opted in never sees folders, and its Predicate is not even called for one.
        /// </summary>
        public bool IncludesFolders { get; }

        /// <summary>Creates an AddressRuleEntry. See the properties above for each parameter's meaning.</summary>
        public AddressRuleEntry(
            string groupName,
            Func<AssetContext, bool> predicate,
            Func<AssetContext, string> addressSelector,
            IReadOnlyList<Func<AssetContext, string>> labelSelectors,
            string sourceClass = null,
            string description = null,
            int ruleIndex = 0)
            : this(groupName, predicate, addressSelector, labelSelectors, sourceClass, description, ruleIndex, includesFolders: false)
        {
        }

        /// <summary>
        /// internal 用オーバーロード。IncludeFolders() を宣言したビルダー（AddressRuleBuilderImpl）と、
        /// GroupDefault() のセンチネル解決時の再構築（RuleEvaluationPipeline.BuildSetup）が使う。
        /// </summary>
        internal AddressRuleEntry(
            string groupName,
            Func<AssetContext, bool> predicate,
            Func<AssetContext, string> addressSelector,
            IReadOnlyList<Func<AssetContext, string>> labelSelectors,
            string sourceClass,
            string description,
            int ruleIndex,
            bool includesFolders)
        {
            // groupName は AnyGroup() 由来のラベル専用エントリでは null を許容する。
            GroupName = groupName;
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            AddressSelector = addressSelector;
            LabelSelectors = labelSelectors ?? Array.Empty<Func<AssetContext, string>>();
            SourceClass = sourceClass;
            Description = description;
            RuleIndex = ruleIndex;
            IncludesFolders = includesFolders;
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
