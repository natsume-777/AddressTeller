using System.Collections.Generic;
using System.Linq;

namespace AddressTeller
{
    /// <summary><see cref="WinnerSelector.Select"/> の判定結果の種類。</summary>
    internal enum WinnerSelectionOutcome
    {
        /// <summary>アドレス候補が1件も無い（ラベル専用ルールのみ、またはどのルールにもマッチしていない）。</summary>
        NoCandidates,

        /// <summary>Order が最小の候補が単独で、かつ同等以上の優先度のアドレス産出ルールに例外が無い。書き込める勝者が確定した。</summary>
        Won,

        /// <summary>Order が最小の候補が2件以上あり、どちらを採るべきか決める根拠が無い。</summary>
        Tied,

        /// <summary>勝者は決まったが、勝者と同等以上の優先度のアドレス産出ルールが例外を送出しているため書き込めない。</summary>
        Blocked,
    }

    /// <summary>
    /// <see cref="WinnerSelector.Select"/> の戻り値。Outcome ごとに意味を持つフィールドだけが設定される。
    /// </summary>
    internal readonly struct WinnerSelection
    {
        public WinnerSelectionOutcome Outcome { get; }

        /// <summary>Outcome が Won のときのみ意味を持つ勝者候補。それ以外は既定値。</summary>
        public AddressCandidate Winner { get; }

        /// <summary>Outcome が Tied のときのみ非 null。Order が最小の同点候補だけを、評価順のまま保持する。</summary>
        public IReadOnlyList<AddressCandidate> TiedCandidates { get; }

        /// <summary>Outcome が Blocked のときのみ非 null。勝者を止めている例外（1件以上）。</summary>
        public IReadOnlyList<RuleEvaluationError> BlockingErrors { get; }

        public WinnerSelection(
            WinnerSelectionOutcome outcome,
            AddressCandidate winner = default,
            IReadOnlyList<AddressCandidate> tiedCandidates = null,
            IReadOnlyList<RuleEvaluationError> blockingErrors = null)
        {
            Outcome = outcome;
            Winner = winner;
            TiedCandidates = tiedCandidates;
            BlockingErrors = blockingErrors;
        }
    }

    /// <summary>
    /// アドレス候補群から書き込む勝者を決め、最終ラベル集合を合成する。Apply / Validate / Predict / Explain が
    /// 同じ判定を共有するための唯一の実装（勝者決定を複数箇所に持つと、Predict と Apply が食い違う温床になる）。
    /// Addressables API に依存しないモデル層。ValidationResult の組み立ては呼び出し側の責務。
    /// </summary>
    internal static class WinnerSelector
    {
        /// <summary>
        /// 勝者を決める。Order が最小の候補が単独ならそれが勝者、最小値が2件以上あれば同点（競合）。
        /// 勝者が確定した後、勝者と同等以上の優先度（Order が勝者以下、同点含む）を持つアドレス産出ルールに
        /// 例外があれば Blocked にする。例外が無ければ勝者になっていたかもしれないルールを差し置いて、
        /// たまたま残った低優先ルールのアドレスを黙って採用してしまうのを防ぐため（同点を黙って解決しないのと
        /// 同じ考え方）。ラベル専用ルール（AddressSelector == null）の例外は勝者と優先度を争わないため対象外。
        /// 単独勝者の通常ケース（同点・例外なし）では LINQ を一切使わず配列走査のみで確定させる
        /// （毎アセット呼ばれる処理のためアロケーションを避ける）。
        /// </summary>
        public static WinnerSelection Select(AddressResolution resolution)
        {
            var candidates = resolution.AddressCandidates;
            if (candidates.Count == 0)
                return new WinnerSelection(WinnerSelectionOutcome.NoCandidates);

            var minOrder = candidates[0].Order;
            var minIndex = 0;
            var minCount = 1;

            for (var i = 1; i < candidates.Count; i++)
            {
                if (candidates[i].Order < minOrder)
                {
                    minOrder = candidates[i].Order;
                    minIndex = i;
                    minCount = 1;
                }
                else if (candidates[i].Order == minOrder)
                {
                    minCount++;
                }
            }

            if (minCount > 1)
            {
                // 同点時のみ、衝突メッセージ用に該当候補を実体化する（同点でない他の候補は含めない。
                // resolution.AddressCandidates はルール評価順を保つため、この絞り込みも同じ相対順序を保つ）。
                var tied = candidates.Where(c => c.Order == minOrder).ToList();
                return new WinnerSelection(WinnerSelectionOutcome.Tied, tiedCandidates: tied);
            }

            var winner = candidates[minIndex];

            var errors = resolution.Errors;
            List<RuleEvaluationError> blocking = null;
            for (var i = 0; i < errors.Count; i++)
            {
                var error = errors[i];
                if (error.CanProduceAddress && error.Order <= winner.Order)
                {
                    blocking ??= new List<RuleEvaluationError>();
                    blocking.Add(error);
                }
            }

            if (blocking != null)
                return new WinnerSelection(WinnerSelectionOutcome.Blocked, blockingErrors: blocking);

            return new WinnerSelection(WinnerSelectionOutcome.Won, winner);
        }

        /// <summary>
        /// 書き込む最終ラベル集合を合成する:
        /// （マッチしたラベル専用エントリのラベル全部）∪（勝者候補を出したエントリのラベル）。
        /// 負けた address 候補のラベルは含めない。勝者を持たない LabelsOnly の経路では
        /// <paramref name="winner"/> に null を渡し、ラベル専用エントリ由来のラベルだけを返す。
        /// どちらか一方が空のときは追加の割り当てをせず、他方をそのまま返す（毎アセット呼ばれるため）。
        /// 戻り値は読み取り専用として扱うこと。
        /// </summary>
        public static IReadOnlyCollection<string> ComposeFinalLabels(AddressResolution resolution, AddressCandidate? winner)
        {
            var labelOnly = resolution.LabelOnlyLabels;
            var winnerLabels = winner.HasValue ? winner.Value.Labels : null;

            if (winnerLabels == null || winnerLabels.Count == 0)
                return labelOnly;
            if (labelOnly.Count == 0)
                return winnerLabels;

            var merged = new HashSet<string>(labelOnly);
            foreach (var label in winnerLabels)
                merged.Add(label);
            return merged;
        }
    }
}
