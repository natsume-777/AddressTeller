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
        /// -executeMethod で起動された Editor プロセスを終了する処理。既定では EditorApplication.Exit を呼ぶが、
        /// EditMode テストが実プロセスを終了させずに CLI 入口の分岐（特に <see cref="RunCliEntryPoint"/> の
        /// 予期しない例外の catch）を検証できるよう差し替え可能にしている（<see cref="s_notifyClearAborted"/> /
        /// <see cref="AddressTellerApplyFlow.s_notifyApplyAborted"/> と同じ「テスト用シーム」の考え方。
        /// テストは差し替え後、TearDown で必ず既定値へ戻すこと）。
        /// </summary>
        internal static Action<int> s_exitCli = EditorApplication.Exit;

        /// <summary>
        /// -executeMethod から呼ばれる各 CLI 入口の本体（<paramref name="body"/>）を、予期しない例外の catch 込みで
        /// 実行する。ClearCLI/ApplyAllCLI/ApplyWithValidateCLI/CheckCLI それぞれの内部分岐は、既知の失敗系
        /// （引数解析エラー・設定読み込み失敗・グループ未検出等）について明示的な exit code（0〜4）を都度呼ぶため、
        /// ここで catch するのはそれらを通り抜けた「本当に予期しない」例外だけ——たとえば同一 GUID が複数グループに
        /// 存在する状態は通常 <see cref="ValidationStatus.DuplicateAssetEntry"/> として結果に現れるが、それを
        /// 見落として書き込み側の Addressables API を直接呼ぶような未知の経路が万一残っていた場合に、
        /// 未捕捉例外で Editor プロセスが exit code 1（drift ありと区別できない）のまま終了するのを防ぐ。
        /// ここで catch した場合の exit code は環境エラーを表す 3 に統一する。
        /// </summary>
        internal static void RunCliEntryPoint(string entryPointName, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                // ex.ToString() は型・メッセージ・スタックトレースをすべて含むため、ここで ex.Message を
                // 別途繰り返さない（GetOrderedEntries の Configure() 失敗ログと同じスタイル）。
                Debug.LogError($"[AddressTeller] {entryPointName} aborted due to an unexpected exception:\n{ex}");
                s_exitCli(3);
            }
        }

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

            // 同一 guid が2つ以上のグループにまたがって存在する状態では、settings.RemoveAssetEntry(guid) が
            // 先勝ちで片方だけを消しうる（AddressTellerClearService.Clear は entry 単位の削除に対処済みだが、
            // それでも「どちらの entry を消すのが正しいか」を AddressTeller は判断できない）。スナップショット
            // 保存より前に検出して中止する——逆順だと、重複 guid を含む読み込み不能なスナップショットが
            // 残ってしまう（AddressTellerSnapshotService.LoadFromFile は重複 guid を拒否する）。
            if (DuplicateAssetEntryDetector.LogAndReturnTrueIfDuplicates(settings, "Clear All"))
            {
                s_notifyClearAborted("Aborted: the same asset has an entry in two or more Addressables groups at once. See the Console for details.");
                return;
            }

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
        /// anything else. If no settings file exists at all, this logs one Info line and proceeds with
        /// default settings (a missing file is not an error).
        /// If the same asset has an entry in two or more Addressables groups at once
        /// (<see cref="ValidationStatus.DuplicateAssetEntry"/>), this aborts with exit code 2 before saving
        /// the snapshot, since <c>settings.RemoveAssetEntry(guid)</c> would otherwise act on whichever entry
        /// it happens to find first.
        /// Exit codes: 0 = completed, 2 = a duplicate asset entry was detected, 3 = environment error
        /// (including rule configuration errors, snapshot save failures, a settings load failure, and any
        /// unexpected exception), 4 = confirmation flag not specified.
        /// </summary>
        public static void ClearCLI() => RunCliEntryPoint(nameof(ClearCLI), ClearCLICore);

        private static void ClearCLICore()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] Failed to parse arguments: {parseError}");
                s_exitCli(3);
                return;
            }
            WarnIfFailOnUnmatchedIgnored(cliArgs, nameof(ClearCLI));

            var settingsGate = AddressTellerSettings.EnsureLoaded();
            if (!settingsGate.Success)
            {
                s_exitCli(3);
                return;
            }
            if (!settingsGate.FileExists)
                Debug.Log("[AddressTeller] No settings file; using defaults.");

            if (!cliArgs.ConfirmClear)
            {
                Debug.LogError($"[AddressTeller] Clear All requires -addressTellerConfirmClear to be specified (intentional rejection).");
                s_exitCli(4);
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found.");
                s_exitCli(3);
                return;
            }

            // ClearAll（対話メニュー）と同じ理由（settings.RemoveAssetEntry(guid) の先勝ち、および重複 guid
            // 入りの読み込み不能なスナップショットを残さないため）で、スナップショット保存よりさらに前に検出する。
            // exit code は他の環境エラー（3）とは区別し、評価不能な状態を検出した Validation 的な失敗として
            // ApplyAllCLI/CheckCLI と同じ 2 を使う。
            if (DuplicateAssetEntryDetector.LogAndReturnTrueIfDuplicates(settings, "Clear All"))
            {
                s_exitCli(2);
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
                    s_exitCli(3);
                    return;
                }
                ownedGroups = resolvedOwnedGroups;
            }

            var snapshotPath = AddressTellerClearSnapshotService.CaptureAndSave(settings, out var snapshotError);
            if (snapshotPath == null)
            {
                Debug.LogError($"[AddressTeller] Clear All: {snapshotError}");
                s_exitCli(3);
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
            s_exitCli(0);
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
            // UnmatchedEntryKept は「CleanupStaleEntries を ON にしたら何が消えるか」を確認するための通知
            // （README クイックスタート手順3）であり、コンソールのサマリ1行だけでは中身が見えない。errorCount が
            // 0 でもこれが1件以上あれば結果ウィンドウを開き、個別の内容を確認できるようにする。
            var unmatchedEntryKeptCount = issues.Count(i => i.Status == ValidationStatus.UnmatchedEntryKept);
            // StaleManagedLabelKept（RemoveStaleManagedLabels を ON にしたら何のラベルが外れるか）も同じ理由で
            // 結果ウィンドウを開く対象にする。
            var staleManagedLabelKeptCount = issues.Count(i => i.Status == ValidationStatus.StaleManagedLabelKept);
            if (errorCount == 0 && unmatchedEntryKeptCount == 0 && staleManagedLabelKeptCount == 0)
            {
                Debug.Log($"[AddressTeller] Validate completed: {issues.Count} notice(s) (no issues).");
                return;
            }

            if (errorCount == 0)
            {
                var including = new List<string>();
                if (unmatchedEntryKeptCount > 0)
                    including.Add($"{unmatchedEntryKeptCount} entr{(unmatchedEntryKeptCount == 1 ? "y" : "ies")} " +
                        "that would be removed if Remove unmatched entries were on");
                if (staleManagedLabelKeptCount > 0)
                    including.Add($"{staleManagedLabelKeptCount} entr{(staleManagedLabelKeptCount == 1 ? "y" : "ies")} " +
                        "with labels that would be removed if Remove stale managed labels were on");
                Debug.Log($"[AddressTeller] Validate completed: {issues.Count} notice(s) (no issues), " +
                    $"including {string.Join(" and ", including)}.");
            }
            else
            {
                Debug.LogError($"[AddressTeller] Validate completed: {errorCount} issue(s) found.");
            }

            // Apply All / Apply with Validate の中止経路と対称に、IsOk=false の問題があるか、UnmatchedEntryKept
            // 通知（中身は結果ウィンドウでしか確認できない）が1件以上ある場合に結果ウィンドウを開く。
            // GroupWillBeCreated 等それ以外の通知のみ（errorCount == 0 かつ UnmatchedEntryKept も0件）では
            // フォーカスを奪わない。
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
        /// anything else. If no settings file exists at all, this logs one Info line and proceeds with
        /// default settings (a missing file is not an error).
        /// Exit codes: 0 = applied successfully (regardless of whether there was drift), 2 = validation
        /// errors found, 3 = environment error (including a settings load failure and any unexpected
        /// exception). This method never returns 1 — drift is not treated as a failure for an apply entry
        /// point, since the apply already completed successfully; use <see cref="CheckCLI"/> to detect
        /// drift without applying.
        /// A duplicate address across two different assets (<see cref="ValidationStatus.DuplicateAddress"/>)
        /// never affects this exit code, since it is a report-only status that does not block a write
        /// (<see cref="ValidationResult.IsBlocking"/> is false) — it is still logged to the console and
        /// included in the report file, but only <see cref="CheckCLI"/>'s exit code reacts to it.
        /// This entry point does not validate, so it does not enforce <see cref="AddressTellerSettings.StrictRuleOverlaps"/>:
        /// undeclared rule overlaps are left out of the report's Issues (one warning is logged) while the report's
        /// <c>Overlaps[]</c> still lists every overlap. Use <see cref="ApplyWithValidateCLI"/> or <see cref="CheckCLI"/> to enforce it.
        /// </summary>
        public static void ApplyAllCLI() => RunCliEntryPoint(nameof(ApplyAllCLI), ApplyAllCLICore);

        private static void ApplyAllCLICore()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] Failed to parse arguments: {parseError}");
                s_exitCli(3);
                return;
            }
            WarnIfFailOnUnmatchedIgnored(cliArgs, nameof(ApplyAllCLI));

            var settingsGate = AddressTellerSettings.EnsureLoaded();
            if (!settingsGate.Success)
            {
                s_exitCli(3);
                return;
            }
            if (!settingsGate.FileExists)
                Debug.Log("[AddressTeller] No settings file; using defaults.");

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found.");
                s_exitCli(3);
                return;
            }

            if (!TryBuildCliRules(cliArgs, out var rules, out var rulesError))
            {
                Debug.LogError($"[AddressTeller] {rulesError}");
                s_exitCli(3);
                return;
            }

            // 母集合を1回確定し、dry-run（レポート化）と実 Apply で同じ対象パスを使う。
            var paths = AssetDatabase.GetAllAssetPaths();
            var dryRun = ExcludeUndeclaredRuleOverlapIssues(
                AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths, rules, isFullScan: true));

            // dry-run の DuplicateAddress は、書き込んだアドレスと無関係な既存の重複（ApplyAll は報告しない）も含む。
            // コンソールに出すため、ApplyAll の後にログする（ApplyAll が同じ内容を返した分は二重にならないよう除く）。
            var duplicateNotices = dryRun.Issues.Where(IsDryRunOnlyNotice).ToList();

            var applyIssues = AddressTellerService.ApplyAll(paths, settings, NullProgressReporter.Instance, rules);
            LogApplyIssuesAndDryRunDuplicates(applyIssues, duplicateNotices);
            AddressTellerIssueLogger.LogUnmatched(dryRun.UnmatchedInDeclaredFolders, failing: false);

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
        /// anything else. If no settings file exists at all, this logs one Info line and proceeds with
        /// default settings (a missing file is not an error).
        /// Exit codes: 0 = applied successfully (regardless of whether there was drift), 2 = validation
        /// errors found (whether from the initial Validate pass, which aborts before Apply runs, or from
        /// the Apply pass itself), 3 = environment error (including a settings load failure and any
        /// unexpected exception). This method never returns 1 — drift is not treated as a failure for an
        /// apply entry point; use <see cref="CheckCLI"/> to detect drift without applying.
        /// A duplicate address across two different assets (<see cref="ValidationStatus.DuplicateAddress"/>)
        /// never aborts the initial Validate pass and never affects this exit code, even when it is reported
        /// as an error (<see cref="ValidationResult.HasWritableDuplicate"/>) — it is a report-only status, so
        /// Apply proceeds and this method exits 0 as long as nothing else is wrong. It is still logged to
        /// the console and included in the report file either way; use <see cref="CheckCLI"/> if you need
        /// its exit code to react to a duplicate.
        /// With <see cref="AddressTellerSettings.StrictRuleOverlaps"/> on, an undeclared rule overlap
        /// (<see cref="ValidationStatus.UndeclaredRuleOverlap"/>) found by the initial Validate pass aborts before Apply runs, with exit code 2.
        /// </summary>
        public static void ApplyWithValidateCLI() => RunCliEntryPoint(nameof(ApplyWithValidateCLI), ApplyWithValidateCLICore);

        private static void ApplyWithValidateCLICore()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] Failed to parse arguments: {parseError}");
                s_exitCli(3);
                return;
            }
            WarnIfFailOnUnmatchedIgnored(cliArgs, nameof(ApplyWithValidateCLI));

            var settingsGate = AddressTellerSettings.EnsureLoaded();
            if (!settingsGate.Success)
            {
                s_exitCli(3);
                return;
            }
            if (!settingsGate.FileExists)
                Debug.Log("[AddressTeller] No settings file; using defaults.");

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found.");
                s_exitCli(3);
                return;
            }

            if (!TryBuildCliRules(cliArgs, out var rules, out var rulesError))
            {
                Debug.LogError($"[AddressTeller] {rulesError}");
                s_exitCli(3);
                return;
            }

            var validateIssues = AddressTellerService.ValidateAll(settings, NullProgressReporter.Instance, rules);

            // validateIssues には GroupWillBeCreated（IsOk=true、AutoCreateMissingGroups による作成予定の提示）や
            // DuplicateAddress（報告専用。書き込みを止めないため ValidationResult.IsBlocking=false）が
            // 含まれる場合がある。
            if (validateIssues.Any(i => i.IsBlocking))
            {
                AddressTellerIssueLogger.LogAll(validateIssues);

                Debug.LogError($"[AddressTeller] Apply aborted: Validate found {validateIssues.Count(i => i.IsBlocking)} issue(s).");

                // Apply を行わないため、現在の状態のままの dry-run をレポート化する。
                var paths = AssetDatabase.GetAllAssetPaths();
                var dryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths, rules, isFullScan: true);
                // validateIssues（RuleMatchedNothing を含む）は上で全件ログ済みなので、未登録アセットの件数だけを足す。
                AddressTellerIssueLogger.LogUnmatched(dryRun.UnmatchedInDeclaredFolders, failing: false);
                ExitWithReport(dryRun, validateIssues, cliArgs, settings);
                return;
            }

            // 中止しない場合でも、validateIssues の DuplicateAddress（書き込んだアドレスと無関係な既存の重複を含む）は
            // コンソールに出す。ApplyAll の後にログし、ApplyAll が同じ内容を返した分は二重にならないよう除く。
            var duplicateNotices = validateIssues.Where(IsDryRunOnlyNotice).ToList();

            // 母集合を1回確定し、dry-run（レポート化）と実 Apply で同じ対象パスを使う。
            var applyPaths = AssetDatabase.GetAllAssetPaths();
            var applyDryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, applyPaths, rules, isFullScan: true);

            var applyIssues = AddressTellerService.ApplyAll(applyPaths, settings, NullProgressReporter.Instance, rules);
            LogApplyIssuesAndDryRunDuplicates(applyIssues, duplicateNotices);
            AddressTellerIssueLogger.LogUnmatched(applyDryRun.UnmatchedInDeclaredFolders, failing: false);

            ExitWithReport(applyDryRun, applyIssues, cliArgs, settings);
        }

        /// <summary>
        /// <see cref="ApplyAllCLI"/> のレポート用 dry-run から、未宣言のルール重なり
        /// （<see cref="ValidationStatus.UndeclaredRuleOverlap"/>）の issue を外す。
        /// </summary>
        /// <remarks>
        /// 「新設定フラグは全エントリポイントで一貫評価する」という原則からの意図的な逸脱。<see cref="AddressTellerSettings.StrictRuleOverlaps"/>
        /// は検証（Validate / dry-run）の設定で、検証を行わない ApplyAll の経路（インポート時の自動適用と、この CLI が実際に
        /// 行う ApplyAll）では評価しない。この CLI の exit code は ApplyAll の結果だけで決まる
        /// （<see cref="AddressTellerReportBuilder.DetermineApplyExitCode"/>）ので、dry-run 由来の issue を残すと、
        /// exit code は 0 なのに JUnit だけ failure になる食い違いが出る。そのため issue は外し、重なりの一覧
        /// （<see cref="DryRunResult.Overlaps"/>、レポートの <c>Overlaps[]</c>）は残す。外したときは、黙って無視せず Warning を1行出す。
        /// 検証してから適用したい場合は <see cref="ApplyWithValidateCLI"/> を使う。
        /// </remarks>
        internal static DryRunResult ExcludeUndeclaredRuleOverlapIssues(DryRunResult dryRun)
        {
            var kept = dryRun.Issues.Where(i => i.Status != ValidationStatus.UndeclaredRuleOverlap).ToList();
            var excluded = dryRun.Issues.Count - kept.Count;
            if (excluded == 0) return dryRun;

            Debug.LogWarning($"[AddressTeller] Strict rule overlaps is on, but {nameof(ApplyAllCLI)} does not check rule overlaps ({excluded} undeclared overlap(s) not enforced); use {nameof(ApplyWithValidateCLI)} or {nameof(CheckCLI)}. The report still lists every overlap under Overlaps[].");
            return new DryRunResult(dryRun.Diff, kept, dryRun.GroupsToCreate, dryRun.After, dryRun.UnmatchedInDeclaredFolders, dryRun.Overlaps);
        }

        /// <summary>
        /// ApplyAll が返さず、事前の検証/dry-run だけが計算する通知（DuplicateAddress は書き込んだアドレス以外の
        /// 既存の重複を含む。RuleMatchedNothing は全件走査でしか分からない）。Apply 系 CLI はこれらを
        /// ApplyAll の後にログする。
        /// </summary>
        private static bool IsDryRunOnlyNotice(ValidationResult issue)
            => issue.Status == ValidationStatus.DuplicateAddress || issue.Status == ValidationStatus.RuleMatchedNothing;

        /// <summary>
        /// ApplyAll の結果をログし、続けて事前の検証/dry-run で見つかった通知（<see cref="IsDryRunOnlyNotice"/>）のうち
        /// ApplyAll 側の結果に同じ Status・Message が無いものをログする。ApplyAll も同じ重複を返すことがあるため、
        /// 単純に両方ログすると二重になる。Predict と Apply のずれで取りこぼさないよう、先にログする側の絞り込みはせず
        /// Status と Message の一致だけで除く。
        /// </summary>
        private static void LogApplyIssuesAndDryRunDuplicates(IReadOnlyList<ValidationResult> applyIssues, IReadOnlyList<ValidationResult> duplicateNotices)
        {
            AddressTellerIssueLogger.LogAll(applyIssues);

            var alreadyLogged = new HashSet<(ValidationStatus, string)>(
                applyIssues.Where(IsDryRunOnlyNotice).Select(i => (i.Status, i.Message)));
            var remaining = duplicateNotices.Where(d => !alreadyLogged.Contains((d.Status, d.Message))).ToList();
            if (remaining.Count > 0)
                AddressTellerIssueLogger.LogAll(remaining);
        }

        /// <summary>
        /// <c>-addressTellerFailOnUnmatched</c> は <see cref="CheckCLI"/> だけが評価する。それ以外のエントリポイントで
        /// 指定されたら、黙って無視せず Warning を1行出す。
        /// </summary>
        /// <remarks>
        /// 「新設定フラグは全エントリポイントで一貫評価する」という原則からの意図的な逸脱。このフラグは永続設定ではなく
        /// 「dry-run の未登録アセットを失敗にする」という CI 判定の指定で、Apply 系は書き込みを完了した後に
        /// 差分の有無を失敗扱いしない（<see cref="AddressTellerReportBuilder.DetermineApplyExitCode"/>）のと同じく、
        /// 未登録アセットで exit code を変えない。
        /// </remarks>
        private static void WarnIfFailOnUnmatchedIgnored(AddressTellerCliArgs cliArgs, string entryPoint)
        {
            if (cliArgs.FailOnUnmatched)
                Debug.LogWarning($"[AddressTeller] -addressTellerFailOnUnmatched is ignored by {entryPoint}; use CheckCLI.");
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
        /// 実行時にしか分からない <see cref="ValidationStatus.StaleRemovalLimitExceeded"/>（削除を全件見送った）を
        /// dry-run 結果に合流させる。dry-run は削除を予測するが、実際には 1 件も削除していないため、
        /// レポートの Removed drift は外し、Issues（と JUnit の failure 対象）にこの issue を加える。
        /// 該当 issue が無ければ dry-run 結果をそのまま返す。
        /// </summary>
        private static DryRunResult ReflectHeldBackRemovals(DryRunResult dryRun, IReadOnlyList<ValidationResult> executionIssues)
        {
            var limitIssues = executionIssues.Where(i => i.Status == ValidationStatus.StaleRemovalLimitExceeded).ToList();
            if (limitIssues.Count == 0) return dryRun;

            var diff = new SnapshotDiff();
            foreach (var added in dryRun.Diff.Added) diff.AddAdded(added);
            foreach (var (before, after) in dryRun.Diff.Changed) diff.AddChanged(before, after);

            var issues = dryRun.Issues.Concat(limitIssues).ToList();
            return new DryRunResult(diff, issues, dryRun.GroupsToCreate, dryRun.After, dryRun.UnmatchedInDeclaredFolders, dryRun.Overlaps);
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
        /// （<see cref="AddressTellerReportWriter.WriteToFile(string, AddressTellerReport, ReportFormat, bool)"/> に treatDriftAsFailure: false を渡す）——
        /// そうしないと exit code は 0 でも、この report を取り込む CI ジョブは drift のたびに赤くなる。
        /// レポートの書き込みに失敗した場合は exit 3。
        /// このメソッドは ApplyAllCLI / ApplyWithValidateCLI からのみ呼ばれる（CheckCLI は独自に
        /// <see cref="AddressTellerReportBuilder.DetermineExitCode(DryRunResult)"/> を使う）ため、
        /// ここで <see cref="AssetDatabase.SaveAssets"/> を呼んでダーティなアセットを明示的に保存する。
        /// </summary>
        internal static void ExitWithReport(DryRunResult dryRun, IReadOnlyList<ValidationResult> executionIssues, AddressTellerCliArgs cliArgs, AddressableAssetSettings settings)
        {
            dryRun = ReflectHeldBackRemovals(dryRun, executionIssues);
            var exitCode = AddressTellerReportBuilder.DetermineApplyExitCode(executionIssues);

            if (!string.IsNullOrEmpty(cliArgs.ReportPath))
            {
                var report = AddressTellerReportBuilder.Build(dryRun, settings);
                report.Summary.ExitCode = exitCode;

                // JUnit の Validation testcase は、Apply 系の exit code 判定と同じ基準（AddressTellerReportBuilder.
                // BuildApplyFailingStatusNames、内部で ValidationResult.IsBlocking を使う）で failure を
                // 付ける Status だけに絞る。report は dryRun から組み立てているため、report.Issues と同じ集合
                // である dryRun.Issues から失敗対象を求める（executionIssues から求めると、report に現れない
                // Status を failure 対象に含めてしまいうる）。
                var failingStatusNames = AddressTellerReportBuilder.BuildApplyFailingStatusNames(dryRun.Issues);

                // ReportPath が指定されている場合、TryParse で ReportFormat は必ず（明示または拡張子推定で）設定済み。
                // treatDriftAsFailure: false — Apply は既に成功しているため、drift の存在を JUnit の
                // <failure> として報告しない（CheckCLI と異なり、drift は失敗ではなく成功した変更の記録）。
                if (!AddressTellerReportWriter.WriteToFile(cliArgs.ReportPath, report, cliArgs.ReportFormat.Value, treatDriftAsFailure: false, failingStatusNames))
                {
                    AssetDatabase.SaveAssets();
                    s_exitCli(3);
                    return;
                }
            }

            AssetDatabase.SaveAssets();
            s_exitCli(exitCode);
        }

        /// <summary>
        /// For CI. Run via -executeMethod AddressTeller.Editor.AddressTellerMenu.CheckCLI.
        /// Performs a read-only dry-run (no Apply) and detects the diff/problems between the current
        /// state and the state after rules would be applied.
        /// A report can be written to a file with -addressTellerReport &lt;path&gt; /
        /// -addressTellerReportFormat json|junit.
        /// If AddressTeller's settings file cannot be loaded (it exists but cannot be read, or is not
        /// recognized as an AddressTeller settings file), this aborts with exit code 3, before doing
        /// anything else. If no settings file exists at all, this logs one Info line and proceeds with
        /// default settings (a missing file is not an error).
        /// Exit codes: 0 = no diff and no issues, 1 = drift found, 2 = validation errors found, 3 = environment
        /// error (including a settings load failure and any unexpected exception).
        /// With -addressTellerFailOnUnmatched, exit code 2 is also returned when an asset under a folder some rule
        /// declared gets no address or label from any rule (<see cref="DryRunResult.UnmatchedInDeclaredFolders"/>).
        /// With <see cref="AddressTellerSettings.StrictRuleOverlaps"/> on, an undeclared rule overlap
        /// (<see cref="ValidationStatus.UndeclaredRuleOverlap"/>) is a validation error and exits with code 2.
        /// </summary>
        public static void CheckCLI() => RunCliEntryPoint(nameof(CheckCLI), CheckCLICore);

        private static void CheckCLICore()
        {
            if (!AddressTellerCliArgs.TryParse(Environment.GetCommandLineArgs(), out var cliArgs, out var parseError))
            {
                Debug.LogError($"[AddressTeller] Failed to parse arguments: {parseError}");
                s_exitCli(3);
                return;
            }

            var settingsGate = AddressTellerSettings.EnsureLoaded();
            if (!settingsGate.Success)
            {
                s_exitCli(3);
                return;
            }
            if (!settingsGate.FileExists)
                Debug.Log("[AddressTeller] No settings file; using defaults.");

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found.");
                s_exitCli(3);
                return;
            }

            if (!TryBuildCliRules(cliArgs, out var rules, out var rulesError))
            {
                Debug.LogError($"[AddressTeller] {rulesError}");
                s_exitCli(3);
                return;
            }

            // CheckCLI は読み取り専用の dry-run であり、Addressables の状態を一切書き換えない
            // （BuildPredictedSnapshot は予測計算のみで、実際の CreateOrMoveEntry 等を呼ばない）。
            // そのため ApplyAllCLI / ApplyWithValidateCLI / ClearCLI と異なり AssetDatabase.SaveAssets() は不要。
            var paths = AssetDatabase.GetAllAssetPaths();
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths, rules, isFullScan: true);

            ReportCheckResult(result, cliArgs, settings);
        }

        /// <summary>
        /// CheckCLI の dry-run 結果の報告（ログ・レポート出力・exit code）。dry-run の計算（<see cref="AssetDatabase"/> と
        /// ルール収集に依存する）から切り離してあるのは、<c>-addressTellerFailOnUnmatched</c> の配線（exit code と
        /// レポートの <c>Summary.ExitCode</c>、ログの重大度）を、実 CLI を起動せずに検証できるようにするため。
        /// </summary>
        internal static void ReportCheckResult(DryRunResult result, AddressTellerCliArgs cliArgs, AddressableAssetSettings settings)
        {
            Debug.Log($"[AddressTeller] Check completed: Added={result.Diff.Added.Count} / Removed={result.Diff.Removed.Count} / Changed={result.Diff.Changed.Count}, Issues={result.Issues.Count}, Unmatched={result.UnmatchedInDeclaredFolders.Count}.");
            AddressTellerIssueLogger.LogAll(result.Issues);
            AddressTellerIssueLogger.LogUnmatched(result.UnmatchedInDeclaredFolders, cliArgs.FailOnUnmatched);

            var exitCode = AddressTellerReportBuilder.DetermineExitCode(result, cliArgs.FailOnUnmatched);

            if (!string.IsNullOrEmpty(cliArgs.ReportPath))
            {
                var report = AddressTellerReportBuilder.Build(result, settings);
                // Build は -addressTellerFailOnUnmatched を知らないため、exit code はここで確定した値に揃える。
                report.Summary.ExitCode = exitCode;
                // JUnit の Validation testcase は、CheckCLI の exit code 判定（DetermineExitCode）と同じ基準
                // （AddressTellerReportBuilder.BuildCheckCliFailingStatusNames）で failure を付ける Status
                // だけに絞る。全 Status に無条件で failure を付けると、exit code に影響しない通知
                // （DuplicateAddress のうち IsOk=true のもの等）まで CI を落としてしまう。
                var failingStatusNames = AddressTellerReportBuilder.BuildCheckCliFailingStatusNames(result.Issues);
                // ReportPath が指定されている場合、TryParse で ReportFormat は必ず（明示または拡張子推定で）設定済み。
                // treatDriftAsFailure: true（既定値と同じだが明示） — CheckCLI は drift の検出そのものが
                // 目的の読み取り専用 dry-run であるため、ExitWithReport（適用系）とは対称に drift を
                // JUnit の <failure> として報告する。
                if (!AddressTellerReportWriter.WriteToFile(cliArgs.ReportPath, report, cliArgs.ReportFormat.Value, treatDriftAsFailure: true, failingStatusNames, treatUnmatchedAsFailure: cliArgs.FailOnUnmatched))
                {
                    s_exitCli(3);
                    return;
                }
            }

            s_exitCli(exitCode);
        }
    }
}
