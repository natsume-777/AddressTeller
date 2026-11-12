using System;
using System.Text;
using System.Text.RegularExpressions;

namespace AddressTeller
{
    /// <summary>
    /// Static helpers for building conditions to pass to Where() concisely.
    /// </summary>
    public static class Match
    {
        /// <summary>
        /// Returns a condition that checks whether the asset is under the given folder.
        /// When recursive=true (default), subfolders are included recursively. When recursive=false,
        /// only direct children of the folder match.
        /// The folder is declared as a dependency (<see cref="AssetCondition.Folders"/>): AddressTeller
        /// reports it when the folder does not exist, or exists only with different letter case.
        /// </summary>
        public static AssetCondition InFolder(string folder, bool recursive = true)
        {
            var declared = new[] { folder };
            if (recursive)
            {
                var description = $"InFolder({folder})";
                return new AssetCondition(ctx => ctx.IsInFolder(folder), description, declared);
            }

            var normalized = AssetCondition.NormalizeFolder(folder);
            var descriptionNonRecursive = $"InFolder({folder}, recursive: false)";
            return new AssetCondition(ctx => ctx.Directory.Equals(normalized, StringComparison.OrdinalIgnoreCase), descriptionNonRecursive, declared, null, declared);
        }

        /// <summary>
        /// Like <see cref="InFolder"/>, but identifies the folder by its asset GUID, so the condition keeps
        /// working after the folder is moved or renamed. AddressTeller resolves the GUID to the folder's
        /// current path when it evaluates the rule. If the GUID does not resolve to a folder in the project,
        /// the condition matches nothing and the problem is reported (the same as a declared folder that
        /// does not exist). The GUID is looked up each time the condition is evaluated, so the same result
        /// is produced inside AddressTeller's evaluation and when a rule is exercised through
        /// <c>RuleInspector.Collect</c> in the Editor. Where no GUID lookup is available (outside the Unity
        /// Editor's AssetDatabase), the condition never matches.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="guid"/> is null or empty.</exception>
        public static AssetCondition InFolderByGuid(string guid, bool recursive = true)
        {
            if (string.IsNullOrEmpty(guid)) throw new ArgumentException("guid must not be empty.", nameof(guid));

            var declaration = new FolderGuidDeclaration(guid, recursive);
            var declaredGuid = guid;
            var description = recursive ? $"InFolderByGuid({guid})" : $"InFolderByGuid({guid}, recursive: false)";
            Func<AssetContext, bool> predicate = ctx =>
            {
                // 解決は評価のたびに FolderGuidResolver から引く（Editor 側の実装が結果をキャッシュする）。
                var resolved = FolderGuidResolver.Resolve(declaredGuid);
                if (string.IsNullOrEmpty(resolved)) return false;
                return recursive
                    ? ctx.IsInFolder(resolved)
                    : ctx.Directory.Equals(resolved, StringComparison.OrdinalIgnoreCase);
            };
            return new AssetCondition(predicate, description, null, new[] { declaration });
        }

        /// <summary>Returns a condition that checks whether the asset's type is assignable to T.</summary>
        public static AssetCondition OfType<T>()
        {
            var targetType = typeof(T);
            var description = $"OfType<{targetType.Name}>()";
            return new AssetCondition(ctx => targetType.IsAssignableFrom(ctx.Type), description);
        }

        /// <summary>
        /// Returns a condition that matches the full path (case-insensitive) against a glob pattern
        /// ("*" = any characters not crossing "/", "**" = any characters including "/", "?" = one
        /// character). The regular expression is compiled once when this method is called and is not
        /// regenerated at evaluation time.
        /// "**" only guarantees a zero-segment match (no intermediate folder) when it appears between
        /// two "/" characters (e.g. "Assets/**/*.png"). A "**" at the start or end of the pattern (e.g.
        /// "**/foo", "foo/**") requires at least one segment to match.
        /// The folder made of the whole path segments before the first wildcard is declared as a
        /// dependency (<see cref="AssetCondition.Folders"/>): "Assets/Art/*.png" declares "Assets/Art", and
        /// "Assets/Art*/x" declares "Assets". A pattern that starts with a wildcard (for example "**/foo")
        /// has no fixed prefix and declares nothing, so AddressTeller cannot report a missing folder for it.
        /// </summary>
        public static AssetCondition Glob(string pattern)
        {
            var regex = new Regex(ConvertGlobToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var description = $"Glob({pattern})";
            var prefix = ExtractGlobFolderPrefix(pattern);
            return new AssetCondition(ctx => regex.IsMatch(ctx.Path), description, prefix == null ? null : new[] { prefix });
        }

        /// <summary>
        /// glob パターンの最初のワイルドカード（"*" / "?"）より前にある、完結したパスセグメントだけから成る
        /// フォルダを返す。ワイルドカードの直前のセグメントは部分一致の可能性があるため含めない
        /// （"Assets/Art*/x" は "Assets"）。固定プレフィックスが無い（先頭がワイルドカード等）場合は null。
        /// </summary>
        internal static string ExtractGlobFolderPrefix(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return null;

            var wildcard = pattern.IndexOfAny(new[] { '*', '?' });
            var fixedPart = wildcard < 0 ? pattern : pattern.Substring(0, wildcard);
            var lastSlash = fixedPart.LastIndexOf('/');
            if (lastSlash <= 0) return null;

            return fixedPart.Substring(0, lastSlash);
        }

        /// <summary>
        /// Combines all given conditions with AND. If none are given, returns a condition that always
        /// returns true (Description is null). <see cref="AssetCondition.Folders"/> of the result is the
        /// union of all given conditions' folders.
        /// </summary>
        public static AssetCondition All(params AssetCondition[] conditions)
        {
            if (conditions == null || conditions.Length == 0)
            {
                return new AssetCondition(ctx => true);
            }

            var combined = conditions[0];
            for (var i = 1; i < conditions.Length; i++)
            {
                combined = combined.And(conditions[i]);
            }

            return combined;
        }

        /// <summary>glob パターンを正規表現パターン文字列に変換する。</summary>
        private static string ConvertGlobToRegex(string pattern)
        {
            var source = pattern ?? string.Empty;
            var sb = new StringBuilder();
            sb.Append('^');

            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                if (c == '*')
                {
                    if (i + 1 < source.Length && source[i + 1] == '*')
                    {
                        // "/**/" は0階層以上のディレクトリ（区切りの「/」自体を含む/含まないの両方）にマッチさせる。
                        // 例: "Assets/**/*.png" は "Assets/Icon.png"（中間階層なし）にもマッチする必要がある。
                        if (i > 0 && source[i - 1] == '/' && i + 2 < source.Length && source[i + 2] == '/')
                        {
                            // 直前に追加済みの "/" を取り除き、"(?:.*/)?" に置き換える。
                            sb.Length -= 1;
                            sb.Append("(?:.*/)?");
                            i += 2; // "**/" の "*", "*", "/" のうち追加2文字分を読み飛ばす
                        }
                        else
                        {
                            sb.Append(".*");
                            i++;
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                }
                else if (c == '?')
                {
                    sb.Append("[^/]");
                }
                else
                {
                    sb.Append(Regex.Escape(c.ToString()));
                }
            }

            sb.Append('$');
            return sb.ToString();
        }
    }
}
