using System.Collections.Generic;

namespace Natsume777.AddressTeller
{
    /// <summary>
    /// 1アセットに対する全ルールの評価結果。Explain 機能の表示単位。
    /// </summary>
    internal sealed class RuleExplanation
    {
        public AssetContext Context { get; }

        /// <summary>全ルール分の評価詳細。entries の順序（Order昇順）のまま。</summary>
        public IReadOnlyList<RuleEvaluationDetail> Details { get; }

        /// <summary><see cref="RuleEvaluator.Evaluate"/> と同一の集計結果。</summary>
        public AddressResolution Resolution { get; }

        public RuleExplanation(AssetContext context, IReadOnlyList<RuleEvaluationDetail> details, AddressResolution resolution)
        {
            Context = context;
            Details = details;
            Resolution = resolution;
        }
    }
}
