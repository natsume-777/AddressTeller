using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    public static class AddressTellerMenu
    {
        [MenuItem("Tools/AddressTeller/Apply All")]
        public static void ApplyAll()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。Addressables を初期化してください。");
                return;
            }

            var issues = AddressTellerService.ApplyAll(settings);
            if (issues.Count == 0)
            {
                Debug.Log("[AddressTeller] ApplyAll が完了しました。");
                return;
            }

            foreach (var issue in issues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            Debug.LogError($"[AddressTeller] ApplyAll 完了: {issues.Count} 件の問題が見つかりました。");
        }

        [MenuItem("Tools/AddressTeller/Validate")]
        public static void Validate()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。Addressables を初期化してください。");
                return;
            }

            var issues = AddressTellerService.ValidateAll(settings);
            if (issues.Count == 0)
            {
                Debug.Log("[AddressTeller] Validate 完了: 問題なし。");
                return;
            }

            foreach (var issue in issues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            Debug.LogError($"[AddressTeller] Validate 完了: {issues.Count} 件の問題が見つかりました。");
        }

        /// <summary>CI 向け。-executeMethod Natsume777.AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI で実行。</summary>
        public static void ApplyAllCLI()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。");
                EditorApplication.Exit(1);
                return;
            }

            var issues = AddressTellerService.ApplyAll(settings);
            foreach (var issue in issues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            EditorApplication.Exit(issues.Count == 0 ? 0 : 1);
        }
    }
}
