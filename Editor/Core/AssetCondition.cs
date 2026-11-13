using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
        /// <see cref="Match.Glob"/> (its fixed prefix), and merged as a union by <see cref="And(AssetCondition)"/>,
        /// <see cref="Or"/>, <see cref="Match.All"/> and <see cref="Match.Any"/>. <see cref="Not"/> keeps its
        /// operand's folders. AddressTeller checks that each declared folder exists whenever it
        /// evaluates the rule. A condition built from a raw lambda declares nothing (empty list), and is
        /// never reported for that. Folders declared inside <see cref="Not"/> are still checked for existence,
        /// but are not treated as a scope the rule covers when AddressTeller lists assets that no rule matched.
        /// </summary>
        public IReadOnlyList<string> Folders { get; }

        /// <summary>
        /// GUID で宣言されたフォルダ（<see cref="Match.InFolderByGuid"/>）。パスは述語が評価のたびに
        /// <see cref="FolderGuidResolver"/> から引く。
        /// </summary>
        internal IReadOnlyList<FolderGuidDeclaration> FolderGuids { get; }

        /// <summary>
        /// <see cref="Folders"/> のうち、直下のアセットだけを対象にするもの（<c>Match.InFolder(path, recursive: false)</c>）。
        /// 公開しないのは、宣言フォルダ配下の未登録アセットの列挙（Unmatched）が「直下だけ」と「子孫すべて」を
        /// 区別するためだけの内部情報だから。<see cref="NegatedFolders"/> に入ったフォルダの直下指定も保持する（<see cref="Not"/> で範囲へ戻るとき用）。
        /// <see cref="And(AssetCondition)"/> / <see cref="Match.All"/> では和集合にする
        /// （AND なので、同じフォルダが再帰・非再帰の両方で宣言された場合は非再帰が実際の範囲になる）。
        /// </summary>
        internal IReadOnlyList<string> NonRecursiveFolders { get; }

        /// <summary>
        /// <see cref="Folders"/> のうち、「範囲」ではなく実在検査のためだけに宣言されているもの
        /// （<see cref="Not"/> の内側で宣言されたフォルダ）。宣言フォルダ配下の未登録アセットの列挙（Unmatched）は
        /// 条件が対象とする範囲を見るもので、除外したフォルダの中身を「未登録」として並べてはいけないため、
        /// ここに入ったフォルダは列挙の範囲から外す（実在検査・大小文字検査は <see cref="Folders"/> のまま行う）。
        /// 合成では、どのオペランドでも「範囲」として宣言されていないフォルダだけがここに残る
        /// （<see cref="Or"/> / <see cref="And(AssetCondition)"/> は和集合、<see cref="Not"/> は反転）。
        /// </summary>
        internal IReadOnlyList<string> NegatedFolders { get; }

        /// <summary>
        /// <see cref="Description"/> が "A AND B" の形（括弧なし）か。<see cref="Or"/> / <see cref="Not"/> が
        /// 説明を合成するとき、この形のオペランドだけ括弧で包んで優先順位を曖昧にしないために持つ。
        /// 公開コンストラクタで渡された説明は中身を知れないので、" AND " / " OR " を含むものはこの形として扱う。
        /// </summary>
        internal bool DescriptionIsConjunction { get; }

        /// <summary>
        /// <see cref="Description"/> が片側のオペランドの説明だけを持つか（説明なしのオペランドと And した結果）。
        /// And 単体では従来どおりその説明を使うが、Or / Not はこの説明をそのまま包むと条件を偽って伝えるため、
        /// 説明なし（null）として扱う。And で伝播する。
        /// </summary>
        internal bool DescriptionIsPartial { get; }

        private bool HasCompleteDescription => Description != null && !DescriptionIsPartial;

        /// <summary>
        /// この条件が「パスだけで決まるフォルダ条件」（InFolder / InFolderByGuid / Glob そのもの）のときの、パスの判定。
        /// <see cref="Not"/> がこれを除外として記録する（合成した条件は null）。
        /// </summary>
        internal Func<string, bool> PathTest { get; }

        /// <summary>
        /// 範囲から取り除くパス条件（<c>Not(InFolder / InFolderByGuid / Glob)</c> の直接の否定）。
        /// 宣言フォルダ配下の未登録アセットの列挙（Unmatched）は、このエントリの範囲のうち、これに当たるパスを
        /// 数えない（このルールが意図して取らない資産だから）。他のエントリの範囲は削らない。
        /// <see cref="And(AssetCondition)"/> は和集合で引き継ぐ。<see cref="Or"/> と、直接でない否定は落とす
        /// （落とす側は列挙が増える向き。列挙が隠れて本当の未登録を見落とすより、余計に出るほうを選ぶ）。
        /// </summary>
        internal IReadOnlyList<Func<string, bool>> ScopeExclusions { get; }

        /// <summary>
        /// Creates an AssetCondition from a predicate, an optional description, and the optional list of
        /// folders the condition depends on (see <see cref="Folders"/>). Null, empty, and duplicate
        /// folder entries are dropped. A description that contains " AND " or " OR " is treated as compound, so
        /// <see cref="Or"/> and <see cref="Not"/> wrap it in parentheses.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        public AssetCondition(Func<AssetContext, bool> predicate, string description = null, IReadOnlyList<string> folders = null)
            : this(predicate, description, folders, null, null, null, null, null,
                descriptionIsConjunction: description != null
                    && (description.Contains(" AND ") || description.Contains(" OR ")))
        {
        }

        internal AssetCondition(
            Func<AssetContext, bool> predicate,
            string description,
            IReadOnlyList<string> folders,
            IReadOnlyList<FolderGuidDeclaration> folderGuids,
            IReadOnlyList<string> nonRecursiveFolders = null,
            IReadOnlyList<string> negatedFolders = null,
            IReadOnlyList<Func<string, bool>> scopeExclusions = null,
            Func<string, bool> pathTest = null,
            bool descriptionIsConjunction = false,
            bool descriptionIsPartial = false)
        {
            DescriptionIsConjunction = descriptionIsConjunction;
            DescriptionIsPartial = descriptionIsPartial;
            ScopeExclusions = scopeExclusions ?? Array.Empty<Func<string, bool>>();
            PathTest = pathTest;
            NonRecursiveFolders = NormalizeFolders(nonRecursiveFolders);
            NegatedFolders = NormalizeFolders(negatedFolders);
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            Description = description;
            Folders = NormalizeFolders(folders);
            FolderGuids = folderGuids ?? Array.Empty<FolderGuidDeclaration>();
        }

        /// <summary>
        /// Creates a condition that behaves exactly like <paramref name="source"/> but reports
        /// <paramref name="description"/> instead of its own description (null means no description). Everything the
        /// source declares (folders, GUID folders, exclusions) is kept. Use this instead of
        /// <c>new AssetCondition(source, description)</c> resolving to the predicate constructor: a condition converts
        /// implicitly to a <c>Func&lt;AssetContext, bool&gt;</c>, and that conversion would drop the declarations.
        /// A description that contains " AND " or " OR " is treated as compound, as in the predicate constructor.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
        public AssetCondition(AssetCondition source, string description)
            : this(Required(source).Predicate, description, source.Folders, source.FolderGuids, source.NonRecursiveFolders,
                source.NegatedFolders, source.ScopeExclusions, source.PathTest,
                descriptionIsConjunction: description != null
                    && (description.Contains(" AND ") || description.Contains(" OR ")))
        {
        }

        private static AssetCondition Required(AssetCondition source)
            => source ?? throw new ArgumentNullException(nameof(source));

        /// <summary>Evaluates Predicate.</summary>
        public bool Test(AssetContext ctx) => Predicate(ctx);

        /// <summary>
        /// Returns a new condition that short-circuit ANDs this condition with <paramref name="other"/>.
        /// If both Descriptions are non-null they are joined as "A AND B"; if only one is non-null, that
        /// one is used (so the description of an AND with a raw lambda names only the described side; an
        /// <see cref="Or"/> or <see cref="Not"/> built on such a result has no description). <see cref="Folders"/>
        /// of the result is the union of both.
        /// </summary>
        public AssetCondition And(AssetCondition other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            var firstPredicate = Predicate;
            var secondPredicate = other.Predicate;
            var description = CombineDescriptions(Description, other.Description);
            var conjunction = (Description != null && other.Description != null)
                || (Description != null ? DescriptionIsConjunction : other.DescriptionIsConjunction);
            var partial = DescriptionIsPartial || other.DescriptionIsPartial
                || (Description == null) != (other.Description == null);
            return WithMergedDeclarations(
                ctx => firstPredicate(ctx) && secondPredicate(ctx),
                description,
                new[] { this, other },
                anyOf: false,
                descriptionIsConjunction: conjunction,
                descriptionIsPartial: partial);
        }

        /// <summary>
        /// Like <see cref="And(AssetCondition)"/>, but <paramref name="description"/> replaces the description of
        /// <paramref name="other"/> before the two are joined, exactly as <see cref="And(Func{AssetContext, bool}, string)"/>
        /// does for a predicate: <c>x.And(y, "text")</c> is described as "X AND text" in both (null means
        /// <paramref name="other"/> has no description, so only X's remains). It is the same as
        /// <c>And(new AssetCondition(other, description))</c>. <see cref="Folders"/> and the other declarations are merged
        /// exactly as in <see cref="And(AssetCondition)"/>. Use this rather than the predicate overload for a condition:
        /// an <see cref="AssetCondition"/> converts implicitly to <c>Func&lt;AssetContext, bool&gt;</c>, and that overload
        /// would drop the declarations of <paramref name="other"/>, including a <c>Not(InFolder ...)</c> exclusion.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
        public AssetCondition And(AssetCondition other, string description)
            => And(new AssetCondition(other, description));

        /// <summary>
        /// Returns a new condition that short-circuit ANDs this condition with the raw predicate
        /// <paramref name="other"/>. Description joining follows the same rule as And(AssetCondition).
        /// A raw predicate declares no folders, so <see cref="Folders"/> of the result is this
        /// condition's own.
        /// </summary>
        public AssetCondition And(Func<AssetContext, bool> other, string description = null)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return And(new AssetCondition(other, description));
        }

        /// <summary>
        /// Returns a new condition that matches when this condition or <paramref name="other"/> matches
        /// (short-circuit: <paramref name="other"/> is not evaluated once this one matches). The description is
        /// "(A OR B)", with an operand that is itself an "X AND Y" wrapped in its own parentheses. If either
        /// operand has no complete description (a raw lambda, or an AND that includes one), the result has none
        /// either, because a description that dropped one side would misstate the condition.
        /// <para>
        /// Declared folders (<see cref="Folders"/>): the union of both operands. A folder that either
        /// operand declares as a scope stays a scope of the result, so assets under it that no rule matches are
        /// still listed as unregistered. Exclusions made with <c>Not(InFolder ...)</c> inside an operand are not carried
        /// over (they applied to that operand only; the result may therefore list more unregistered assets, never
        /// fewer). If the same folder is declared both recursively and
        /// non-recursively (<c>InFolder(path, recursive: false)</c>), the recursive declaration wins, since either
        /// operand may match there.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
        public AssetCondition Or(AssetCondition other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return AnyOf(new[] { this, other });
        }

        /// <summary>
        /// Returns a new condition that matches when this condition does not. The description is
        /// "NOT A" ("NOT (A)" when A is an "X AND Y"; an "(X OR Y)" already carries its own parentheses). A
        /// condition without a complete description (a raw lambda, or an AND that includes one) gives a result
        /// without one.
        /// <para>
        /// Declared folders (<see cref="Folders"/>): kept, so a mistyped or deleted folder inside a
        /// <c>Not</c> is still reported as missing (excluding a folder that does not exist is a silent no-op,
        /// which is worth knowing about). But a folder declared inside <c>Not</c> is not a scope the rule
        /// covers. When the operand is directly <c>Match.InFolder</c>, <c>Match.InFolderByGuid</c> or
        /// <c>Match.Glob</c>, the excluded paths are also recorded as an exclusion, so that when this is combined with
        /// <see cref="And(AssetCondition)"/> with a folder, assets under the excluded part are not listed as
        /// unregistered just because this rule leaves them out (another rule that covers them still lists them).
        /// A <c>Not</c> of any other condition records no exclusion. Applying <c>Not</c> twice restores the
        /// original declarations.
        /// </para>
        /// </summary>
        public AssetCondition Not()
        {
            var predicate = Predicate;
            var description = !HasCompleteDescription ? null : DescriptionIsConjunction ? $"NOT ({Description})" : $"NOT {Description}";

            // 範囲として宣言されていたものは除外側へ、除外側だったものは範囲へ反転する。
            var scopeFolders = new HashSet<string>(Folders, StringComparer.Ordinal);
            scopeFolders.ExceptWith(NegatedFolders);
            var flipped = new List<string>();
            foreach (var folder in Folders)
                if (scopeFolders.Contains(folder)) flipped.Add(folder);

            var guids = new List<FolderGuidDeclaration>(FolderGuids.Count);
            foreach (var guid in FolderGuids)
                guids.Add(new FolderGuidDeclaration(guid.Guid, guid.Recursive, !guid.Negated));

            var exclusions = PathTest != null ? new[] { PathTest } : null;
            return new AssetCondition(ctx => !predicate(ctx), description, Folders, guids, NonRecursiveFolders, flipped, exclusions);
        }

        /// <summary>
        /// Or の n 項版（Match.Any / Or の実体）。説明は "(A OR B OR C)" にフラット化する。
        /// </summary>
        internal static AssetCondition AnyOf(AssetCondition[] operands)
        {
            var predicates = new Func<AssetContext, bool>[operands.Length];
            string description = null;
            var allDescribed = true;
            var sb = new StringBuilder("(");
            for (var i = 0; i < operands.Length; i++)
            {
                predicates[i] = operands[i].Predicate;
                if (!operands[i].HasCompleteDescription)
                {
                    allDescribed = false;
                    continue;
                }

                if (i > 0) sb.Append(" OR ");
                sb.Append(operands[i].DescriptionIsConjunction ? $"({operands[i].Description})" : operands[i].Description);
            }

            if (allDescribed) description = sb.Append(')').ToString();

            return WithMergedDeclarations(
                ctx =>
                {
                    for (var i = 0; i < predicates.Length; i++)
                        if (predicates[i](ctx)) return true;
                    return false;
                },
                description,
                operands,
                anyOf: true);
        }

        /// <summary>
        /// 合成した条件に、オペランドの宣言（Folders / FolderGuids / NonRecursiveFolders / NegatedFolders）を
        /// 引き継がせる。範囲として宣言されたフォルダ（Folders から NegatedFolders を除いたもの）は和集合。
        /// 直下だけの宣言（NonRecursiveFolders）は、And ではいずれかのオペランドが直下だけなら直下だけ（AND なので
        /// 範囲は狭い側）、Or ではそのフォルダを範囲として宣言した全オペランドが直下だけのときだけ直下だけ
        /// （どれかが再帰なら再帰が範囲）。除外側（Not 内）の直下指定は、範囲の判定では数えない（範囲側の宣言があればそちらで決まる）。
        /// 除外側にだけ宣言されたフォルダは、外側の Not で範囲へ反転したときのために、反転後の意味で別に集計して残す。
        /// </summary>
        private static AssetCondition WithMergedDeclarations(
            Func<AssetContext, bool> predicate,
            string description,
            AssetCondition[] operands,
            bool anyOf,
            bool descriptionIsConjunction = false,
            bool descriptionIsPartial = false)
        {
            var folders = new List<string>();
            var folderGuids = new List<FolderGuidDeclaration>();
            var exclusions = new List<Func<string, bool>>();
            var scope = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operand in operands)
            {
                folders.AddRange(operand.Folders);
                folderGuids.AddRange(operand.FolderGuids);
                if (!anyOf) exclusions.AddRange(operand.ScopeExclusions);
                foreach (var folder in operand.Folders)
                    if (!operand.NegatedFolders.Contains(folder)) scope.Add(folder);
            }

            // Or では、同じ GUID が一方で再帰・他方で直下だけ宣言されていたら再帰が範囲になる（パス宣言と同じ扱い）。
            // 解決後のパスで合成する時点では GUID 単位の区別が残っていないので、ここで揃える。
            if (anyOf) folderGuids = MergeRecursiveGuids(folderGuids);

            var negated = new List<string>();
            foreach (var folder in folders)
                if (!scope.Contains(folder)) negated.Add(folder);

            var nonRecursive = new List<string>();
            foreach (var folder in scope)
            {
                var declaredByAny = false;
                var directInAny = false;
                var directInAll = true;
                foreach (var operand in operands)
                {
                    if (!operand.Folders.Contains(folder) || operand.NegatedFolders.Contains(folder)) continue;
                    declaredByAny = true;
                    if (operand.NonRecursiveFolders.Contains(folder)) directInAny = true;
                    else directInAll = false;
                }

                if (declaredByAny && (anyOf ? directInAll : directInAny)) nonRecursive.Add(folder);
            }

            // 除外側にだけ宣言されたフォルダの直下指定も残す。外側の Not で範囲へ反転したときに、
            // 再帰・非再帰の区別が失われないようにするため。反転後の意味に合わせ、
            // And は全員が直下のときだけ直下、Or は誰かが直下なら直下。
            foreach (var folder in negated)
            {
                var directInAny = false;
                var directInAll = true;
                foreach (var operand in operands)
                {
                    if (!operand.Folders.Contains(folder)) continue;
                    if (operand.NonRecursiveFolders.Contains(folder)) directInAny = true;
                    else directInAll = false;
                }

                if (anyOf ? directInAny : directInAll) nonRecursive.Add(folder);
            }

            // HashSet の列挙順に依存しないよう、登場順（folders）で並べ直す。
            nonRecursive.Sort((a, b) => folders.IndexOf(a).CompareTo(folders.IndexOf(b)));

            return new AssetCondition(predicate, description, folders, folderGuids, nonRecursive, negated, exclusions,
                descriptionIsConjunction: descriptionIsConjunction, descriptionIsPartial: descriptionIsPartial);
        }

        /// <summary>
        /// 範囲側（Not の内側でない）で再帰宣言された GUID があれば、同じ GUID の範囲側の直下だけ宣言も再帰にそろえる。
        /// 変更が無ければ元の一覧をそのまま返す。
        /// </summary>
        private static List<FolderGuidDeclaration> MergeRecursiveGuids(List<FolderGuidDeclaration> declarations)
        {
            HashSet<string> recursiveGuids = null;
            foreach (var declaration in declarations)
                if (declaration.Recursive && !declaration.Negated)
                    (recursiveGuids ??= new HashSet<string>(StringComparer.Ordinal)).Add(declaration.Guid);
            if (recursiveGuids == null) return declarations;

            var merged = new List<FolderGuidDeclaration>(declarations.Count);
            foreach (var declaration in declarations)
            {
                merged.Add(!declaration.Recursive && !declaration.Negated && recursiveGuids.Contains(declaration.Guid)
                    ? new FolderGuidDeclaration(declaration.Guid, true, false)
                    : declaration);
            }

            return merged;
        }

        /// <summary>
        /// <paramref name="path"/> が <paramref name="folder"/>（正規化済み）の配下か（大小文字を区別しない）。
        /// recursive=false なら直下のみ。毎資産ループから呼ばれるので割り当てない。
        /// </summary>
        internal static bool IsUnderFolder(string path, string folder, bool recursive)
        {
            if (path.Length <= folder.Length + 1 || path[folder.Length] != '/') return false;
            if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return false;
            return recursive || path.IndexOf('/', folder.Length + 1) < 0;
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

        /// <summary>
        /// Implicit conversion to Predicate, for compatibility with Where(Func&lt;AssetContext,bool&gt;). The conversion
        /// keeps only the predicate: the declared folders and exclusions are dropped. To add a description to a condition
        /// without losing them, use <c>Where(condition, description)</c>, <c>condition.And(other, description)</c> or
        /// <c>new AssetCondition(condition, description)</c> (the AssetCondition overloads), never the Func ones.
        /// </summary>
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

        /// <summary>false なら、解決したフォルダの直下のアセットだけが対象（<c>recursive: false</c>）。</summary>
        public bool Recursive { get; }

        /// <summary>
        /// <see cref="AssetCondition.Not"/> の内側で宣言されたもの（奇数回の反転）。実在検査だけに使い、未登録アセット
        /// 列挙の範囲には入れない（<see cref="AssetCondition.NegatedFolders"/> と同じ扱い）。
        /// </summary>
        public bool Negated { get; }

        public FolderGuidDeclaration(string guid, bool recursive = true, bool negated = false)
        {
            Guid = guid;
            Recursive = recursive;
            Negated = negated;
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
