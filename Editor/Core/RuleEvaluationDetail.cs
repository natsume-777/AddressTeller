using System;
using System.Collections.Generic;

namespace Natsume777.AddressTeller
{
    /// <summary>
    /// ルール1件×アセット1件の評価結果。Explain 機能で各ルールの判定理由を表示するために使う。
    /// </summary>
    public readonly struct RuleEvaluationDetail
    {
        /// <summary>このルールの識別文字列（<see cref="AddressRuleEntry.DescribeSource"/> の結果）。</summary>
        public string RuleSource { get; }

        public string GroupName { get; }

        /// <summary>Where(predicate, description) の description。指定されていない場合は null。</summary>
        public string Description { get; }

        public RuleMatchOutcome Outcome { get; }

        /// <summary>Matched かつ AddressSelector が指定されている場合のアドレス。それ以外は null。</summary>
        public string ProducedAddress { get; }

        /// <summary>Matched 時に発行されたラベル。マッチしていない／ラベル指定がない場合は空リスト。</summary>
        public IReadOnlyList<string> ProducedLabels { get; }

        /// <summary>Errored の場合の例外メッセージ。それ以外は null。</summary>
        public string ErrorMessage { get; }

        public RuleEvaluationDetail(
            string ruleSource,
            string groupName,
            string description,
            RuleMatchOutcome outcome,
            string producedAddress,
            IReadOnlyList<string> producedLabels,
            string errorMessage)
        {
            RuleSource = ruleSource;
            GroupName = groupName;
            Description = description;
            Outcome = outcome;
            ProducedAddress = producedAddress;
            ProducedLabels = producedLabels ?? Array.Empty<string>();
            ErrorMessage = errorMessage;
        }
    }
}
