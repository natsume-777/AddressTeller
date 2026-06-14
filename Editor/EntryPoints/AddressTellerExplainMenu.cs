using System.Collections.Generic;
using UnityEditor;

namespace AddressTeller.Editor
{
    /// <summary>Project ウィンドウのコンテキストメニューから Explain ウィンドウを開く。</summary>
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
