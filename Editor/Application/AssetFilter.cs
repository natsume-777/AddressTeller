using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace AddressTeller.Editor
{
    internal static class AssetFilter
    {
        // com.unity.addressables 2.x の AddressableAssetUtility.IsPathValidForEntry（internal、
        // Editor/Settings/AddressableAssetUtility.cs）が拡張子ありパスを除外する条件と同じ集合。
        // ".preset" ".asmdef" を含めて Addressables 本体と揃えないと、Groups ウィンドウのドラッグや
        // Inspector の Addressable チェックでは登録できないファイルが AddressTeller 経由でだけ登録できてしまう。
        // 比較子も移植元と同じ既定（大文字小文字を区別する）にしている。移植元は
        // `new HashSet<string>(new[] { ".cs", ... })` と既定比較子で構築しており、Path.GetExtension の
        // 結果を ToLower 等で正規化してもいない（実装を実測確認済み）ため、".PRESET" のような大文字拡張子は
        // 移植元・AddressTeller のどちらでもこの集合に一致せず除外されない。
        private static readonly HashSet<string> ExcludedExtensions = new HashSet<string>
        {
            ".cs", ".js", ".boo", ".exe", ".dll", ".meta", ".preset", ".asmdef",
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
        /// パス文字列のみで判定できる除外条件（<see cref="IsPathValidForAddressablesEntry"/> と AddressTeller 独自の型除外の
        /// うちパスだけで判断できる部分）。<see cref="AssetContext"/> の構築（GUID/型取得などの AssetDatabase
        /// 呼び出し）の前に早期リターンするためのもの。型に基づく除外（<see cref="ExcludedAddressablesTypes"/> 等）は
        /// <see cref="AssetContext"/> 構築後に <see cref="ShouldExclude"/> で判定する。
        /// </summary>
        public static bool ShouldExcludeByPath(string path, string addressablesConfigFolder = null)
        {
            // path が null の場合、以降の判定は成立しない（安全側に倒し除外扱いとする）。
            if (path == null) return true;

            return !IsPathValidForAddressablesEntry(path, addressablesConfigFolder);
        }

        /// <summary>
        /// Addressables 本体が「このパスへのエントリ登録を受け付けるかどうか」を判定する。
        /// com.unity.addressables 2.x の AddressableAssetUtility.IsPathValidForEntry / IsPathValidPackageAsset
        /// （どちらも internal、Editor/Settings/AddressableAssetUtility.cs）を移植したもの。
        /// Groups ウィンドウへのドラッグや Inspector の Addressable チェックが拒否する対象を AddressTeller 経由でだけ
        /// 登録できてしまう逆方向の不整合を防ぐため、AddressTeller の評価対象もこれに揃える。
        /// AddressTeller のパスは常にフォワードスラッシュ区切りに正規化されているため、移植元が
        /// Path.DirectorySeparatorChar への変換を挟んでいる部分は素のフォワードスラッシュ判定に書き換えている。
        /// なお移植元は組み込みリソースパス（"library/unity editor resources" 等）も明示的に除外しているが、
        /// それらは "Assets"/有効なパッケージアセットのいずれでもないため、以下の最初のガードで既に除外される
        /// （移植元でも到達しない分岐であり、ここでは実装していない）。
        /// ConfigFolder の判定も含め、移植元と完全に同じ境界なしの前方一致（<c>path.StartsWith(configFolder)</c>）
        /// を使う。以前は隣接フォルダ（例: configFolder="Assets/AddressableAssetsData" に対する
        /// "Assets/AddressableAssetsData_Backup/Hero.prefab"）を誤って除外しないよう独自に境界付き判定へ
        /// 変更していたが、これは Addressables 本体の実際の挙動（Groups ウィンドウのドラッグ・Inspector の
        /// Addressable チェックも含め、隣接フォルダを区別せず除外する）と乖離していた。AddressTeller が「本体なら
        /// 登録できるのに拒否する／本体なら拒否するのに登録してしまう」の両方向で不整合を起こさないことを
        /// 優先し、本体の挙動へ意図的に揃えている。
        /// </summary>
        internal static bool IsPathValidForAddressablesEntry(string path, string addressablesConfigFolder)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // AssetContext のコンストラクタと同じ正規化を行い、ShouldExclude と判定結果を一致させる。
            path = path.Replace('\\', '/');

            if (!path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) && !IsValidPackageAssetPath(path))
                return false;

            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
            {
                // 拡張子なしはフォルダとして扱う。実際にフォルダかどうかはここでは検証しない
                // （移植元の Addressables もパス文字列だけで判定しており、拡張子なしのファイルは
                // 実体を問わずここではフォルダ扱いになる。実際のフォルダ判定は AssetContext.IsFolder の役割で、
                // そちらは IncludeFolders() のオプトイン判定専用に残す）。
                if (string.Equals(path, "Assets", StringComparison.Ordinal))
                    return false;

                var editorIndex = path.IndexOf("/Editor", StringComparison.OrdinalIgnoreCase);
                if (editorIndex != -1)
                {
                    // フォルダ自身が "Editor" という名前の場合（パス末尾が "/Editor"）。
                    if (editorIndex == path.Length - 7)
                        return false;
                    // "Editor" フォルダの配下にあるフォルダの場合。
                    if (path[editorIndex + 7] == '/')
                        return false;
                    // 上記2つの位置がたまたま別の名前（例: "EditorThings"）にマッチしていた場合に備えて、
                    // より深い階層に本物の "/Editor/" がないか改めて調べる。
                    if (path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
                        return false;
                }
            }
            else
            {
                if (path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
                if (ExcludedExtensions.Contains(ext))
                    return false;
            }

            // 移植元と同じ境界なしの前方一致。ConfigFolder 自身はもちろん、隣接フォルダ
            // （例: "Assets/AddressableAssetsData_Backup/Hero.prefab"）も区別せず除外する
            // （移植元がそうであるため。上記コメント参照）。
            if (addressablesConfigFolder != null
                && path.StartsWith(addressablesConfigFolder, StringComparison.Ordinal))
                return false;

            return true;
        }

        /// <summary>
        /// "Packages/&lt;name&gt;/..." で始まり、かつパッケージ直下の "package.json" 自体ではないパスかどうかを判定する。
        /// 移植元は AddressableAssetUtility.IsPathValidPackageAsset。
        /// </summary>
        private static bool IsValidPackageAssetPath(string path)
        {
            var segments = path.Split('/');
            if (segments.Length < 3)
                return false;
            if (!string.Equals(segments[0], "Packages", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.Equals(segments[2], "package.json", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
    }
}
