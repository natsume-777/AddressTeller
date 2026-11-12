using System.Collections.Generic;
using UnityEditor;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Core の <see cref="FolderGuidResolver"/> へ、AssetDatabase による GUID → フォルダパス解決を注入する。
    /// <see cref="Match.InFolderByGuid"/> の述語は評価のたびにここを引くため、毎資産の評価で
    /// AssetDatabase を叩かないよう結果をキャッシュする。キャッシュは、規則の評価ラン
    /// （<see cref="DeclaredFolderInspector.ResolveGuids(IReadOnlyList{AddressRuleEntry}, List{ValidationResult})"/>
    /// の冒頭）と、import のたび（Postprocessor）に捨てる。
    /// </summary>
    [InitializeOnLoad]
    internal static class AssetDatabaseFolderGuidResolver
    {
        /// <summary>guid → 解決結果（フォルダに引けなければ null）。</summary>
        private static readonly Dictionary<string, string> s_cache = new Dictionary<string, string>();

        static AssetDatabaseFolderGuidResolver()
        {
            FolderGuidResolver.Resolver = ResolveCached;
        }

        /// <summary>キャッシュを捨てる。</summary>
        internal static void ClearCache() => s_cache.Clear();

        /// <summary>キャッシュ経由で解決する。</summary>
        internal static string ResolveCached(string guid)
        {
            if (!s_cache.TryGetValue(guid, out var path))
            {
                path = ResolveUncached(guid);
                s_cache[guid] = path;
            }

            return path;
        }

        /// <summary>GUID がフォルダに引ければその現在のパス（"/" 区切り）、引けなければ null。</summary>
        private static string ResolveUncached(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) return null;
            return path.Replace('\\', '/');
        }
    }
}
