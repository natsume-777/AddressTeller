using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace AddressTeller.Editor
{
    internal static class AssetFilter
    {
        private static readonly HashSet<string> ExcludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".js", ".boo", ".exe", ".dll", ".meta",
        };

        // Addressables 内部アセットは型で除外する。AddressableAssetGroupSchema は
        // 抽象基底なので IsAssignableFrom で継承型ごと弾く。
        private static readonly HashSet<Type> ExcludedAddressablesTypes = new HashSet<Type>
        {
            typeof(AddressableAssetSettings),
            typeof(AddressableAssetGroup),
            typeof(AddressableAssetGroupSortSettings),
        };

        public static bool ShouldExclude(AssetContext context, string addressablesConfigFolder = null)
        {
            if (ShouldExcludeByPath(context.Path, addressablesConfigFolder))
                return true;

            if (context.Type != null)
            {
                if (ExcludedAddressablesTypes.Contains(context.Type)) return true;
                if (typeof(AddressableAssetGroupSchema).IsAssignableFrom(context.Type)) return true;
            }

            return false;
        }

        /// <summary>
        /// パス文字列のみで判定できる除外条件（拡張子・/Editor/・AddressablesConfigFolder）。
        /// <see cref="AssetContext"/> の構築（GUID/型取得などの AssetDatabase 呼び出し）の前に
        /// 早期リターンするためのもの。型に基づく除外（<see cref="ExcludedAddressablesTypes"/> 等）は
        /// <see cref="AssetContext"/> 構築後に <see cref="ShouldExclude"/> で判定する。
        /// </summary>
        public static bool ShouldExcludeByPath(string path, string addressablesConfigFolder = null)
        {
            // path が null の場合、以降の判定は成立しない（安全側に倒し除外扱いとする）。
            if (path == null) return true;

            // AssetContext のコンストラクタと同じ正規化を行い、ShouldExclude と判定結果を一致させる。
            path = path.Replace('\\', '/');

            var ext = Path.GetExtension(path);
            if (ExcludedExtensions.Contains(ext))
                return true;

            if (path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // "/" 境界込みで比較する。末尾に "/" を付けずに StartsWith するだけだと、
            // 例えば configFolder="Assets/AddressableAssetsData" のとき
            // "Assets/AddressableAssetsData_Backup/Hero.prefab" のような隣接フォルダの
            // アセットまで誤って除外されてしまう。
            if (addressablesConfigFolder != null
                && path.StartsWith(addressablesConfigFolder.TrimEnd('/') + "/", StringComparison.Ordinal))
                return true;

            return false;
        }
    }
}
