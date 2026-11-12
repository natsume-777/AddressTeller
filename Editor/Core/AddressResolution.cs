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

        private readonly IReadOnlyList<string> _labels;

        /// <summary>
        /// Labels produced by the rule that produced this candidate. They are written to the asset only when
        /// this candidate wins; a candidate that loses on <see cref="Order"/> contributes no labels. Never null;
        /// contains no duplicates and is in the order the rule's <c>Label()</c> calls produced them.
        /// </summary>
        public IReadOnlyList<string> Labels => _labels ?? Array.Empty<string>();

        /// <summary>
        /// この候補を出したエントリが <c>OverriddenBy&lt;TRule&gt;()</c> で許可したルールクラス名（null は無し）。
        /// 重なりの集計（<see cref="RuleOverlapCollector"/>）が、負けた候補の上書きが宣言済みかを判定するために使う。
        /// </summary>
        internal IReadOnlyList<string> OverriddenBy { get; }

        /// <summary>
        /// 公開コンストラクタではなく internal（ライブラリ内部のルール評価エンジンからのみ構築される想定）。
        /// テストからは <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> 経由で参照する。
        /// <paramref name="labels"/> は、このエントリが候補と同時に出したラベル（null は空）。
        /// </summary>
        internal AddressCandidate(string groupName, string address, string sourceClass = null, string description = null, int ruleIndex = 0, int order = 0, IReadOnlyList<string> labels = null, IReadOnlyList<string> overriddenBy = null)
        {
            GroupName = groupName;
            Address = address;
            SourceClass = sourceClass;
            Description = description;
            RuleIndex = ruleIndex;
            Order = order;
            _labels = labels;
            OverriddenBy = overriddenBy;
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
        /// 例外を送出したルールの <see cref="AddressRuleBase.Order"/>。WinnerSelector.Select が、
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
    /// 2件以上（同点）のときのみ競合になる（WinnerSelector.Select 参照）。0 件でも LabelOnlyLabels が
    /// 1件以上あればラベルのみルールがマッチしている（AddressTellerApplier.Validate の LabelsOnly 判定）。
    /// AddressCandidates も LabelOnlyLabels も 0 件のときのみ、このアセットはどのルールにもマッチしていない（対象外）。
    /// 書き込む最終ラベル集合は LabelOnlyLabels と勝者候補の Labels の和集合で、<see cref="WinnerSelector.ComposeFinalLabels"/> が
    /// 合成する。負けた候補のラベルは含まれない。
    /// </summary>
    internal sealed class AddressResolution
    {
        /// <summary>アドレスを発行したルールのグループ名＋アドレスのリスト。</summary>
        public IReadOnlyList<AddressCandidate> AddressCandidates { get; }

        /// <summary>
        /// ラベル専用エントリ（AddressSelector == null。AnyGroup() や Address() を呼ばない Group()）由来のラベル。
        /// 勝敗と無関係に常に書き込まれる。アドレス候補を出したエントリのラベルは各 <see cref="AddressCandidate.Labels"/> が持つ。
        /// </summary>
        public IReadOnlyCollection<string> LabelOnlyLabels { get; }

        /// <summary>評価中に発生したルール例外のリスト。</summary>
        public IReadOnlyList<RuleEvaluationError> Errors { get; }

        public AddressResolution(
            IReadOnlyList<AddressCandidate> candidates,
            IReadOnlyCollection<string> labelOnlyLabels,
            IReadOnlyList<RuleEvaluationError> errors = null)
        {
            AddressCandidates = candidates;
            LabelOnlyLabels = labelOnlyLabels;
            Errors = errors ?? Array.Empty<RuleEvaluationError>();
        }
    }
}
