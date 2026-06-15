using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    public static class AddressTellerMenu
    {
        [MenuItem("Tools/AddressTeller/Apply All")]
        public static void ApplyAll()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。Addressables を初期化してください。");
                return;
            }

            AddressTellerApplyFlow.Run(settings, validateFirst: false);
        }

        /// <summary>
        /// 指定グループの現メンバー起点で、有効な全ルールを適用した場合の dry-run プレビューを表示する。
        /// 即時 Apply は行わない（ResultWindow から手動で Apply All / Validate を実行する）。
        /// グループ選択はメニュー直下のドロップダウンで行う。
        /// </summary>
        [MenuItem("Tools/AddressTeller/Preview Group...")]
        public static void PreviewGroup()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。Addressables を初期化してください。");
                return;
            }

            var groups = settings.groups
                .Where(g => g != null)
                .OrderBy(g => g.Name, StringComparer.Ordinal)
                .ToList();

            if (groups.Count == 0)
            {
                Debug.LogError("[AddressTeller] グループが存在しません。");
                return;
            }

            var menuItems = groups.Select(g => new GUIContent(g.Name)).ToArray();

            EditorUtility.DisplayCustomMenu(
                new Rect(Event.current?.mousePosition ?? Vector2.zero, Vector2.zero),
                menuItems,
                -1,
                (data, options, selected) =>
                {
                    if (selected < 0 || selected >= groups.Count) return;
                    AddressTellerScopedPreview.RunGroupPreview(settings, groups[selected]);
                },
                null);
        }

        /// <summary>
        /// AddressTeller が管理するグループ（いずれかのルールが GroupName として参照しているグループ）のエントリ
        /// （アドレス・グループ割り当て・ラベル）を削除する。
        /// 既定の安全側運用（資産単位の所有権判定・デフォルト OFF）から意図的に逸脱した、
        /// 公開前パッケージの初期セットアップ用途向けの割り切り機能。実行前に専用スナップショット
        /// （SnapshotFolder/Clear 以下、ローテーション対象外）を必須で保存し、確認ダイアログを経て実行する。
        /// 全エントリを対象にする場合は <see cref="ClearCLI"/> の -addressTellerClearScope all を使う。
        /// </summary>
        [MenuItem("Tools/AddressTeller/Clear All Addresses & Labels...")]
        public static void ClearAll()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。Addressables を初期化してください。");
                return;
            }

            var setup = RuleEvaluationPipeline.BuildSetup(settings, RuleCollector.CollectRules());
            var managedGroups = setup.ManagedGroups;

            var entryCount = settings.groups
                .Where(g => g != null && managedGroups.Contains(g.Name))
                .Sum(g => g.entries.Count);

            var message = $"管理対象グループの Addressable エントリ {entryCount} 件（アドレス・グループ割り当て・ラベル）を削除します。\n"
                + $"対象: Managed\n\n"
                + "実行前に SnapshotFolder/Clear/ 以下へスナップショットを保存します。\n"
                + "この操作はそのスナップショットから Restore で復元できます。よろしいですか？";

            if (!EditorUtility.DisplayDialog("AddressTeller - Clear All Addresses & Labels", message, "クリアする", "キャンセル"))
                return;

            var snapshotPath = AddressTellerClearSnapshotService.CaptureAndSave(settings, out var snapshotError);
            if (snapshotPath == null)
            {
                EditorUtility.DisplayDialog(
                    "AddressTeller - Clear All Addresses & Labels",
                    $"クリア前のスナップショット保存に失敗したため中止しました。\n\n{snapshotError}",
                    "OK");
                Debug.LogError($"[AddressTeller] Clear All: {snapshotError}");
                return;
            }

            var cleared = AddressTellerClearService.Clear(settings, ClearScope.Managed, managedGroups);
            foreach (var entry in cleared)
                Debug.LogWarning($"[AddressTeller] Cleared entry: guid={entry.Guid}, group='{entry.GroupName}', address='{entry.Address}', labels=[{string.Join(", ", entry.Labels)}]");

            Debug.Log($"[AddressTeller] Clear All 完了: {cleared.Count} 件のエントリを削除しました（scope=Managed）。スナップショット: {snapshotPath}");
        }

        /// <summary>
        /// CI 向け。-executeMethod AddressTeller.Editor.AddressTellerMenu.ClearCLI で実行。
        /// 確認フラグ <c>-addressTellerConfirmClear</c> が無い場合は意図的な拒否として exit code 4 で終了する
        /// （ダイアログを出せない CLI での誤実行防止）。
        /// <c>-addressTellerClearScope all|managed</c> でクリア対象を切り替える（既定 managed）。
        /// 実行前に専用スナップショット（SnapshotFolder/Clear 以下）の保存を必須とし、失敗時は exit code 3 で中止する。
        /// exit code: 0=完了、3=実行環境エラー（スナップショット保存失敗を含む）、4=確認フラグ未指定。
        /// </summary>
        public static void ClearCLI()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] 引数の解析に失敗しました: {parseError}");
                EditorApplication.Exit(3);
                return;
            }

            if (!cliArgs.ConfirmClear)
            {
                Debug.LogError($"[AddressTeller] Clear All の実行には -addressTellerConfirmClear の指定が必要です（意図的な拒否）。");
                EditorApplication.Exit(4);
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。");
                EditorApplication.Exit(3);
                return;
            }

            var snapshotPath = AddressTellerClearSnapshotService.CaptureAndSave(settings, out var snapshotError);
            if (snapshotPath == null)
            {
                Debug.LogError($"[AddressTeller] Clear All: {snapshotError}");
                EditorApplication.Exit(3);
                return;
            }

            IReadOnlyCollection<string> managedGroups = null;
            if (cliArgs.ClearScope == ClearScope.Managed)
            {
                var setup = RuleEvaluationPipeline.BuildSetup(settings, RuleCollector.CollectRules());
                managedGroups = setup.ManagedGroups;
            }

            var cleared = AddressTellerClearService.Clear(settings, cliArgs.ClearScope, managedGroups);
            foreach (var entry in cleared)
                Debug.LogWarning($"[AddressTeller] Cleared entry: guid={entry.Guid}, group='{entry.GroupName}', address='{entry.Address}', labels=[{string.Join(", ", entry.Labels)}]");

            Debug.Log($"[AddressTeller] Clear All 完了: {cleared.Count} 件のエントリを削除しました（scope={cliArgs.ClearScope}）。スナップショット: {snapshotPath}");

            EditorApplication.Exit(0);
        }

        [MenuItem("Tools/AddressTeller/Validate")]
        public static void Validate()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。Addressables を初期化してください。");
                return;
            }

            var issues = AddressTellerService.ValidateAll(settings);
            if (issues.Count == 0)
            {
                Debug.Log("[AddressTeller] Validate 完了: 問題なし。");
                return;
            }

            foreach (var issue in issues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            Debug.LogError($"[AddressTeller] Validate 完了: {issues.Count} 件の問題が見つかりました。");
        }

        /// <summary>
        /// CI 向け。-executeMethod AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI で実行。
        /// 自動スナップショット（<see cref="AddressTellerSettings.AutoSnapshotBeforeApplyAll"/>）は
        /// 対話メニュー（Apply All / Apply with Validate）のみが対象であり、
        /// ビルド時間とディスク I/O を避けるため CLI/CI では実行しない。
        /// -addressTellerReport &lt;path&gt; / -addressTellerReportFormat json|junit でレポートをファイル出力できる
        /// （Apply 実行前の dry-run 結果を基にレポート化する）。
        /// exit code: 0=差分なし・問題なし、1=ドリフトあり、2=Validation エラーあり、3=実行環境エラー。
        /// </summary>
        public static void ApplyAllCLI()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] 引数の解析に失敗しました: {parseError}");
                EditorApplication.Exit(3);
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。");
                EditorApplication.Exit(3);
                return;
            }

            if (!TryBuildCliRules(cliArgs, out var rules, out var rulesError))
            {
                Debug.LogError($"[AddressTeller] {rulesError}");
                EditorApplication.Exit(3);
                return;
            }

            // 母集合を1回確定し、dry-run（レポート化）と実 Apply で同じ対象パスを使う。
            var paths = AssetDatabase.GetAllAssetPaths();
            var dryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths, rules);

            var applyIssues = AddressTellerService.ApplyAll(paths, settings, NullProgressReporter.Instance, rules);
            foreach (var issue in applyIssues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            ExitWithReport(dryRun, applyIssues, cliArgs, settings);
        }

        /// <summary>Validate で問題が見つかった場合は Apply を中止する。</summary>
        [MenuItem("Tools/AddressTeller/Apply with Validate")]
        public static void ApplyWithValidate()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。Addressables を初期化してください。");
                return;
            }

            AddressTellerApplyFlow.Run(settings, validateFirst: true);
        }

        /// <summary>
        /// CI 向け。-executeMethod AddressTeller.Editor.AddressTellerMenu.ApplyWithValidateCLI で実行。
        /// 自動スナップショット（<see cref="AddressTellerSettings.AutoSnapshotBeforeApplyAll"/>）は
        /// 対話メニュー（Apply All / Apply with Validate）のみが対象であり、
        /// ビルド時間とディスク I/O を避けるため CLI/CI では実行しない。
        /// -addressTellerReport &lt;path&gt; / -addressTellerReportFormat json|junit でレポートをファイル出力できる
        /// （Validate で問題が見つかった場合は Apply 前の dry-run 結果、それ以外は Apply 実行前の dry-run 結果を基にレポート化する）。
        /// exit code: 0=差分なし・問題なし、1=ドリフトあり、2=Validation エラーあり、3=実行環境エラー。
        /// </summary>
        public static void ApplyWithValidateCLI()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] 引数の解析に失敗しました: {parseError}");
                EditorApplication.Exit(3);
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。");
                EditorApplication.Exit(3);
                return;
            }

            if (!TryBuildCliRules(cliArgs, out var rules, out var rulesError))
            {
                Debug.LogError($"[AddressTeller] {rulesError}");
                EditorApplication.Exit(3);
                return;
            }

            var validateIssues = AddressTellerService.ValidateAll(settings, NullProgressReporter.Instance, rules);

            // validateIssues には GroupWillBeCreated（IsOk=true、AutoCreateMissingGroups による作成予定の提示）が
            // 含まれる場合がある。中止が必要なのは IsOk=false の要素のみ。
            if (validateIssues.Any(i => !i.IsOk))
            {
                foreach (var issue in validateIssues)
                    Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

                Debug.LogError($"[AddressTeller] Validate で {validateIssues.Count(i => !i.IsOk)} 件の問題が見つかったため、Apply を中止しました。");

                // Apply を行わないため、現在の状態のままの dry-run をレポート化する。
                var paths = AssetDatabase.GetAllAssetPaths();
                var dryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths, rules);
                ExitWithReport(dryRun, validateIssues, cliArgs, settings);
                return;
            }

            // 母集合を1回確定し、dry-run（レポート化）と実 Apply で同じ対象パスを使う。
            var applyPaths = AssetDatabase.GetAllAssetPaths();
            var applyDryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, applyPaths, rules);

            var applyIssues = AddressTellerService.ApplyAll(applyPaths, settings, NullProgressReporter.Instance, rules);
            foreach (var issue in applyIssues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            ExitWithReport(applyDryRun, applyIssues, cliArgs, settings);
        }

        /// <summary>
        /// CLI指定の <c>-addressTellerDisableRules</c> と永続設定（<see cref="AddressTellerSettings.DisabledRuleClassNames"/>）
        /// の和集合で除外したルール一覧を返す。
        /// </summary>
        /// <remarks>
        /// この除外は CLI 実行限定の一時除外であり、Postprocessor/Menu には波及しない。
        /// DEVELOPMENT_GUIDELINESの「設定フラグの全エントリポイント一貫評価」原則からの意図的な逸脱。
        /// </remarks>
        private static bool TryBuildCliRules(AddressTellerCliArgs cliArgs, out IReadOnlyList<AddressRuleBase> rules, out string error)
        {
            if (!RuleCollector.TryCollectEnabledRules(RuleCollector.CollectRules(), AddressTellerSettings.DisabledRuleClassNames, cliArgs.DisableRuleFullNames, out rules, out var unknown))
            {
                error = $"-addressTellerDisableRules に未知のルールクラスが指定されています: {string.Join(", ", unknown)}";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// dry-run 結果からレポート DTO を生成し、要求されていればファイル出力する。
        /// exit code は dry-run の判定（<see cref="AddressTellerReportBuilder.DetermineExitCode"/>）を基本としつつ、
        /// Apply/Validate 実行後に得られた issues（<paramref name="executionIssues"/>）に
        /// エラー（IsOk=false）が含まれる場合は 2 に昇格させる。
        /// レポートの書き込みに失敗した場合は exit 3。
        /// </summary>
        private static void ExitWithReport(DryRunResult dryRun, IReadOnlyList<ValidationResult> executionIssues, AddressTellerCliArgs cliArgs, AddressableAssetSettings settings)
        {
            var exitCode = AddressTellerReportBuilder.DetermineExitCode(dryRun, executionIssues);

            if (!string.IsNullOrEmpty(cliArgs.ReportPath))
            {
                var report = AddressTellerReportBuilder.Build(dryRun, settings);
                report.Summary.ExitCode = exitCode;

                if (!AddressTellerReportWriter.WriteToFile(cliArgs.ReportPath, report, cliArgs.ReportFormat))
                {
                    EditorApplication.Exit(3);
                    return;
                }
            }

            EditorApplication.Exit(exitCode);
        }

        /// <summary>
        /// CI 向け。-executeMethod AddressTeller.Editor.AddressTellerMenu.CheckCLI で実行。
        /// Apply を行わない dry-run（読み取り専用）で、ルール適用後の状態と現在の状態の差分・問題を検出する。
        /// -addressTellerReport &lt;path&gt; / -addressTellerReportFormat json|junit でレポートをファイル出力できる。
        /// exit code: 0=差分なし・問題なし、1=ドリフトあり、2=Validation エラーあり、3=実行環境エラー。
        /// </summary>
        public static void CheckCLI()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] 引数の解析に失敗しました: {parseError}");
                EditorApplication.Exit(3);
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。");
                EditorApplication.Exit(3);
                return;
            }

            if (!TryBuildCliRules(cliArgs, out var rules, out var rulesError))
            {
                Debug.LogError($"[AddressTeller] {rulesError}");
                EditorApplication.Exit(3);
                return;
            }

            var paths = AssetDatabase.GetAllAssetPaths();
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths, rules);

            Debug.Log($"[AddressTeller] Check 完了: 差分 追加{result.Diff.Added.Count}件 / 削除{result.Diff.Removed.Count}件 / 変更{result.Diff.Changed.Count}件、問題 {result.Issues.Count}件。");
            foreach (var issue in result.Issues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            if (!string.IsNullOrEmpty(cliArgs.ReportPath))
            {
                var report = AddressTellerReportBuilder.Build(result, settings);
                if (!AddressTellerReportWriter.WriteToFile(cliArgs.ReportPath, report, cliArgs.ReportFormat))
                {
                    EditorApplication.Exit(3);
                    return;
                }
            }

            EditorApplication.Exit(AddressTellerReportBuilder.DetermineExitCode(result));
        }
    }
}
