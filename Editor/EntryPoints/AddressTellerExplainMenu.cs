using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;

namespace AddressTeller.Editor
{
    /// <summary>Opens the Explain window / preview from the Project window's context menu.</summary>
    public static class AddressTellerExplainMenu
    {
        /// <summary>Opens the Explain window showing which rule (if any) matched each selected asset, and why.</summary>
        [MenuItem("Assets/AddressTeller/Explain")]
        public static void Explain()
        {
            var paths = GetSelectedAssetPaths();
            var explanations = RuleExplainService.Explain(paths, null, RuleCollector.CollectEnabledRules(), out var configureFailures);
            AddressTellerExplainWindow.ShowWindow(explanations, configureFailures);
        }

        /// <summary>Unity menu validate function for <see cref="Explain"/>: enabled only when at least one asset is selected.</summary>
        [MenuItem("Assets/AddressTeller/Explain", true)]
        public static bool ExplainValidate()
        {
            var guids = Selection.assetGUIDs;
            return guids != null && guids.Length > 0;
        }

        /// <summary>
        /// Shows a dry-run preview of applying all enabled rules to the selected assets (including
        /// folders). Does not Apply immediately (run Apply All / Validate manually from the ResultWindow).
        /// </summary>
        [MenuItem("Assets/AddressTeller/Preview (Apply Preview)")]
        public static void Preview()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                UnityEngine.Debug.LogError("[AddressTeller] AddressableAssetSettings not found. Please initialize Addressables.");
                return;
            }

            var paths = GetSelectedAssetPaths();
            AddressTellerScopedPreview.RunAssetPreview(settings, paths);
        }

        /// <summary>Unity menu validate function for <see cref="Preview"/>: enabled only when at least one asset is selected.</summary>
        [MenuItem("Assets/AddressTeller/Preview (Apply Preview)", true)]
        public static bool PreviewValidate()
        {
            var guids = Selection.assetGUIDs;
            return guids != null && guids.Length > 0;
        }

        private static List<string> GetSelectedAssetPaths()
        {
            var guids = Selection.assetGUIDs;
            if (guids == null) return new List<string>();

            var paths = new List<string>(guids.Length);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }

            return paths;
        }
    }
}
