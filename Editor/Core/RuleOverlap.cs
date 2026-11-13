using System;
using System.Collections.Generic;
using System.Linq;

namespace AddressTeller
{
    /// <summary>
    /// One pair of rule classes whose addresses overlap: for some assets both matched and produced an address,
    /// and <see cref="OverridingRule"/> won on <see cref="AddressRuleBase.Order"/>, so the address of
    /// <see cref="OverriddenRule"/> was not used. This is how a broad, low-priority rule is overridden by a narrower,
    /// high-priority one on purpose; the list makes it visible so an override nobody meant can be told apart.
    /// An override is declared with <see cref="IAddressRuleGroupBuilder.OverriddenBy{TRule}"/> on the overridden rule.
    /// </summary>
    public sealed class RuleOverlap
    {
        /// <summary>Name of the rule class whose address lost (the <c>AddressRuleBase</c> subclass's type name).</summary>
        public string OverriddenRule { get; }

        /// <summary>Name of the rule class whose address won.</summary>
        public string OverridingRule { get; }

        /// <summary>Number of assets on which <see cref="OverriddenRule"/> lost to <see cref="OverridingRule"/>.</summary>
        public int Count { get; }

        /// <summary>
        /// Up to a few of those asset paths, the smallest in Ordinal order (so the same assets are named on every run).
        /// Never null.
        /// </summary>
        public IReadOnlyList<string> ExamplePaths { get; }

        /// <summary>
        /// True when the overridden rule declared this override with <see cref="IAddressRuleGroupBuilder.OverriddenBy{TRule}"/>.
        /// A pair appears twice in a list when only some of the overridden rule class's <c>Group()</c> chains declared it:
        /// once with the declared assets, once with the undeclared ones.
        /// </summary>
        public bool IsDeclared { get; }

        internal RuleOverlap(string overriddenRule, string overridingRule, int count, IReadOnlyList<string> examplePaths, bool isDeclared)
        {
            OverriddenRule = overriddenRule;
            OverridingRule = overridingRule;
            Count = count;
            ExamplePaths = examplePaths ?? Array.Empty<string>();
            IsDeclared = isDeclared;
        }
    }

    /// <summary>
    /// アセットごとの評価結果から <see cref="RuleOverlap"/> を集計する。Addressables API に依存しないモデル層。
    /// 数えるのは「アドレス候補が2件以上あり、同点でなく、書き込める勝者が確定した」アセットだけ
    /// （<see cref="WinnerSelector.Select"/> が Won を返したもの）。同点（Conflict）はどちらを採るか決まらず、
    /// ルール例外で Blocked になったアセットは勝者が確定しないため、どちらも重なりとしては数えない。
    /// 毎資産ループから呼ばれるので、候補が1件以下のアセットは何も割り当てずに返す。
    /// </summary>
    internal sealed class RuleOverlapCollector
    {
        /// <summary>1組あたりに残す例パスの最大数。</summary>
        internal const int MaxExamplePaths = 3;

        /// <summary><see cref="AddressCandidate.SourceClass"/> が無い候補（公開コンストラクタで作ったエントリ）の表示名。</summary>
        internal const string UnnamedRule = "(unnamed rule)";

        private sealed class Accumulator
        {
            public int Count;
            // Ordinal 昇順で最大 MaxExamplePaths 件。走査順に依らず同じ例が残るよう、小さい方を残す。
            public readonly List<string> Examples = new List<string>(MaxExamplePaths);
        }

        private readonly Dictionary<(string overridden, string overriding, bool declared), Accumulator> _pairs
            = new Dictionary<(string, string, bool), Accumulator>();

        /// <summary>1アセットの評価結果を取り込む。</summary>
        public void Add(string path, AddressResolution resolution)
        {
            var candidates = resolution.AddressCandidates;
            if (candidates.Count < 2) return;

            // Predict/Validate も同じ勝者決定を行うが、重なりは書き込み可否と無関係に数えるため、ここで独立に決める。
            var selection = WinnerSelector.Select(resolution);
            if (selection.Outcome != WinnerSelectionOutcome.Won) return;

            var winner = selection.Winner;
            var winnerName = winner.SourceClass ?? UnnamedRule;

            for (var i = 0; i < candidates.Count; i++)
            {
                var loser = candidates[i];
                // 勝者は Order が単独最小なので、勝者以外はすべて Order が大きい。
                if (loser.Order == winner.Order) continue;

                var loserName = loser.SourceClass ?? UnnamedRule;
                var declared = IsDeclared(loser, winner.SourceClass);

                // 同じクラスの複数の Group() が1アセットで同じ勝者に負けても、そのアセットは1回だけ数える。
                if (CountedEarlier(candidates, i, winner, loserName, declared)) continue;

                var key = (loserName, winnerName, declared);
                if (!_pairs.TryGetValue(key, out var accumulator))
                {
                    accumulator = new Accumulator();
                    _pairs.Add(key, accumulator);
                }

                accumulator.Count++;
                AddExample(accumulator.Examples, path);
            }
        }

        private static bool IsDeclared(AddressCandidate loser, string winnerClass)
        {
            var allowed = loser.OverriddenBy;
            if (allowed == null || winnerClass == null) return false;
            for (var i = 0; i < allowed.Count; i++)
                if (string.Equals(allowed[i], winnerClass, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static bool CountedEarlier(IReadOnlyList<AddressCandidate> candidates, int index, AddressCandidate winner, string loserName, bool declared)
        {
            for (var j = 0; j < index; j++)
            {
                var earlier = candidates[j];
                if (earlier.Order == winner.Order) continue;
                if (string.Equals(earlier.SourceClass ?? UnnamedRule, loserName, StringComparison.Ordinal)
                    && IsDeclared(earlier, winner.SourceClass) == declared)
                    return true;
            }
            return false;
        }

        private static void AddExample(List<string> examples, string path)
        {
            if (examples.Count == MaxExamplePaths
                && string.CompareOrdinal(path, examples[examples.Count - 1]) >= 0)
                return;

            // 昇順を保って挿入する（件数は高々 MaxExamplePaths なので線形探索で足りる）。
            var at = 0;
            while (at < examples.Count && string.CompareOrdinal(examples[at], path) < 0) at++;
            if (at < examples.Count && examples[at] == path) return;

            examples.Insert(at, path);
            if (examples.Count > MaxExamplePaths)
                examples.RemoveAt(examples.Count - 1);
        }

        /// <summary>
        /// 集計結果を、(敗者, 勝者) のクラス名の Ordinal 順（宣言済みが後）で返す。
        /// 辞書の列挙順に依存しないよう、毎回明示的に並べる。
        /// </summary>
        public IReadOnlyList<RuleOverlap> Build()
            => _pairs
                .OrderBy(p => p.Key.overridden, StringComparer.Ordinal)
                .ThenBy(p => p.Key.overriding, StringComparer.Ordinal)
                .ThenBy(p => p.Key.declared)
                .Select(p => new RuleOverlap(p.Key.overridden, p.Key.overriding, p.Value.Count, p.Value.Examples.ToArray(), p.Key.declared))
                .ToList();
    }
}
