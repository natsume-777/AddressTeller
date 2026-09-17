using System;

namespace AddressTeller
{
    /// <summary>
    /// Base class for rule definitions. Consumers derive from this to describe address/label
    /// assignment rules in C#.
    /// </summary>
    public abstract class AddressRuleBase
    {
        /// <summary>Evaluation order. Lower values are evaluated first.</summary>
        public virtual int Order => 0;

        /// <summary>Builds the rules for this class.</summary>
        public abstract void Configure(IAddressRuleBuilder rules);
    }

    /// <summary>
    /// Builder used to assemble rules. Call Group() to add a group rule.
    /// </summary>
    public interface IAddressRuleBuilder
    {
        /// <summary>Adds a rule scoped to the Addressables group named <paramref name="groupName"/>.</summary>
        /// <exception cref="ArgumentException"><paramref name="groupName"/> is null or empty.</exception>
        IAddressRuleGroupBuilder Group(string groupName);

        /// <summary>
        /// Assigns an address/label to the Addressables DefaultGroup. The group name is resolved from
        /// <c>AddressableAssetSettings.DefaultGroup</c> at evaluation time, so renaming the DefaultGroup
        /// is followed automatically. Where / Address / Label can be chained the same way as
        /// <see cref="Group(string)"/>.
        /// </summary>
        IAddressRuleGroupBuilder GroupDefault();

        /// <summary>
        /// Adds a label-only rule that is not scoped to any group.
        /// Use this to attach labels (by path or other conditions) to assets that already have an
        /// address assigned elsewhere. Labels are only actually written when the asset's existing entry
        /// belongs to a group managed by AddressTeller (a group referenced by at least one rule); entries
        /// in unmanaged groups are left untouched.
        /// </summary>
        ILabelRuleBuilder AnyGroup();
    }

    /// <summary>
    /// Label-only builder returned by <see cref="IAddressRuleBuilder.AnyGroup"/>.
    /// There is no Address(); only Where / Label can be specified.
    /// </summary>
    public interface ILabelRuleBuilder
    {
        /// <summary>
        /// Restricts this rule to assets matching <paramref name="predicate"/>.
        /// May be called at most once per <see cref="IAddressRuleBuilder.AnyGroup"/> call. Combine multiple
        /// conditions into a single lambda using &amp;&amp;. A second call throws
        /// <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Where() has already been called once for this rule.</exception>
        ILabelRuleBuilder Where(Func<AssetContext, bool> predicate);

        /// <summary>
        /// Restricts this rule to assets matching <paramref name="predicate"/>. <paramref name="description"/>
        /// is used in error messages to identify which Where condition matched.
        /// May be called at most once per <see cref="IAddressRuleBuilder.AnyGroup"/> call (shared limit with
        /// the other Where() overloads). A second call throws <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Where() has already been called once for this rule.</exception>
        ILabelRuleBuilder Where(Func<AssetContext, bool> predicate, string description);

        /// <summary>
        /// Restricts this rule using an <see cref="AssetCondition"/> (predicate + description).
        /// May be called at most once per <see cref="IAddressRuleBuilder.AnyGroup"/> call (shared limit with
        /// the other Where() overloads). A second call throws <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="condition"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Where() has already been called once for this rule.</exception>
        ILabelRuleBuilder Where(AssetCondition condition);

        /// <summary>Adds a label produced by <paramref name="selector"/> for each matching asset.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> is null.</exception>
        ILabelRuleBuilder Label(Func<AssetContext, string> selector);

        /// <summary>Adds the fixed label <paramref name="label"/> for each matching asset.</summary>
        /// <exception cref="ArgumentException"><paramref name="label"/> is null or empty.</exception>
        ILabelRuleBuilder Label(string label);

        /// <summary>
        /// Opts this rule in to seeing folder assets. Without this call, Where() is never invoked for a
        /// folder and this rule cannot match one. A folder entry, once created, implicitly covers every
        /// asset beneath it as far as Addressables is concerned, and labels assigned to the folder are
        /// inherited by those assets. May be called at most once per <see cref="IAddressRuleBuilder.AnyGroup"/>
        /// call; a second call throws <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">IncludeFolders() has already been called once for this rule.</exception>
        ILabelRuleBuilder IncludeFolders();
    }

    /// <summary>
    /// Per-group rule configuration. Where / Address / Label are chained to describe the rule.
    /// </summary>
    public interface IAddressRuleGroupBuilder
    {
        /// <summary>
        /// May be called at most once per group. Combine multiple conditions into a single lambda
        /// using &amp;&amp;. A second call throws <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Where() has already been called once for this group.</exception>
        IAddressRuleGroupBuilder Where(Func<AssetContext, bool> predicate);

        /// <summary>
        /// May be called at most once per group (shared limit with the other Where() overload).
        /// <paramref name="description"/> is used in error messages to identify which Where condition
        /// matched. A second call throws <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Where() has already been called once for this group.</exception>
        IAddressRuleGroupBuilder Where(Func<AssetContext, bool> predicate, string description);

        /// <summary>
        /// Where specified via an <see cref="AssetCondition"/>; its Predicate and Description are used
        /// as-is. May be called at most once per group (shared limit with the other Where() overloads).
        /// A second call throws <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="condition"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Where() has already been called once for this group.</exception>
        IAddressRuleGroupBuilder Where(AssetCondition condition);

        /// <summary>
        /// May be called at most once per group (shared limit with the other Address() overload).
        /// A second call throws <see cref="InvalidOperationException"/> instead of silently overwriting.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Address() has already been called once for this group.</exception>
        IAddressRuleGroupBuilder Address(Func<AssetContext, string> selector);

        /// <summary>
        /// May be called at most once per group (shared limit with the other Address() overload).
        /// A second call throws <see cref="InvalidOperationException"/> instead of silently overwriting.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="address"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Address() has already been called once for this group.</exception>
        IAddressRuleGroupBuilder Address(string address);

        /// <summary>Adds a label produced by <paramref name="selector"/> for each matching asset.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> is null.</exception>
        IAddressRuleGroupBuilder Label(Func<AssetContext, string> selector);

        /// <summary>Adds the fixed label <paramref name="label"/> for each matching asset.</summary>
        /// <exception cref="ArgumentException"><paramref name="label"/> is null or empty.</exception>
        IAddressRuleGroupBuilder Label(string label);

        /// <summary>
        /// Opts this rule in to seeing folder assets. Without this call, Where() is never invoked for a
        /// folder and this rule cannot match one. A folder entry, once created, implicitly covers every
        /// asset beneath it as far as Addressables is concerned, and labels assigned to the folder are
        /// inherited by those assets. May be called at most once per group; a second call throws
        /// <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">IncludeFolders() has already been called once for this group.</exception>
        IAddressRuleGroupBuilder IncludeFolders();
    }
}
