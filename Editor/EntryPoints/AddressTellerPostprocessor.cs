using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;

namespace Natsume777.AddressTeller.Editor
{
    public class AddressTellerPostprocessor : AssetPostprocessor
    {
        // ApplyAll 実行中に自身の変更が再トリガーしてもループしないよう再入を防ぐ
        private static bool s_isApplying;

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssets)
        {
            if (s_isApplying) return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var configFolder = settings.ConfigFolder;
            var changed = importedAssets.Concat(deletedAssets).Concat(movedAssets).ToArray();
            if (changed.Length == 0) return;
            if (changed.All(p => p.StartsWith(configFolder, System.StringComparison.Ordinal))) return;

            s_isApplying = true;
            try
            {
                AddressTellerService.ApplyAll(settings);
            }
            finally
            {
                s_isApplying = false;
            }
        }
    }
}
