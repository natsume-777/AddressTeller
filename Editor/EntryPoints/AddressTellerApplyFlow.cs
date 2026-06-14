using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// Apply All / Apply with Validate の対話フロー（dry-run → 確認ダイアログ → 実行）を担う調停クラス。
    /// CLI（ApplyAllCLI/ApplyWithValidateCLI）はダイアログを出せないため対象外。
    /// </summary>
    internal static class AddressTellerApplyFlow
    {
        /// <summary>
        /// dry-run で差分・問題点を計算し、確認ダイアログを経て Apply を実行する。
        /// validateFirst が true の場合は先に ValidateAll を行い、問題があれば中止する。
        /// </summary>
        public static void Run(AddressableAssetSettings settings, bool validateFirst, string title = "AddressTeller - Apply Preview")
        {
            if (validateFirst)
            {
                var validateIssues = AddressTellerService.ValidateAll(settings);

                // validateIssues には GroupWillBeCreated（IsOk=true、AutoCreateMissingGroups による作成予定の提示）が
                // 含まれる場合がある。中止が必要なのは IsOk=false の要素のみ。
                if (validateIssues.Any(i => !i.IsOk))
                {
                    foreach (var issue in validateIssues)
                        Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

                    Debug.LogError($"[AddressTeller] Validate で {validateIssues.Count(i => !i.IsOk)} 件の問題が見つかったため、Apply を中止しました。");
                    AddressTellerResultWindow.Show(validateIssues, title);
                    return;
                }
            }

            // 母集合を1回確定し、dry-run と実 Apply で同じ対象パスを使う。
            // 「詳細を見る」でウィンドウを開いている間にアセット構成が変化しても、
            // この paths は更新されない（古いスナップショットに対して Apply される）。意図的な割り切り（M-1）。
            var paths = AssetDatabase.GetAllAssetPaths();

            var dryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths);

            if (dryRun.Diff.IsEmpty && dryRun.Issues.Count == 0)
            {
                Debug.Log("[AddressTeller] 変更はありません。");
                return;
            }

            var message = $"追加 {dryRun.Diff.Added.Count} 件 / 変更 {dryRun.Diff.Changed.Count} 件";
            if (dryRun.Diff.Removed.Count > 0)
                message += $" / ⚠ 削除 {dryRun.Diff.Removed.Count} 件";
            if (dryRun.Issues.Count > 0)
                message += $" / 問題 {dryRun.Issues.Count} 件";

            var result = EditorUtility.DisplayDialogComplex(title, message, "実行", "キャンセル", "詳細を見る");

            switch (result)
            {
                case 0: // 実行
                    ExecuteApply(settings, paths);
                    break;
                case 1: // キャンセル
                    break;
                case 2: // 詳細を見る
                    AddressTellerResultWindow.Show(dryRun, title, () => ExecuteApply(settings, paths), settings);
                    break;
            }
        }

        // 詳細ウィンドウ経由で呼ばれる場合、ここで Validate の再実行は行わない。
        // dry-run 時に validateFirst で検証済みの状態を前提とするが、ウィンドウを開いている間に
        // ルールやアセットが変化していた場合、「問題があれば中止」の契約は保証されない。意図的な割り切り（M-2）。
        private static void ExecuteApply(AddressableAssetSettings settings, IReadOnlyList<string> paths)
        {
            if (AddressTellerSettings.AutoSnapshotBeforeApplyAll)
                AddressTellerAutoSnapshotService.CaptureAndSave(settings);

            var issues = AddressTellerService.ApplyAll(paths, settings);
            if (issues.Count == 0)
            {
                Debug.Log("[AddressTeller] ApplyAll が完了しました。");
                return;
            }

            foreach (var issue in issues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            Debug.LogError($"[AddressTeller] ApplyAll 完了: {issues.Count} 件の問題が見つかりました。");
        }
    }
}
