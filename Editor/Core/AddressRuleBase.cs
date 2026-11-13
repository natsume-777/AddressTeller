using System;

namespace AddressTeller
{
    /// <summary>
    /// Base class for rule definitions. Consumers derive from this to describe address/label
    /// assignment rules in C#.
    /// </summary>
    public abstract class AddressRuleBase
    {
        /// <summary>
        /// Priority for this rule class. Rules are evaluated in ascending <see cref="Order"/>, and it also
        /// doubles as the priority used to resolve address conflicts: when two or more matching rules
        /// produce an address for the same asset, the one with the lowest <see cref="Order"/> wins and its
        /// address is written — this is how a broad, low-priority rule can be overridden by a narrower,
        /// high-priority one for special cases. It is a conflict only when two or more of the
        /// lowest-value matches tie.
        /// </summary>
        public virtual int Order => 0;

        /// <summary>Builds the rules for this class.</summary>
        public abstract void Configure(IAddressRuleBuilder rules);
    }

    /// <summary>
    /// Builder used to assemble rules. Call Group() to add a group rule.
    /// </summary>
    public interface IAddressRuleBuilder
    {
        /// <summary>
        /// Adds a rule scoped to the Addressables group named <paramref name="groupName"/>.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="groupName"/> is null or empty, or contains <c>/</c> or <c>\</c>. Those two
        /// characters are rejected because Addressables itself replaces them with <c>-</c> when a group
        /// is actually created or renamed, so a rule referencing the un-replaced name would never match
        /// the group Addressables ends up with.
        /// </exception>
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
        /// address assigned elsewhere. If the asset has an existing entry, labels are added to it
        /// regardless of which group that entry belongs to. If the asset has no entry yet, nothing
        /// happens — this never creates a new entry.
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

        /// <summary>
        /// Like <see cref="Where(AssetCondition)"/>, but reports <paramref name="description"/> instead of the
        /// condition's own <see cref="AssetCondition.Description"/> (null means the rule has no description).
        /// Everything else the condition declares (its folders and exclusions) is kept, which is the difference
        /// from passing <c>condition</c> as a plain predicate: a <see cref="Match"/> helper converts implicitly to
        /// a <c>Func&lt;AssetContext, bool&gt;</c>, so <c>Where(Match.InFolder(...), "text")</c> is this overload,
        /// not the predicate one. The description is only the text shown for this rule; it does not change how the
        /// condition composes with <c>And</c> / <c>Or</c> / <c>Not</c>, and it is not parsed. May be called at most
        /// once per <see cref="IAddressRuleBuilder.AnyGroup"/> call (shared limit with the other Where() overloads). A second call throws
        /// <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="condition"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Where() has already been called once for this rule.</exception>
        ILabelRuleBuilder Where(AssetCondition condition, string description);

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
        /// Like <see cref="Where(AssetCondition)"/>, but reports <paramref name="description"/> instead of the
        /// condition's own <see cref="AssetCondition.Description"/> (null means the rule has no description).
        /// Everything else the condition declares (its folders and exclusions) is kept, which is the difference
        /// from passing <c>condition</c> as a plain predicate: a <see cref="Match"/> helper converts implicitly to
        /// a <c>Func&lt;AssetContext, bool&gt;</c>, so <c>Where(Match.InFolder(...), "text")</c> is this overload,
        /// not the predicate one. The description is only the text shown for this rule; it does not change how the
        /// condition composes with <c>And</c> / <c>Or</c> / <c>Not</c>, and it is not parsed. May be called at most
        /// once per group (shared limit with the other Where() overloads). A second call throws
        /// <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="condition"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Where() has already been called once for this group.</exception>
        IAddressRuleGroupBuilder Where(AssetCondition condition, string description);

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

        /// <summary>
        /// Declares that this rule's address is meant to be overridden by the rule class <typeparamref name="TRule"/>:
        /// when both match an asset and <typeparamref name="TRule"/> wins on <see cref="AddressRuleBase.Order"/>, the
        /// overlap is intended. It does not change which rule wins or what is written. It only marks the pair
        /// (this rule class, <typeparamref name="TRule"/>) as expected, so that with the "Strict rule overlaps"
        /// setting on, <c>ValidationStatus.UndeclaredRuleOverlap</c> is reported only for overlaps that
        /// were not declared this way. May be called several times to allow several rules; repeating the same
        /// <typeparamref name="TRule"/> has no further effect. The declaration belongs to this
        /// <c>Group()</c> chain, not to the whole rule class. Only meaningful on a chain that produces an address
        /// (one that calls <c>Address()</c>); overlaps are about addresses, so a label-only chain never has one.
        /// Pass the concrete rule class. The pair is matched by the class's short type name, so two rule classes with
        /// the same name in different namespaces cannot be told apart. A declaration that matches no overlap
        /// (for example after the other rule changed) is not reported.
        /// </summary>
        /// <typeparam name="TRule">The rule class that is allowed to override this rule.</typeparam>
        IAddressRuleGroupBuilder OverriddenBy<TRule>() where TRule : AddressRuleBase;
    }
}
