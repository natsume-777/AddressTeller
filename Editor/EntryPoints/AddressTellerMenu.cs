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

        /// <summary>Validate で問題が見つかった場合は Apply を中止する。</summary>
        [MenuItem("Tools/AddressTeller/Apply with Validate")]
        public static void ApplyWithValidate()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。Addressables を初期化してください。");
                return;
            }

            var validateIssues = AddressTellerService.ValidateAll(settings);
            if (validateIssues.Count > 0)
            {
                foreach (var issue in validateIssues)
                    Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

                Debug.LogError($"[AddressTeller] Validate で {validateIssues.Count} 件の問題が見つかったため、Apply を中止しました。");
                return;
            }

            var applyIssues = AddressTellerService.ApplyAll(settings);
            if (applyIssues.Count == 0)
            {
                Debug.Log("[AddressTeller] ApplyWithValidate が完了しました。");
                return;
            }

            foreach (var issue in applyIssues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            Debug.LogError($"[AddressTeller] ApplyWithValidate 完了: {applyIssues.Count} 件の問題が見つかりました。");
        }

        /// <summary>CI 向け。-executeMethod Natsume777.AddressTeller.Editor.AddressTellerMenu.ApplyWithValidateCLI で実行。</summary>
        public static void ApplyWithValidateCLI()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。");
                EditorApplication.Exit(1);
                return;
            }

            var validateIssues = AddressTellerService.ValidateAll(settings);
            if (validateIssues.Count > 0)
            {
                foreach (var issue in validateIssues)
                    Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

                EditorApplication.Exit(1);
                return;
            }

            var applyIssues = AddressTellerService.ApplyAll(settings);
            foreach (var issue in applyIssues)
                Debug.LogError($"[AddressTeller] {issue.Status}: {issue.Message}");

            EditorApplication.Exit(applyIssues.Count == 0 ? 0 : 1);
        }
    }
}
