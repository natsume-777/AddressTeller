using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// スナップショットファイルの保存時に使う共通処理。
    /// <see cref="AddressTellerAutoSnapshotService"/>・<see cref="AddressTellerClearSnapshotService"/>・
    /// <see cref="AddressTellerSnapshotMenu"/> の手動保存パスに重複していたロジック
    /// （ファイル名の一意化、Assets 配下判定）を集約する。
    /// </summary>
    internal static class SnapshotFileHelper
    {
        /// <summary>スナップショットファイル名の共通プレフィックス。</summary>
        public const string FilePrefix = "AddressTellerSnapshot_";

        /// <summary>スナップショットファイルの拡張子。</summary>
        public const string FileExtension = ".json";

        /// <summary>
        /// AddressTellerSnapshot_yyyyMMdd_HHmmss.json の命名規則でファイル名を組み立て、
        /// 同名が既に存在する場合は連番サフィックス _1, _2... で一意化する。
        /// </summary>
        public static string ResolveUniquePath(string folder, DateTime timestamp)
        {
            var baseName = $"{FilePrefix}{timestamp:yyyyMMdd_HHmmss}";
            var path = Path.Combine(folder, baseName + FileExtension);
            if (!File.Exists(path)) return path;

            for (var i = 1; ; i++)
            {
                var candidate = Path.Combine(folder, $"{baseName}_{i}{FileExtension}");
                if (!File.Exists(candidate)) return candidate;
            }
        }

        /// <summary>
        /// Assets 配下に保存した場合のみ AssetDatabase.Refresh() で Project ウィンドウに反映する。
        /// 境界判定は <see cref="IsInsideAssets"/> に委譲する。
        /// </summary>
        public static void RefreshIfInsideAssets(string absolutePath)
        {
            if (IsInsideAssets(absolutePath))
                AssetDatabase.Refresh();
        }

        /// <summary>
        /// <paramref name="absolutePath"/> が Application.dataPath（Assets フォルダ）配下かどうかを判定する。
        /// 比較は末尾にディレクトリ区切りを付けた「/」境界込みで行う（例えば "&lt;project&gt;/AssetsSnapshots" の
        /// ような紛らわしい名前のフォルダを、単純な StartsWith(dataPath) では誤って Assets 配下と
        /// 判定してしまうため）。<see cref="RefreshIfInsideAssets"/> から分離し、AssetDatabase を呼ばずに
        /// 境界判定そのものを単体テストできるようにしている。
        /// </summary>
        public static bool IsInsideAssets(string absolutePath)
        {
            var dataPath = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(absolutePath);
            return fullPath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
