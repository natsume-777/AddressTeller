using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Entry points for Apply/Validate and deletion follow-up, all backed by rule evaluation.
    /// </summary>
    /// <remarks>
    /// Null contract for these arguments: each method's settings argument is optional/nullable, and
    /// falls back to <see cref="AddressableAssetSettingsDefaultObject.Settings"/> when omitted
    /// (an intentional default resolution — it does not throw). Likewise, paths
    /// (<see cref="ApplyAll(IEnumerable{string}, AddressableAssetSettings)"/> etc.) and
    /// deletedGuids (<see cref="RemoveEntriesForDeletedAssets(IEnumerable{string}, AddressableAssetSettings)"/>
    /// etc.) are treated as an empty sequence when null is passed (the same fallback-style contract).
    /// Note that this differs from entry points such as <see cref="AddressTellerSnapshotService.Restore"/>
    /// or <see cref="AddressTellerClearService.Clear"/>, where the caller must explicitly specify the target.
    /// </remarks>
    public static class AddressTellerService
    {
        // Menu/CLI から ApplyAll() を呼んだ場合、その内部の Addressables 設定変更が
        // Postprocessor.OnPostprocessAllAssets を再トリガーし、そこから ApplyAll() が
        // 再度呼ばれて無限ループになるのを防ぐガード。
        // Postprocessor.s_isApplying は Postprocessor 自身の再入を防ぐ別ガードで、
        // Menu 経由の呼び出しではそちらは false のままのため、両方が必要。
        private static bool s_isApplying;

        /// <summary>
        /// Applies all collected rules to every asset in the project and returns the results that had a
        /// problem (conflicts, missing groups, rule exceptions, etc.).
        /// If settings is null, the project's default settings are used. Returns an empty list if
        /// Addressables is not set up.
        /// </summary>
        /// <remarks>
        /// The return value may include <see cref="ValidationStatus.GroupWillBeCreated"/> entries
        /// (IsOk=true; informational notices about a group that AutoCreateMissingGroups will create or
        /// has created). When treating the result as a "problem" (e.g. deciding whether to abort Apply),
        /// filter with <c>!result.IsOk</c>.
        /// </remarks>
        public static IReadOnlyList<ValidationResult> ApplyAll(AddressableAssetSettings settings = null)
        {
            return ApplyAll(settings, NullProgressReporter.Instance);
        }

        /// <summary>
        /// Overload of <see cref="ApplyAll(AddressableAssetSettings)"/> that adds progress reporting and
        /// cancellation. Returns the results accumulated up to the point where progress returns false and
        /// stops there.
        /// </summary>
        /// <remarks>
        /// On cancellation, writes for assets already processed up to that point have already completed
        /// (partial apply). Addressables state is not rolled back after cancellation.
        /// </remarks>
        public static IReadOnlyList<ValidationResult> ApplyAll(AddressableAssetSettings settings, IProgressReporter progress)
        {
            return ApplyAll(AssetDatabase.GetAllAssetPaths(), settings, progress);
        }

        /// <summary>
        /// Applies all collected rules only to the assets specified by <paramref name="paths"/>, and
        /// returns the results that had a problem (conflicts, missing groups, rule exceptions, etc.).
        /// If settings is null, the project's default settings are used. Returns an empty list if
        /// Addressables is not set up.
        /// </summary>
        /// <remarks>
        /// Cleanup of entries that "no longer match any rule" is only performed for assets that were
        /// themselves passed to this method. Project-wide consistency checks remain the responsibility of
        /// the parameterless <see cref="ApplyAll(AddressableAssetSettings)"/> (the full scan used by
        /// Menu/CLI). By contrast, cleanup of entries whose path is structurally invalid for an
        /// Addressables entry (for example leftovers from an older AddressTeller version) is not scoped by
        /// <paramref name="paths"/>: every call scans every entry already sitting in a managed group,
        /// regardless of what was passed in, since an asset whose stale entry needs removing may never
        /// appear in an incremental <paramref name="paths"/> list again. This check skips entries whose
        /// path cannot currently be resolved at all (empty <c>AssetPath</c> — e.g. an unfetched LFS
        /// pointer, an in-progress branch switch, or a missing package); a genuinely deleted asset is
        /// instead handled by <see cref="RemoveEntriesForDeletedAssets(IEnumerable{string}, AddressableAssetSettings)"/>.
        /// </remarks>
        public static IReadOnlyList<ValidationResult> ApplyAll(IEnumerable<string> paths, AddressableAssetSettings settings = null)
        {
            return ApplyAll(paths, settings, NullProgressReporter.Instance);
        }

        /// <summary>
        /// Overload of <see cref="ApplyAll(IEnumerable{string}, AddressableAssetSettings)"/> that adds
        /// progress reporting and cancellation. Returns the results accumulated up to the point where
        /// progress returns false and stops there.
        /// </summary>
        /// <remarks>
        /// On cancellation, writes for assets already processed up to that point have already completed
        /// (partial apply). Addressables state is not rolled back after cancellation.
        /// </remarks>
        public static IReadOnlyList<ValidationResult> ApplyAll(IEnumerable<string> paths, AddressableAssetSettings settings, IProgressReporter progress)
        {
            return ApplyAll(paths, settings, progress, RuleCollector.CollectEnabledRules());
        }

        /// <summary>
        /// Overload of <see cref="ApplyAll(IEnumerable{string}, AddressableAssetSettings, IProgressReporter)"/>
        /// that adds injection of the rules to evaluate. Does not go through reflection-based rule
        /// collection (<c>RuleCollector.CollectEnabledRules()</c>); instead it evaluates the exact
        /// rule list the caller supplies (intended for use in tests, etc.).
        /// </summary>
        public static IReadOnlyList<ValidationResult> ApplyAll(IEnumerable<string> paths, AddressableAssetSettings settings, IProgressReporter progress, IReadOnlyList<AddressRuleBase> rules)
        {
            if (s_isApplying) return Array.Empty<ValidationResult>();
            s_isApplying = true;
            try
            {
                settings ??= AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null) return Array.Empty<ValidationResult>();

                progress ??= NullProgressReporter.Instance;
                rules ??= Array.Empty<AddressRuleBase>();
                paths ??= Array.Empty<string>();

                var setup = RuleEvaluationPipeline.BuildSetup(settings, rules);
                // ManagedGroups（CleanupStaleEntriesの対象判定）は有効化されているルールのグループのみが対象。
                // 無効化中のルールが管理するグループのエントリはApplyAllでは掃除対象外（managed外扱い）になるが、
                // 資産削除時のRemoveEntriesForDeletedAssetsは全ルール対象で掃除するため、両者の間に非対称が存在する。

                var hasConfigureFailures = setup.ConfigureFailures.Count > 0;
                // Configure() に失敗したルールがある場合、managedGroups は「失敗したルールが本来担当していた
                // グループを、別の正常なルールがたまたま宣言していたため残っただけ」の可能性があり信頼できない。
                // そのためこの実行全体で stale クリーンアップ（Apply の Skipped 分岐での削除）を停止する。
                if (hasConfigureFailures && AddressTellerSettings.CleanupStaleEntries)
                    Debug.LogWarning($"[AddressTeller] Skipping stale entry cleanup for this run because {setup.ConfigureFailures.Count} rule(s) failed to configure.");

                // 旧バージョンの AddressTeller が作成した、いまはパスが無効なエントリ（ProjectSettings/*.asset の
                // readOnly エントリ等）を掃除する。paths（このメソッドに渡された対象アセット）には依存させない
                // — Postprocessor 経由の差分適用では、旧エントリのパス自体は変更イベントに含まれないため、
                // 対象を絞ると永遠に掃除されなくなってしまう。既存の stale クリーンアップと同じ所有権判定
                // （管理対象グループ限定）・同じ条件（CleanupStaleEntries、Configure() 失敗時は停止）を使う。
                // 削除は RemoveInvalidPathEntries 側で個別に Warning ログ済みのため、戻り値は破棄する。
                if (!hasConfigureFailures && AddressTellerSettings.CleanupStaleEntries)
                    _ = AddressTellerApplier.RemoveInvalidPathEntries(settings, setup.ManagedGroups, setup.ConfigFolder);

                var issues = new List<ValidationResult>(setup.ConfigureFailures);

                var pathList = paths as IList<string> ?? paths.ToList();
                var total = pathList.Count;

                var cancelled = false;
                for (var i = 0; i < total; i++)
                {
                    var path = pathList[i];

                    if (!progress.Report(i, total, path))
                    {
                        cancelled = true;
                        break;
                    }

                    if (AssetFilter.ShouldExcludeByPath(path, setup.ConfigFolder)) continue;

                    var ctx = RuleEvaluationPipeline.BuildContext(path);
                    if (ctx == null) continue;
                    if (AssetFilter.ShouldExclude(ctx, setup.ConfigFolder)) continue;

                    var resolution = RuleEvaluator.Evaluate(ctx, setup.Entries);
                    RuleEvaluationPipeline.AddRuleErrors(ctx, resolution, issues);

                    var result = AddressTellerApplier.Apply(ctx, resolution, settings, setup.ExistingGroupNames, setup.ManagedGroups, setup.AutoCreateMissingGroups, hasConfigureFailures);
                    // GroupWillBeCreated は IsOk=true（グループ自動作成が成功した）だが、
                    // 「作成された」ことを提示するため issues に情報として積む。
                    if (!result.IsOk || result.Status == ValidationStatus.GroupWillBeCreated)
                        issues.Add(result);
                }

                // キャンセル時は完了通知を呼ばない（中断したのに「完了」を通知すると意味的に矛盾するため）。
                if (!cancelled)
                    progress.Report(total, total, string.Empty);

                return issues;
            }
            finally
            {
                s_isApplying = false;
            }
        }

        /// <summary>
        /// Removes entries corresponding to deleted asset GUIDs (as obtained via
        /// <see cref="AssetPathToGUIDOptions.IncludeRecentlyDeletedAssets"/>), but only when the entry
        /// belongs to a group managed by AddressTeller. Rule evaluation is not performed.
        /// If settings is null, the project's default settings are used. Does nothing if Addressables is
        /// not set up.
        /// </summary>
        /// <remarks>
        /// Paths under ConfigFolder are not excluded here (since the asset is already deleted, the only
        /// thing available to judge by is the GUID, not the path). However, removal is limited to entries
        /// whose group is in managedGroups (asset-level ownership check), so there is no practical risk of
        /// an asset inside ConfigFolder being removed by mistake.
        /// </remarks>
        /// <returns>The entries that were actually removed (empty list if none were removed).</returns>
        public static IReadOnlyList<ClearedEntry> RemoveEntriesForDeletedAssets(IEnumerable<string> deletedGuids, AddressableAssetSettings settings = null)
        {
            // ルールの On/Off 設定に関わらず、削除追従の所有権判定（managedGroups）は全ルールを対象にする。
            // 無効化されたルールが過去に作ったエントリも、設定の有無に関係なく一貫して掃除対象として認識する必要があるため。
            return RemoveEntriesForDeletedAssets(deletedGuids, settings, RuleCollector.CollectRules());
        }

        /// <summary>
        /// Overload of <see cref="RemoveEntriesForDeletedAssets(IEnumerable{string}, AddressableAssetSettings)"/>
        /// that adds injection of the rules to evaluate. The caller supplies the exact rule list used for
        /// ownership determination (managedGroups) as-is (intended for use in tests, etc.). No decision
        /// about how rules were collected (e.g. enabled/disabled filtering) is made here.
        /// </summary>
        /// <returns>The entries that were actually removed (empty list if none were removed).</returns>
        public static IReadOnlyList<ClearedEntry> RemoveEntriesForDeletedAssets(IEnumerable<string> deletedGuids, AddressableAssetSettings settings, IReadOnlyList<AddressRuleBase> rules)
        {
            settings ??= AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return Array.Empty<ClearedEntry>();
            rules ??= Array.Empty<AddressRuleBase>();
            deletedGuids ??= Array.Empty<string>();

            var setup = RuleEvaluationPipeline.BuildSetup(settings, rules);

            // ApplyAll と同じ理由（managedGroups が信頼できなくなる）で、Configure() に失敗したルールが
            // ある場合はこの実行全体で削除追従（stale クリーンアップ）を停止する。
            var hasConfigureFailures = setup.ConfigureFailures.Count > 0;
            if (hasConfigureFailures && AddressTellerSettings.CleanupStaleEntries)
                Debug.LogWarning($"[AddressTeller] Skipping stale entry cleanup for this run because {setup.ConfigureFailures.Count} rule(s) failed to configure.");

            var cleared = new List<ClearedEntry>();
            foreach (var guid in deletedGuids)
            {
                if (string.IsNullOrEmpty(guid)) continue;
                var removed = AddressTellerApplier.RemoveEntryForDeletedAsset(guid, settings, setup.ManagedGroups, hasConfigureFailures);
                if (removed.HasValue) cleared.Add(removed.Value);
            }

            return cleared;
        }

        /// <summary>
        /// Validates all rules against every asset and returns the results that had a problem.
        /// If settings is null, the project's default settings are used. Returns an empty list if
        /// Addressables is not set up.
        /// </summary>
        /// <remarks>
        /// The return value may include <see cref="ValidationStatus.GroupWillBeCreated"/> entries
        /// (IsOk=true; informational notices about a group that AutoCreateMissingGroups will create).
        /// When treating the result as a "problem" (e.g. deciding whether to abort Apply), filter with
        /// <c>!result.IsOk</c>.
        /// </remarks>
        public static IReadOnlyList<ValidationResult> ValidateAll(AddressableAssetSettings settings = null)
        {
            return ValidateAll(settings, NullProgressReporter.Instance);
        }

        /// <summary>
        /// Overload of <see cref="ValidateAll(AddressableAssetSettings)"/> that adds progress reporting
        /// and cancellation. Returns the results accumulated up to the point where progress returns false
        /// and stops there (Validate performs no writes, so cancellation has no side effects).
        /// </summary>
        public static IReadOnlyList<ValidationResult> ValidateAll(AddressableAssetSettings settings, IProgressReporter progress)
        {
            return ValidateAll(settings, progress, RuleCollector.CollectEnabledRules());
        }

        /// <summary>
        /// Overload of <see cref="ValidateAll(AddressableAssetSettings, IProgressReporter)"/> that adds
        /// injection of the rules to evaluate. Does not go through reflection-based rule collection
        /// (<c>RuleCollector.CollectEnabledRules()</c>); instead it evaluates the exact rule list
        /// the caller supplies (intended for use in tests, etc.).
        /// </summary>
        public static IReadOnlyList<ValidationResult> ValidateAll(AddressableAssetSettings settings, IProgressReporter progress, IReadOnlyList<AddressRuleBase> rules)
        {
            settings ??= AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return Array.Empty<ValidationResult>();

            progress ??= NullProgressReporter.Instance;
            rules ??= Array.Empty<AddressRuleBase>();

            var setup = RuleEvaluationPipeline.BuildSetup(settings, rules);

            var issues = new List<ValidationResult>(setup.ConfigureFailures);

            var pathList = AssetDatabase.GetAllAssetPaths();
            var total = pathList.Length;

            var cancelled = false;
            for (var i = 0; i < total; i++)
            {
                var path = pathList[i];

                if (!progress.Report(i, total, path))
                {
                    cancelled = true;
                    break;
                }

                if (AssetFilter.ShouldExcludeByPath(path, setup.ConfigFolder)) continue;

                var ctx = RuleEvaluationPipeline.BuildContext(path);
                if (ctx == null) continue;
                if (AssetFilter.ShouldExclude(ctx, setup.ConfigFolder)) continue;

                var resolution = RuleEvaluator.Evaluate(ctx, setup.Entries);
                RuleEvaluationPipeline.AddRuleErrors(ctx, resolution, issues);

                var result = AddressTellerApplier.Validate(ctx, resolution, setup.ExistingGroupNames, setup.AutoCreateMissingGroups);
                // GroupWillBeCreated は IsOk=true（Apply をブロックしない）だが、
                // 「作成予定N件」を提示するため issues に情報として積む。
                if (!result.IsOk || result.Status == ValidationStatus.GroupWillBeCreated)
                    issues.Add(result);
            }

            // キャンセル時は完了通知を呼ばない（中断したのに「完了」を通知すると意味的に矛盾するため）。
            if (!cancelled)
                progress.Report(total, total, string.Empty);

            return issues;
        }
    }
}
