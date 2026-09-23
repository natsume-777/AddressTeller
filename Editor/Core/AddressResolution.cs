using System;
using System.Collections.Generic;

namespace AddressTeller
{
    /// <summary>
    /// One address candidate (group name + address string).
    /// </summary>
    public readonly struct AddressCandidate
    {
        /// <summary>Name of the group this candidate targets.</summary>
        public string GroupName { get; }

        /// <summary>The address string produced by the matching rule.</summary>
        public string Address { get; }

        /// <summary>Name of the AddressRuleBase subclass that produced this candidate.</summary>
        public string SourceClass { get; }

        /// <summary>Description passed to Where, or null if none was given.</summary>
        public string Description { get; }

        /// <summary>Order (zero-based) in which Group() was called within Configure().</summary>
        public int RuleIndex { get; }

        /// <summary>
        /// The <see cref="AddressRuleBase.Order"/> of the rule class that produced this candidate. When two
        /// or more matching rules produce an address for the same asset, the candidate with the lowest
        /// <see cref="Order"/> wins; it is a conflict only when two or more of the lowest-value candidates
        /// tie.
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// 公開コンストラクタではなく internal（ライブラリ内部のルール評価エンジンからのみ構築される想定）。
        /// テストからは <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> 経由で参照する。
        /// </summary>
        internal AddressCandidate(string groupName, string address, string sourceClass = null, string description = null, int ruleIndex = 0, int order = 0)
        {
            GroupName = groupName;
            Address = address;
            SourceClass = sourceClass;
            Description = description;
            RuleIndex = ruleIndex;
            Order = order;
        }

        /// <summary>Identifier string for error messages, e.g. <c>MyRule &gt; "InFolder(Assets/Characters)"</c>.</summary>
        public string DescribeSource() => AddressRuleEntry.DescribeSource(SourceClass, Description, RuleIndex);
    }

    /// <summary>
    /// ルール評価中に Predicate / AddressSelector / LabelSelector が送出した例外1件。
    /// </summary>
    internal readonly struct RuleEvaluationError
    {
        /// <summary>例外を送出したルールの識別文字列。</summary>
        public string RuleSource { get; }

        /// <summary>例外メッセージ。</summary>
        public string Message { get; }

        /// <summary>
        /// 例外を送出したルールの <see cref="AddressRuleBase.Order"/>。AddressTellerApplier.Validate が、
        /// 勝者候補の Order 以下（同点含む）のアドレス産出ルールに例外があったかを判定するために使う。
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// 例外を送出したルールがアドレスを出しうるか（<c>AddressSelector != null</c>）。例外の発生箇所が
        /// Predicate / AddressSelector / LabelSelector のいずれであっても、このエントリ自体がアドレスを
        /// 産出しうるルールかどうかで判定する（ラベル専用ルールの例外は書き込みを止めないため）。
        /// </summary>
        public bool CanProduceAddress { get; }

        public RuleEvaluationError(string ruleSource, string message, int order, bool canProduceAddress)
        {
            RuleSource = ruleSource;
            Message = message;
            Order = order;
            CanProduceAddress = canProduceAddress;
        }
    }

    /// <summary>
    /// 1アセットに対するルール評価結果。
    /// AddressCandidates が2件以上のとき、Order が最小の候補が単独ならそれを採用し、最小値の候補が
    /// 2件以上（同点）のときのみ競合になる（AddressTellerApplier.Validate 参照）。0 件でも Labels が
    /// 1件以上あればラベルのみルールがマッチしている（AddressTellerApplier.Validate の LabelsOnly 判定）。
    /// AddressCandidates も Labels も 0 件のときのみ、このアセットはどのルールにもマッチしていない（対象外）。
    /// Labels は全マッチルールから蓄積される（Order による優先順位の影響を受けない）。
    /// </summary>
    internal sealed class AddressResolution
    {
        /// <summary>アドレスを発行したルールのグループ名＋アドレスのリスト。</summary>
        public IReadOnlyList<AddressCandidate> AddressCandidates { get; }

        /// <summary>全マッチルールから蓄積されたラベルセット。</summary>
        public IReadOnlyCollection<string> Labels { get; }

        /// <summary>評価中に発生したルール例外のリスト。</summary>
        public IReadOnlyList<RuleEvaluationError> Errors { get; }

        public AddressResolution(
            IReadOnlyList<AddressCandidate> candidates,
            IReadOnlyCollection<string> labels,
            IReadOnlyList<RuleEvaluationError> errors = null)
        {
            AddressCandidates = candidates;
            Labels = labels;
            Errors = errors ?? Array.Empty<RuleEvaluationError>();
        }
    }
}
