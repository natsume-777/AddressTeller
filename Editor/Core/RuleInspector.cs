using System;
using System.Collections.Generic;

namespace AddressTeller.Testing
{
    /// <summary>
    /// Public API for unit-testing rule classes without an Addressables project, from a consumer's own tests.
    /// <see cref="Verify"/> checks a table of expected outcomes against a set of rules, and
    /// <see cref="Evaluate"/> returns what the rules decide for one asset (the basis of the
    /// <c>Samples~/RuleUnitTestHelper</c> sample). <see cref="Collect"/> retrieves the
    /// <see cref="AddressRuleEntry"/> objects a single rule class builds, for tests of the rule's shape.
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
            // sourceClass・order には RuleEvaluationPipeline.GetOrderedEntries と同じ値
            // （rule.GetType().Name, rule.Order）を渡す。ここは意図的に try/catch しない設計のため
            // 共通化はせず、値が食い違わないことをコメントで担保する。
            var builder = new AddressRuleBuilderImpl(rule.GetType().Name, rule.Order);
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

        /// <summary>
        /// Evaluates <paramref name="rules"/> against <paramref name="context"/> and returns the verdict
        /// AddressTeller itself would reach for that asset: the winning rule (lowest
        /// <see cref="AddressRuleBase.Order"/>), a conflict, a label-only match, or no match at all.
        /// </summary>
        /// <remarks>
        /// The winner and the final label set are decided by the same code the package uses when it applies,
        /// validates and previews rules, so the outcome agrees with a dry run for the same rules and asset.
        /// The order in which the rules are given does not change the verdict (priority comes from
        /// <see cref="AddressRuleBase.Order"/>); it only affects the order of the tied rules listed in
        /// <see cref="RuleOutcome.Messages"/> for a conflict.
        /// <para>
        /// Conditions built with <see cref="Match.InFolderByGuid"/> are resolved against the open Unity project's
        /// AssetDatabase, so they depend on the project the tests run in.
        /// </para>
        /// <para>
        /// What is not covered, because this runs without an Addressables project: whether the winning rule's group
        /// exists, and which group <see cref="IAddressRuleBuilder.GroupDefault"/> resolves to
        /// (<see cref="RuleOutcome.UsesDefaultGroup"/> only says that the default group is targeted).
        /// Whether an asset passes AddressTeller's asset filter, and whether labels are actually written (a
        /// <see cref="RuleOutcomeKind.LabelsOnly"/> asset receives them only if it already has an Addressables
        /// entry), are not covered either.
        /// </para>
        /// <para>
        /// If a rule's <see cref="AddressRuleBase.Configure"/> throws, the exception propagates to the caller
        /// (see <see cref="Collect"/>). An exception thrown by a rule's condition, address selector or label
        /// selector does not propagate: it is reported in <see cref="RuleOutcome.Messages"/>, and blocks the
        /// outcome (<see cref="RuleOutcomeKind.BlockedByRuleError"/>) when it comes from a rule that could have
        /// won, as in AddressTeller itself.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="rules"/> or <paramref name="context"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="rules"/> contains a null element.</exception>
        public static RuleOutcome Evaluate(IEnumerable<AddressRuleBase> rules, AssetContext context)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (context == null) throw new ArgumentNullException(nameof(context));
            return EvaluateEntries(CollectAll(rules), context);
        }

        /// <summary>
        /// Evaluates <paramref name="rules"/> against every row of an expectation table and returns one message
        /// per row that does not match; an empty list means every row passed.
        /// </summary>
        /// <remarks>
        /// The list is returned instead of asserting, so the package stays independent of any test framework:
        /// in NUnit, <c>CollectionAssert.IsEmpty(RuleInspector.Verify(rules, table))</c> prints every failing
        /// row at once. <see cref="AddressRuleBase.Configure"/> runs once per rule for the whole table.
        /// A row also fails if a rule threw while it was evaluated, even when the rest of the row matches; the one
        /// exception is a row that expects <see cref="RuleOutcomeKind.BlockedByRuleError"/>, where the exceptions
        /// that block the winner are expected (any other exception still fails the row). A row whose expectations
        /// contradict each other (for example <see cref="RuleExpectation.Group"/> together with
        /// <see cref="RuleExpectation.UsesDefaultGroup"/> = true) is reported as a "contradictory expectation"
        /// without being evaluated. Each failure message starts with the 1-based row number and the asset path.
        /// Each row is evaluated with a placeholder GUID; use <see cref="Evaluate"/> with your own
        /// <see cref="AssetContext"/> for a rule that depends on the GUID. Group existence and the name of the
        /// project's default group are not verified (see <see cref="Evaluate"/>).
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="rules"/> or <paramref name="expectations"/> is null.</exception>
        /// <exception cref="ArgumentException">A rule or an expectation is null.</exception>
        public static IReadOnlyList<string> Verify(IEnumerable<AddressRuleBase> rules, IEnumerable<RuleExpectation> expectations)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (expectations == null) throw new ArgumentNullException(nameof(expectations));

            var entries = CollectAll(rules);
            var failures = new List<string>();
            var index = 0;
            foreach (var expectation in expectations)
            {
                if (expectation == null)
                    throw new ArgumentException("expectations must not contain null.", nameof(expectations));

                var rowIndex = index;
                index++;

                var contradiction = FindContradiction(expectation);
                if (contradiction != null)
                {
                    failures.Add($"row {rowIndex + 1}: {expectation.Path}: contradictory expectation: {contradiction}");
                    continue;
                }

                var context = new AssetContext($"test-{rowIndex:x8}", expectation.Path, expectation.Type, expectation.IsFolder);
                var outcome = EvaluateEntries(entries, context);
                var differences = Compare(expectation, outcome);
                if (differences.Count == 0)
                    continue;

                var text = $"row {rowIndex + 1}: {expectation.Path}: {string.Join("; ", differences)}";
                if (outcome.Messages.Count > 0)
                    text += $" [{string.Join(" | ", outcome.Messages)}]";
                failures.Add(text);
            }
            return failures;
        }

        private static List<AddressRuleEntry> CollectAll(IEnumerable<AddressRuleBase> rules)
        {
            var all = new List<AddressRuleEntry>();
            foreach (var rule in rules)
            {
                if (rule == null) throw new ArgumentException("rules must not contain null.", nameof(rules));
                all.AddRange(Collect(rule));
            }
            return all;
        }

        private static RuleOutcome EvaluateEntries(IReadOnlyList<AddressRuleEntry> entries, AssetContext context)
        {
            var resolution = RuleEvaluator.Evaluate(context, entries);

            var messages = new List<string>();
            foreach (var error in resolution.Errors)
                messages.Add($"{error.RuleSource} threw: {error.Message}");
            var errorCount = resolution.Errors.Count;

            var selection = WinnerSelector.Select(resolution);
            switch (selection.Outcome)
            {
                case WinnerSelectionOutcome.NoCandidates:
                {
                    var labels = SortedLabels(WinnerSelector.ComposeFinalLabels(resolution, null));
                    var kind = labels.Count > 0 ? RuleOutcomeKind.LabelsOnly : RuleOutcomeKind.None;
                    return new RuleOutcome(kind, null, false, null, labels, null, messages, errorCount);
                }
                case WinnerSelectionOutcome.Tied:
                    foreach (var tied in selection.TiedCandidates)
                        messages.Add($"Tied at Order={tied.Order}: {tied.DescribeSource()} [{AddressRuleBuilderImpl.DisplayGroupName(tied.GroupName)}] -> \"{tied.Address}\"");
                    return new RuleOutcome(RuleOutcomeKind.Conflict, null, false, null, null, null, messages, errorCount);
                case WinnerSelectionOutcome.Blocked:
                    return new RuleOutcome(RuleOutcomeKind.BlockedByRuleError, null, false, null, null, null, messages, errorCount, selection.BlockingErrors.Count);
                case WinnerSelectionOutcome.Won:
                {
                    var winner = selection.Winner;
                    if (string.IsNullOrEmpty(winner.Address))
                    {
                        messages.Add($"{winner.DescribeSource()} returned a null/empty address.");
                        return new RuleOutcome(RuleOutcomeKind.InvalidAddress, null, false, null, null, winner.DescribeSource(), messages, errorCount);
                    }

                    var usesDefaultGroup = IsUnresolvedDefaultGroup(winner.GroupName);
                    return new RuleOutcome(
                        RuleOutcomeKind.Address,
                        usesDefaultGroup ? null : winner.GroupName,
                        usesDefaultGroup,
                        winner.Address,
                        SortedLabels(WinnerSelector.ComposeFinalLabels(resolution, winner)),
                        winner.DescribeSource(),
                        messages,
                        errorCount);
                }
                default:
                    throw new InvalidOperationException($"Unexpected winner selection outcome: {selection.Outcome}");
            }
        }

        private static List<string> SortedLabels(IEnumerable<string> labels)
        {
            var sorted = new List<string>(labels);
            sorted.Sort(StringComparer.Ordinal);
            return sorted;
        }

        private static List<string> Compare(RuleExpectation expected, RuleOutcome actual)
        {
            var differences = new List<string>();

            // Kind 省略時の推論。Address 系の項目があれば Address、Labels だけなら「None 以外（Address か LabelsOnly）」、
            // 何も無ければ None。
            RuleOutcomeKind expectedKind;
            if (expected.Kind.HasValue)
                expectedKind = expected.Kind.Value;
            else if (expected.Address != null || expected.Group != null || expected.UsesDefaultGroup == true)
                expectedKind = RuleOutcomeKind.Address;
            else
                expectedKind = RuleOutcomeKind.None;

            var expectsAnyMatch = !expected.Kind.HasValue
                && expectedKind == RuleOutcomeKind.None
                && expected.Labels != null && expected.Labels.Count > 0;
            if (expectsAnyMatch)
            {
                if (actual.Kind != RuleOutcomeKind.Address && actual.Kind != RuleOutcomeKind.LabelsOnly)
                    differences.Add($"expected a match (Address or LabelsOnly) but was {actual.Kind}");
            }
            else if (actual.Kind != expectedKind)
            {
                differences.Add($"expected {expectedKind} but was {actual.Kind}");
            }

            if (expected.Group != null && expected.Group != actual.GroupName)
            {
                var actualGroup = actual.UsesDefaultGroup ? "(Default Group)" : actual.GroupName ?? "(none)";
                differences.Add($"expected group '{expected.Group}' but was '{actualGroup}'");
            }

            if (expected.UsesDefaultGroup.HasValue && expected.UsesDefaultGroup.Value != actual.UsesDefaultGroup)
                differences.Add(expected.UsesDefaultGroup.Value
                    ? $"expected the default group but was '{actual.GroupName ?? "(none)"}'"
                    : "expected a named group but the default group was used");

            if (expected.Address != null && !string.Equals(expected.Address, actual.Address, StringComparison.Ordinal))
                differences.Add($"expected address '{expected.Address}' but was '{actual.Address ?? "(none)"}'");

            if (expected.Labels != null)
            {
                var expectedLabels = new SortedSet<string>(expected.Labels, StringComparer.Ordinal);
                if (!expectedLabels.SetEquals(actual.Labels))
                    differences.Add($"expected labels [{string.Join(", ", expectedLabels)}] but was [{string.Join(", ", actual.Labels)}]");
            }

            // ルール例外は原則として失敗にする。ただし BlockedByRuleError を期待する行では、勝者を止めた例外は
            // 期待どおりなので数えない（それ以外の例外は失敗のまま）。
            var expectedErrors = expectedKind == RuleOutcomeKind.BlockedByRuleError && actual.Kind == RuleOutcomeKind.BlockedByRuleError
                ? actual.BlockingErrorCount
                : 0;
            if (actual.RuleErrorCount > expectedErrors)
                differences.Add("a rule threw during evaluation");

            return differences;
        }

        /// <summary>期待値の行が自己矛盾していれば、その理由を返す（矛盾が無ければ null）。</summary>
        private static string FindContradiction(RuleExpectation e)
        {
            if (e.Group != null && e.UsesDefaultGroup == true)
                return "Group and UsesDefaultGroup = true cannot both hold.";

            if (e.Kind.HasValue && e.Kind.Value != RuleOutcomeKind.Address)
            {
                if (e.Address != null || e.Group != null || e.UsesDefaultGroup == true)
                    return $"Address, Group and UsesDefaultGroup = true only apply to an Address outcome, but Kind is {e.Kind.Value}.";
                if (e.Kind.Value != RuleOutcomeKind.LabelsOnly && e.Labels != null && e.Labels.Count > 0)
                    return $"no labels are written for a {e.Kind.Value} outcome, but Labels is not empty.";
            }
            return null;
        }
    }
}
