using System.Collections.Generic;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// <see cref="ValidationResult"/> のログ出力を全エントリポイント（Postprocessor/Menu）で共通化する補助処理。
    /// <see cref="ValidationStatus.GroupWillBeCreated"/> のように IsOk=true な結果は正常系の通知であり、
    /// エラーとして扱うべきではないため、IsOk に応じて Warning/Error を振り分ける。
    /// </summary>
    internal static class AddressTellerIssueLogger
    {
        /// <summary>issues の各要素を IsOk に応じて振り分けてログ出力する。</summary>
        public static void LogAll(IReadOnlyList<ValidationResult> issues)
        {
            foreach (var issue in issues)
                Log(issue);
        }

        /// <summary>
        /// 1件の <see cref="ValidationResult"/> をログ出力する。IsOk=true（GroupWillBeCreated 等の正常系通知）は
        /// LogWarning、IsOk=false（実際の問題）は LogError とする。
        /// </summary>
        public static void Log(ValidationResult issue)
        {
            var message = $"[AddressTeller] {issue.Status}: {issue.Message}";
            if (issue.IsOk)
                Debug.LogWarning(message);
            else
                Debug.LogError(message);
        }
    }
}
