using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// アセットインポート時に自動でルールを適用する。
    /// </summary>
    public class AddressTellerPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssets)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            AddressTellerService.ApplyAll(settings);
        }
    }
}
