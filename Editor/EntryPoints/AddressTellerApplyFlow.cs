using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Apply All / Apply with Validate の対話フロー（dry-run → 確認ダイアログ → 実行）を担う調停クラス。
    /// CLI（ApplyAllCLI/ApplyWithValidateCLI）はダイアログを出せないため対象外。
    /// </summary>
    internal static class AddressTellerApplyFlow
    {
        /// <summary>ExecuteApply() の既定タイトル。Run() 経由の呼び出し（title 未指定）で使う。</summary>
        private const string DefaultApplyAllTitle = "AddressTeller - Apply All";

        /// <summary>
        /// 自動スナップショットの保存に失敗し Apply を中止したことをダイアログでユーザーへ通知する処理。
        /// 第1引数はダイアログタイトル（Run() から渡された title。Apply All / Apply with Validate で異なる）、
        /// 第2引数がメッセージ本文。既定では EditorUtility.DisplayDialog を呼ぶが、EditMode テストが
        /// 実モーダルダイアログを開かずにこの分岐（ExecuteApply の中止経路）を検証できるよう差し替え可能にしている
        /// （既存の ExecuteApply の rules 注入・AddressTellerPostprocessor.ResolveDeletedGuids の
        /// pathToGuid 注入と同じ「テスト用シーム」の考え方。テストは差し替え後、TearDown で必ず既定値へ戻すこと）。
        /// </summary>
        internal static Action<string, string> s_notifyApplyAborted = (title, message) =>
            EditorUtility.DisplayDialog(title, message, "OK");

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

                    Debug.LogError($"[AddressTeller] Apply aborted: Validate found {validateIssues.Count(i => !i.IsOk)} issue(s).");
                    AddressTellerResultWindow.Show(validateIssues, title);
                    return;
                }
            }

            // 母集合を1回確定し、dry-run と実 Apply で同じ対象パスを使う。
            // 「詳細を見る」でウィンドウを開いている間にアセット構成が変化しても、
            // この paths は更新されない（古いスナップショットに対して Apply される）。意図的な割り切り。
            var paths = AssetDatabase.GetAllAssetPaths();

            var dryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths);

            if (dryRun.Diff.IsEmpty && dryRun.Issues.Count == 0)
            {
                Debug.Log("[AddressTeller] No changes detected.");
                return;
            }

            var message = $"Added: {dryRun.Diff.Added.Count} / Changed: {dryRun.Diff.Changed.Count}";
            if (dryRun.Diff.Removed.Count > 0)
                message += $" / ⚠ Removed: {dryRun.Diff.Removed.Count}";
            if (dryRun.Issues.Count > 0)
                message += $" / Issues: {dryRun.Issues.Count}";

            var result = EditorUtility.DisplayDialogComplex(title, message, "Apply", "Cancel", "Details");

            switch (result)
            {
                case 0: // 実行
                    ExecuteApply(settings, paths, title);
                    break;
                case 1: // キャンセル
                    break;
                case 2: // 詳細を見る
                    AddressTellerResultWindow.Show(dryRun, title, () => ExecuteApply(settings, paths, title), settings);
                    break;
            }
        }

        // 詳細ウィンドウ経由で呼ばれる場合、ここで Validate の再実行は行わない。
        // dry-run 時に validateFirst で検証済みの状態を前提とするが、ウィンドウを開いている間に
        // ルールやアセットが変化していた場合、「問題があれば中止」の契約は保証されない。意図的な割り切り。
        // internal（private ではない）: Run() からの通常経路に加え、テストから直接呼び出して
        // rules=null（リフレクション収集）経路がダミーパスに対して例外なく完了することを確認できるようにする。
        internal static void ExecuteApply(AddressableAssetSettings settings, IReadOnlyList<string> paths) =>
            ExecuteApply(settings, paths, DefaultApplyAllTitle, null);

        /// <summary>
        /// <see cref="ExecuteApply(AddressableAssetSettings, IReadOnlyList{string})"/> に評価対象ルールの注入を
        /// 追加したオーバーロード。rules が null の場合はリフレクションによるルール収集（本来の Run() 経由の挙動）を使う。
        /// テストからスナップショット・ログ・分岐のみを検証するために、リフレクション収集を経由せず
        /// 呼び出し側が用意したルール一覧を評価に使えるようにする（<see cref="AddressTellerService"/> の他のオーバーロードと同じ意図）。
        /// </summary>
        internal static void ExecuteApply(AddressableAssetSettings settings, IReadOnlyList<string> paths, IReadOnlyList<AddressRuleBase> rules) =>
            ExecuteApply(settings, paths, DefaultApplyAllTitle, rules);

        /// <summary>
        /// <see cref="ExecuteApply(AddressableAssetSettings, IReadOnlyList{string})"/> に Run() の title を
        /// 引き継ぐオーバーロード。Apply All / Apply with Validate のどちらから中止されたかがダイアログタイトルに
        /// 反映されるよう、Run() の switch から呼ぶ（rules は既定のリフレクション収集を使う）。
        /// </summary>
        internal static void ExecuteApply(AddressableAssetSettings settings, IReadOnlyList<string> paths, string title) =>
            ExecuteApply(settings, paths, title, null);

        /// <summary>
        /// ExecuteApply の実処理。title は中止ダイアログ（<see cref="s_notifyApplyAborted"/>）に渡すタイトル、
        /// rules が null の場合はリフレクションによるルール収集（本来の Run() 経由の挙動）を使う。
        /// </summary>
        internal static void ExecuteApply(AddressableAssetSettings settings, IReadOnlyList<string> paths, string title, IReadOnlyList<AddressRuleBase> rules)
        {
            if (AddressTellerSettings.AutoSnapshotBeforeApplyAll)
            {
                var snapshotPath = AddressTellerAutoSnapshotService.CaptureAndSave(settings);
                if (snapshotPath == null)
                {
                    // CleanupStaleEntries が有効な場合、Apply は managed グループのエントリを削除しうる。
                    // その後ろ盾となる Undo Last Apply 用スナップショットが保存できていない状態のまま
                    // 破壊的操作を進めないよう、ClearAll のスナップショット保存失敗時中止と対称に Apply 自体を中止する。
                    // CaptureAndSave は失敗理由を既にログ出力済みのため、ここでは中止した事実のみを追加でログする。
                    Debug.LogError("[AddressTeller] ApplyAll aborted: failed to save the auto snapshot before Apply. See the previous error for details.");
                    // ClearAll のスナップショット保存失敗時中止（ダイアログあり）と対称にする。
                    s_notifyApplyAborted(
                        title,
                        "Apply All was aborted because the auto snapshot could not be saved before Apply.\n\n" +
                        "See the previous error in the Console for details.\n\n" +
                        "Fix the SnapshotFolder setting in Project Settings, or temporarily disable " +
                        "\"Auto-snapshot before Apply\" and try again.");
                    return;
                }
            }

            IReadOnlyList<ValidationResult> issues;
            bool wasCancelled;
            // GUI 経由（Menu）のみ EditorProgressReporter でプログレスバーを表示する。
            // CLI（ApplyAllCLI/ApplyWithValidateCLI）はコンソール表示のみで進捗バーは不要なため対象外
            // （このクラス自体が CLI 非対応であることはクラス冒頭のコメントの通り）。
            using (var progress = new EditorProgressReporter("AddressTeller - Apply All"))
            {
                issues = rules != null
                    ? AddressTellerService.ApplyAll(paths, settings, progress, rules)
                    : AddressTellerService.ApplyAll(paths, settings, progress);
                wasCancelled = progress.WasCancelled;
            }

            if (wasCancelled)
            {
                // ApplyAll はキャンセル時点までの処理済みアセットへの書き込みを既に完了している（部分適用）。
                // 巻き戻しは行わないため、その旨を明示してユーザーに伝える。
                Debug.LogWarning("[AddressTeller] ApplyAll was cancelled by the user. Assets processed before cancellation have already been written (partial apply).");
                EditorUtility.DisplayDialog(
                    "AddressTeller - Apply All",
                    "Apply All was cancelled.\n\nAssets processed before cancellation have already been written (partial apply).",
                    "OK");
            }

            if (issues.Count == 0)
            {
                if (!wasCancelled)
                    Debug.Log("[AddressTeller] ApplyAll completed.");
                return;
            }

            foreach (var issue in issues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            Debug.LogError($"[AddressTeller] ApplyAll completed with {issues.Count} issue(s).");
        }
    }
}
