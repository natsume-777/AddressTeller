using System;

namespace AddressTeller
{
    /// <summary>
    /// A condition paired with a description. Wrap a predicate with this and pass it to Where() to
    /// make error messages easier to read.
    /// </summary>
    public sealed class AssetCondition
    {
        /// <summary>The predicate used for matching.</summary>
        public Func<AssetContext, bool> Predicate { get; }

        /// <summary>Description used in error messages, etc. Null if not specified.</summary>
        public string Description { get; }

        /// <summary>Creates an AssetCondition from a predicate and an optional description.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        public AssetCondition(Func<AssetContext, bool> predicate, string description = null)
        {
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            Description = description;
        }

        /// <summary>Evaluates Predicate.</summary>
        public bool Test(AssetContext ctx) => Predicate(ctx);

        /// <summary>
        /// Returns a new condition that short-circuit ANDs this condition with <paramref name="other"/>.
        /// If both Descriptions are non-null they are joined as "A AND B"; if only one is non-null, that
        /// one is used.
        /// </summary>
        public AssetCondition And(AssetCondition other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return CombineAnd(this, other.Predicate, other.Description);
        }

        /// <summary>
        /// Returns a new condition that short-circuit ANDs this condition with the raw predicate
        /// <paramref name="other"/>. Description joining follows the same rule as And(AssetCondition).
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

        /// <summary>Implicit conversion to Predicate, for compatibility with Where(Func&lt;AssetContext,bool&gt;).</summary>
        public static implicit operator Func<AssetContext, bool>(AssetCondition condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            return condition.Predicate;
        }
    }
}
