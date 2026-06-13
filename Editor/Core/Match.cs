using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Natsume777.AddressTeller
{
    /// <summary>
    /// Where() に渡す条件を簡潔に組み立てるための静的ヘルパー群。
    /// </summary>
    public static class Match
    {
        /// <summary>
        /// 指定フォルダ配下かどうかを判定する条件を返す。
        /// recursive=true（既定）の場合は配下を再帰的に含む。recursive=false の場合は直下のみを対象とする。
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

        /// <summary>指定型に代入可能かどうかを判定する条件を返す。</summary>
        public static AssetCondition OfType<T>()
        {
            var targetType = typeof(T);
            var description = $"OfType<{targetType.Name}>()";
            return new AssetCondition(ctx => targetType.IsAssignableFrom(ctx.Type), description);
        }

        /// <summary>
        /// glob パターン（"*"=「/」を跨がない任意文字列、"**"=「/」を含む任意文字列、"?"=1文字）でパス全体（大文字小文字無視）を判定する条件を返す。
        /// 正規表現はこの呼び出し時に1回だけコンパイルし、評価時は再生成しない。
        /// "**" は「/」で挟まれた中間位置（例: "Assets/**/*.png"）でのみ0階層マッチ（中間フォルダなし）を保証する。
        /// パターン先頭または末尾の "**"（例: "**/foo", "foo/**"）は1階層以上のマッチを前提とする。
        /// </summary>
        public static AssetCondition Glob(string pattern)
        {
            var regex = new Regex(ConvertGlobToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var description = $"Glob({pattern})";
            return new AssetCondition(ctx => regex.IsMatch(ctx.Path), description);
        }

        /// <summary>
        /// 渡された全条件を AND で合成する。0件の場合は常に true を返す条件（Description は null）を返す。
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
