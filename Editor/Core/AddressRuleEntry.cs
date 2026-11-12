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
        /// このエントリを生成したルールクラスの <see cref="AddressRuleBase.Order"/>。ライブラリ内部の評価
        /// パイプライン（<see cref="AddressRuleBuilderImpl"/> 経由）でのみ設定される。公開コンストラクタからは
        /// 設定できない（既定値 0）ため、利用者が独自に構築したエントリでは常に 0 になる。
        /// </summary>
        internal int Order { get; }

        /// <summary>
        /// True when this rule opted in to seeing folder assets (via IncludeFolders() on the builder).
        /// Not part of the public constructor: it is only ever set by AddressRuleBuilderImpl, which is
        /// the sole producer of entries that rule evaluation consumes. Consumers that build their own
        /// evaluation loop over collected entries (e.g. unit-testing helpers) must check this before
        /// invoking Predicate for a folder AssetContext, to match the production evaluator's behavior:
        /// a rule that has not opted in never sees folders, and its Predicate is not even called for one.
        /// </summary>
        public bool IncludesFolders { get; }

        /// <summary>
        /// Folders this rule's condition depends on (see <see cref="AssetCondition.Folders"/>): project-relative
        /// paths with "/" separators and no trailing "/". Empty when the rule's Where() was given a raw
        /// lambda or no condition. For entries evaluated by AddressTeller, folders declared by GUID
        /// (<see cref="Match.InFolderByGuid"/>) are included here as the paths they resolved to.
        /// </summary>
        public IReadOnlyList<string> DeclaredFolders { get; }

        /// <summary>
        /// GUID で宣言されたフォルダ。BuildSetup が解決し、解決後のパスを <see cref="DeclaredFolders"/> に加えた
        /// エントリへ差し替える（<see cref="WithDeclaredFolders"/>）。
        /// </summary>
        internal IReadOnlyList<FolderGuidDeclaration> FolderGuids { get; }

        /// <summary>Creates an AddressRuleEntry. See the properties above for each parameter's meaning.</summary>
        public AddressRuleEntry(
            string groupName,
            Func<AssetContext, bool> predicate,
            Func<AssetContext, string> addressSelector,
            IReadOnlyList<Func<AssetContext, string>> labelSelectors,
            string sourceClass = null,
            string description = null,
            int ruleIndex = 0,
            IReadOnlyList<string> declaredFolders = null)
            : this(groupName, predicate, addressSelector, labelSelectors, sourceClass, description, ruleIndex, includesFolders: false, order: 0, declaredFolders: declaredFolders, folderGuids: null)
        {
        }

        /// <summary>
        /// internal 用オーバーロード。IncludeFolders() を宣言したビルダー（AddressRuleBuilderImpl）と、
        /// GroupDefault() のセンチネル解決時・GUID フォルダ解決時の再構築（RuleEvaluationPipeline.BuildSetup）が使う。
        /// </summary>
        internal AddressRuleEntry(
            string groupName,
            Func<AssetContext, bool> predicate,
            Func<AssetContext, string> addressSelector,
            IReadOnlyList<Func<AssetContext, string>> labelSelectors,
            string sourceClass,
            string description,
            int ruleIndex,
            bool includesFolders,
            int order,
            IReadOnlyList<string> declaredFolders = null,
            IReadOnlyList<FolderGuidDeclaration> folderGuids = null)
        {
            DeclaredFolders = AssetCondition.NormalizeFolders(declaredFolders);
            FolderGuids = folderGuids ?? Array.Empty<FolderGuidDeclaration>();
            // groupName は AnyGroup() 由来のラベル専用エントリでは null を許容する。
            GroupName = groupName;
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            AddressSelector = addressSelector;
            LabelSelectors = labelSelectors ?? Array.Empty<Func<AssetContext, string>>();
            SourceClass = sourceClass;
            Description = description;
            RuleIndex = ruleIndex;
            IncludesFolders = includesFolders;
            Order = order;
        }

        /// <summary>グループ名だけを差し替えた複製を返す（GroupDefault() のセンチネル解決用）。</summary>
        internal AddressRuleEntry WithGroupName(string groupName)
            => new AddressRuleEntry(groupName, Predicate, AddressSelector, LabelSelectors, SourceClass, Description, RuleIndex, IncludesFolders, Order, DeclaredFolders, FolderGuids);

        /// <summary>宣言フォルダだけを差し替えた複製を返す（GUID で宣言されたフォルダの解決結果を反映する用）。</summary>
        internal AddressRuleEntry WithDeclaredFolders(IReadOnlyList<string> declaredFolders)
            => new AddressRuleEntry(GroupName, Predicate, AddressSelector, LabelSelectors, SourceClass, Description, RuleIndex, IncludesFolders, Order, declaredFolders, FolderGuids);

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
