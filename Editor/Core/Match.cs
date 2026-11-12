using System;
using System.Collections.Generic;
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
                var normalizedRecursive = AssetCondition.NormalizeFolder(folder);
                return new AssetCondition(ctx => ctx.IsInFolder(folder), description, declared, null, null, null, null,
                    pathTest: path => AssetCondition.IsUnderFolder(path, normalizedRecursive, true));
            }

            var normalized = AssetCondition.NormalizeFolder(folder);
            var descriptionNonRecursive = $"InFolder({folder}, recursive: false)";
            return new AssetCondition(ctx => ctx.Directory.Equals(normalized, StringComparison.OrdinalIgnoreCase), descriptionNonRecursive, declared, null, declared, null, null,
                pathTest: path => AssetCondition.IsUnderFolder(path, normalized, false));
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
            // 除外として記録されるときのパス判定。評価のたびに GUID を引く（述語と同じ）。
            Func<string, bool> pathTest = path =>
            {
                var resolved = FolderGuidResolver.Resolve(declaredGuid);
                return !string.IsNullOrEmpty(resolved) && AssetCondition.IsUnderFolder(path, AssetCondition.NormalizeFolder(resolved), recursive);
            };
            return new AssetCondition(predicate, description, null, new[] { declaration }, null, null, null, pathTest);
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
            return new AssetCondition(ctx => regex.IsMatch(ctx.Path), description, prefix == null ? null : new[] { prefix }, null, null, null, null,
                pathTest: path => regex.IsMatch(path));
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

        /// <summary>
        /// Combines the given conditions with OR (short-circuit, in the order given). The description is
        /// "(A OR B OR C)" (see <see cref="AssetCondition.Or"/> for how an operand without a description and an
        /// "X AND Y" operand are handled). <see cref="AssetCondition.Folders"/> of the result is the union of all
        /// operands' folders; see <see cref="AssetCondition.Or"/> for the scope rules. With exactly one condition,
        /// that condition is returned as is.
        /// Unlike <see cref="All"/>, giving no conditions is an error: an empty OR would match nothing, and a
        /// rule that silently matches nothing is easy to write by accident (for example from an empty
        /// array).
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="conditions"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException">An element of <paramref name="conditions"/> is null.</exception>
        public static AssetCondition Any(params AssetCondition[] conditions)
        {
            if (conditions == null || conditions.Length == 0)
                throw new ArgumentException("At least one condition is required.", nameof(conditions));
            for (var i = 0; i < conditions.Length; i++)
            {
                if (conditions[i] == null)
                    throw new ArgumentNullException(nameof(conditions), $"conditions[{i}] must not be null.");
            }

            if (conditions.Length == 1) return conditions[0];
            return AssetCondition.AnyOf((AssetCondition[])conditions.Clone());
        }

        /// <summary>
        /// Returns a condition that checks whether the asset's file extension is one of the given extensions.
        /// Each extension may be written with or without the leading dot and in any letter case
        /// (<c>"png"</c>, <c>".PNG"</c> and <c>".png"</c> are the same); surrounding white space is ignored.
        /// Only the last extension of a file name counts, as in <see cref="AssetContext.Extension"/>, so an extension
        /// that itself contains a dot (<c>".tar.gz"</c>) or a path separator is rejected rather than silently
        /// never matching. A folder asset (<see cref="AssetContext.IsFolder"/>) has no extension and matches nothing, even if its name contains a dot (for example "Foo.bundle"). The description is
        /// <c>WithExtension(.png, .jpg)</c> in the normalized spelling, duplicates removed. Declares no folders.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="extensions"/> is null or empty, or an element is null, empty, white space, only a dot,
        /// or contains another dot or a path separator.
        /// </exception>
        public static AssetCondition WithExtension(params string[] extensions)
        {
            if (extensions == null || extensions.Length == 0)
                throw new ArgumentException("At least one extension is required.", nameof(extensions));

            var normalized = new List<string>(extensions.Length);
            foreach (var extension in extensions)
            {
                var trimmed = extension?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && trimmed[0] == '.') trimmed = trimmed.Substring(1);
                if (string.IsNullOrEmpty(trimmed) || trimmed.IndexOfAny(new[] { '.', '/', '\\' }) >= 0)
                    throw new ArgumentException($"'{extension}' is not a single file extension (expected something like \"png\" or \".png\").", nameof(extensions));

                var withDot = "." + trimmed.ToLowerInvariant();
                if (!normalized.Contains(withDot)) normalized.Add(withDot);
            }

            var accepted = normalized.ToArray();
            var description = $"WithExtension({string.Join(", ", accepted)})";
            return new AssetCondition(ctx =>
            {
                // フォルダは拡張子を持たない扱い（"Foo.bundle" のようなフォルダ名の ".bundle" は拡張子ではない）。
                if (ctx.IsFolder) return false;

                // AssetContext.Extension は小文字化で割り当てるので、1回だけ取り出して比較する。
                var actual = ctx.Extension;
                for (var i = 0; i < accepted.Length; i++)
                    if (string.Equals(actual, accepted[i], StringComparison.Ordinal)) return true;
                return false;
            }, description);
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
