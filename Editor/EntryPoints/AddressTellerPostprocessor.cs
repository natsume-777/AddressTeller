using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    public class AddressTellerPostprocessor : AssetPostprocessor
    {
        // 自身が ApplyAll() で書き込んだ変更が OnPostprocessAllAssets を再トリガーしても
        // 無限ループにならないようにするための再入ガード。
        // Service.s_isApplying は Menu/CLI 経由の呼び出しでの再入を防ぐ別ガードで、
        // Postprocessor から呼ばれた場合はそちらが false のままのため、両方が必要。
        private static bool s_isApplying;

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssets)
        {
            if (s_isApplying) return;
            if (!AddressTellerSettings.PostprocessEnabled) return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var configFolder = settings.ConfigFolder;
            var changed = importedAssets.Concat(deletedAssets).Concat(movedAssets).ToArray();
            if (changed.Length == 0) return;
            if (changed.All(p => p.StartsWith(configFolder, System.StringComparison.Ordinal))) return;

            s_isApplying = true;
            try
            {
                var issues = AddressTellerService.ApplyAll(settings);
                foreach (var issue in issues)
                    Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");
            }
            finally
            {
                s_isApplying = false;
            }
        }
    }
}
