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
        /// </summary>
        public static AssetCondition InFolder(string folder, bool recursive = true)
        {
            if (recursive)
            {
                var description = $"InFolder({folder})";
                return new AssetCondition(ctx => ctx.IsInFolder(folder), description);
            }

            var normalized = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            var descriptionNonRecursive = $"InFolder({folder}, recursive: false)";
            return new AssetCondition(ctx => ctx.Directory.Equals(normalized, StringComparison.OrdinalIgnoreCase), descriptionNonRecursive);
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
        /// </summary>
        public static AssetCondition Glob(string pattern)
        {
            var regex = new Regex(ConvertGlobToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var description = $"Glob({pattern})";
            return new AssetCondition(ctx => regex.IsMatch(ctx.Path), description);
        }

        /// <summary>
        /// Combines all given conditions with AND. If none are given, returns a condition that always
        /// returns true (Description is null).
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
