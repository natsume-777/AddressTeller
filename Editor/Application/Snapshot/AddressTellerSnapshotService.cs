using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>How to treat labels that are not present in the snapshot when restoring.</summary>
    public enum SnapshotRestoreMode
    {
        /// <summary>Writes only the Address/Label recorded in the snapshot. Labels added after the snapshot was taken are kept.</summary>
        Additive,

        /// <summary>Strips labels not present in the snapshot (exact label match). Does not remove entries.</summary>
        Exact,
    }

    /// <summary>
    /// Collects, writes back, and diffs state between AddressableAssetSettings and a snapshot.
    /// </summary>
    /// <remarks>
    /// Null contract for these arguments: <see cref="Capture(AddressableAssetSettings)"/>,
    /// <see cref="Restore"/>, <see cref="RestoreExactWithRemoval"/>,
    /// <see cref="BuildPredictedSnapshot(AddressableAssetSettings, IEnumerable{string})"/>, and
    /// <see cref="Diff"/> are public API entry points that consumers call explicitly, so they throw
    /// <see cref="ArgumentNullException"/> when a required argument is null (the same policy as
    /// <see cref="AddressTellerClearService.Clear"/>). This intentionally differs from the default-value
    /// fallback that each <see cref="AddressTellerService"/> method applies to its settings argument
    /// (using the current AddressableAssetSettings when omitted), which does not throw.
    /// </remarks>
    public static class AddressTellerSnapshotService
    {
        /// <summary>Currently supported snapshot schema version.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Collects the current Addressables state as a snapshot.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        public static AddressTellerSnapshot Capture(AddressableAssetSettings settings) => Capture(settings, "");

        /// <summary>
        /// Collects the current Addressables state as a snapshot.
        /// <paramref name="comment"/> is recorded as-is as a user-supplied free-form comment.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        public static AddressTellerSnapshot Capture(AddressableAssetSettings settings, string comment)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var snapshot = new AddressTellerSnapshot();

            foreach (var group in settings.groups)
            {
                if (group == null) continue;

                foreach (var entry in group.entries)
                {
                    snapshot.Entries.Add(new SnapshotEntry
                    {
                        Guid = entry.guid,
                        Address = entry.address,
                        GroupName = group.Name,
                        Labels = entry.labels.OrderBy(l => l, StringComparer.Ordinal).ToList(),
                    });
                }
            }

            // List.Sort は不安定ソート（同値の相対順序が保証されない）。他箇所（Labels の OrderBy 等）と揃え、
            // 決定的な順序を保証する OrderBy + StringComparer.Ordinal を使う。
            snapshot.Entries = snapshot.Entries.OrderBy(e => e.Guid, StringComparer.Ordinal).ToList();

            snapshot.CapturedAtIso = DateTime.UtcNow.ToString("o");
            snapshot.Comment = comment ?? "";
            snapshot.UnityVersion = Application.unityVersion;
            snapshot.PackageVersion = GetPackageVersion();
            snapshot.SchemaVersion = CurrentSchemaVersion;

            return snapshot;
        }

        /// <summary>
        /// AddressTeller パッケージのバージョンを取得する。
        /// 取得に失敗した場合はログを出力し空文字を返す（呼び出し側で握りつぶさない）。
        /// </summary>
        private static string GetPackageVersion()
        {
            try
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AddressTellerSnapshot).Assembly);
                return packageInfo?.version ?? "";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AddressTeller] Failed to retrieve package version: {e.Message}");
                return "";
            }
        }

        /// <summary>
        /// Loads a snapshot JSON file and reconstructs it as an <see cref="AddressTellerSnapshot"/>.
        /// If loading, parsing, or content validation fails, sets a message in <paramref name="error"/>
        /// and returns false.
        /// </summary>
        public static bool LoadFromFile(string path, out AddressTellerSnapshot snapshot, out string error)
        {
            snapshot = null;

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                error = $"Failed to load snapshot: {path} ({e.Message})";
                return false;
            }

            AddressTellerSnapshot parsed;
            try
            {
                parsed = AddressTellerSnapshot.FromJson(json);
            }
            catch (Exception e)
            {
                error = $"Failed to parse snapshot: {path} ({e.Message})";
                return false;
            }

            if (parsed == null || parsed.Entries == null)
            {
                error = $"Snapshot content is invalid: {path}";
                return false;
            }

            if (parsed.SchemaVersion > CurrentSchemaVersion)
            {
                error = $"Snapshot schema version ({parsed.SchemaVersion}) is not supported: {path}";
                return false;
            }

            var seenGuids = new HashSet<string>();
            foreach (var entry in parsed.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Guid))
                {
                    error = $"Snapshot content is invalid (an entry has an empty GUID): {path}";
                    return false;
                }

                if (!seenGuids.Add(entry.Guid))
                {
                    error = $"Snapshot content is invalid (duplicate GUID '{entry.Guid}'): {path}";
                    return false;
                }

                // GroupName/Labels は Restore() 実行時にも欠落チェックしているが（そちらは任意の
                // AddressTellerSnapshot を受け取れる公開 API のためのエントリ単位スキップ）、
                // ファイル読み込み時点で壊れた内容を検出できるよう、ここではファイル全体のロードを失敗させる
                // （fail-closed。1件のエントリ破損のせいで残り全件の復元が塞がれるのは意図した割り切りで、
                // 復旧手段としてはエラーメッセージの通り該当エントリを JSON から削除して再読込する）。
                if (string.IsNullOrEmpty(entry.GroupName))
                {
                    error = $"Snapshot content is invalid (an entry has an empty GroupName; GUID '{entry.Guid}'): {path}. "
                        + "Removing this entry from the JSON file allows the rest of the snapshot to be restored.";
                    return false;
                }

                // JsonUtility はここまで到達しない実測結果あり（"Labels": null を指定しても List<T> フィールドの
                // 初期化子由来の空リストが保持される。参照型フィールドへの null 反映自体を JsonUtility がサポート
                // していないため）。手動編集で Labels キー自体を削除した等、将来の入力パターンに備えた防御として維持する。
                if (entry.Labels == null)
                {
                    error = $"Snapshot content is invalid (an entry has no Labels list; GUID '{entry.Guid}'): {path}. "
                        + "Removing this entry from the JSON file allows the rest of the snapshot to be restored.";
                    return false;
                }
            }

            snapshot = parsed;
            error = null;
            return true;
        }

        /// <summary>
        /// Writes the snapshot's content back to Addressables.
        /// If a group recorded in the snapshot does not exist, its entries are skipped and a message is
        /// returned. Since Addressables does not guarantee group name uniqueness, if the restore target
        /// group name is duplicated, the first group found is used and processing continues, with the
        /// duplication reported in issues (only for group names actually referenced by
        /// <paramref name="snapshot"/>; unrelated duplicate groups are not reported).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="settings"/> is null.</exception>
        public static IReadOnlyList<string> Restore(
            AddressTellerSnapshot snapshot,
            AddressableAssetSettings settings,
            SnapshotRestoreMode mode = SnapshotRestoreMode.Additive)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var issues = new List<string>();

            // 重複グループ名の報告対象を、このスナップショットが実際に使うグループ名だけに絞り込む
            // （settings 全体の重複を無条件に報告すると、スナップショットと無関係なグループの重複まで
            // issues に混ざりノイズになるため）。
            var neededGroupNames = new HashSet<string>(
                snapshot.Entries
                    .Where(e => e != null && !string.IsNullOrEmpty(e.GroupName))
                    .Select(e => e.GroupName));

            // FindGroup は内部で settings.groups を毎回線形探索するため、
            // ループ外で一度だけ Dictionary 化して参照する。
            // グループ名は Addressables 上で一意性が保証されていない（UI からは一意性が強制されるが、
            // API 直接操作・アセット複製・別フォルダ配置等では重複しうる）ため、
            // ToDictionary（重複キーで例外）は使わず、重複を検出したら issues に報告した上で
            // 最初に見つかったグループを採用して処理を継続する。
            var groupsByName = new Dictionary<string, AddressableAssetGroup>();
            foreach (var group in settings.groups.Where(g => g != null))
            {
                if (groupsByName.ContainsKey(group.Name))
                {
                    if (neededGroupNames.Contains(group.Name))
                        issues.Add($"Multiple groups are named '{group.Name}'. The first one found will be used for restoring matching entries.");
                    continue;
                }

                groupsByName.Add(group.Name, group);
            }

            foreach (var entry in snapshot.Entries)
            {
                // Restore/RestoreExactWithRemoval は任意の AddressTellerSnapshot を受け取れる公開 API のため、
                // 呼び出し側が手で組み立てたスナップショット（LoadFromFile を経由しないもの）では
                // entry 自体や GroupName/Labels が null のまま渡ってくることがある。CreateOrMoveEntry の
                // null チェックと同様、ここでも1件のスキップとして扱い、復元全体を止めない。
                if (entry == null)
                {
                    issues.Add("Snapshot entry is invalid (null entry). Skipped.");
                    continue;
                }

                if (string.IsNullOrEmpty(entry.GroupName) || entry.Labels == null)
                {
                    issues.Add($"Snapshot entry is invalid (missing GroupName/Labels). Skipped entry '{entry.Guid}'.");
                    continue;
                }

                if (!groupsByName.TryGetValue(entry.GroupName, out var group))
                {
                    issues.Add($"Group '{entry.GroupName}' not found. Skipped entry '{entry.Guid}'.");
                    continue;
                }

                var assetEntry = settings.CreateOrMoveEntry(entry.Guid, group);
                if (assetEntry == null)
                {
                    // guid に対応するアセットが既に存在しない等の理由で作成できなかった場合、NRE で
                    // ループを中断せず、このエントリだけをスキップして復元を継続する（部分的な復元中断防止）。
                    issues.Add($"Failed to create/move entry for GUID '{entry.Guid}' into group '{entry.GroupName}' (the asset may no longer exist). Skipped.");
                    continue;
                }

                assetEntry.SetAddress(entry.Address);

                if (mode == SnapshotRestoreMode.Exact)
                {
                    foreach (var existingLabel in assetEntry.labels.ToList())
                        if (!entry.Labels.Contains(existingLabel))
                            assetEntry.SetLabel(existingLabel, false);
                }

                foreach (var label in entry.Labels)
                {
                    settings.AddLabel(label);
                    assetEntry.SetLabel(label, true);
                }
            }

            return issues;
        }

        /// <summary>
        /// Restore logic dedicated to Undo Last Apply. Removes the entries for the GUIDs in
        /// <paramref name="guidsToRemove"/> first, then runs <see cref="Restore"/> in Exact mode.
        /// The general-purpose <see cref="Restore"/> never removes entries (an intentional design choice
        /// to avoid accidentally removing entries in unmanaged groups), so entry removal is only
        /// performed here, in a context like Undo Last Apply where "revert to the state immediately
        /// before Apply" is unambiguous, and only for GUIDs the caller has already filtered by ownership
        /// (managedGroups).
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="snapshot"/>, <paramref name="settings"/>, or <paramref name="guidsToRemove"/> is null.
        /// </exception>
        public static IReadOnlyList<string> RestoreExactWithRemoval(
            AddressTellerSnapshot snapshot,
            AddressableAssetSettings settings,
            IEnumerable<string> guidsToRemove)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (guidsToRemove == null) throw new ArgumentNullException(nameof(guidsToRemove));

            foreach (var guid in guidsToRemove)
            {
                var entry = settings.FindAssetEntry(guid);
                if (entry?.parentGroup == null) continue;

                Debug.LogWarning($"[AddressTeller] Removing entry: guid={guid}, group='{entry.parentGroup.Name}', address='{entry.address}' (undo of last apply).");
                settings.RemoveAssetEntry(guid);
            }

            return Restore(snapshot, settings, SnapshotRestoreMode.Exact);
        }

        /// <summary>
        /// Without executing Apply, computes a snapshot representing the post-apply state as a diff
        /// against the current state.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> or <paramref name="paths"/> is null.</exception>
        public static DryRunResult BuildPredictedSnapshot(AddressableAssetSettings settings, IEnumerable<string> paths)
            => BuildPredictedSnapshot(settings, paths, RuleCollector.CollectEnabledRules());

        /// <summary>
        /// Overload that explicitly specifies the rule list. Used for dry-run computation in tests or a
        /// specific scope.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> or <paramref name="paths"/> is null.</exception>
        public static DryRunResult BuildPredictedSnapshot(AddressableAssetSettings settings, IEnumerable<string> paths, IReadOnlyList<AddressRuleBase> rules)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (paths == null) throw new ArgumentNullException(nameof(paths));

            var before = Capture(settings);
            var afterMap = before.Entries.ToDictionary(e => e.Guid);

            // 重複 Order の警告は RuleCollector.CollectRules() のキャッシュ構築時（ドメインリロードごとに1回）に
            // 出力済みのため、dry-run では出さない（二重ログ防止）。
            var setup = RuleEvaluationPipeline.BuildSetup(settings, rules);
            // ApplyAll/RemoveEntriesForDeletedAssets と同じ理由（managedGroups が信頼できなくなる）で、
            // Configure() に失敗したルールがある場合は Remove の予測（stale クリーンアップ）を行わない。
            // dry-run のため、スキップ自体のログはここでは出さない（実 Apply 側で1本出れば十分なため）。
            var hasConfigureFailures = setup.ConfigureFailures.Count > 0;

            var issues = new List<ValidationResult>(setup.ConfigureFailures);
            var groupsToCreate = new HashSet<string>();

            foreach (var path in paths)
            {
                if (AssetFilter.ShouldExcludeByPath(path, setup.ConfigFolder)) continue;

                var ctx = RuleEvaluationPipeline.BuildContext(path);
                if (ctx == null) continue;
                if (AssetFilter.ShouldExclude(ctx, setup.ConfigFolder)) continue;

                var resolution = RuleEvaluator.Evaluate(ctx, setup.Entries);
                RuleEvaluationPipeline.AddRuleErrors(ctx, resolution, issues);

                var prediction = AddressTellerApplier.Predict(ctx, resolution, settings, setup.ExistingGroupNames, setup.ManagedGroups, setup.AutoCreateMissingGroups, hasConfigureFailures);

                switch (prediction.Action)
                {
                    case PredictedAction.AddOrUpdate:
                        afterMap[ctx.Guid] = prediction.PredictedEntry;
                        break;
                    case PredictedAction.Remove:
                        afterMap.Remove(ctx.Guid);
                        break;
                    case PredictedAction.NoOp:
                        break;
                }

                if (prediction.Validation.Status == ValidationStatus.GroupWillBeCreated)
                    groupsToCreate.Add(prediction.PredictedEntry.GroupName);

                if (!prediction.Validation.IsOk)
                    issues.Add(prediction.Validation);
            }

            var after = new AddressTellerSnapshot();
            after.Entries.AddRange(afterMap.Values.OrderBy(e => e.Guid, StringComparer.Ordinal));

            var diff = Diff(before, after);
            var sortedGroupsToCreate = groupsToCreate.OrderBy(g => g, StringComparer.Ordinal).ToList();
            return new DryRunResult(diff, issues, sortedGroupsToCreate, after);
        }

        /// <summary>
        /// Compares two snapshots per-GUID and returns the added/removed/changed diff.
        /// Throws <see cref="ArgumentNullException"/> if <paramref name="before"/>/<paramref name="after"/>
        /// is null (since this is a public API entry point that consumers call explicitly).
        /// </summary>
        public static SnapshotDiff Diff(AddressTellerSnapshot before, AddressTellerSnapshot after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));

            var diff = new SnapshotDiff();
            var beforeMap = before.Entries.ToDictionary(e => e.Guid);
            var afterGuids = new HashSet<string>();

            foreach (var entry in after.Entries)
            {
                afterGuids.Add(entry.Guid);

                if (!beforeMap.TryGetValue(entry.Guid, out var prev))
                {
                    diff.AddAdded(entry);
                    continue;
                }

                if (prev.Address != entry.Address
                    || prev.GroupName != entry.GroupName
                    || !prev.Labels.SequenceEqual(entry.Labels))
                    diff.AddChanged(prev, entry);
            }

            foreach (var entry in before.Entries)
                if (!afterGuids.Contains(entry.Guid))
                    diff.AddRemoved(entry);

            return diff;
        }
    }

    /// <summary>
    /// Diff between two snapshots.
    /// <see cref="Added"/>/<see cref="Removed"/>/<see cref="Changed"/> are read-only views; entries are
    /// added to the underlying mutable lists only through <see cref="AddAdded"/>/<see cref="AddRemoved"/>/
    /// <see cref="AddChanged"/> (internal, same-assembly only).
    /// </summary>
    public sealed class SnapshotDiff
    {
        private readonly List<SnapshotEntry> _added = new();
        private readonly List<SnapshotEntry> _removed = new();
        private readonly List<(SnapshotEntry Before, SnapshotEntry After)> _changed = new();

        /// <summary>
        /// <see cref="AddressTellerSnapshotService.Diff"/> でのみ構築される。外部からは意味を持たない
        /// 引数なしインスタンスを作れないよう internal 化している（テストは InternalsVisibleTo 経由で構築可能）。
        /// </summary>
        internal SnapshotDiff() { }

        /// <summary>Entries present in the after snapshot but not in the before snapshot.</summary>
        public IReadOnlyList<SnapshotEntry> Added => _added;

        /// <summary>Entries present in the before snapshot but not in the after snapshot.</summary>
        public IReadOnlyList<SnapshotEntry> Removed => _removed;

        /// <summary>Entries present in both snapshots whose Address/GroupName/Labels differ, as (Before, After) pairs.</summary>
        public IReadOnlyList<(SnapshotEntry Before, SnapshotEntry After)> Changed => _changed;

        /// <summary>True when there is no <see cref="Added"/>, <see cref="Removed"/>, or <see cref="Changed"/> entry.</summary>
        public bool IsEmpty => _added.Count == 0 && _removed.Count == 0 && _changed.Count == 0;

        /// <summary>追加差分を1件登録する。<see cref="AddressTellerSnapshotService.Diff"/> 専用。</summary>
        internal void AddAdded(SnapshotEntry entry) => _added.Add(entry);

        /// <summary>削除差分を1件登録する。<see cref="AddressTellerSnapshotService.Diff"/> 専用。</summary>
        internal void AddRemoved(SnapshotEntry entry) => _removed.Add(entry);

        /// <summary>変更差分を1件登録する。<see cref="AddressTellerSnapshotService.Diff"/> 専用。</summary>
        internal void AddChanged(SnapshotEntry before, SnapshotEntry after) => _changed.Add((before, after));
    }

    /// <summary>
    /// Result of <see cref="AddressTellerSnapshotService.BuildPredictedSnapshot"/>.
    /// Returns the diff that would result from running Apply, together with any problems (conflicts,
    /// missing groups, rule exceptions, etc.).
    /// </summary>
    public readonly struct DryRunResult
    {
        /// <summary>Predicted diff between the current state and the post-apply state.</summary>
        public SnapshotDiff Diff { get; }

        /// <summary>Validation results that had a problem, encountered while computing the prediction.</summary>
        public IReadOnlyList<ValidationResult> Issues { get; }

        /// <summary>
        /// With AutoCreateMissingGroups enabled, the set of group names this dry-run would create.
        /// Sorted in Ordinal order. The dry-run does not actually create them (zero side effects).
        /// </summary>
        public IReadOnlyList<string> GroupsToCreate { get; }

        /// <summary>
        /// All entries of the predicted post-apply state. Kept (not just the diff) for derived
        /// computations that need the placement of every asset, such as the logical bundle distribution
        /// summary (<see cref="BundleDistributionCalculator"/>).
        /// May be null when constructed outside the dry-run computation (e.g. in a test).
        /// </summary>
        public AddressTellerSnapshot After { get; }

        /// <summary>
        /// <see cref="AddressTellerSnapshotService.BuildPredictedSnapshot(AddressableAssetSettings, IEnumerable{string})"/>
        /// でのみ構築される。外部からは意味を持たないインスタンスを組み立てられないよう internal 化している
        /// （テストは InternalsVisibleTo 経由で構築可能）。
        /// </summary>
        internal DryRunResult(SnapshotDiff diff, IReadOnlyList<ValidationResult> issues, IReadOnlyList<string> groupsToCreate = null, AddressTellerSnapshot after = null)
        {
            Diff = diff;
            Issues = issues;
            GroupsToCreate = groupsToCreate ?? Array.Empty<string>();
            After = after;
        }
    }
}
