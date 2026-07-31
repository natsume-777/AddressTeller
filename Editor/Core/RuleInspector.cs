using System;
using System.Collections.Generic;

namespace AddressTeller.Testing
{
    /// <summary>
    /// Public API for inspecting the result of <see cref="AddressRuleBase.Configure"/> without an
    /// Addressables project. Primarily intended for use from a consumer's own unit tests to retrieve
    /// the set of <see cref="AddressRuleEntry"/> objects a rule class builds
    /// (the implementation basis of the <c>Samples~/RuleUnitTestHelper</c> sample).
    /// </summary>
    public static class RuleInspector
    {
        /// <summary>
        /// Runs <paramref name="rule"/>'s <see cref="AddressRuleBase.Configure"/> and returns the collected
        /// list of <see cref="AddressRuleEntry"/> objects.
        /// </summary>
        /// <remarks>
        /// If <see cref="AddressRuleBase.Configure"/> throws, the exception is not caught here and propagates
        /// to the caller unchanged. This is intentionally different from the production evaluation path
        /// (<c>RuleEvaluationPipeline</c>), which catches per-rule and skips the failing rule while continuing
        /// the rest of evaluation (reported as <c>ValidationStatus.RuleConfigureFailed</c>). <see cref="Collect"/>
        /// is meant for a unit test that exercises a single rule class in isolation, where a bug in
        /// <c>Configure()</c> should surface directly as a test failure.
        /// Each returned entry's <see cref="AddressRuleEntry.RuleIndex"/> is a zero-based registration order
        /// scoped to <paramref name="rule"/>'s own <c>Configure()</c> call, not a project-wide sequence number.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="rule"/> is null.</exception>
        public static IReadOnlyList<AddressRuleEntry> Collect(AddressRuleBase rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            // sourceClass には RuleEvaluationPipeline.GetOrderedEntries と同じ値（rule.GetType().Name）を渡す。
            // ここは意図的に try/catch しない設計のため共通化はせず、値が食い違わないことをコメントで担保する。
            var builder = new AddressRuleBuilderImpl(rule.GetType().Name);
            rule.Configure(builder);
            // AddressRuleBuilderImpl.Entries の宣言型は IReadOnlyList<AddressRuleEntry>（配列を返すのは
            // あくまで実装詳細）なので、無検査キャストせず List にコピーしてから読み取り専用ビューを返す。
            return new List<AddressRuleEntry>(builder.Entries).AsReadOnly();
        }

        /// <summary>
        /// Returns true if <paramref name="groupName"/> (the value of <see cref="AddressRuleEntry.GroupName"/>)
        /// is the unresolved sentinel produced by <see cref="IAddressRuleBuilder.GroupDefault"/>.
        /// </summary>
        /// <remarks>
        /// During real evaluation (<c>RuleEvaluationPipeline.BuildSetup</c>) the sentinel is resolved to the
        /// actual <c>AddressableAssetSettings.DefaultGroup</c> name before the loop starts, so this method only
        /// ever returns true for entries collected via <see cref="Collect"/>, which does not go through
        /// Addressables settings. It returns false for any already-resolved group name, including the real
        /// default group's name (e.g. "Default Local Group") — it does not tell you whether a given name
        /// happens to be the project's default group. The raw sentinel string is an internal implementation
        /// detail that may change between versions, so always use this helper instead of comparing against a
        /// hard-coded string.
        /// </remarks>
        public static bool IsUnresolvedDefaultGroup(string groupName) => groupName == AddressRuleBuilderImpl.DefaultGroupSentinel;

        /// <summary>
        /// Formats <paramref name="groupName"/> for display or diagnostic messages. Converts the unresolved
        /// <see cref="IAddressRuleBuilder.GroupDefault"/> sentinel into the human-readable placeholder
        /// "(Default Group)"; any other value is returned unchanged.
        /// </summary>
        /// <remarks>
        /// Always use this helper (rather than the raw <see cref="AddressRuleEntry.GroupName"/> value) when
        /// logging or asserting on a group name, since entries produced via <c>GroupDefault()</c> carry a
        /// sentinel string containing control characters until it is resolved by the production pipeline.
        /// This method is for display purposes only — since a project could legitimately have a group
        /// literally named "(Default Group)", never use its output for equality comparisons; use
        /// <see cref="IsUnresolvedDefaultGroup"/> for that instead.
        /// For a label-only entry produced by <see cref="IAddressRuleBuilder.AnyGroup"/> (whose
        /// <see cref="AddressRuleEntry.GroupName"/> is null), this returns null unchanged.
        /// </remarks>
        public static string DisplayGroupName(string groupName) => AddressRuleBuilderImpl.DisplayGroupName(groupName);
    }
}
