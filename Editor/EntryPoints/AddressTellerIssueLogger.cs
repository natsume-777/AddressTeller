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
        /// 埋め尽くしてしまう。<see cref="ValidationStatus.AssetTypeUnresolved"/> も、スクリプト欠落のアセットが多数あると
        /// 同様にコンソールを埋めるため、件数だけの1行サマリ（Warning）にまとめる。意図的に CleanupStaleEntries を OFF のまま運用しているプロジェクトでは毎回この
        /// 通知が出続けることになるため、Warning ではなく Info にして、意図した運用を阻害しないようにしている。
        /// <see cref="ValidationStatus.StaleManagedLabelKept"/> も、除去を OFF のまま運用している間は毎回出続けうる
        /// ため、<see cref="ValidationStatus.UnmatchedEntryKept"/> と同じく件数だけの1行サマリ（Info）にまとめる。
        /// <see cref="ValidationStatus.BlockedByRuleError"/> も同じ理由（1つのルール例外が多数のアセットに
        /// 波及しうるため）で個別には出さず件数だけの1行サマリにまとめるが、こちらは実際の問題（IsOk=false）
        /// のため LogError にする。個別の内容は Result Window（<see cref="AddressTellerResultWindow"/>）と
        /// レポート（JSON/JUnit）に渡す issues 一覧側でそのまま提供されるため、ここで間引いても情報は失われない。
        /// </summary>
        public static void LogAll(IReadOnlyList<ValidationResult> issues)
        {
            var unmatchedEntryKeptCount = 0;
            var blockedByRuleErrorCount = 0;
            var assetTypeUnresolvedCount = 0;
            var staleManagedLabelKeptCount = 0;

            foreach (var issue in issues)
            {
                if (issue.Status == ValidationStatus.StaleManagedLabelKept)
                {
                    staleManagedLabelKeptCount++;
                    continue;
                }

                if (issue.Status == ValidationStatus.AssetTypeUnresolved)
                {
                    assetTypeUnresolvedCount++;
                    continue;
                }

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

            if (staleManagedLabelKeptCount > 0)
            {
                var isSingle = staleManagedLabelKeptCount == 1;
                Debug.Log($"[AddressTeller] StaleManagedLabelKept: {staleManagedLabelKeptCount} entr{(isSingle ? "y carries" : "ies carry")} " +
                    "label(s) AddressTeller wrote earlier that no rule produces now; Remove stale managed labels would remove them if it were on. " +
                    "Run Validate to see the individual entries in the Result Window, or inspect a JSON/JUnit report.");
            }

            if (assetTypeUnresolvedCount > 0)
            {
                var isSingle = assetTypeUnresolvedCount == 1;
                Debug.LogWarning($"[AddressTeller] AssetTypeUnresolved: {assetTypeUnresolvedCount} asset{(isSingle ? "" : "s")} " +
                    $"could not be evaluated because {(isSingle ? "its" : "their")} type could not be resolved " +
                    "(for example, a missing script). Run Validate to see the individual assets in the Result Window, " +
                    "or inspect a JSON/JUnit report.");
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
        /// 宣言フォルダ内の未登録アセット（<see cref="DryRunResult.UnmatchedInDeclaredFolders"/>）を件数だけの1行で
        /// ログする。個別のパスは結果ウィンドウの Unmatched タブとレポートの <c>Unmatched[]</c> で提供するため、
        /// ここでは出さない。0 件なら何も出さない。<paramref name="failing"/>（<c>-addressTellerFailOnUnmatched</c>）が
        /// true のときは、exit code 2 の原因になるので LogError にする。
        /// </summary>
        public static void LogUnmatched(IReadOnlyList<string> unmatched, bool failing)
        {
            if (unmatched.Count == 0) return;

            var isSingle = unmatched.Count == 1;
            var message = $"[AddressTeller] Unmatched: {unmatched.Count} asset{(isSingle ? "" : "s")} under folders that rules declare " +
                $"get{(isSingle ? "s" : "")} no address or label from any rule (first: {unmatched[0]}). Inspect the Unmatched tab of the Result Window " +
                "or the Unmatched[] list of a JSON report.";
            if (failing)
                Debug.LogError(message);
            else
                Debug.LogWarning(message);
        }

        /// <summary>
        /// dry-run が計算した <see cref="ValidationStatus.RuleMatchedNothing"/> の通知だけを個別にログする。
        /// Apply 系の対話・CLI 経路は dry-run の issues 全体をログしないため、これだけを取り出して出す。
        /// </summary>
        public static void LogRuleMatchedNothing(IReadOnlyList<ValidationResult> issues)
        {
            foreach (var issue in issues)
                if (issue.Status == ValidationStatus.RuleMatchedNothing)
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
