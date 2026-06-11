using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// AddressResolution を受け取り、検証または Addressables への書き込みを行う。
    /// </summary>
    public static class AddressTellerApplier
    {
        /// <summary>
        /// 書き込みは行わず、検証結果だけを返す。
        /// グループ存在チェックはテスト可能な existingGroupNames で行う。
        /// </summary>
        public static ValidationResult Validate(
            AssetContext context,
            AddressResolution resolution,
            IEnumerable<string> existingGroupNames)
        {
            if (resolution.AddressCandidates.Count == 0)
                return new ValidationResult(context, ValidationStatus.Skipped, "No matching rule.");

            if (resolution.AddressCandidates.Count > 1)
            {
                var sb = new StringBuilder();
                sb.Append($"Address conflict for '{context.Path}':");
                foreach (var c in resolution.AddressCandidates)
                    sb.Append($"\n  {c.DescribeSource()} [{c.GroupName}] → \"{c.Address}\"");

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

            if (!existingGroupNames.Contains(candidate.GroupName))
            {
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
        public static ValidationResult Apply(
            AssetContext context,
            AddressResolution resolution,
            AddressableAssetSettings settings,
            IEnumerable<string> existingGroupNames,
            IReadOnlyCollection<string> managedGroups = null)
        {
            var result = Validate(context, resolution, existingGroupNames);

            if (result.Status == ValidationStatus.Skipped)
            {
                // ルール例外があった場合は「全ルールが正常評価された上でマッチ0件」とは言えないため、
                // クリーンアップは行わない(ルールのバグで誤ってエントリを削除しないようにする)。
                if (resolution.Errors.Count == 0
                    && managedGroups != null && AddressTellerSettings.CleanupStaleEntries)
                    RemoveStaleEntryIfManaged(context.Guid, settings, managedGroups);
                return result;
            }

            if (!result.IsOk)
                return result;

            var candidate = resolution.AddressCandidates[0];
            var group = settings.FindGroup(candidate.GroupName);
            var entry = settings.CreateOrMoveEntry(context.Guid, group);
            entry.SetAddress(candidate.Address);

            foreach (var label in resolution.Labels)
            {
                settings.AddLabel(label);
                entry.SetLabel(label, true);
            }

            return result;
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
