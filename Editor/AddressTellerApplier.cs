using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

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
                return new ValidationResult(
                    context,
                    ValidationStatus.ConflictingAddress,
                    $"Address conflict: {resolution.AddressCandidates.Count} rules matched for '{context.Path}'.",
                    resolution.AddressCandidates);
            }

            var candidate = resolution.AddressCandidates[0];
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
        public static ValidationResult Apply(
            AssetContext context,
            AddressResolution resolution,
            AddressableAssetSettings settings)
        {
            var groupNames = settings.groups.Select(g => g.Name);
            var result = Validate(context, resolution, groupNames);

            if (!result.IsOk || result.Status == ValidationStatus.Skipped)
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
    }
}
