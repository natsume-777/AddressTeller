using System;
using System.Collections.Generic;
using System.IO;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// アドレス付与の対象外とすべきアセットを判定する。
    /// </summary>
    public static class AssetFilter
    {
        private static readonly HashSet<string> ExcludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".js", ".boo", ".exe", ".dll", ".meta",
        };

        /// <summary>
        /// 対象外と判定した場合 true を返す。
        /// </summary>
        /// <param name="context">対象アセット。</param>
        /// <param name="addressablesConfigFolder">Addressables 設定フォルダのパス（例: "Assets/AddressableAssetsData"）。null なら省略。</param>
        public static bool ShouldExclude(AssetContext context, string addressablesConfigFolder = null)
        {
            var ext = Path.GetExtension(context.Path);
            if (ExcludedExtensions.Contains(ext))
                return true;

            if (context.Path.Contains("/Editor/"))
                return true;

            if (addressablesConfigFolder != null
                && context.Path.StartsWith(addressablesConfigFolder, StringComparison.Ordinal))
                return true;

            return false;
        }
    }
}
