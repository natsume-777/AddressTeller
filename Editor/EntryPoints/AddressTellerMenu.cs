using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>Menu items under Tools/AddressTeller for the interactive Apply/Validate/Clear workflow, plus their CLI counterparts.</summary>
    public static class AddressTellerMenu
    {
        /// <summary>Applies all enabled rules to every asset in the project without a preceding Validate pass.</summary>
        [MenuItem("Tools/AddressTeller/Apply All")]
        public static void ApplyAll()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found. Please initialize Addressables.");
                return;
            }

            AddressTellerApplyFlow.Run(settings, validateFirst: false);
        }

        /// <summary>
        /// Shows a dry-run preview, starting from the current members of the selected group, of applying
        /// all enabled rules. Does not Apply immediately (run Apply All / Validate manually from the
        /// ResultWindow). The group is chosen from the dropdown shown directly under the menu item.
        /// </summary>
        [MenuItem("Tools/AddressTeller/Preview Group...")]
        public static void PreviewGroup()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found. Please initialize Addressables.");
                return;
            }

            var groups = settings.groups
                .Where(g => g != null)
                .OrderBy(g => g.Name, StringComparer.Ordinal)
                .ToList();

            if (groups.Count == 0)
            {
                Debug.LogError("[AddressTeller] No groups exist.");
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
        /// ClearAll() のルール構成エラー中止通知を差し替え可能にするテスト用シーム。
        /// 既定では EditorUtility.DisplayDialog を呼ぶが、EditMode テストが実モーダルダイアログを開かずに
        /// この中止経路を検証できるよう差し替え可能にしている
        /// （AddressTellerApplyFlow.s_notifyApplyAborted と同じ「テスト用シーム」の考え方。
        /// テストは差し替え後、TearDown で必ず既定値へ戻すこと）。
        /// </summary>
        internal static Action<string> s_notifyClearAborted = message =>
            EditorUtility.DisplayDialog("AddressTeller - Clear All Addresses & Labels", message, "OK");

        /// <summary>
        /// Removes entries (address, group assignment, and labels) from groups managed by AddressTeller
        /// (any group referenced as a rule's GroupName).
        /// This is an intentional exception to the default safe-by-default policy (asset-level ownership
        /// checks, off by default), aimed at pre-release package initial setup scenarios. After the
        /// confirmation dialog is accepted, a dedicated snapshot (under SnapshotFolder/Clear, excluded
        /// from rotation) is saved immediately before the removal; if saving fails, the clear is aborted.
        /// To target every entry, use -addressTellerClearScope all with <see cref="ClearCLI"/>.
        /// If even one rule's Configure() has failed, managedGroups (the ownership check) cannot be
        /// trusted, so this aborts before showing the confirmation dialog (mirroring how
        /// <see cref="ClearCLI"/> aborts with exit code 3).
        /// </summary>
        [MenuItem("Tools/AddressTeller/Clear All Addresses & Labels...")]
        public static void ClearAll()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found. Please initialize Addressables.");
                return;
            }

            ClearAll(settings, RuleCollector.CollectRules());
        }

        /// <summary>
        /// <see cref="ClearAll()"/> のコア処理。評価対象ルールを注入可能にしたオーバーロード（internal）。
        /// RuleCollector.CollectRules() はテストアセンブリ（nunit.framework 参照）を除外するため、
        /// テストからルール構成エラー（Configure() 失敗）による中止分岐を決定的に検証できるよう分離する
        /// （AddressTellerApplyFlow.ExecuteApply の rules 注入オーバーロードと同じ意図）。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> が null の場合。</exception>
        internal static void ClearAll(AddressableAssetSettings settings, IReadOnlyList<AddressRuleBase> rules)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            if (!TryResolveManagedGroups(settings, rules, out var managedGroups, out var abortDetail))
            {
                // ルールの Configure() が1件でも失敗していると managedGroups（所有権判定）が信頼できない。
                // 破壊的操作である Clear はこの状態のまま実行してはいけないため、確認ダイアログを出す前に中止する
                // （ClearCLI が exit code 3 で中止するのと対称の安全対策）。
                s_notifyClearAborted($"Aborted: {abortDetail}");
                Debug.LogError($"[AddressTeller] Clear All aborted: {abortDetail}");
                return;
            }

            var entryCount = settings.groups
                .Where(g => g != null && managedGroups.Contains(g.Name))
                .Sum(g => g.entries.Count);

            var message = $"This will remove {entryCount} Addressable entry/entries (address, group assignment, and labels) from managed groups.\n"
                + "Scope: Managed\n\n"
                + "A snapshot will be saved to SnapshotFolder/Clear/ before the operation.\n"
                + "You can restore the previous state by using Restore on that snapshot. Proceed?";

            if (!EditorUtility.DisplayDialog("AddressTeller - Clear All Addresses & Labels", message, "Clear", "Cancel"))
                return;

            var snapshotPath = AddressTellerClearSnapshotService.CaptureAndSave(settings, out var snapshotError);
            if (snapshotPath == null)
            {
                EditorUtility.DisplayDialog(
                    "AddressTeller - Clear All Addresses & Labels",
                    $"Aborted: failed to save snapshot before clear.\n\n{snapshotError}",
                    "OK");
                Debug.LogError($"[AddressTeller] Clear All: {snapshotError}");
                return;
            }

            var cleared = AddressTellerClearService.Clear(settings, ClearScope.Managed, managedGroups);
            foreach (var entry in cleared)
                Debug.LogWarning($"[AddressTeller] Cleared entry: guid={entry.Guid}, group='{entry.GroupName}', address='{entry.Address}', labels=[{string.Join(", ", entry.Labels)}]");

            Debug.Log($"[AddressTeller] Clear All completed: {cleared.Count} entry/entries removed (scope=Managed). Snapshot: {snapshotPath}");
        }

        /// <summary>
        /// scope=Managed でのクリア系操作（<see cref="ClearAll(AddressableAssetSettings, IReadOnlyList{AddressRuleBase})"/> /
        /// <see cref="ClearCLI"/>）が共有する、managedGroups（所有権判定）の解決処理。
        /// ルールの Configure() が1件でも失敗している場合、managedGroups は信頼できないため false を返し、
        /// <paramref name="abortDetail"/> に失敗したルールごとの詳細メッセージを返す
        /// （RuleCollector.CollectRules() は無効化中のルールも含むため、Project Settings でルールを無効化しても
        /// この中止は解除されない旨も含める）。
        /// </summary>
        private static bool TryResolveManagedGroups(AddressableAssetSettings settings, IReadOnlyList<AddressRuleBase> rules, out HashSet<string> managedGroups, out string abortDetail)
        {
            var setup = RuleEvaluationPipeline.BuildSetup(settings, rules);
            if (setup.ConfigureFailures.Count > 0)
            {
                var detail = string.Join("; ", setup.ConfigureFailures.Select(f => f.Message));
                abortDetail = $"{setup.ConfigureFailures.Count} rule(s) failed to configure; managed-group ownership cannot be trusted for scope=Managed. {detail} "
                    + "Disabling the rule in Project Settings will not resolve this (RuleCollector still collects disabled rules for managed-group tracking); fix the rule's Configure() instead.";
                managedGroups = null;
                return false;
            }

            managedGroups = setup.ManagedGroups;
            abortDetail = null;
            return true;
        }

        /// <summary>
        /// For CI. Run via -executeMethod AddressTeller.Editor.AddressTellerMenu.ClearCLI.
        /// If the confirmation flag <c>-addressTellerConfirmClear</c> is absent, this is treated as an
        /// intentional refusal and exits with code 4 (to prevent accidental execution in a CLI
        /// environment where no dialog can be shown).
        /// <c>-addressTellerClearScope all|managed</c> switches the clear target (default managed).
        /// For scope=managed, if even one rule's Configure() has failed, managedGroups (the ownership
        /// check) cannot be trusted, so this aborts with exit code 3 before saving the snapshot (to avoid
        /// a snapshot being left behind with nothing actually removed). Saving a dedicated snapshot
        /// (under SnapshotFolder/Clear) before running is required, and a failure to save also aborts
        /// with exit code 3.
        /// Exit codes: 0 = completed, 3 = environment error (including rule configuration errors and
        /// snapshot save failures), 4 = confirmation flag not specified.
        /// </summary>
        public static void ClearCLI()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] Failed to parse arguments: {parseError}");
                EditorApplication.Exit(3);
                return;
            }

            if (!cliArgs.ConfirmClear)
            {
                Debug.LogError($"[AddressTeller] Clear All requires -addressTellerConfirmClear to be specified (intentional rejection).");
                EditorApplication.Exit(4);
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found.");
                EditorApplication.Exit(3);
                return;
            }

            // managedGroups の解決（所有権判定が信頼できるかの確認）は、スナップショット保存より先に行う。
            // 逆順だと、ルール構成エラーで中止した際に「何も削除していないのにスナップショットだけが残る」
            // 孤児ファイルを作ってしまう。
            IReadOnlyCollection<string> managedGroups = null;
            if (cliArgs.ClearScope == ClearScope.Managed)
            {
                if (!TryResolveManagedGroups(settings, RuleCollector.CollectRules(), out var resolvedManagedGroups, out var abortDetail))
                {
                    Debug.LogError($"[AddressTeller] Clear All aborted: {abortDetail}");
                    EditorApplication.Exit(3);
                    return;
                }
                managedGroups = resolvedManagedGroups;
            }

            var snapshotPath = AddressTellerClearSnapshotService.CaptureAndSave(settings, out var snapshotError);
            if (snapshotPath == null)
            {
                Debug.LogError($"[AddressTeller] Clear All: {snapshotError}");
                EditorApplication.Exit(3);
                return;
            }

            var cleared = AddressTellerClearService.Clear(settings, cliArgs.ClearScope, managedGroups);
            foreach (var entry in cleared)
                Debug.LogWarning($"[AddressTeller] Cleared entry: guid={entry.Guid}, group='{entry.GroupName}', address='{entry.Address}', labels=[{string.Join(", ", entry.Labels)}]");

            Debug.Log($"[AddressTeller] Clear All completed: {cleared.Count} entry/entries removed (scope={cliArgs.ClearScope}). Snapshot: {snapshotPath}");

            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Runs all enabled rules against every asset in the project without writing anything, and logs
        /// any problems found. Opens the result window when at least one issue (IsOk=false) is detected.
        /// </summary>
        [MenuItem("Tools/AddressTeller/Validate")]
        public static void Validate()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found. Please initialize Addressables.");
                return;
            }

            Validate(settings, null);
        }

        /// <summary>
        /// Validate() が issue（IsOk=false）を検出した際に結果ウィンドウを開く処理。既定では
        /// <see cref="AddressTellerResultWindow.Show(IReadOnlyList{ValidationResult}, string)"/> を呼ぶが、
        /// EditMode テストが実ウィンドウを開かずにこの経路（呼ばれた issues・タイトル）を検証できるよう
        /// 差し替え可能にしている（<see cref="s_notifyClearAborted"/> / <see cref="AddressTellerApplyFlow.s_notifyApplyAborted"/>
        /// と同じ「テスト用シーム」の考え方。テストは差し替え後、TearDown で必ず既定値へ戻すこと）。
        /// </summary>
        internal static Action<IReadOnlyList<ValidationResult>, string> s_showResultWindow =
            (issues, title) => AddressTellerResultWindow.Show(issues, title);

        /// <summary>
        /// <see cref="Validate()"/> のコア処理。評価対象ルールを注入可能にしたオーバーロード（internal）。
        /// rules が null の場合はリフレクションによるルール収集（本来の Validate() 経由の挙動）を使う
        /// （<see cref="AddressTellerApplyFlow.ExecuteApply(AddressableAssetSettings, IReadOnlyList{string}, IReadOnlyList{AddressRuleBase})"/>
        /// と同じ「テスト用シーム」の意図。テストから issues が実際に発生する状況を決定的に再現できるようにする）。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> が null の場合。</exception>
        internal static void Validate(AddressableAssetSettings settings, IReadOnlyList<AddressRuleBase> rules)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var issues = rules != null
                ? AddressTellerService.ValidateAll(settings, NullProgressReporter.Instance, rules)
                : AddressTellerService.ValidateAll(settings);

            if (issues.Count == 0)
            {
                Debug.Log("[AddressTeller] Validate completed: no issues.");
                return;
            }

            AddressTellerIssueLogger.LogAll(issues);

            // issues には GroupWillBeCreated（IsOk=true、グループ自動作成の通知）が含まれる場合があるため、
            // 「完了」を error として扱うべきかどうかは IsOk=false の件数で判定する。
            var errorCount = issues.Count(i => !i.IsOk);
            if (errorCount == 0)
            {
                Debug.Log($"[AddressTeller] Validate completed: {issues.Count} notice(s) (no issues).");
                return;
            }

            Debug.LogError($"[AddressTeller] Validate completed: {errorCount} issue(s) found.");

            // Apply All / Apply with Validate の中止経路と対称に、IsOk=false の問題が1件以上ある場合のみ
            // 結果ウィンドウを開く。GroupWillBeCreated 等の通知のみ（errorCount == 0）ではフォーカスを奪わない。
            s_showResultWindow(issues, "AddressTeller - Validate");
        }

        /// <summary>
        /// For CI. Run via -executeMethod AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI.
        /// The automatic snapshot (<see cref="AddressTellerSettings.AutoSnapshotBeforeApplyAll"/>) only
        /// applies to the interactive menu items (Apply All / Apply with Validate); it is not taken from
        /// the CLI/CI, to avoid extra build time and disk I/O.
        /// A report can be written to a file with -addressTellerReport &lt;path&gt; /
        /// -addressTellerReportFormat json|junit (built from the dry-run result computed before Apply runs).
        /// Exit codes: 0 = no diff and no issues, 1 = drift found, 2 = validation errors found, 3 = environment error.
        /// </summary>
        public static void ApplyAllCLI()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] Failed to parse arguments: {parseError}");
                EditorApplication.Exit(3);
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found.");
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
            AddressTellerIssueLogger.LogAll(applyIssues);

            ExitWithReport(dryRun, applyIssues, cliArgs, settings);
        }

        /// <summary>Aborts Apply if Validate finds a problem.</summary>
        [MenuItem("Tools/AddressTeller/Apply with Validate")]
        public static void ApplyWithValidate()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found. Please initialize Addressables.");
                return;
            }

            AddressTellerApplyFlow.Run(settings, validateFirst: true);
        }

        /// <summary>
        /// For CI. Run via -executeMethod AddressTeller.Editor.AddressTellerMenu.ApplyWithValidateCLI.
        /// The automatic snapshot (<see cref="AddressTellerSettings.AutoSnapshotBeforeApplyAll"/>) only
        /// applies to the interactive menu items (Apply All / Apply with Validate); it is not taken from
        /// the CLI/CI, to avoid extra build time and disk I/O.
        /// A report can be written to a file with -addressTellerReport &lt;path&gt; /
        /// -addressTellerReportFormat json|junit (built from the pre-Apply dry-run result if Validate
        /// found a problem, or from the dry-run result computed before Apply runs otherwise).
        /// Exit codes: 0 = no diff and no issues, 1 = drift found, 2 = validation errors found, 3 = environment error.
        /// </summary>
        public static void ApplyWithValidateCLI()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] Failed to parse arguments: {parseError}");
                EditorApplication.Exit(3);
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found.");
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
                AddressTellerIssueLogger.LogAll(validateIssues);

                Debug.LogError($"[AddressTeller] Apply aborted: Validate found {validateIssues.Count(i => !i.IsOk)} issue(s).");

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
            AddressTellerIssueLogger.LogAll(applyIssues);

            ExitWithReport(applyDryRun, applyIssues, cliArgs, settings);
        }

        /// <summary>
        /// CLI指定の <c>-addressTellerDisableRules</c> と永続設定（<see cref="AddressTellerSettings.DisabledRuleClassNames"/>）
        /// の和集合で除外したルール一覧を返す。
        /// </summary>
        /// <remarks>
        /// この除外は CLI 実行限定の一時除外であり、Postprocessor/Menu には波及しない。
        /// 「新設定フラグは全エントリポイントで一貫評価する」という原則からの意図的な逸脱。
        /// </remarks>
        private static bool TryBuildCliRules(AddressTellerCliArgs cliArgs, out IReadOnlyList<AddressRuleBase> rules, out string error)
        {
            if (!RuleCollector.TryCollectEnabledRules(RuleCollector.CollectRules(), AddressTellerSettings.DisabledRuleClassNames, cliArgs.DisableRuleFullNames, out rules, out var unknown))
            {
                error = $"-addressTellerDisableRules contains unknown rule class name(s): {string.Join(", ", unknown)}";
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

                // ReportPath が指定されている場合、TryParse で ReportFormat は必ず（明示または拡張子推定で）設定済み。
                if (!AddressTellerReportWriter.WriteToFile(cliArgs.ReportPath, report, cliArgs.ReportFormat.Value))
                {
                    EditorApplication.Exit(3);
                    return;
                }
            }

            EditorApplication.Exit(exitCode);
        }

        /// <summary>
        /// For CI. Run via -executeMethod AddressTeller.Editor.AddressTellerMenu.CheckCLI.
        /// Performs a read-only dry-run (no Apply) and detects the diff/problems between the current
        /// state and the state after rules would be applied.
        /// A report can be written to a file with -addressTellerReport &lt;path&gt; /
        /// -addressTellerReportFormat json|junit.
        /// Exit codes: 0 = no diff and no issues, 1 = drift found, 2 = validation errors found, 3 = environment error.
        /// </summary>
        public static void CheckCLI()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] Failed to parse arguments: {parseError}");
                EditorApplication.Exit(3);
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found.");
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

            Debug.Log($"[AddressTeller] Check completed: Added={result.Diff.Added.Count} / Removed={result.Diff.Removed.Count} / Changed={result.Diff.Changed.Count}, Issues={result.Issues.Count}.");
            AddressTellerIssueLogger.LogAll(result.Issues);

            if (!string.IsNullOrEmpty(cliArgs.ReportPath))
            {
                var report = AddressTellerReportBuilder.Build(result, settings);
                // ReportPath が指定されている場合、TryParse で ReportFormat は必ず（明示または拡張子推定で）設定済み。
                if (!AddressTellerReportWriter.WriteToFile(cliArgs.ReportPath, report, cliArgs.ReportFormat.Value))
                {
                    EditorApplication.Exit(3);
                    return;
                }
            }

            EditorApplication.Exit(AddressTellerReportBuilder.DetermineExitCode(result));
        }
    }
}
