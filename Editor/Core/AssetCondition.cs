using System;

namespace Natsume777.AddressTeller
{
    /// <summary>
    /// 説明文付きの条件式。Where() に渡してエラーメッセージ表示を分かりやすくするためのラッパー。
    /// </summary>
    public sealed class AssetCondition
    {
        /// <summary>判定に使う述語。</summary>
        public Func<AssetContext, bool> Predicate { get; }

        /// <summary>エラーメッセージ等で使う説明文。未指定の場合は null。</summary>
        public string Description { get; }

        public AssetCondition(Func<AssetContext, bool> predicate, string description = null)
        {
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            Description = description;
        }

        /// <summary>Predicate を実行する。</summary>
        public bool Test(AssetContext ctx) => Predicate(ctx);

        /// <summary>
        /// この条件と other を短絡AND評価する新しい条件を返す。
        /// Description は両方非nullなら "A AND B" 形式で連結し、一方のみ非nullならその方を使う。
        /// </summary>
        public AssetCondition And(AssetCondition other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return CombineAnd(this, other.Predicate, other.Description);
        }

        /// <summary>
        /// この条件と生の述語 other を短絡AND評価する新しい条件を返す。
        /// Description の連結ルールは And(AssetCondition) と同じ。
        /// </summary>
        public AssetCondition And(Func<AssetContext, bool> other, string description = null)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return CombineAnd(this, other, description);
        }

        private static AssetCondition CombineAnd(AssetCondition first, Func<AssetContext, bool> secondPredicate, string secondDescription)
        {
            var firstPredicate = first.Predicate;
            var combinedPredicate = (Func<AssetContext, bool>)(ctx => firstPredicate(ctx) && secondPredicate(ctx));
            var combinedDescription = CombineDescriptions(first.Description, secondDescription);
            return new AssetCondition(combinedPredicate, combinedDescription);
        }

        private static string CombineDescriptions(string a, string b)
        {
            if (a != null && b != null) return $"{a} AND {b}";
            return a ?? b;
        }

        /// <summary>Where(Func&lt;AssetContext,bool&gt;) との互換のため、Predicate への暗黙変換を提供する。</summary>
        public static implicit operator Func<AssetContext, bool>(AssetCondition condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            return condition.Predicate;
        }
    }
}
