using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>復元時にスナップショットにないラベルをどう扱うか。</summary>
    public enum SnapshotRestoreMode
    {
        /// <summary>スナップショットの Address/Label のみ書き込む。スナップショット作成後に付与されたラベルは保持する。</summary>
        Additive,

        /// <summary>スナップショットにないラベルを剥がし、スナップショットの状態に完全一致させる。</summary>
        Exact,
    }

    /// <summary>
    /// AddressableAssetSettings とスナップショット間の状態の収集・書き戻し・差分計算を行う。
    /// </summary>
    public static class AddressTellerSnapshotService
    {
        /// <summary>現在の Addressables の状態をスナップショットとして収集する。</summary>
        public static AddressTellerSnapshot Capture(AddressableAssetSettings settings)
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
            return snapshot;
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

            foreach (var entry in snapshot.Entries)
            {
                var group = settings.FindGroup(entry.GroupName);
                if (group == null)
                {
                    issues.Add($"Group '{entry.GroupName}' not found. Skipped entry '{entry.Guid}'.");
                    continue;
                }

                var assetEntry = settings.CreateOrMoveEntry(entry.Guid, group);
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

            // 重複 Order の警告は Apply 本体側で既に出るため、dry-run では出さない（二重ログ防止）。
            // プレビュー単独実行（Apply を伴わない呼び出し）では警告が出ないため、
            // 必要なら呼び出し側（UI）で RuleEvaluationPipeline.WarnOnDuplicateOrders を別途呼ぶこと。
            var setup = RuleEvaluationPipeline.BuildSetup(settings, rules);

            var issues = new List<ValidationResult>();
            var groupsToCreate = new HashSet<string>();

            foreach (var path in paths)
            {
                var ctx = RuleEvaluationPipeline.BuildContext(path);
                if (ctx == null) continue;
                if (AssetFilter.ShouldExclude(ctx, setup.ConfigFolder)) continue;

                var resolution = RuleEvaluator.Evaluate(ctx, setup.Entries);
                RuleEvaluationPipeline.AddRuleErrors(ctx, resolution, issues);

                var prediction = AddressTellerApplier.Predict(ctx, resolution, settings, setup.ExistingGroupNames, setup.ManagedGroups, setup.AutoCreateMissingGroups);

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
            return new DryRunResult(diff, issues, sortedGroupsToCreate);
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

        public DryRunResult(SnapshotDiff diff, IReadOnlyList<ValidationResult> issues, IReadOnlyList<string> groupsToCreate = null)
        {
            Diff = diff;
            Issues = issues;
            GroupsToCreate = groupsToCreate ?? Array.Empty<string>();
        }
    }
}
