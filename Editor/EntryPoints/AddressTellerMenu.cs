using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
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
        /// CI 向け。-executeMethod Natsume777.AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI で実行。
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

            // 母集合を1回確定し、dry-run（レポート化）と実 Apply で同じ対象パスを使う。
            var paths = AssetDatabase.GetAllAssetPaths();
            var dryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths);

            var applyIssues = AddressTellerService.ApplyAll(paths, settings);
            foreach (var issue in applyIssues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            ExitWithReport(dryRun, applyIssues, cliArgs);
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
        /// CI 向け。-executeMethod Natsume777.AddressTeller.Editor.AddressTellerMenu.ApplyWithValidateCLI で実行。
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

            var validateIssues = AddressTellerService.ValidateAll(settings);
            if (validateIssues.Count > 0)
            {
                foreach (var issue in validateIssues)
                    Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

                Debug.LogError($"[AddressTeller] Validate で {validateIssues.Count} 件の問題が見つかったため、Apply を中止しました。");

                // Apply を行わないため、現在の状態のままの dry-run をレポート化する。
                var paths = AssetDatabase.GetAllAssetPaths();
                var dryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths);
                ExitWithReport(dryRun, validateIssues, cliArgs);
                return;
            }

            // 母集合を1回確定し、dry-run（レポート化）と実 Apply で同じ対象パスを使う。
            var applyPaths = AssetDatabase.GetAllAssetPaths();
            var applyDryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, applyPaths);

            var applyIssues = AddressTellerService.ApplyAll(applyPaths, settings);
            foreach (var issue in applyIssues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            ExitWithReport(applyDryRun, applyIssues, cliArgs);
        }

        /// <summary>
        /// dry-run 結果からレポート DTO を生成し、要求されていればファイル出力する。
        /// exit code は dry-run の判定（<see cref="AddressTellerReportBuilder.DetermineExitCode"/>）を基本としつつ、
        /// Apply/Validate 実行後に得られた issues（<paramref name="executionIssues"/>）に
        /// エラー（IsOk=false）が含まれる場合は 2 に昇格させる。
        /// レポートの書き込みに失敗した場合は exit 3。
        /// </summary>
        private static void ExitWithReport(DryRunResult dryRun, IReadOnlyList<ValidationResult> executionIssues, AddressTellerCliArgs cliArgs)
        {
            var exitCode = AddressTellerReportBuilder.DetermineExitCode(dryRun, executionIssues);

            if (!string.IsNullOrEmpty(cliArgs.ReportPath))
            {
                var report = AddressTellerReportBuilder.Build(dryRun);
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
        /// CI 向け。-executeMethod Natsume777.AddressTeller.Editor.AddressTellerMenu.CheckCLI で実行。
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

            var paths = AssetDatabase.GetAllAssetPaths();
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths);

            Debug.Log($"[AddressTeller] Check 完了: 差分 追加{result.Diff.Added.Count}件 / 削除{result.Diff.Removed.Count}件 / 変更{result.Diff.Changed.Count}件、問題 {result.Issues.Count}件。");
            foreach (var issue in result.Issues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            if (!string.IsNullOrEmpty(cliArgs.ReportPath))
            {
                var report = AddressTellerReportBuilder.Build(result);
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
