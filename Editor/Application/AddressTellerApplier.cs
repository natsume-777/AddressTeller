using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>Predict が返す、適用後のアセットの予測される変化の種類。</summary>
    internal enum PredictedAction
    {
        /// <summary>エントリが新規追加または更新される（現状と内容が一致している場合も含む）。</summary>
        AddOrUpdate,

        /// <summary>エントリが削除される（CleanupStaleEntries による）。</summary>
        Remove,

        /// <summary>Apply を実行しても Addressables の状態に変化はない（Validate が Ok 以外、または削除対象外の Skipped）。</summary>
        NoOp,
    }

    /// <summary>
    /// 書き込みを行わずに Apply 実行後の状態を予測した結果。
    /// </summary>
    internal readonly struct ApplyPrediction
    {
        public PredictedAction Action { get; }
        public ValidationResult Validation { get; }

        /// <summary>Action が AddOrUpdate のときのみ意味を持つ、適用後のエントリ状態。</summary>
        public SnapshotEntry PredictedEntry { get; }

        /// <summary>Action が Remove のときのみ意味を持つ、削除元のグループ名。</summary>
        public string RemovedFromGroup { get; }

        public ApplyPrediction(PredictedAction action, ValidationResult validation, SnapshotEntry predictedEntry, string removedFromGroup)
        {
            Action = action;
            Validation = validation;
            PredictedEntry = predictedEntry;
            RemovedFromGroup = removedFromGroup;
        }
    }

    /// <summary>
    /// AddressResolution を受け取り、検証または Addressables への書き込みを行う。
    /// </summary>
    internal static class AddressTellerApplier
    {
        /// <summary>
        /// 書き込みは行わず、検証結果だけを返す。
        /// グループ存在チェックはテスト可能な existingGroupNames で行う。
        /// </summary>
        /// <param name="autoCreateMissingGroups">
        /// <see cref="AddressTellerSettings.AutoCreateMissingGroups"/> の値。true の場合、未存在グループは
        /// <see cref="ValidationStatus.GroupNotFound"/> ではなく <see cref="ValidationStatus.GroupWillBeCreated"/>
        /// として返す（Validate/Predict では実際の作成は行わない）。
        /// </param>
        public static ValidationResult Validate(
            AssetContext context,
            AddressResolution resolution,
            IEnumerable<string> existingGroupNames,
            bool autoCreateMissingGroups = false)
        {
            if (resolution.AddressCandidates.Count == 0)
                return new ValidationResult(context, ValidationStatus.Skipped, "No matching rule.");

            if (resolution.AddressCandidates.Count > 1)
            {
                var sb = new StringBuilder();
                sb.Append($"Address conflict for '{context.Path}':");
                foreach (var c in resolution.AddressCandidates)
                    sb.Append($"\n  {c.DescribeSource()} [{AddressRuleBuilderImpl.DisplayGroupName(c.GroupName)}] → \"{c.Address}\"");

                return new ValidationResult(
                    context,
                    ValidationStatus.ConflictingAddress,
                    sb.ToString(),
                    resolution.AddressCandidates);
            }

            var candidate = resolution.AddressCandidates[0];
            if (string.IsNullOrEmpty(candidate.Address))
            {
                return new ValidationResult(
                    context,
                    ValidationStatus.InvalidAddress,
                    $"{candidate.DescribeSource()} returned a null/empty address for '{context.Path}'.");
            }

            // RuleEvaluationPipeline.BuildSetup が DefaultGroup を取得できなかった場合、
            // GroupDefault() を使うエントリのグループ名はセンチネルのまま残っている。
            // existingGroupNames には存在しないため、GroupNotFound より優先してここで報告する。
            if (candidate.GroupName == AddressRuleBuilderImpl.DefaultGroupSentinel)
            {
                return new ValidationResult(
                    context,
                    ValidationStatus.DefaultGroupUnavailable,
                    $"{candidate.DescribeSource()} uses GroupDefault(), but AddressableAssetSettings.DefaultGroup could not be resolved for '{context.Path}'.");
            }

            if (!existingGroupNames.Contains(candidate.GroupName))
            {
                if (autoCreateMissingGroups)
                {
                    return new ValidationResult(
                        context,
                        ValidationStatus.GroupWillBeCreated,
                        $"Group '{candidate.GroupName}' does not exist and will be created from DefaultGroup on Apply.");
                }

                return new ValidationResult(
                    context,
                    ValidationStatus.GroupNotFound,
                    $"Group '{candidate.GroupName}' not found in AddressableAssetSettings.");
            }

            return new ValidationResult(context, ValidationStatus.Ok, null);
        }

        /// <summary>
        /// 検証を行い、問題なければ Addressables へ書き込む。
        /// </summary>
        /// <param name="existingGroupNames">
        /// settings.groups から事前に構築したグループ名の集合（呼び出し側でループ外に1回だけ構築する想定）。
        /// </param>
        /// <param name="managedGroups">
        /// AddressTeller のいずれかのルールが GroupName として参照しているグループ名の集合。
        /// 指定された場合、どのルールにもマッチしなくなった（Skipped な）アセットが
        /// この中のグループに属していれば、<see cref="AddressTellerSettings.CleanupStaleEntries"/>
        /// が true のときエントリを削除する。
        /// </param>
        /// <param name="autoCreateMissingGroups">
        /// <see cref="AddressTellerSettings.AutoCreateMissingGroups"/> の値。true の場合、未存在グループを
        /// <see cref="AddressTellerGroupFactory.EnsureGroup"/> で DefaultGroup から複製して作成し、
        /// 成功すればそのまま書き込みを続行する。失敗した場合は <see cref="ValidationStatus.GroupCreationFailed"/>
        /// を返し書き込みは行わない。
        /// </param>
        public static ValidationResult Apply(
            AssetContext context,
            AddressResolution resolution,
            AddressableAssetSettings settings,
            IEnumerable<string> existingGroupNames,
            IReadOnlyCollection<string> managedGroups = null,
            bool autoCreateMissingGroups = false)
        {
            var result = Validate(context, resolution, existingGroupNames, autoCreateMissingGroups);

            if (result.Status == ValidationStatus.Skipped)
            {
                // ルール例外があった場合は「全ルールが正常評価された上でマッチ0件」とは言えないため、
                // クリーンアップは行わない(ルールのバグで誤ってエントリを削除しないようにする)。
                if (resolution.Errors.Count == 0
                    && managedGroups != null && AddressTellerSettings.CleanupStaleEntries)
                    RemoveStaleEntryIfManaged(context.Guid, settings, managedGroups);
                return result;
            }

            AddressableAssetGroup group;
            if (result.Status == ValidationStatus.GroupWillBeCreated)
            {
                // Validate では作成しないが、Apply ではここで実際に DefaultGroup を複製して作成する。
                var candidateForCreate = resolution.AddressCandidates[0];
                if (!AddressTellerGroupFactory.EnsureGroup(settings, candidateForCreate.GroupName, out group, out var failureReason))
                {
                    return new ValidationResult(
                        context,
                        ValidationStatus.GroupCreationFailed,
                        $"Failed to auto-create group '{candidateForCreate.GroupName}' for '{context.Path}': {failureReason}");
                }
            }
            else if (!result.IsOk)
            {
                return result;
            }
            else
            {
                var candidate = resolution.AddressCandidates[0];
                group = settings.FindGroup(candidate.GroupName);
            }

            var entry = settings.CreateOrMoveEntry(context.Guid, group);
            entry.SetAddress(resolution.AddressCandidates[0].Address);

            foreach (var label in resolution.Labels)
            {
                settings.AddLabel(label);
                entry.SetLabel(label, true);
            }

            return result;
        }

        /// <summary>
        /// 書き込みを行わず、<see cref="Apply"/> を実行した場合にこのアセットがどう変化するかを予測する。
        /// 判定分岐は Apply / Validate と1対1で対応させている。
        /// </summary>
        /// <param name="existingGroupNames">settings.groups から事前に構築したグループ名の集合。</param>
        /// <param name="managedGroups">AddressTeller のいずれかのルールが GroupName として参照しているグループ名の集合。</param>
        /// <param name="autoCreateMissingGroups">
        /// <see cref="AddressTellerSettings.AutoCreateMissingGroups"/> の値。true の場合、未存在グループは
        /// <see cref="ValidationStatus.GroupNotFound"/> ではなく <see cref="ValidationStatus.GroupWillBeCreated"/>
        /// となり（IsOk=true）、AddOrUpdate として予測される（実際の作成は行わない）。
        /// </param>
        public static ApplyPrediction Predict(
            AssetContext context,
            AddressResolution resolution,
            AddressableAssetSettings settings,
            HashSet<string> existingGroupNames,
            HashSet<string> managedGroups,
            bool autoCreateMissingGroups = false)
        {
            var result = Validate(context, resolution, existingGroupNames, autoCreateMissingGroups);

            if (result.Status == ValidationStatus.Skipped)
            {
                var entry = settings.FindAssetEntry(context.Guid);
                if (entry?.parentGroup != null
                    && managedGroups.Contains(entry.parentGroup.Name)
                    && AddressTellerSettings.CleanupStaleEntries
                    && resolution.Errors.Count == 0)
                {
                    return new ApplyPrediction(PredictedAction.Remove, result, null, entry.parentGroup.Name);
                }

                return new ApplyPrediction(PredictedAction.NoOp, result, null, null);
            }

            if (!result.IsOk)
                return new ApplyPrediction(PredictedAction.NoOp, result, null, null);

            var candidate = resolution.AddressCandidates[0];
            var existingEntry = settings.FindAssetEntry(context.Guid);

            // 実際の Apply は SetLabel(label, true) で加算するのみで既存ラベルを剥がさないため、
            // 予測も既存ラベルと resolution.Labels の和集合とする。
            var labels = new HashSet<string>(resolution.Labels);
            if (existingEntry != null)
                foreach (var existingLabel in existingEntry.labels)
                    labels.Add(existingLabel);

            var predictedEntry = new SnapshotEntry
            {
                Guid = context.Guid,
                Address = candidate.Address,
                GroupName = candidate.GroupName,
                Labels = labels.OrderBy(l => l, StringComparer.Ordinal).ToList(),
            };

            return new ApplyPrediction(PredictedAction.AddOrUpdate, result, predictedEntry, null);
        }

        /// <summary>
        /// 削除されたアセットの GUID に対応するエントリが AddressTeller 管理下のグループに
        /// 属している場合のみ削除する。管理外グループ（ユーザーが手動で登録したエントリ等）には触れない。
        /// <see cref="AddressTellerSettings.CleanupStaleEntries"/> が true のときのみ削除する。
        /// ルール評価を伴わない（資産が既に存在しない）削除専用のエントリポイント。
        /// </summary>
        public static void RemoveEntryForDeletedAsset(
            string guid,
            AddressableAssetSettings settings,
            IReadOnlyCollection<string> managedGroups)
        {
            if (managedGroups == null) return;
            if (!AddressTellerSettings.CleanupStaleEntries) return;

            RemoveStaleEntryIfManaged(guid, settings, managedGroups);
        }

        /// <summary>
        /// 既存のエントリが AddressTeller 管理下のグループに属している場合のみ削除する。
        /// 管理外グループ（ユーザーが手動で登録したエントリ等）には触れない。
        /// </summary>
        private static void RemoveStaleEntryIfManaged(
            string guid,
            AddressableAssetSettings settings,
            IReadOnlyCollection<string> managedGroups)
        {
            var entry = settings.FindAssetEntry(guid);
            if (entry?.parentGroup == null) return;
            if (!managedGroups.Contains(entry.parentGroup.Name)) return;

            Debug.LogWarning($"[AddressTeller] Removing stale entry: guid={guid}, group='{entry.parentGroup.Name}', address='{entry.address}' (no longer matched by any rule).");
            settings.RemoveAssetEntry(guid);
        }
    }
}
