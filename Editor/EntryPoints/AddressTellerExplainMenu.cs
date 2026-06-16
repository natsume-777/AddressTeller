using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;

namespace AddressTeller.Editor
{
    /// <summary>Project ウィンドウのコンテキストメニューから Explain ウィンドウ・プレビューを開く。</summary>
    public static class AddressTellerExplainMenu
    {
        [MenuItem("Assets/AddressTeller/Explain")]
        public static void Explain()
        {
            var paths = GetSelectedAssetPaths();
            var explanations = RuleExplainService.Explain(paths);
            AddressTellerExplainWindow.ShowWindow(explanations);
        }

        [MenuItem("Assets/AddressTeller/Explain", true)]
        public static bool ExplainValidate()
        {
            var guids = Selection.assetGUIDs;
            return guids != null && guids.Length > 0;
        }

        /// <summary>
        /// 選択アセット（フォルダ含む）に有効な全ルールを適用した場合の dry-run プレビューを表示する。
        /// 即時 Apply は行わない（ResultWindow から手動で Apply All / Validate を実行する）。
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
