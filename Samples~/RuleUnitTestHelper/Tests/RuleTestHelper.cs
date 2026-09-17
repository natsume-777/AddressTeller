using System;
using System.Collections.Generic;
using AddressTeller;
using AddressTeller.Testing;

namespace AddressTellerSamples
{
    /// <summary>
    /// Utilities for unit-testing AddressRuleBase subclasses.
    /// Depends only on AddressTeller's public API — no Addressables project required.
    /// </summary>
    public static class RuleTestHelper
    {
        /// <summary>
        /// Returns true if <paramref name="groupName"/> (the value of <see cref="AddressRuleEntry.GroupName"/>)
        /// is the unresolved sentinel produced by <see cref="IAddressRuleBuilder.GroupDefault"/>.
        /// Use this instead of comparing against a hard-coded string; the real sentinel value is an internal
        /// implementation detail of the package and may change between versions.
        /// </summary>
        public static bool IsUnresolvedDefaultGroup(string groupName) => RuleInspector.IsUnresolvedDefaultGroup(groupName);

        /// <summary>
        /// Formats <paramref name="groupName"/> for display or diagnostic messages. Converts the unresolved
        /// <see cref="IAddressRuleBuilder.GroupDefault"/> sentinel into the human-readable placeholder
        /// "(Default Group)"; any other value is returned unchanged. Use this whenever a group name is
        /// included in an assertion message or log output, since the raw sentinel contains unprintable
        /// control characters. This is for display only — do not use it for equality comparisons.
        /// </summary>
        public static string DisplayGroupName(string groupName) => RuleInspector.DisplayGroupName(groupName);

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
        /// Runs <see cref="AddressRuleBase.Configure"/> and returns all collected <see cref="AddressRuleEntry"/>
        /// objects. Delegates to <see cref="RuleInspector.Collect"/>, so it shares the exact same builder
        /// contract as the package's own evaluation pipeline: calling <c>Where()</c> or <c>Address()</c> a
        /// second time on the same group throws <see cref="InvalidOperationException"/>. Note that the
        /// *handling* of that exception differs from the package's own evaluation pipeline
        /// (<c>RuleEvaluationPipeline</c>), which catches it per-rule and skips the failing rule instead of
        /// propagating it — here, the exception propagates out of this call unchanged, so a misused builder
        /// call fails the test directly.
        /// Evaluate entries with <c>entry.Predicate(ctx)</c>, <c>entry.AddressSelector?.Invoke(ctx)</c>,
        /// and <c>entry.LabelSelectors</c>. When <paramref name="rule"/> may see folder AssetContexts
        /// (<c>AssetContext.IsFolder</c> true), check <see cref="AddressRuleEntry.IncludesFolders"/> before
        /// calling Predicate: the production evaluator never invokes Predicate for a folder unless the
        /// entry opted in via IncludeFolders() on the builder, and a hand-rolled evaluation loop that skips
        /// this check can call into a rule's Predicate with a folder it was never written to handle.
        /// </summary>
        public static IReadOnlyList<AddressRuleEntry> Collect(AddressRuleBase rule) => RuleInspector.Collect(rule);
    }
}
