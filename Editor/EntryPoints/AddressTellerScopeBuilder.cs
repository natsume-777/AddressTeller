using System;
using System.Collections.Generic;
using System.Linq;

namespace AddressTeller.Editor
{
    /// <summary>
    /// 開発時専用のスコープ付きプレビュー（<see cref="AddressTellerScopedPreview"/>）で使う
    /// パス展開・タイトル/注意文の組み立てを行う純粋関数群。Unity API に依存しない。
    /// </summary>
    internal static class AddressTellerScopeBuilder
    {
        /// <summary>
        /// 選択パス群を展開する。フォルダは <paramref name="findAssetsUnderFolder"/> で配下アセットに再帰展開し、
        /// ファイルはそのまま含める。フォルダ自身のパスも含める（Apply All の評価対象と揃えるため。
        /// Apply All は AssetDatabase.GetAllAssetPaths() が返すフォルダも評価対象に含んでおり、
        /// IncludeFolders() を宣言したルールはフォルダ自体にマッチしうる）。結果は重複排除し、
        /// Ordinal 順で決定的にソートして返す。
        /// </summary>
        /// <param name="selectedPaths">選択された資産パス（フォルダ・ファイル混在可）。</param>
        /// <param name="isFolder">パスがフォルダかどうかを判定する関数。</param>
        /// <param name="findAssetsUnderFolder">フォルダ配下の全アセットパスを返す関数。</param>
        public static IReadOnlyList<string> ExpandFolders(
            IEnumerable<string> selectedPaths,
            Func<string, bool> isFolder,
            Func<string, IEnumerable<string>> findAssetsUnderFolder)
        {
            var expanded = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in selectedPaths ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(path)) continue;

                if (isFolder(path))
                {
                    expanded.Add(path);

                    foreach (var assetPath in findAssetsUnderFolder(path) ?? Array.Empty<string>())
                    {
                        if (!string.IsNullOrEmpty(assetPath))
                            expanded.Add(assetPath);
                    }
                }
                else
                {
                    expanded.Add(path);
                }
            }

            return expanded.OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        /// <summary>絞り込みスコープ付きのウィンドウタイトルを組み立てる。</summary>
        public static string BuildScopeTitle(string baseTitle, string scopeLabel)
        {
            if (string.IsNullOrEmpty(scopeLabel)) return baseTitle;
            return $"{baseTitle} — {scopeLabel}";
        }

        /// <summary>
        /// ルールを絞り込んだプレビューの場合のみ、ManagedGroups が縮小される旨の注意文を返す。
        /// ルール絞り込みでない場合は null。
        /// </summary>
        public static string BuildScopeNotice(bool isRuleFiltered)
        {
            if (!isRuleFiltered) return null;

            return "Rule filtering is active; entry removal predictions for entries under other rules are not shown. " +
                   "Use Apply All / Validate to see the final result across all rules.";
        }
    }
}
