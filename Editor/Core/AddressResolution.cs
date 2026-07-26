using System;
using System.Collections.Generic;

namespace AddressTeller
{
    /// <summary>
    /// アドレス候補1件（グループ名 + アドレス文字列）。
    /// </summary>
    public readonly struct AddressCandidate
    {
        public string GroupName { get; }
        public string Address { get; }
        public string SourceClass { get; }
        public string Description { get; }

        /// <summary>Configure() 内で Group() が呼ばれた順序（0始まり）。</summary>
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

        /// <summary>エラーメッセージ表示用の識別文字列（"{SourceClass} > \"{Description}\"" など）。</summary>
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
