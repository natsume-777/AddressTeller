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
        /// Removes entries (address, group assignment, and labels) from groups AddressTeller owns
        /// (see design-decisions.md for the ownership definition).
        /// This is an intentional exception to the default safe-by-default policy (asset-level ownership
        /// checks, off by default), aimed at pre-release package initial setup scenarios. After the
        /// confirmation dialog is accepted, a dedicated snapshot (under SnapshotFolder/Clear, excluded
        /// from rotation) is saved immediately before the removal; if saving fails, the clear is aborted.
        /// To target every entry, use -addressTellerClearScope all with <see cref="ClearCLI"/>.
        /// If even one rule's Configure() has failed, ownedGroups (the ownership check) cannot be
        /// trusted, so this aborts before showing the confirmation dialog (mirroring how
        /// <see cref="ClearCLI"/> aborts with exit code 3).
        /// </summary>
        [MenuItem("Tools/AddressTeller/Clear All Addresses & Labels...")]
        public static void ClearAll()
        {
            if (!AddressTellerSettings.EnsureLoaded()) return;

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

            if (!TryResolveOwnedGroups(settings, rules, out var ownedGroups, out var abortDetail))
            {
                // ルールの Configure() が1件でも失敗していると ownedGroups（所有権判定）が信頼できない。
                // 破壊的操作である Clear はこの状態のまま実行してはいけないため、確認ダイアログを出す前に中止する
                // （ClearCLI が exit code 3 で中止するのと対称の安全対策）。
                s_notifyClearAborted($"Aborted: {abortDetail}");
                Debug.LogError($"[AddressTeller] Clear All aborted: {abortDetail}");
                return;
            }

            var entryCount = settings.groups
                .Where(g => g != null && ownedGroups.Contains(g.Name))
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

            var cleared = AddressTellerClearService.Clear(settings, ClearScope.Managed, ownedGroups);
            foreach (var entry in cleared)
                Debug.LogWarning($"[AddressTeller] Cleared entry: guid={entry.Guid}, group='{entry.GroupName}', address='{entry.Address}', labels=[{string.Join(", ", entry.Labels)}]");

            Debug.Log($"[AddressTeller] Clear All completed: {cleared.Count} entry/entries removed (scope=Managed). Snapshot: {snapshotPath}");
        }

        /// <summary>
        /// scope=Managed でのクリア系操作（<see cref="ClearAll(AddressableAssetSettings, IReadOnlyList{AddressRuleBase})"/> /
        /// <see cref="ClearCLI"/>）が共有する、ownedGroups（所有権判定）の解決処理。
        /// ルールの Configure() が1件でも失敗している場合、ownedGroups は信頼できないため false を返し、
        /// <paramref name="abortDetail"/> に失敗したルールごとの詳細メッセージを返す
        /// （RuleCollector.CollectRules() は無効化中のルールも含むため、Project Settings でルールを無効化しても
        /// この中止は解除されない旨も含める）。
        /// </summary>
        private static bool TryResolveOwnedGroups(AddressableAssetSettings settings, IReadOnlyList<AddressRuleBase> rules, out HashSet<string> ownedGroups, out string abortDetail)
        {
            var setup = RuleEvaluationPipeline.BuildSetup(settings, rules);
            if (setup.ConfigureFailures.Count > 0)
            {
                var detail = string.Join("; ", setup.ConfigureFailures.Select(f => f.Message));
                abortDetail = $"{setup.ConfigureFailures.Count} rule(s) failed to configure; group ownership cannot be trusted for scope=Managed. {detail} "
                    + "Disabling the rule in Project Settings will not resolve this (RuleCollector still collects disabled rules for ownership tracking); fix the rule's Configure() instead.";
                ownedGroups = null;
                return false;
            }

            ownedGroups = setup.OwnedGroups;
            abortDetail = null;
            return true;
        }

        /// <summary>
        /// For CI. Run via -executeMethod AddressTeller.Editor.AddressTellerMenu.ClearCLI.
        /// If the confirmation flag <c>-addressTellerConfirmClear</c> is absent, this is treated as an
        /// intentional refusal and exits with code 4 (to prevent accidental execution in a CLI
        /// environment where no dialog can be shown).
        /// <c>-addressTellerClearScope all|managed</c> switches the clear target (default managed).
        /// For scope=managed, if even one rule's Configure() has failed, ownedGroups (the ownership
        /// check) cannot be trusted, so this aborts with exit code 3 before saving the snapshot (to avoid
        /// a snapshot being left behind with nothing actually removed). Saving a dedicated snapshot
        /// (under SnapshotFolder/Clear) before running is required, and a failure to save also aborts
        /// with exit code 3.
        /// If AddressTeller's settings file cannot be loaded (it exists but cannot be read, or is not
        /// recognized as an AddressTeller settings file), this also aborts with exit code 3, before doing
        /// anything else.
        /// Exit codes: 0 = completed, 3 = environment error (including rule configuration errors, snapshot
        /// save failures, and a settings load failure), 4 = confirmation flag not specified.
        /// </summary>
        public static void ClearCLI()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] Failed to parse arguments: {parseError}");
                EditorApplication.Exit(3);
                return;
            }

            if (!AddressTellerSettings.EnsureLoaded())
            {
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

            // ownedGroups の解決（所有権判定が信頼できるかの確認）は、スナップショット保存より先に行う。
            // 逆順だと、ルール構成エラーで中止した際に「何も削除していないのにスナップショットだけが残る」
            // 孤児ファイルを作ってしまう。
            IReadOnlyCollection<string> ownedGroups = null;
            if (cliArgs.ClearScope == ClearScope.Managed)
            {
                if (!TryResolveOwnedGroups(settings, RuleCollector.CollectRules(), out var resolvedOwnedGroups, out var abortDetail))
                {
                    Debug.LogError($"[AddressTeller] Clear All aborted: {abortDetail}");
                    EditorApplication.Exit(3);
                    return;
                }
                ownedGroups = resolvedOwnedGroups;
            }

            var snapshotPath = AddressTellerClearSnapshotService.CaptureAndSave(settings, out var snapshotError);
            if (snapshotPath == null)
            {
                Debug.LogError($"[AddressTeller] Clear All: {snapshotError}");
                EditorApplication.Exit(3);
                return;
            }

            var cleared = AddressTellerClearService.Clear(settings, cliArgs.ClearScope, ownedGroups);
            foreach (var entry in cleared)
                Debug.LogWarning($"[AddressTeller] Cleared entry: guid={entry.Guid}, group='{entry.GroupName}', address='{entry.Address}', labels=[{string.Join(", ", entry.Labels)}]");

            Debug.Log($"[AddressTeller] Clear All completed: {cleared.Count} entry/entries removed (scope={cliArgs.ClearScope}). Snapshot: {snapshotPath}");

            // Clear() が書き換えたダーティなアセットを、EditorApplication.Exit に頼らず明示的に保存する
            // （EditorApplication.Exit がダーティアセットをフラッシュする挙動は実測で確認済みだが、
            // Unity が文書化した契約ではなく観測された挙動にすぎないため）。
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Runs all enabled rules against every asset in the project without writing anything, and logs
        /// any problems found. Opens the result window when at least one issue (IsOk=false) is detected.
        /// </summary>
        [MenuItem("Tools/AddressTeller/Validate")]
        public static void Validate()
        {
            if (!AddressTellerSettings.EnsureLoaded()) return;

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
        /// If AddressTeller's settings file cannot be loaded (it exists but cannot be read, or is not
        /// recognized as an AddressTeller settings file), this aborts with exit code 3, before doing
        /// anything else.
        /// Exit codes: 0 = applied successfully (regardless of whether there was drift), 2 = validation
        /// errors found, 3 = environment error (including a settings load failure). This method never
        /// returns 1 — drift is not treated as a failure for an apply entry point, since the apply already
        /// completed successfully; use <see cref="CheckCLI"/> to detect drift without applying.
        /// A duplicate address across two different assets (<see cref="ValidationStatus.DuplicateAddress"/>)
        /// never affects this exit code either way, since it is a report-only status computed from the
        /// dry-run, not from the Apply results this exit code is based on — it is still logged to the
        /// console and included in the report file, but only <see cref="CheckCLI"/>'s exit code reacts to it.
        /// </summary>
        public static void ApplyAllCLI()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] Failed to parse arguments: {parseError}");
                EditorApplication.Exit(3);
                return;
            }

            if (!AddressTellerSettings.EnsureLoaded())
            {
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

            // DuplicateAddress は ApplyAll（実際の書き込み）では検出されず dry-run 側にしか現れないため、
            // ここでログしておかないとコンソールには一切出ないままレポートファイルにだけ残ることになる
            // （「報告のみ」を確実にするため。ExitWithReport の exit code 判定には影響しない）。
            var duplicateNotices = dryRun.Issues.Where(i => i.Status == ValidationStatus.DuplicateAddress).ToList();
            if (duplicateNotices.Count > 0)
                AddressTellerIssueLogger.LogAll(duplicateNotices);

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
        /// If AddressTeller's settings file cannot be loaded (it exists but cannot be read, or is not
        /// recognized as an AddressTeller settings file), this aborts with exit code 3, before doing
        /// anything else.
        /// Exit codes: 0 = applied successfully (regardless of whether there was drift), 2 = validation
        /// errors found (whether from the initial Validate pass, which aborts before Apply runs, or from
        /// the Apply pass itself), 3 = environment error (including a settings load failure). This method
        /// never returns 1 — drift is not treated as a failure for an apply entry point; use
        /// <see cref="CheckCLI"/> to detect drift without applying.
        /// A duplicate address across two different assets (<see cref="ValidationStatus.DuplicateAddress"/>)
        /// never aborts the initial Validate pass and never affects this exit code, even when it is reported
        /// as an error (<see cref="ValidationResult.HasWritableDuplicate"/>) — it is a report-only status, so
        /// Apply proceeds and this method exits 0 as long as nothing else is wrong. It is still logged to
        /// the console and included in the report file either way; use <see cref="CheckCLI"/> if you need
        /// its exit code to react to a duplicate.
        /// </summary>
        public static void ApplyWithValidateCLI()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] Failed to parse arguments: {parseError}");
                EditorApplication.Exit(3);
                return;
            }

            if (!AddressTellerSettings.EnsureLoaded())
            {
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

            // validateIssues には GroupWillBeCreated（IsOk=true、AutoCreateMissingGroups による作成予定の提示）や
            // DuplicateAddress（報告専用。書き込みを止めないため、HasBlockingIssue の対象外。
            // AddressTellerApplyFlow.HasBlockingIssue 参照）が含まれる場合がある。
            if (AddressTellerApplyFlow.HasBlockingIssue(validateIssues))
            {
                AddressTellerIssueLogger.LogAll(validateIssues);

                Debug.LogError($"[AddressTeller] Apply aborted: Validate found {validateIssues.Count(i => !i.IsOk && i.Status != ValidationStatus.DuplicateAddress)} issue(s).");

                // Apply を行わないため、現在の状態のままの dry-run をレポート化する。
                var paths = AssetDatabase.GetAllAssetPaths();
                var dryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths, rules);
                ExitWithReport(dryRun, validateIssues, cliArgs, settings);
                return;
            }

            // 中止しない場合でも、DuplicateAddress は ExitWithReport の exit code 判定（Apply 実行結果のみを見る
            // AddressTellerReportBuilder.DetermineApplyExitCode）には反映されない。「報告のみ」を確実にするため、
            // コンソールにはここで一度ログしておく（レポートファイルには dry-run の Issues として別途含まれる）。
            var duplicateNotices = validateIssues.Where(i => i.Status == ValidationStatus.DuplicateAddress).ToList();
            if (duplicateNotices.Count > 0)
                AddressTellerIssueLogger.LogAll(duplicateNotices);

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
        /// exit code は「実行後 issues（<paramref name="executionIssues"/>）に書き込みを見送るべき問題が
        /// 含まれるか」のみで決まる（<see cref="AddressTellerReportBuilder.DetermineApplyExitCode"/>。
        /// <see cref="ValidationStatus.DuplicateAddress"/> は IsOk=false であっても対象外——同メソッドの
        /// XML doc 参照）。dry-run の差分の有無は適用系の exit code には反映しない——Apply は実際に
        /// 書き込みを完了しているため、差分があったことを理由に失敗扱い（exit 1）にすると、`set -e` の下で
        /// 正常な適用が毎回失敗になってしまう。差分検出は <see cref="CheckCLI"/> の役割。
        /// 同じ理由で、JUnit 出力の drift testcase も failure なしで出す
        /// （<see cref="AddressTellerReportWriter.WriteToFile"/> に treatDriftAsFailure: false を渡す）——
        /// そうしないと exit code は 0 でも、この report を取り込む CI ジョブは drift のたびに赤くなる。
        /// レポートの書き込みに失敗した場合は exit 3。
        /// このメソッドは ApplyAllCLI / ApplyWithValidateCLI からのみ呼ばれる（CheckCLI は独自に
        /// <see cref="AddressTellerReportBuilder.DetermineExitCode(DryRunResult)"/> を使う）ため、
        /// ここで <see cref="AssetDatabase.SaveAssets"/> を呼んでダーティなアセットを明示的に保存する。
        /// </summary>
        private static void ExitWithReport(DryRunResult dryRun, IReadOnlyList<ValidationResult> executionIssues, AddressTellerCliArgs cliArgs, AddressableAssetSettings settings)
        {
            var exitCode = AddressTellerReportBuilder.DetermineApplyExitCode(executionIssues);

            if (!string.IsNullOrEmpty(cliArgs.ReportPath))
            {
                var report = AddressTellerReportBuilder.Build(dryRun, settings);
                report.Summary.ExitCode = exitCode;

                // ReportPath が指定されている場合、TryParse で ReportFormat は必ず（明示または拡張子推定で）設定済み。
                // treatDriftAsFailure: false — Apply は既に成功しているため、drift の存在を JUnit の
                // <failure> として報告しない（CheckCLI と異なり、drift は失敗ではなく成功した変更の記録）。
                if (!AddressTellerReportWriter.WriteToFile(cliArgs.ReportPath, report, cliArgs.ReportFormat.Value, treatDriftAsFailure: false))
                {
                    AssetDatabase.SaveAssets();
                    EditorApplication.Exit(3);
                    return;
                }
            }

            AssetDatabase.SaveAssets();
            EditorApplication.Exit(exitCode);
        }

        /// <summary>
        /// For CI. Run via -executeMethod AddressTeller.Editor.AddressTellerMenu.CheckCLI.
        /// Performs a read-only dry-run (no Apply) and detects the diff/problems between the current
        /// state and the state after rules would be applied.
        /// A report can be written to a file with -addressTellerReport &lt;path&gt; /
        /// -addressTellerReportFormat json|junit.
        /// If AddressTeller's settings file cannot be loaded (it exists but cannot be read, or is not
        /// recognized as an AddressTeller settings file), this aborts with exit code 3, before doing
        /// anything else.
        /// Exit codes: 0 = no diff and no issues, 1 = drift found, 2 = validation errors found, 3 = environment
        /// error (including a settings load failure).
        /// </summary>
        public static void CheckCLI()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] Failed to parse arguments: {parseError}");
                EditorApplication.Exit(3);
                return;
            }

            if (!AddressTellerSettings.EnsureLoaded())
            {
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

            // CheckCLI は読み取り専用の dry-run であり、Addressables の状態を一切書き換えない
            // （BuildPredictedSnapshot は予測計算のみで、実際の CreateOrMoveEntry 等を呼ばない）。
            // そのため ApplyAllCLI / ApplyWithValidateCLI / ClearCLI と異なり AssetDatabase.SaveAssets() は不要。
            var paths = AssetDatabase.GetAllAssetPaths();
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths, rules);

            Debug.Log($"[AddressTeller] Check completed: Added={result.Diff.Added.Count} / Removed={result.Diff.Removed.Count} / Changed={result.Diff.Changed.Count}, Issues={result.Issues.Count}.");
            AddressTellerIssueLogger.LogAll(result.Issues);

            if (!string.IsNullOrEmpty(cliArgs.ReportPath))
            {
                var report = AddressTellerReportBuilder.Build(result, settings);
                // ReportPath が指定されている場合、TryParse で ReportFormat は必ず（明示または拡張子推定で）設定済み。
                // treatDriftAsFailure: true（既定値と同じだが明示） — CheckCLI は drift の検出そのものが
                // 目的の読み取り専用 dry-run であるため、ExitWithReport（適用系）とは対称に drift を
                // JUnit の <failure> として報告する。
                if (!AddressTellerReportWriter.WriteToFile(cliArgs.ReportPath, report, cliArgs.ReportFormat.Value, treatDriftAsFailure: true))
                {
                    EditorApplication.Exit(3);
                    return;
                }
            }

            EditorApplication.Exit(AddressTellerReportBuilder.DetermineExitCode(result));
        }
    }
}
