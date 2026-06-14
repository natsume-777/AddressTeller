using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// AutoCreateMissingGroups が有効なときに、ルールが参照するグループを
    /// DefaultGroup のスキーマ構成を複製して自動作成するためのヘルパー。
    /// </summary>
    internal static class AddressTellerGroupFactory
    {
        /// <summary>
        /// 指定した名前のグループを取得する。存在しなければ DefaultGroup のスキーマ構成を
        /// 複製して新規作成する。DefaultGroup が取得できない、または作成中に例外が発生した
        /// 場合は false を返す（書き込みは行われない）。
        /// </summary>
        /// <param name="settings">対象の AddressableAssetSettings。</param>
        /// <param name="groupName">作成・取得したいグループ名。</param>
        /// <param name="group">成功時、取得または新規作成されたグループ。</param>
        /// <param name="failureReason">
        /// 失敗時、呼び出し元（<see cref="ValidationStatus.GroupCreationFailed"/> の Message）に
        /// 提示するための失敗理由。成功時は null。
        /// </param>
        /// <returns>取得または作成に成功した場合 true。</returns>
        public static bool EnsureGroup(AddressableAssetSettings settings, string groupName, out AddressableAssetGroup group, out string failureReason)
        {
            group = settings.FindGroup(groupName);
            if (group != null)
            {
                failureReason = null;
                return true;
            }

            var defaultGroup = settings.DefaultGroup;
            if (defaultGroup == null)
            {
                // AddressableAssetSettings.DefaultGroup は通常 null を返さない（未設定時は自動生成される）。
                // ここに到達するのは将来の Addressables 挙動変更や異常系に備えた防御コード。
                failureReason = "DefaultGroup is not available.";
                Debug.LogWarning($"[AddressTeller] Cannot auto-create group '{groupName}': {failureReason}");
                group = null;
                return false;
            }

            try
            {
                group = settings.CreateGroup(groupName, setAsDefaultGroup: false, readOnly: false, postEvent: true,
                    schemasToCopy: defaultGroup.Schemas);
                Debug.LogWarning($"[AddressTeller] Auto-created group '{groupName}' (copied from DefaultGroup '{defaultGroup.Name}').");
                failureReason = null;
                return true;
            }
            catch (System.Exception e)
            {
                // CreateGroup が例外を投げるのは通常想定されないが、将来の Addressables 挙動変更や
                // 異常系（書き込み権限不足等）に備えた防御コード。
                failureReason = e.Message;
                Debug.LogWarning($"[AddressTeller] Failed to auto-create group '{groupName}': {failureReason}");
                group = null;
                return false;
            }
        }
    }
}
