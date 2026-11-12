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
        /// ルール収集（<see cref="RuleCollector.CollectEnabledRules"/>）は <see cref="AddressTellerSettingsAsset.Current"/>
        /// の無効化ルール一覧を読むため、設定ファイルが読み込めない状態で呼ぶと、直前まで残っていた古い
        /// メモリ上の値で収集してしまう恐れがある（このメソッドを経由する各ラッパーオーバーロードは、
        /// 呼び出し先の中核オーバーロードが改めてゲートするとはいえ、ルール収集自体はその前に起きるため）。
        /// ゲートに成功した場合のみ収集し、失敗時は空リストを返す——どのみち中核オーバーロードが同じ
        /// ゲートで失敗して <see cref="ValidationStatus.SettingsUnavailable"/> を返すため、ここで集めたルールは
        /// 使われない。
        /// </summary>
        private static IReadOnlyList<AddressRuleBase> CollectEnabledRulesIfSettingsLoaded()
            => AddressTellerSettingsAsset.EnsureLoaded().Success
                ? RuleCollector.CollectEnabledRules()
                : Array.Empty<AddressRuleBase>();

        /// <summary>
        /// <see cref="CollectEnabledRulesIfSettingsLoaded"/> と同じ理由で、無効化されたルールも含めた
        /// 全ルール収集（<see cref="RuleCollector.CollectRules"/>）をゲート後に限定する版。
        /// <see cref="RemoveEntriesForDeletedAssets(IEnumerable{string}, AddressableAssetSettings)"/> 専用。
        /// </summary>
        private static IReadOnlyList<AddressRuleBase> CollectRulesIfSettingsLoaded()
            => AddressTellerSettingsAsset.EnsureLoaded().Success
                ? RuleCollector.CollectRules()
                : Array.Empty<AddressRuleBase>();

        /// <summary>stale クリーンアップを止めた理由（Configure() の失敗件数・欠落した宣言フォルダ件数）を警告ログ用に整形する。</summary>
        private static string DescribeCleanupSuspension(EvaluationSetup setup)
        {
            var reasons = new List<string>();
            if (setup.ConfigureFailures.Count > 0)
                reasons.Add($"{setup.ConfigureFailures.Count} rule(s) failed to configure");
            var missingFolders = setup.DeclaredFolderIssues.Count(i => i.Status == ValidationStatus.DeclaredFolderMissing);
            if (missingFolders > 0)
                reasons.Add($"{missingFolders} declared folder(s) do not exist");
            return string.Join(" and ", reasons);
        }

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
        /// filter with <c>!result.IsOk</c>. If the same asset has an entry in two or more Addressables
        /// groups at once, only <see cref="ValidationStatus.DuplicateAssetEntry"/> entries (one per
        /// duplicated asset) are returned and nothing is written that run — see that status.
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
        /// Both kinds of removal are carried out together at the end of the run, not as each asset is
        /// processed, and are skipped entirely if the run is cancelled. If
        /// <see cref="AddressTellerSettings.MaxStaleRemovalsPerRun"/> is set and the removals would exceed it,
        /// none are carried out and a <see cref="ValidationStatus.StaleRemovalLimitExceeded"/> result is returned
        /// (the Apply All menu item is the exception: its confirmation dialog shows the removals first).
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
            return ApplyAll(paths, settings, progress, CollectEnabledRulesIfSettingsLoaded());
        }

        /// <summary>
        /// Overload of <see cref="ApplyAll(IEnumerable{string}, AddressableAssetSettings, IProgressReporter)"/>
        /// that adds injection of the rules to evaluate. Does not go through reflection-based rule
        /// collection (<c>RuleCollector.CollectEnabledRules()</c>); instead it evaluates the exact
        /// rule list the caller supplies (intended for use in tests, etc.).
        /// </summary>
        public static IReadOnlyList<ValidationResult> ApplyAll(IEnumerable<string> paths, AddressableAssetSettings settings, IProgressReporter progress, IReadOnlyList<AddressRuleBase> rules)
            => ApplyAllCore(paths, settings, progress, rules, useDeclaredFolderCache: false, enforceStaleRemovalLimit: true);

        /// <summary>
        /// Postprocessor が import のたびに呼ぶ増分適用。<see cref="ApplyAll(IEnumerable{string}, AddressableAssetSettings)"/>
        /// と同じ処理だが、ルールが宣言したフォルダの検査結果を（Postprocessor が変更されたパスで無効化しながら）
        /// ドメインリロードまでキャッシュして再利用する。公開 API の ApplyAll は常に検査し直す。
        /// 非対話経路なので stale 削除の件数上限（<see cref="AddressTellerSettings.MaxStaleRemovalsPerRun"/>）を適用する。
        /// </summary>
        internal static IReadOnlyList<ValidationResult> ApplyAllIncremental(IEnumerable<string> paths, AddressableAssetSettings settings)
            => ApplyAllIncremental(paths, settings, CollectEnabledRulesIfSettingsLoaded());

        /// <summary>評価するルールを差し替えられる <see cref="ApplyAllIncremental(IEnumerable{string}, AddressableAssetSettings)"/>（テスト用）。</summary>
        internal static IReadOnlyList<ValidationResult> ApplyAllIncremental(IEnumerable<string> paths, AddressableAssetSettings settings, IReadOnlyList<AddressRuleBase> rules)
            => ApplyAllCore(paths, settings, NullProgressReporter.Instance, rules, useDeclaredFolderCache: true, enforceStaleRemovalLimit: true);

        /// <summary>
        /// メニューの確認ダイアログ（削除件数を見せて承認を得た後）から呼ぶ適用。公開 API の
        /// <see cref="ApplyAll(IEnumerable{string}, AddressableAssetSettings, IProgressReporter, IReadOnlyList{AddressRuleBase})"/>
        /// と同じ処理だが、stale 削除の件数上限（<see cref="AddressTellerSettings.MaxStaleRemovalsPerRun"/>）を適用しない。
        /// rules が null のときは公開 API の rules なしオーバーロードと同じくリフレクションでルールを収集する。
        /// </summary>
        internal static IReadOnlyList<ValidationResult> ApplyAllConfirmed(IEnumerable<string> paths, AddressableAssetSettings settings, IProgressReporter progress, IReadOnlyList<AddressRuleBase> rules)
            => ApplyAllCore(paths, settings, progress, rules ?? CollectEnabledRulesIfSettingsLoaded(), useDeclaredFolderCache: false, enforceStaleRemovalLimit: false);

        private static IReadOnlyList<ValidationResult> ApplyAllCore(IEnumerable<string> paths, AddressableAssetSettings settings, IProgressReporter progress, IReadOnlyList<AddressRuleBase> rules, bool useDeclaredFolderCache, bool enforceStaleRemovalLimit)
        {
            if (s_isApplying) return Array.Empty<ValidationResult>();
            s_isApplying = true;
            try
            {
                // 呼び出し元（メニュー・CLI・Postprocessor）は既に入口でログ付きのゲート
                // （AddressTellerSettings.EnsureLoaded）を通しているため、通常はここで失敗しない。
                // ただしこのメソッドは公開 API であり、利用者が独自のエディタ拡張から直接呼ぶ経路も
                // ゲートせずに既定値のまま動かしてはいけないため、ここでもログを出さないゲート
                // （AddressTellerSettingsAsset.EnsureLoaded）を通す。ログは呼び出し元の責務のまま。
                var gate = AddressTellerSettingsAsset.EnsureLoaded();
                if (!gate.Success)
                    return new ValidationResult[] { new ValidationResult(null, ValidationStatus.SettingsUnavailable, gate.Error) };

                settings ??= AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null) return Array.Empty<ValidationResult>();

                progress ??= NullProgressReporter.Instance;
                rules ??= Array.Empty<AddressRuleBase>();
                paths ??= Array.Empty<string>();

                // 同一 guid が2つ以上のグループにまたがって存在する状態では、どちらのエントリを対象に
                // 書き込むべきかが定義できない（settings.FindAssetEntry は先勝ちで片方を無言で選び、
                // もう片方は記録に残らないまま消える）。このランでは掃除も含めて何も書き込まず、重複の
                // 報告だけを返す（RuleEvaluationPipeline.BuildPredictedRunState と同じ判定）。BuildSetup
                // （Configure() の実行、GroupDefault() 使用時の DefaultGroup 自動作成を含む）より前に置く
                // ——重複を理由にこのランを丸ごと中止する以上、ルール構成の評価や DefaultGroup の作成のような
                // 副作用を先に発生させるべきではなく、RuleConfigureFailed の報告も重複の報告に埋もれさせない。
                var duplicateAssetEntries = DuplicateAssetEntryDetector.Detect(settings);
                if (duplicateAssetEntries.Count > 0)
                    return duplicateAssetEntries;

                var setup = RuleEvaluationPipeline.BuildSetup(settings, rules, useDeclaredFolderCache);
                // OwnedGroups（CleanupStaleEntriesの対象判定）は有効化されているルールが Address() を
                // 宣言しているグループのみが対象。無効化中のルールが所有するグループのエントリは
                // ApplyAllでは掃除対象外（所有外扱い）になるが、資産削除時のRemoveEntriesForDeletedAssetsは
                // 全ルール対象で掃除するため、両者の間に非対称が存在する。

                // Configure() に失敗したルールがある場合、ownedGroups は「失敗したルールが本来担当していた
                // グループを、別の正常なルールがたまたま宣言していたため残っただけ」の可能性があり信頼できない。
                // 宣言フォルダが実在しないルールがある場合も、そのルールが何にもマッチしないだけで、本来は
                // マッチするはずのエントリが「無マッチ」に見えている可能性がある。
                // どちらもこの実行全体で stale クリーンアップ（無マッチ判定に基づく削除と無効パスの掃除）を停止する。
                // 以降の suspendStaleCleanup は両方を含む（EvaluationSetup.SuspendsStaleCleanup 参照）。
                var suspendStaleCleanup = setup.SuspendsStaleCleanup;
                // 増分適用（useDeclaredFolderCache）で、停止の理由が宣言フォルダの欠落だけの場合は、ここでは
                // 警告しない。欠落が直らない間 import のたびに同じ警告が出てしまうため、Postprocessor が欠落の
                // 報告と合わせて内容が変わったときだけ出す（Configure() の失敗は従来どおり毎回出す）。
                if (suspendStaleCleanup && AddressTellerSettings.CleanupStaleEntries
                    && !(useDeclaredFolderCache && setup.ConfigureFailures.Count == 0))
                    Debug.LogWarning($"[AddressTeller] Skipping stale entry cleanup for this run because {DescribeCleanupSuspension(setup)}.");

                // stale クリーンアップ（無マッチ判定に基づくエントリ削除と、無効パスエントリの掃除）はループ中に
                // 実行せず、削除予定として staleRemovals に積み、ラン末尾でまとめて実行する。件数上限を超える場合に
                // 全件を見送れるようにするため（一部だけ削除済みの状態で超過に気付かないようにする）。
                // cleanupStale はラン中不変なのでループ外で1回だけ評価する。
                var cleanupStale = AddressTellerSettings.CleanupStaleEntries && !suspendStaleCleanup;
                var staleRemovals = new StaleRemovalPlan();
                var writtenGuids = new HashSet<string>();

                var issues = new List<ValidationResult>(setup.ConfigureFailures);
                issues.AddRange(setup.DeclaredFolderIssues);

                var pathList = paths as IList<string> ?? paths.ToList();
                var total = pathList.Count;

                // ラベル台帳の記録をラン単位にする: 除去判定の管理済み集合はここで固定し、このランで書くラベル名は
                // ラン末尾（EndRun）でまとめて台帳ファイルへ書く。
                AddressTellerLabelLedger.BeginRun();

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

                    var ctx = RuleEvaluationPipeline.BuildContext(path, out var typeUnresolved);
                    if (ctx == null)
                    {
                        if (typeUnresolved != null) issues.Add(typeUnresolved);
                        continue;
                    }
                    if (AssetFilter.ShouldExclude(ctx, setup.ConfigFolder)) continue;

                    var resolution = RuleEvaluator.Evaluate(ctx, setup.Entries);
                    RuleEvaluationPipeline.AddRuleErrors(ctx, resolution, issues);

                    var result = AddressTellerApplier.Apply(ctx, resolution, settings, setup.ExistingGroupNames, setup.AutoCreateMissingGroups, suspendStaleCleanup);
                    // GroupWillBeCreated は IsOk=true（グループ自動作成が成功した）だが、
                    // 「作成された」ことを提示するため issues に情報として積む。
                    if (!result.IsOk || result.Status == ValidationStatus.GroupWillBeCreated)
                        issues.Add(result);

                    // アドレスを実際に書き込んだ資産（Ok/GroupWillBeCreated は Apply が書き込みまで成功した場合のみ。
                    // LabelsOnly は既存アドレスを維持するだけなので対象外）。ラン末尾の重複検出の入力。
                    if (result.Status == ValidationStatus.Ok || result.Status == ValidationStatus.GroupWillBeCreated)
                        writtenGuids.Add(ctx.Guid);

                    // ルール例外(resolution.Errors)があった場合、「全ルールが正常評価された上でマッチ0件」とは
                    // 言えないため削除予定にしない（ルールのバグで誤ってエントリを削除しないようにする）。
                    // Configure() 失敗・宣言フォルダ欠落は cleanupStale に織り込み済み。
                    if (cleanupStale && result.Status == ValidationStatus.Skipped && resolution.Errors.Count == 0)
                        staleRemovals.AddUnmatched(AddressTellerApplier.FindStaleManagedEntry(ctx.Guid, settings, setup.OwnedGroups));
                }

                // 書いたラベル名を台帳へ記録する。書けなかった場合は例外でランを中断せず、blocking な結果として報告する
                // （キャンセル時も、そこまでに書いたラベルは記録する）。
                var ledgerError = AddressTellerLabelLedger.EndRun();
                if (ledgerError != null)
                    issues.Add(new ValidationResult(null, ValidationStatus.LabelLedgerWriteFailed, ledgerError));

                // キャンセル時は削除を一切行わない（途中までの走査結果で「無マッチ」「無効パス」を確定させない。
                // 書き込みは処理済みアセット分が完了済み＝部分適用のままで、巻き戻さないのは従来どおり）。
                if (!cancelled && cleanupStale)
                {
                    // 旧バージョンの AddressTeller が作成した、いまはパスが無効なエントリ（ProjectSettings/*.asset の
                    // readOnly エントリ等）も同じ計画に合流させる。paths（このメソッドに渡された対象アセット）には
                    // 依存させない — Postprocessor 経由の差分適用では、旧エントリのパス自体は変更イベントに
                    // 含まれないため、対象を絞ると永遠に掃除されなくなってしまう。既存の stale クリーンアップと
                    // 同じ所有権判定（所有グループ限定）を使う。
                    foreach (var invalidPathEntry in AddressTellerApplier.FindInvalidPathManagedEntries(settings, setup.OwnedGroups, setup.ConfigFolder))
                        staleRemovals.AddInvalidPath(invalidPathEntry);

                    // 上限（0 = 無制限）を超えたら全件見送り、超過をブロッキングの結果として報告する。
                    // 適用は非対話経路（Postprocessor・CLI）のみ。メニュー経路は確認ダイアログが安全弁。
                    var limit = enforceStaleRemovalLimit ? AddressTellerSettings.MaxStaleRemovalsPerRun : 0;
                    if (limit > 0 && staleRemovals.Count > limit)
                        issues.Add(staleRemovals.BuildLimitExceededResult(limit));
                    else
                        // 削除された各エントリは個別に Warning ログ済みのため、戻り値は意図的に破棄する。
                        _ = staleRemovals.Execute(settings);
                }

                // 別アセットとのアドレス重複検出。stale 削除の後に行い、削除されたエントリを重複として数えない
                // （ValidateAll の予測後集合と同じ見方）。書き込みが1件も無いランでは索引を作らない。
                // 非ブロッキング（DuplicateAddress は書き込みを止めない報告専用ステータス）。
                issues.AddRange(DuplicateAddressDetector.DetectForWrittenGuids(settings, writtenGuids));

                // キャンセル時は完了通知を呼ばない（中断したのに「完了」を通知すると意味的に矛盾するため）。
                if (!cancelled)
                    progress.Report(total, total, string.Empty);

                return issues;
            }
            finally
            {
                // 例外でループを抜けた場合に、ラン状態（固定した管理済み集合）を残さないための後始末。
                // 正常経路では EndRun 済みで、ここでは何もしない。
                AddressTellerLabelLedger.EndRun();
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
        /// in groups AddressTeller owns (asset-level ownership check), so there is no practical risk of
        /// an asset inside ConfigFolder being removed by mistake.
        /// </remarks>
        /// <returns>The entries that were actually removed (empty list if none were removed).</returns>
        public static IReadOnlyList<ClearedEntry> RemoveEntriesForDeletedAssets(IEnumerable<string> deletedGuids, AddressableAssetSettings settings = null)
        {
            // ルールの On/Off 設定に関わらず、削除追従の所有権判定（ownedGroups）は全ルールを対象にする。
            // 無効化されたルールが過去に作ったエントリも、設定の有無に関係なく一貫して掃除対象として認識する必要があるため。
            return RemoveEntriesForDeletedAssets(deletedGuids, settings, CollectRulesIfSettingsLoaded());
        }

        /// <summary>
        /// Overload of <see cref="RemoveEntriesForDeletedAssets(IEnumerable{string}, AddressableAssetSettings)"/>
        /// that adds injection of the rules to evaluate. The caller supplies the exact rule list used for
        /// ownership determination (ownedGroups) as-is (intended for use in tests, etc.). No decision
        /// about how rules were collected (e.g. enabled/disabled filtering) is made here.
        /// </summary>
        /// <returns>The entries that were actually removed (empty list if none were removed).</returns>
        public static IReadOnlyList<ClearedEntry> RemoveEntriesForDeletedAssets(IEnumerable<string> deletedGuids, AddressableAssetSettings settings, IReadOnlyList<AddressRuleBase> rules)
        {
            // 削除系 API は ValidationResult を返さない（戻り値は削除済みエントリ一覧）ため、他の中核
            // オーバーロードのように SettingsUnavailable を返して報告することができない。破壊的操作
            // （エントリ削除）を既定値のまま進めるわけにもいかないため、ここだけは例外的にログを出して
            // 何もせず戻る。Postprocessor 経由の呼び出しは、この呼び出しより前にログ付きゲート
            // （AddressTellerSettings.EnsureLoaded）を既に通しているため、通常この経路には到達しない。
            var gate = AddressTellerSettingsAsset.EnsureLoaded();
            if (!gate.Success)
            {
                Debug.LogError($"[AddressTeller] {gate.Error}");
                return Array.Empty<ClearedEntry>();
            }

            settings ??= AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return Array.Empty<ClearedEntry>();
            rules ??= Array.Empty<AddressRuleBase>();
            deletedGuids ??= Array.Empty<string>();

            // ApplyAll と同じ理由（どちらのエントリを対象にすべきか定義できない）で、同一 guid が2つ以上の
            // グループにまたがって存在する状態ではこの実行全体で何もせず戻る。削除系 API は ValidationResult を
            // 返せないため、内容はログで報告する（gate 失敗時と同じ扱い）。ApplyAll と同様、BuildSetup
            // （Configure() の実行、DefaultGroup 自動作成を含む）より前に置く。
            var duplicateAssetEntries = DuplicateAssetEntryDetector.Detect(settings);
            if (duplicateAssetEntries.Count > 0)
            {
                foreach (var duplicate in duplicateAssetEntries)
                    Debug.LogError($"[AddressTeller] {duplicate.Message}");
                return Array.Empty<ClearedEntry>();
            }

            var setup = RuleEvaluationPipeline.BuildSetup(settings, rules);

            // ApplyAll と同じ理由（ownedGroups が信頼できなくなる）で、Configure() に失敗したルールが
            // ある場合はこの実行全体で削除追従（stale クリーンアップ）を停止する。
            // 宣言フォルダの欠落でも同様に止める（EvaluationSetup.SuspendsStaleCleanup 参照）。
            var suspendStaleCleanup = setup.SuspendsStaleCleanup;
            if (suspendStaleCleanup && AddressTellerSettings.CleanupStaleEntries)
                Debug.LogWarning($"[AddressTeller] Skipping stale entry cleanup for this run because {DescribeCleanupSuspension(setup)}.");

            var cleared = new List<ClearedEntry>();
            foreach (var guid in deletedGuids)
            {
                if (string.IsNullOrEmpty(guid)) continue;
                var removed = AddressTellerApplier.RemoveEntryForDeletedAsset(guid, settings, setup.OwnedGroups, suspendStaleCleanup);
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
        /// (IsOk=true; informational notices about a group that AutoCreateMissingGroups will create), and
        /// <see cref="ValidationStatus.DuplicateAddress"/> entries, which can have IsOk=false
        /// (<see cref="ValidationResult.HasWritableDuplicate"/> is true) without meaning a write should be
        /// blocked — DuplicateAddress is never a reason to abort an apply (see
        /// <see cref="ValidationResult.IsOk"/> and <see cref="ValidationResult.HasWritableDuplicate"/> for
        /// the exact rule). When deciding whether to abort based on this result (e.g. before an Apply),
        /// filter with <see cref="ValidationResult.IsBlocking"/>, not <c>!result.IsOk</c> alone. Note also
        /// that a <see cref="ValidationStatus.RuleError"/> entry for one address-producing rule does not by
        /// itself mean nothing would be written for that asset: if the winning candidate has strictly
        /// higher priority (a lower <c>Order</c>) than the rule that threw, its address is still written —
        /// the failing rule could never have outranked it anyway. But if the failing rule's <c>Order</c>
        /// was equal to or lower than (i.e. as high or higher priority as) the winning candidate's, nothing
        /// is written for that asset at all — see <see cref="ValidationStatus.BlockedByRuleError"/>, which
        /// is reported alongside the <see cref="ValidationStatus.RuleError"/> in that case (the latter is
        /// the cause, the former is the effect on this asset's write). If the same asset has an entry in two
        /// or more Addressables groups at once (<see cref="ValidationStatus.DuplicateAssetEntry"/>), only
        /// entries of that status are returned (one per duplicated asset) — no rule was evaluated that run.
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
            return ValidateAll(settings, progress, CollectEnabledRulesIfSettingsLoaded());
        }

        /// <summary>
        /// Overload of <see cref="ValidateAll(AddressableAssetSettings, IProgressReporter)"/> that adds
        /// injection of the rules to evaluate. Does not go through reflection-based rule collection
        /// (<c>RuleCollector.CollectEnabledRules()</c>); instead it evaluates the exact rule list
        /// the caller supplies (intended for use in tests, etc.).
        /// </summary>
        public static IReadOnlyList<ValidationResult> ValidateAll(AddressableAssetSettings settings, IProgressReporter progress, IReadOnlyList<AddressRuleBase> rules)
        {
            // ApplyAll と同じ理由（メニュー・CLI・Postprocessor は既に入口でゲート済み。このメソッドは
            // 公開 API であり、利用者が直接呼ぶ経路もゲートせず既定値のまま動かしてはいけない）で、
            // ログを出さないゲートを通す。ログは呼び出し元の責務のまま。
            var gate = AddressTellerSettingsAsset.EnsureLoaded();
            if (!gate.Success)
                return new ValidationResult[] { new ValidationResult(null, ValidationStatus.SettingsUnavailable, gate.Error) };

            settings ??= AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return Array.Empty<ValidationResult>();

            progress ??= NullProgressReporter.Instance;
            rules ??= Array.Empty<AddressRuleBase>();

            var pathList = AssetDatabase.GetAllAssetPaths();

            // BuildPredictedRunState は BuildPredictedSnapshot と共有の計算（Capture 起点の afterMap 構築、
            // 無効パス掃除、Predict による AddOrUpdate/Remove の反映）を行う。ValidateAll 独自に
            // 「予測される最終状態」を再実装すると、stale クリーンアップの反映漏れなどで
            // BuildPredictedSnapshot と食い違いうるため、ここに集約している
            // （PredictedRunState の XML doc 参照）。各アセットの個別 issue（GroupNotFound 等）は
            // 従来通り AddressTellerApplier.Validate の結果そのもの（Predict は内部でこれをラップするだけ）。
            // state.Issues には GroupWillBeCreated（IsOk=true の作成予定通知）もアセットごとに含まれる
            // ——ValidateAll の従来契約どおり、フィルタせずそのまま返す（BuildPredictedSnapshot は逆に
            // GroupsToCreate で提示するため自分の側で除外している。AddressTellerSnapshotService 参照）。
            var state = RuleEvaluationPipeline.BuildPredictedRunState(settings, pathList, rules, progress);

            // 別アセット間のアドレス重複を検出する（同一アセットへの複数ルールの衝突とは別軸。
            // ValidationStatus.DuplicateAddress の XML doc 参照）。
            state.Issues.AddRange(DuplicateAddressDetector.Detect(
                state.AfterMap.Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value.Address)),
                state.WrittenGuids));

            return state.Issues;
        }
    }
}
