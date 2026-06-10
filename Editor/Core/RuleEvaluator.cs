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
    }
}
