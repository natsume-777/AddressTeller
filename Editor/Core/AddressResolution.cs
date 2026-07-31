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
        /// 公開コンストラクタではなく internal（ライブラリ内部のルール評価エンジンからのみ構築される想定）。
        /// テストからは <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> 経由で参照する。
        /// </summary>
        internal AddressCandidate(string groupName, string address, string sourceClass = null, string description = null, int ruleIndex = 0)
        {
            GroupName = groupName;
            Address = address;
            SourceClass = sourceClass;
            Description = description;
            RuleIndex = ruleIndex;
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

        public RuleEvaluationError(string ruleSource, string message)
        {
            RuleSource = ruleSource;
            Message = message;
        }
    }

    /// <summary>
    /// 1アセットに対するルール評価結果。
    /// AddressCandidates が 2 件以上のとき競合。0 件でも Labels が1件以上あればラベルのみルールがマッチしている
    /// （AddressTellerApplier.Validate の LabelsOnly 判定）。AddressCandidates も Labels も 0 件のときのみ、
    /// このアセットはどのルールにもマッチしていない（対象外）。
    /// Labels は全マッチルールから蓄積される。
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
