using System;
using System.IO;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Tools/AddressTeller/Apply All・Apply with Validate メニューの実行直前に保存する
    /// 自動スナップショット（SnapshotFolder/Auto 以下）の保存・ローテーション・読み込みを担う。
    /// 既存の <see cref="AddressTellerSnapshot"/>/<see cref="AddressTellerSnapshotService"/> を再利用する。
    /// </summary>
    internal static class AddressTellerAutoSnapshotService
    {
        /// <summary>SnapshotFolder 配下の自動スナップショット用サブフォルダ名。SnapshotFileCatalog から自動判定にも使う。</summary>
        internal const string AutoFolderName = "Auto";

        /// <summary>SnapshotFolder/Auto の絶対パスを返す。フォルダがなければ作成する。</summary>
        public static string GetAutoSnapshotFolder()
        {
            var path = Path.Combine(AddressTellerSettings.GetSnapshotFolderAbsolutePath(), AutoFolderName);
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// 現在の Addressables の状態をキャプチャし、SnapshotFolder/Auto 以下に保存する。
        /// 保存後、<see cref="AddressTellerSettings.AutoSnapshotRetention"/> を超える古いファイルを削除する。
        /// 例外発生時はログを出力して null を返す。
        /// </summary>
        /// <returns>保存先の絶対パス。失敗時は null。</returns>
        public static string CaptureAndSave(AddressableAssetSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AutoSnapshot: AddressableAssetSettings is null.");
                return null;
            }

            try
            {
                var folder = GetAutoSnapshotFolder();
                var snapshot = AddressTellerSnapshotService.Capture(settings);
                var path = SnapshotFileHelper.ResolveUniquePath(folder, DateTime.Now);

                File.WriteAllText(path, snapshot.ToJson());
                SnapshotFileHelper.RefreshIfInsideAssets(path);

                Rotate(AddressTellerSettings.AutoSnapshotRetention);

                return path;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AddressTeller] AutoSnapshot: Failed to save auto snapshot: {e}");
                return null;
            }
        }

        /// <summary>
        /// SnapshotFolder/Auto 配下の AddressTellerSnapshot_*.json のうち、retention 件を超える
        /// 古いファイルを削除する。命名パターンに一致しないファイルは対象外。
        /// </summary>
        public static void Rotate(int retention)
        {
            var files = EnumerateAutoSnapshotFiles();
            if (files.Count <= retention) return;

            var toDelete = files.Skip(retention).ToList();
            foreach (var file in toDelete)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AddressTeller] AutoSnapshot: Failed to delete old snapshot: {file} ({e.Message})");
                }
            }

            if (toDelete.Count > 0)
                Debug.Log($"[AddressTeller] AutoSnapshot: Deleted {toDelete.Count} old auto snapshot(s) exceeding the retention limit of {retention}.");
        }

        /// <summary>最新の自動スナップショットの絶対パスを返す。存在しなければ null。</summary>
        public static string FindLatestAuto()
        {
            return EnumerateAutoSnapshotFiles().FirstOrDefault();
        }

        /// <summary>
        /// 自動スナップショットファイルを読み込み、<see cref="AddressTellerSnapshot"/> として復元する。
        /// 読み込み・パースに失敗した場合は警告をログ出力して null を返す。
        /// </summary>
        public static AddressTellerSnapshot LoadAuto(string path)
        {
            if (!AddressTellerSnapshotService.LoadFromFile(path, out var snapshot, out var error))
            {
                Debug.LogWarning($"[AddressTeller] AutoSnapshot: {error}");
                return null;
            }

            return snapshot;
        }

        /// <summary>
        /// SnapshotFolder/Auto 配下の AddressTellerSnapshot_*.json を、最終更新日時の降順
        /// （同値時はファイル名で Ordinal 比較した降順）で決定的にソートして返す。
        /// Auto フォルダが存在しない場合は空リストを返す。
        /// </summary>
        private static System.Collections.Generic.List<string> EnumerateAutoSnapshotFiles()
        {
            var folder = Path.Combine(AddressTellerSettings.GetSnapshotFolderAbsolutePath(), AutoFolderName);
            if (!Directory.Exists(folder)) return new System.Collections.Generic.List<string>();

            return Directory.GetFiles(folder, $"{SnapshotFileHelper.FilePrefix}*{SnapshotFileHelper.FileExtension}")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(Path.GetFileName, StringComparer.Ordinal)
                .ToList();
        }
    }
}
