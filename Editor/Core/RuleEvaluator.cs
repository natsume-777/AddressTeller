using System;
using System.Collections.Generic;

namespace Natsume777.AddressTeller
{
    /// <summary>
    /// AssetContext に対してルールエントリを評価し AddressResolution を返す。
    /// Addressables API に依存しないモデル層。
    /// ソート・競合チェック・書き込みは呼び出し側の責務。
    /// </summary>
    public static class RuleEvaluator
    {
        public static AddressResolution Evaluate(AssetContext context, IEnumerable<AddressRuleEntry> entries)
        {
            var candidates = new List<AddressCandidate>();
            var labels = new HashSet<string>();
            var errors = new List<RuleEvaluationError>();

            foreach (var entry in entries)
            {
                try
                {
                    if (!entry.Predicate(context))
                        continue;

                    if (entry.AddressSelector != null)
                    {
                        var address = entry.AddressSelector(context);
                        candidates.Add(new AddressCandidate(entry.GroupName, address, entry.SourceClass, entry.Description, entry.RuleIndex));
                    }

                    foreach (var labelSelector in entry.LabelSelectors)
                    {
                        var label = labelSelector(context);
                        if (!string.IsNullOrEmpty(label))
                            labels.Add(label);
                    }
                }
                catch (Exception ex)
                {
                    var source = AddressRuleEntry.DescribeSource(entry.SourceClass, entry.Description, entry.RuleIndex);
                    errors.Add(new RuleEvaluationError(source, ex.Message));
                }
            }

            return new AddressResolution(candidates, labels, errors);
        }

        /// <summary>
        /// 1アセットに対して全ルールを評価し、ルールごとの判定理由（<see cref="RuleEvaluationDetail"/>）と
        /// <see cref="Evaluate"/> と同一の集計結果（<see cref="AddressResolution"/>）を合わせて返す。
        /// Explain 機能向けの読み取り専用処理であり、Apply/Validate/Predict には使用しない。
        /// </summary>
        public static RuleExplanation Explain(AssetContext context, IReadOnlyList<AddressRuleEntry> entries)
        {
            var details = new List<RuleEvaluationDetail>(entries.Count);
            var candidates = new List<AddressCandidate>();
            var labels = new HashSet<string>();
            var errors = new List<RuleEvaluationError>();

            foreach (var entry in entries)
            {
                var ruleSource = AddressRuleEntry.DescribeSource(entry.SourceClass, entry.Description, entry.RuleIndex);

                try
                {
                    // Predicate は1回だけ評価する（副作用・コストの二重実行を避ける）。
                    if (!entry.Predicate(context))
                    {
                        details.Add(new RuleEvaluationDetail(
                            ruleSource, entry.GroupName, entry.Description,
                            RuleMatchOutcome.NotMatched, null, null, null));
                        continue;
                    }

                    string producedAddress = null;
                    if (entry.AddressSelector != null)
                    {
                        producedAddress = entry.AddressSelector(context);
                        candidates.Add(new AddressCandidate(entry.GroupName, producedAddress, entry.SourceClass, entry.Description, entry.RuleIndex));
                    }

                    var producedLabels = new List<string>();
                    foreach (var labelSelector in entry.LabelSelectors)
                    {
                        var label = labelSelector(context);
                        if (!string.IsNullOrEmpty(label))
                        {
                            labels.Add(label);
                            producedLabels.Add(label);
                        }
                    }

                    details.Add(new RuleEvaluationDetail(
                        ruleSource, entry.GroupName, entry.Description,
                        RuleMatchOutcome.Matched, producedAddress, producedLabels, null));
                }
                catch (Exception ex)
                {
                    errors.Add(new RuleEvaluationError(ruleSource, ex.Message));
                    details.Add(new RuleEvaluationDetail(
                        ruleSource, entry.GroupName, entry.Description,
                        RuleMatchOutcome.Errored, null, null, ex.Message));
                }
            }

            var resolution = new AddressResolution(candidates, labels, errors);
            return new RuleExplanation(context, details, resolution);
        }
    }
}
