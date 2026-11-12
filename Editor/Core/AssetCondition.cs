using System;
using System.Collections.Generic;

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

        /// <summary>
        /// Folders this condition depends on, as project-relative paths with "/" separators and no
        /// trailing "/" (for example "Assets/Art/Icons"). Declared by <see cref="Match.InFolder"/> and
        /// <see cref="Match.Glob"/> (its fixed prefix), and merged as a union by <see cref="And(AssetCondition)"/>
        /// and <see cref="Match.All"/>. AddressTeller checks that each declared folder exists whenever it
        /// evaluates the rule. A condition built from a raw lambda declares nothing (empty list), and is
        /// never reported for that.
        /// </summary>
        public IReadOnlyList<string> Folders { get; }

        /// <summary>
        /// GUID で宣言されたフォルダ（<see cref="Match.InFolderByGuid"/>）。パスは述語が評価のたびに
        /// <see cref="FolderGuidResolver"/> から引く。
        /// </summary>
        internal IReadOnlyList<FolderGuidDeclaration> FolderGuids { get; }

        /// <summary>
        /// Creates an AssetCondition from a predicate, an optional description, and the optional list of
        /// folders the condition depends on (see <see cref="Folders"/>). Null, empty, and duplicate
        /// folder entries are dropped.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        public AssetCondition(Func<AssetContext, bool> predicate, string description = null, IReadOnlyList<string> folders = null)
            : this(predicate, description, folders, null)
        {
        }

        internal AssetCondition(Func<AssetContext, bool> predicate, string description, IReadOnlyList<string> folders, IReadOnlyList<FolderGuidDeclaration> folderGuids)
        {
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            Description = description;
            Folders = NormalizeFolders(folders);
            FolderGuids = folderGuids ?? Array.Empty<FolderGuidDeclaration>();
        }

        /// <summary>Evaluates Predicate.</summary>
        public bool Test(AssetContext ctx) => Predicate(ctx);

        /// <summary>
        /// Returns a new condition that short-circuit ANDs this condition with <paramref name="other"/>.
        /// If both Descriptions are non-null they are joined as "A AND B"; if only one is non-null, that
        /// one is used. <see cref="Folders"/> of the result is the union of both.
        /// </summary>
        public AssetCondition And(AssetCondition other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return CombineAnd(this, other.Predicate, other.Description, other.Folders, other.FolderGuids);
        }

        /// <summary>
        /// Returns a new condition that short-circuit ANDs this condition with the raw predicate
        /// <paramref name="other"/>. Description joining follows the same rule as And(AssetCondition).
        /// A raw predicate declares no folders, so <see cref="Folders"/> of the result is this
        /// condition's own.
        /// </summary>
        public AssetCondition And(Func<AssetContext, bool> other, string description = null)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return CombineAnd(this, other, description, null, null);
        }

        private static AssetCondition CombineAnd(
            AssetCondition first,
            Func<AssetContext, bool> secondPredicate,
            string secondDescription,
            IReadOnlyList<string> secondFolders,
            IReadOnlyList<FolderGuidDeclaration> secondFolderGuids)
        {
            var firstPredicate = first.Predicate;
            var combinedPredicate = (Func<AssetContext, bool>)(ctx => firstPredicate(ctx) && secondPredicate(ctx));
            var combinedDescription = CombineDescriptions(first.Description, secondDescription);

            var folders = new List<string>(first.Folders);
            if (secondFolders != null) folders.AddRange(secondFolders);

            var folderGuids = new List<FolderGuidDeclaration>(first.FolderGuids);
            if (secondFolderGuids != null) folderGuids.AddRange(secondFolderGuids);

            return new AssetCondition(combinedPredicate, combinedDescription, folders, folderGuids);
        }

        private static string CombineDescriptions(string a, string b)
        {
            if (a != null && b != null) return $"{a} AND {b}";
            return a ?? b;
        }

        /// <summary>
        /// 宣言フォルダを "/" 区切り・末尾 "/" 無しに正規化し、null/空/重複（Ordinal）を取り除く。
        /// 登場順は保つ（検査結果の並びを決定的にするため）。
        /// </summary>
        internal static IReadOnlyList<string> NormalizeFolders(IReadOnlyList<string> folders)
        {
            if (folders == null || folders.Count == 0) return Array.Empty<string>();

            var result = new List<string>(folders.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var folder in folders)
            {
                var normalized = NormalizeFolder(folder);
                if (normalized.Length == 0) continue;
                if (seen.Add(normalized)) result.Add(normalized);
            }

            return result.Count == 0 ? Array.Empty<string>() : result.AsReadOnly();
        }

        internal static string NormalizeFolder(string folder)
            => (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');

        /// <summary>Implicit conversion to Predicate, for compatibility with Where(Func&lt;AssetContext,bool&gt;).</summary>
        public static implicit operator Func<AssetContext, bool>(AssetCondition condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            return condition.Predicate;
        }
    }

    /// <summary>
    /// <see cref="Match.InFolderByGuid"/> が立てる、GUID で指定されたフォルダの宣言。ルールの Configure() ごとに
    /// 新しく作られる不変オブジェクトで、解決済みパスは保持しない（述語が評価のたびに
    /// <see cref="FolderGuidResolver"/> から引く）。
    /// </summary>
    internal sealed class FolderGuidDeclaration
    {
        public string Guid { get; }

        public FolderGuidDeclaration(string guid)
        {
            Guid = guid;
        }
    }

    /// <summary>
    /// GUID → フォルダパスの解決器。Core は AssetDatabase を参照できないため、Editor 側
    /// （AssetDatabaseFolderGuidResolver、[InitializeOnLoad]）が起動時に注入する。未注入（Core 単体）の間は
    /// 何も解決できず、<see cref="Match.InFolderByGuid"/> の条件は常に不一致になる。
    /// </summary>
    internal static class FolderGuidResolver
    {
        /// <summary>guid を受け取り、フォルダなら現在のパス、フォルダに引けなければ null を返す。</summary>
        internal static Func<string, string> Resolver { get; set; }

        /// <summary>解決器が未注入、または GUID がフォルダに引けない場合は null。</summary>
        internal static string Resolve(string guid) => Resolver?.Invoke(guid);
    }
}
