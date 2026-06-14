using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// SnapshotFolder 配下のスナップショット JSON ファイルを列挙し、メタデータを読み込んで一覧化する。
    /// GUI に依存しないため、Snapshot Manager ウィンドウとテストの両方から利用できる。
    /// </summary>
    internal static class SnapshotFileCatalog
    {
        /// <summary>
        /// <paramref name="folderAbsolutePath"/> 配下の *.json を列挙し、各ファイルのメタデータを読み込む。
        /// 読み込みに失敗したファイルも <see cref="SnapshotFileInfo.LoadError"/> 付きで結果に含める（破棄しない）。
        /// フォルダが存在しない場合は空リストを返す。
        /// </summary>
        public static IReadOnlyList<SnapshotFileInfo> Collect(string folderAbsolutePath, bool includeAuto)
        {
            if (string.IsNullOrEmpty(folderAbsolutePath) || !Directory.Exists(folderAbsolutePath))
                return Array.Empty<SnapshotFileInfo>();

            var autoFolder = Path.GetFullPath(Path.Combine(folderAbsolutePath, AddressTellerAutoSnapshotService.AutoFolderName)) + Path.DirectorySeparatorChar;
            var result = new List<SnapshotFileInfo>();

            foreach (var path in Directory.GetFiles(folderAbsolutePath, "*.json", SearchOption.AllDirectories))
            {
                var isAuto = IsUnderFolder(path, autoFolder);
                if (isAuto && !includeAuto) continue;

                result.Add(BuildInfo(path, isAuto));
            }

            return result;
        }

        /// <summary><paramref name="path"/> が <paramref name="folderWithTrailingSeparator"/> 配下にあるかを判定する。</summary>
        private static bool IsUnderFolder(string path, string folderWithTrailingSeparator)
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(folderWithTrailingSeparator, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>1ファイル分のメタデータを読み込む。失敗時は LoadError にメッセージを設定する。</summary>
        private static SnapshotFileInfo BuildInfo(string path, bool isAuto)
        {
            var info = new SnapshotFileInfo
            {
                Path = Path.GetFullPath(path),
                FileName = Path.GetFileName(path),
                IsAuto = isAuto,
            };

            if (!AddressTellerSnapshotService.LoadFromFile(path, out var snapshot, out var error))
            {
                info.LoadError = error;
                return info;
            }

            info.CapturedAtIso = snapshot.CapturedAtIso ?? "";
            info.Comment = snapshot.Comment ?? "";
            info.SchemaVersion = snapshot.SchemaVersion;
            info.EntryCount = snapshot.Entries?.Count ?? 0;
            return info;
        }

        /// <summary>
        /// CapturedAtIso の降順（新しい順）でソートする。CapturedAtIso が空文字（旧形式）の項目は末尾に来る。
        /// 同値時は FileName の Ordinal 昇順で決定的にする。
        /// </summary>
        public static IReadOnlyList<SnapshotFileInfo> SortByCapturedDesc(IEnumerable<SnapshotFileInfo> items)
        {
            return items
                .OrderBy(i => string.IsNullOrEmpty(i.CapturedAtIso) ? 1 : 0)
                .ThenByDescending(i => i.CapturedAtIso, StringComparer.Ordinal)
                .ThenBy(i => i.FileName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// FileName または Comment に <paramref name="keyword"/> が大小文字無視で部分一致する項目のみを返す。
        /// keyword が null/空の場合は <paramref name="items"/> をそのまま返す。
        /// </summary>
        public static IReadOnlyList<SnapshotFileInfo> Filter(IEnumerable<SnapshotFileInfo> items, string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return items.ToList();

            return items
                .Where(i =>
                    i.FileName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                    || i.Comment.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }
    }

    /// <summary>スナップショット1ファイル分の表示用メタデータ。</summary>
    internal sealed class SnapshotFileInfo
    {
        /// <summary>絶対パス。</summary>
        public string Path { get; set; } = "";

        public string FileName { get; set; } = "";

        /// <summary>取得日時（UTC、ISO 8601 形式）。旧形式や読込失敗時は空文字。</summary>
        public string CapturedAtIso { get; set; } = "";

        public string Comment { get; set; } = "";

        public int SchemaVersion { get; set; }

        public int EntryCount { get; set; }

        /// <summary>SnapshotFolder/Auto 配下のファイルかどうか。</summary>
        public bool IsAuto { get; set; }

        /// <summary>読込に失敗した場合のエラーメッセージ。正常時は null。</summary>
        public string LoadError { get; set; }
    }
}
