using System;
using System.Collections.Generic;

namespace AddressTeller
{
    internal sealed class AddressRuleBuilderImpl : IAddressRuleBuilder
    {
        /// <summary>
        /// GroupDefault() の内部表現として使う予約文字列。通常のグループ名と衝突しないよう
        /// NUL 文字を含む。RuleEvaluationPipeline.BuildSetup でループ開始前に
        /// AddressableAssetSettings.DefaultGroup.Name へ正規化される。
        /// </summary>
        internal const string DefaultGroupSentinel = "\0AddressTeller.DefaultGroup\0";

        /// <summary>
        /// 表示用にグループ名を整形する。<see cref="DefaultGroupSentinel"/> を解決できなかった場合
        /// （評価結果が <c>ValidationStatus.DefaultGroupUnavailable</c> 相当になったケース）に、
        /// 生のセンチネル文字列を画面へ漏出させないための共通ヘルパー。
        /// </summary>
        internal static string DisplayGroupName(string groupName)
            => groupName == DefaultGroupSentinel ? "(Default Group)" : groupName;

        private readonly string _sourceClass;

        /// <summary>
        /// このビルダーが属するルールクラスの AddressRuleBase.Order。生成する全エントリに刻印し、
        /// AddressCandidate まで運んで AddressTellerApplier の優先順位判定に使う。
        /// </summary>
        private readonly int _order;

        private readonly List<IEntryBuilder> _builders = new List<IEntryBuilder>();

        public AddressRuleBuilderImpl(string sourceClass = null, int order = 0)
        {
            _sourceClass = sourceClass;
            _order = order;
        }

        public IReadOnlyList<AddressRuleEntry> Entries
        {
            get
            {
                var result = new AddressRuleEntry[_builders.Count];
                for (int i = 0; i < _builders.Count; i++)
                    result[i] = _builders[i].Build(i);
                return result;
            }
        }

        public IAddressRuleGroupBuilder Group(string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) throw new ArgumentException("groupName must not be empty.", nameof(groupName));
            // Addressables はグループの作成・改名時に '/' '\' を '-' へ置き換える（グループ名がファイル名の
            // 一部として使われるため）。FindGroup は完全一致でしか探さないので、置換前の名前をルールに
            // 書いても実際に作られたグループには一生マッチしない。Auto-create ON の場合はさらに悪く、
            // マッチしないまま毎回 EnsureGroup が新規グループを作ろうとして増殖する。ここで早期に拒否する。
            if (groupName.IndexOf('/') >= 0 || groupName.IndexOf('\\') >= 0)
            {
                // 実際に Addressables が使う名前を提示する。ただし同名グループが既に存在する場合は
                // Addressables 側でさらに連番が付くことがあり、ここではその実際の付番結果までは
                // 分からないため、'likely' で言い切らずぼかす。
                var replaced = groupName.Replace('/', '-').Replace('\\', '-');
                throw new ArgumentException(
                    $"groupName '{groupName}' must not contain '/' or '\\'. Addressables replaces those " +
                    "characters with '-' when it creates or renames a group, so a rule referencing the " +
                    $"un-replaced name would never match. Did you mean '{replaced}'? (Addressables' actual " +
                    "name is likely this, but may differ, e.g. with a numeric suffix, if a group with that " +
                    "name already exists.)",
                    nameof(groupName));
            }
            return AddGroupBuilder(groupName);
        }

        public IAddressRuleGroupBuilder GroupDefault()
        {
            return AddGroupBuilder(DefaultGroupSentinel);
        }

        private IAddressRuleGroupBuilder AddGroupBuilder(string groupName)
        {
            var builder = new AddressRuleGroupBuilder(groupName, _sourceClass, _order);
            _builders.Add(builder);
            return builder;
        }

        public ILabelRuleBuilder AnyGroup()
        {
            var builder = new LabelRuleGroupBuilder(_sourceClass, _order);
            _builders.Add(builder);
            return builder;
        }

        private interface IEntryBuilder
        {
            AddressRuleEntry Build(int index);
        }

        private sealed class AddressRuleGroupBuilder : IEntryBuilder, IAddressRuleGroupBuilder
        {
            private readonly string _groupName;
            private readonly string _sourceClass;
            private readonly int _order;
            private string _description;
            private Func<AssetContext, bool> _predicate = _ => true;
            private bool _whereSet;
            private Func<AssetContext, string> _addressSelector;
            private bool _addressSet;
            private readonly List<Func<AssetContext, string>> _labelSelectors = new List<Func<AssetContext, string>>();
            private bool _includeFolders;
            private bool _includeFoldersSet;

            internal AddressRuleGroupBuilder(string groupName, string sourceClass, int order)
            {
                _groupName = groupName;
                _sourceClass = sourceClass;
                _order = order;
            }

            public IAddressRuleGroupBuilder Where(Func<AssetContext, bool> predicate)
            {
                if (predicate == null) throw new ArgumentNullException(nameof(predicate));
                ThrowIfWhereAlreadySet();
                _predicate = predicate;
                _whereSet = true;
                return this;
            }

            public IAddressRuleGroupBuilder Where(Func<AssetContext, bool> predicate, string description)
            {
                if (predicate == null) throw new ArgumentNullException(nameof(predicate));
                ThrowIfWhereAlreadySet();
                _predicate = predicate;
                _description = description;
                _whereSet = true;
                return this;
            }

