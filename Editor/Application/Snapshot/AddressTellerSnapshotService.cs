using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>復元時にスナップショットにないラベルをどう扱うか。</summary>
    public enum SnapshotRestoreMode
    {
        /// <summary>スナップショットの Address/Label のみ書き込む。スナップショット作成後に付与されたラベルは保持する。</summary>
        Additive,

        /// <summary>スナップショットにないラベルを剥がす（ラベルの完全一致）。エントリの削除は行わない。</summary>
        Exact,
    }

    /// <summary>
    /// AddressableAssetSettings とスナップショット間の状態の収集・書き戻し・差分計算を行う。
    /// </summary>
    public static class AddressTellerSnapshotService
    {
        /// <summary>現在サポートしているスナップショットのスキーマバージョン。</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>現在の Addressables の状態をスナップショットとして収集する。</summary>
        public static AddressTellerSnapshot Capture(AddressableAssetSettings settings) => Capture(settings, "");

        /// <summary>
        /// 現在の Addressables の状態をスナップショットとして収集する。
        /// <paramref name="comment"/> はユーザーが付与する任意のコメントとしてそのまま記録される。
        /// </summary>
        public static AddressTellerSnapshot Capture(AddressableAssetSettings settings, string comment)
        {
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

            snapshot.Entries.Sort((a, b) => string.Compare(a.Guid, b.Guid, StringComparison.Ordinal));

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
        /// スナップショット JSON ファイルを読み込み、<see cref="AddressTellerSnapshot"/> として復元する。
        /// 読み込み・パース・内容検証のいずれかに失敗した場合は <paramref name="error"/> にメッセージを設定して false を返す。
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
            }

            snapshot = parsed;
            error = null;
            return true;
        }

        /// <summary>
        /// スナップショットの内容を Addressables へ書き戻す。
        /// スナップショットに記録されたグループが存在しない場合、そのエントリをスキップしメッセージを返す。
        /// </summary>
        public static IReadOnlyList<string> Restore(
            AddressTellerSnapshot snapshot,
            AddressableAssetSettings settings,
            SnapshotRestoreMode mode = SnapshotRestoreMode.Additive)
        {
            var issues = new List<string>();

            // FindGroup は内部で settings.groups を毎回線形探索するため、
            // ループ外で一度だけ Dictionary 化して参照する。
            var groupsByName = settings.groups
                .Where(g => g != null)
                .ToDictionary(g => g.Name, g => g);

            foreach (var entry in snapshot.Entries)
            {
                // JsonUtility はデフォルトコンストラクタ・フィールド初期化子を経由せずオブジェクトを生成するため、
                // 手動編集された/壊れたスナップショット JSON では entry 自体や GroupName/Labels が
                // null のまま渡ってくることがある。CreateOrMoveEntry の null チェックと同様、
                // ここでも1件のスキップとして扱い、復元全体を止めない。
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
        /// Undo Last Apply 専用の復元処理。<paramref name="guidsToRemove"/> に含まれる GUID のエントリを
        /// 先に削除してから <see cref="Restore"/> を Exact モードで実行する。
        /// 汎用の <see cref="Restore"/> はエントリの削除を一切行わないため（意図的な設計。管理外グループへの
        /// 誤削除を防ぐ）、Undo Last Apply のように「Apply 直前の状態へ戻す」ことが明確な文脈でのみ、
        /// 呼び出し側が所有権判定（managedGroups）で絞り込んだ GUID を渡してエントリ削除を行う。
        /// </summary>
        public static IReadOnlyList<string> RestoreExactWithRemoval(
            AddressTellerSnapshot snapshot,
            AddressableAssetSettings settings,
            IEnumerable<string> guidsToRemove)
        {
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
        /// Apply を実行せずに、適用後の状態を表すスナップショットを現在の状態との差分として計算する。
        /// </summary>
        public static DryRunResult BuildPredictedSnapshot(AddressableAssetSettings settings, IEnumerable<string> paths)
            => BuildPredictedSnapshot(settings, paths, RuleCollector.CollectEnabledRules());

        /// <summary>
        /// ルール一覧を明示的に指定する版。テストや特定スコープでの dry-run 計算に使う。
        /// </summary>
        public static DryRunResult BuildPredictedSnapshot(AddressableAssetSettings settings, IEnumerable<string> paths, IReadOnlyList<AddressRuleBase> rules)
        {
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

        /// <summary>2つのスナップショットを GUID 単位で比較し、追加・削除・変更の差分を返す。</summary>
        public static SnapshotDiff Diff(AddressTellerSnapshot before, AddressTellerSnapshot after)
        {
            var diff = new SnapshotDiff();
            var beforeMap = before.Entries.ToDictionary(e => e.Guid);
            var afterGuids = new HashSet<string>();

            foreach (var entry in after.Entries)
            {
                afterGuids.Add(entry.Guid);

                if (!beforeMap.TryGetValue(entry.Guid, out var prev))
                {
                    diff.Added.Add(entry);
                    continue;
                }

                if (prev.Address != entry.Address
                    || prev.GroupName != entry.GroupName
                    || !prev.Labels.SequenceEqual(entry.Labels))
                    diff.Changed.Add((prev, entry));
            }

            foreach (var entry in before.Entries)
                if (!afterGuids.Contains(entry.Guid))
                    diff.Removed.Add(entry);

            return diff;
        }
    }

    /// <summary>2つのスナップショット間の差分。</summary>
    public sealed class SnapshotDiff
    {
        public List<SnapshotEntry> Added { get; } = new();
        public List<SnapshotEntry> Removed { get; } = new();
        public List<(SnapshotEntry Before, SnapshotEntry After)> Changed { get; } = new();

        public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
    }

    /// <summary>
    /// <see cref="AddressTellerSnapshotService.BuildPredictedSnapshot"/> の結果。
    /// Apply を実行した場合の差分と、衝突・グループ未検出・ルール例外などの問題点をまとめて返す。
    /// </summary>
    public readonly struct DryRunResult
    {
        public SnapshotDiff Diff { get; }
        public IReadOnlyList<ValidationResult> Issues { get; }

        /// <summary>
        /// AutoCreateMissingGroups が有効な状態で、この dry-run の対象に新規作成予定のグループ名集合。
        /// Ordinal 順でソート済み。dry-run では実際の作成は行わない（副作用ゼロ）。
        /// </summary>
        public IReadOnlyList<string> GroupsToCreate { get; }

        /// <summary>
        /// Apply 適用後の予測状態の全エントリ。論理バンドル分布サマリ（<see cref="BundleDistributionCalculator"/>）など、
        /// 差分だけでなく全アセットの配置情報が必要な派生計算のために保持する。
        /// dry-run 計算経由でない構築（テスト等）では null になる場合がある。
        /// </summary>
        public AddressTellerSnapshot After { get; }

        public DryRunResult(SnapshotDiff diff, IReadOnlyList<ValidationResult> issues, IReadOnlyList<string> groupsToCreate = null, AddressTellerSnapshot after = null)
        {
            Diff = diff;
            Issues = issues;
            GroupsToCreate = groupsToCreate ?? Array.Empty<string>();
            After = after;
        }
    }
}
