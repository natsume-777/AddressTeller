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

            foreach (var entry in entries)
            {
                if (!entry.Predicate(context))
                    continue;

                if (entry.AddressSelector != null)
                {
                    var address = entry.AddressSelector(context);
                    candidates.Add(new AddressCandidate(entry.GroupName, address));
                }

                foreach (var labelSelector in entry.LabelSelectors)
                {
                    var label = labelSelector(context);
                    if (!string.IsNullOrEmpty(label))
                        labels.Add(label);
                }
            }

            return new AddressResolution(candidates, labels);
        }
    }
}
