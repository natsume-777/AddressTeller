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
        /// <summary>
        /// issues の各要素を IsOk に応じて振り分けてログ出力する。<see cref="ValidationStatus.UnmatchedEntryKept"/>
        /// は個別には出さず件数だけの1行サマリ（Debug.Log/Info）にまとめる——CleanupStaleEntries が OFF の間、
        /// 所有グループの無マッチ・無効パスエントリ全件が毎回この通知になり得るため、件数次第ではコンソールを
        /// 埋め尽くしてしまう。意図的に CleanupStaleEntries を OFF のまま運用しているプロジェクトでは毎回この
        /// 通知が出続けることになるため、Warning ではなく Info にして、意図した運用を阻害しないようにしている。
        /// <see cref="ValidationStatus.BlockedByRuleError"/> も同じ理由（1つのルール例外が多数のアセットに
        /// 波及しうるため）で個別には出さず件数だけの1行サマリにまとめるが、こちらは実際の問題（IsOk=false）
        /// のため LogError にする。個別の内容は Result Window（<see cref="AddressTellerResultWindow"/>）と
        /// レポート（JSON/JUnit）に渡す issues 一覧側でそのまま提供されるため、ここで間引いても情報は失われない。
        /// </summary>
        public static void LogAll(IReadOnlyList<ValidationResult> issues)
        {
            var unmatchedEntryKeptCount = 0;
            var blockedByRuleErrorCount = 0;

            foreach (var issue in issues)
            {
                if (issue.Status == ValidationStatus.UnmatchedEntryKept)
                {
                    unmatchedEntryKeptCount++;
                    continue;
                }

                if (issue.Status == ValidationStatus.BlockedByRuleError)
                {
                    blockedByRuleErrorCount++;
                    continue;
                }

                Log(issue);
            }

            if (unmatchedEntryKeptCount > 0)
            {
                var isSingle = unmatchedEntryKeptCount == 1;
                Debug.Log($"[AddressTeller] UnmatchedEntryKept: {unmatchedEntryKeptCount} entr{(isSingle ? "y" : "ies")} " +
                    $"would be removed by Remove unmatched entries if it were on, and {(isSingle ? "is" : "are")} being " +
                    "kept as-is. Run Validate to see the individual entries in the Result Window, or inspect a JSON/JUnit report.");
            }

            if (blockedByRuleErrorCount > 0)
            {
                var isSingle = blockedByRuleErrorCount == 1;
                Debug.LogError($"[AddressTeller] BlockedByRuleError: {blockedByRuleErrorCount} asset{(isSingle ? "" : "s")} " +
                    $"{(isSingle ? "was" : "were")} not written because a rule at the same or higher priority threw for " +
                    $"{(isSingle ? "it" : "them")} (possibly the winning rule itself). See the RuleError entries above for " +
                    "the exception detail. Run Validate to see the individual entries in the Result Window, or inspect a " +
                    "JSON/JUnit report.");
            }
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
