using System;
using System.Collections.Generic;

namespace AddressTeller.Testing
{
    /// <summary>
    /// What the rules decide for one asset, as reported by <see cref="RuleInspector.Evaluate"/>.
    /// Mirrors the verdicts AddressTeller itself reaches when it validates or applies the same rules.
    /// </summary>
    public enum RuleOutcomeKind
    {
        /// <summary>No rule matched the asset (it would be skipped).</summary>
        None = 0,

        /// <summary>A single winning rule assigns an address (and a group); see <see cref="RuleOutcome.Address"/>.</summary>
        Address = 1,

        /// <summary>
        /// Only label-only rules (<see cref="IAddressRuleBuilder.AnyGroup"/> or a group rule without
        /// <c>Address()</c>) matched. No address is assigned; the labels are added only if the asset already
        /// has an Addressables entry.
        /// </summary>
        LabelsOnly = 2,

        /// <summary>Two or more rules with the lowest <see cref="AddressRuleBase.Order"/> tie; nothing would be written.</summary>
        Conflict = 3,

        /// <summary>The winning rule returned a null or empty address; nothing would be written.</summary>
        InvalidAddress = 4,

        /// <summary>
        /// A rule that could have won (same or higher priority than the winner) threw while being evaluated,
        /// so nothing would be written. The exception text is in <see cref="RuleOutcome.Messages"/>.
        /// </summary>
        BlockedByRuleError = 5,
    }

    /// <summary>
    /// The result of evaluating a set of rules against one asset with <see cref="RuleInspector.Evaluate"/>.
    /// </summary>
    public sealed class RuleOutcome
    {
        /// <summary>What the rules decided for the asset.</summary>
        public RuleOutcomeKind Kind { get; }

        /// <summary>
        /// Name of the winning rule's group when <see cref="Kind"/> is <see cref="RuleOutcomeKind.Address"/>;
        /// otherwise null. Null as well when the winning rule uses <see cref="IAddressRuleBuilder.GroupDefault"/>
        /// (see <see cref="UsesDefaultGroup"/>): the project's actual default group is not known here.
        /// </summary>
        public string GroupName { get; }

        /// <summary>
        /// True when <see cref="Kind"/> is <see cref="RuleOutcomeKind.Address"/> and the winning rule targets
        /// the project's default group via <see cref="IAddressRuleBuilder.GroupDefault"/>.
        /// </summary>
        public bool UsesDefaultGroup { get; }

        /// <summary>The winning rule's address when <see cref="Kind"/> is <see cref="RuleOutcomeKind.Address"/>; otherwise null.</summary>
        public string Address { get; }

        /// <summary>
        /// Labels AddressTeller would write: the labels of every matching label-only rule plus the labels of the
        /// winning rule, without duplicates, sorted by ordinal comparison. Empty for <see cref="RuleOutcomeKind.None"/>,
        /// <see cref="RuleOutcomeKind.Conflict"/>, <see cref="RuleOutcomeKind.InvalidAddress"/> and
        /// <see cref="RuleOutcomeKind.BlockedByRuleError"/>. Never null.
        /// </summary>
        public IReadOnlyList<string> Labels { get; }

        /// <summary>
        /// Identifier of the winning rule (class name and description, e.g. <c>MyRule &gt; "InFolder(Assets/A)"</c>)
        /// when <see cref="Kind"/> is <see cref="RuleOutcomeKind.Address"/> or <see cref="RuleOutcomeKind.InvalidAddress"/>;
        /// otherwise null.
        /// </summary>
        public string WinningRule { get; }

        /// <summary>
        /// Human-readable diagnostics: the tied rules of a conflict, the rule that returned a null or empty
        /// address for <see cref="RuleOutcomeKind.InvalidAddress"/>, and every exception thrown by a rule's
        /// condition, address or label selector during this evaluation. Never null.
        /// </summary>
        public IReadOnlyList<string> Messages { get; }

        /// <summary>Number of rule exceptions during evaluation (a subset of <see cref="Messages"/>).</summary>
        internal int RuleErrorCount { get; }

        /// <summary>Number of the rule exceptions that blocked the winner (non-zero only for BlockedByRuleError).</summary>
        internal int BlockingErrorCount { get; }

        /// <summary>A one-line summary (kind, group, address, labels) for test failure output.</summary>
        public override string ToString()
        {
            var text = Kind.ToString();
            if (Kind == RuleOutcomeKind.Address)
                text += $" {(UsesDefaultGroup ? "(Default Group)" : GroupName)} -> \"{Address}\"";
            if (Labels.Count > 0)
                text += $" [{string.Join(", ", Labels)}]";
            return text;
        }

        internal RuleOutcome(
            RuleOutcomeKind kind,
            string groupName,
            bool usesDefaultGroup,
            string address,
            IReadOnlyList<string> labels,
            string winningRule,
            IReadOnlyList<string> messages,
            int ruleErrorCount,
            int blockingErrorCount = 0)
        {
            BlockingErrorCount = blockingErrorCount;
            Kind = kind;
            GroupName = groupName;
            UsesDefaultGroup = usesDefaultGroup;
            Address = address;
            Labels = labels ?? Array.Empty<string>();
            WinningRule = winningRule;
            Messages = messages ?? Array.Empty<string>();
            RuleErrorCount = ruleErrorCount;
        }
    }
}
