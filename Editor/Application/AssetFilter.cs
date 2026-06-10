using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Natsume777.AddressTeller.Editor
{
    public static class AssetFilter
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
            var ext = Path.GetExtension(context.Path);
            if (ExcludedExtensions.Contains(ext))
                return true;

            if (context.Path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (addressablesConfigFolder != null
                && context.Path.StartsWith(addressablesConfigFolder, StringComparison.Ordinal))
                return true;

            if (context.Type != null)
            {
                if (ExcludedAddressablesTypes.Contains(context.Type)) return true;
                if (typeof(AddressableAssetGroupSchema).IsAssignableFrom(context.Type)) return true;
            }

            return false;
        }
    }
}