            public IAddressRuleGroupBuilder Where(AssetCondition condition)
            {
                if (condition == null) throw new ArgumentNullException(nameof(condition));
                return Where(condition.Predicate, condition.Description);
            }

            private void ThrowIfWhereAlreadySet()
            {
                if (_whereSet)
                    throw new InvalidOperationException(
                        $"Where() can be called only once on Group(\"{_groupName}\"). " +
                        "Combine multiple conditions into a single lambda using &&.");
            }

            public IAddressRuleGroupBuilder Address(Func<AssetContext, string> selector)
            {
                ThrowIfAddressAlreadySet();
                _addressSelector = selector ?? throw new ArgumentNullException(nameof(selector));
                _addressSet = true;
                return this;
            }

            public IAddressRuleGroupBuilder Address(string address)
            {
                if (string.IsNullOrEmpty(address)) throw new ArgumentException("address must not be empty.", nameof(address));
                ThrowIfAddressAlreadySet();
                _addressSelector = _ => address;
                _addressSet = true;
                return this;
            }

            // Where() と同様、2回目の Address() 呼び出しを黙って上書きせず例外にする。
            // 「1ルールにつきアドレスは1件」という曖昧さのない状態を保証し、意図しない上書きに
            // 気づけるようにするため（design-decisions.md のアドレス衝突方針と同じ考え方）。
            private void ThrowIfAddressAlreadySet()
            {
                if (_addressSet)
                    throw new InvalidOperationException(
                        $"Address() can be called only once on Group(\"{_groupName}\"). " +
                        "Calling it again would silently overwrite the previous address.");
            }

            public IAddressRuleGroupBuilder Label(Func<AssetContext, string> selector)
            {
                _labelSelectors.Add(selector ?? throw new ArgumentNullException(nameof(selector)));
                return this;
            }

            public IAddressRuleGroupBuilder Label(string label)
            {
                if (string.IsNullOrEmpty(label)) throw new ArgumentException("label must not be empty.", nameof(label));
                _labelSelectors.Add(_ => label);
                return this;
            }

            public IAddressRuleGroupBuilder IncludeFolders()
            {
                if (_includeFoldersSet)
                    throw new InvalidOperationException(
                        $"IncludeFolders() can be called only once on Group(\"{_groupName}\").");
                _includeFolders = true;
                _includeFoldersSet = true;
                return this;
            }

            public AddressRuleEntry Build(int index)
            {
                return new AddressRuleEntry(_groupName, _predicate, _addressSelector, _labelSelectors.AsReadOnly(), _sourceClass, _description, index, _includeFolders, _order);
            }
        }

        private sealed class LabelRuleGroupBuilder : IEntryBuilder, ILabelRuleBuilder
        {
            private readonly string _sourceClass;
            private readonly int _order;
            private string _description;
            private Func<AssetContext, bool> _predicate = _ => true;
            private bool _whereSet;
            private readonly List<Func<AssetContext, string>> _labelSelectors = new List<Func<AssetContext, string>>();
            private bool _includeFolders;
            private bool _includeFoldersSet;

            internal LabelRuleGroupBuilder(string sourceClass, int order)
            {
                _sourceClass = sourceClass;
                _order = order;
            }

            public ILabelRuleBuilder Where(Func<AssetContext, bool> predicate)
            {
                if (predicate == null) throw new ArgumentNullException(nameof(predicate));
                ThrowIfWhereAlreadySet();
                _predicate = predicate;
                _whereSet = true;
                return this;
            }

            public ILabelRuleBuilder Where(Func<AssetContext, bool> predicate, string description)
            {
                if (predicate == null) throw new ArgumentNullException(nameof(predicate));
                ThrowIfWhereAlreadySet();
                _predicate = predicate;
                _description = description;
                _whereSet = true;
                return this;
            }

            public ILabelRuleBuilder Where(AssetCondition condition)
            {
                if (condition == null) throw new ArgumentNullException(nameof(condition));
                return Where(condition.Predicate, condition.Description);
            }

            private void ThrowIfWhereAlreadySet()
            {
                if (_whereSet)
                    throw new InvalidOperationException(
                        "Where() can be called only once on AnyGroup(). " +
                        "Combine multiple conditions into a single lambda using &&.");
            }

            public ILabelRuleBuilder Label(Func<AssetContext, string> selector)
            {
                _labelSelectors.Add(selector ?? throw new ArgumentNullException(nameof(selector)));
                return this;
            }

            public ILabelRuleBuilder Label(string label)
            {
                if (string.IsNullOrEmpty(label)) throw new ArgumentException("label must not be empty.", nameof(label));
                _labelSelectors.Add(_ => label);
                return this;
            }

            public ILabelRuleBuilder IncludeFolders()
            {
                if (_includeFoldersSet)
                    throw new InvalidOperationException("IncludeFolders() can be called only once on AnyGroup().");
                _includeFolders = true;
                _includeFoldersSet = true;
                return this;
            }

            public AddressRuleEntry Build(int index)
            {
                return new AddressRuleEntry(null, _predicate, null, _labelSelectors.AsReadOnly(), _sourceClass, _description, index, _includeFolders, _order);
            }
        }
    }
}
