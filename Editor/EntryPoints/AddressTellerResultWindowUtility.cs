using UnityEditor;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>結果ウィンドウの TreeView 間で共有する補助処理。</summary>
    internal static class AddressTellerResultWindowUtility
    {
        /// <summary>
        /// 指定パスのアセットを Project ウィンドウで ping する。
        /// パスが GUID（解決不能）またはアセットが存在しない場合は何もしない。
        /// </summary>
        public static void PingAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;

            var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset == null) return;

            EditorGUIUtility.PingObject(asset);
        }
    }
}
