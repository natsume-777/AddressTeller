using System;
using System.IO;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Clear All Addresses &amp; Labels（<see cref="AddressTellerClearService"/>）の実行直前に保存する
    /// 専用スナップショット（SnapshotFolder/Clear 以下）の保存を担う。
    /// 既存の <see cref="AddressTellerSnapshot"/>/<see cref="AddressTellerSnapshotService"/> を再利用する。
    /// Auto スナップショットと異なりローテーションは行わない（全件保持）。
    /// </summary>
    internal static class AddressTellerClearSnapshotService
    {
        /// <summary>SnapshotFolder 配下の Clear 専用スナップショット用サブフォルダ名。</summary>
        internal const string ClearFolderName = "Clear";

        /// <summary>SnapshotFolder/Clear の絶対パスを返す。フォルダがなければ作成する。</summary>
        public static string GetClearSnapshotFolder()
        {
            var path = Path.Combine(AddressTellerSettings.GetSnapshotFolderAbsolutePath(), ClearFolderName);
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// 現在の Addressables の状態をキャプチャし、SnapshotFolder/Clear 以下に保存する。
        /// ローテーションは行わない（全件保持）。
        /// </summary>
        /// <returns>保存先の絶対パス。失敗時は null（<paramref name="error"/> にメッセージを設定する）。</returns>
        public static string CaptureAndSave(AddressableAssetSettings settings, out string error)
        {
            if (settings == null)
            {
                error = "AddressableAssetSettings is null.";
                return null;
            }

            try
            {
                var folder = GetClearSnapshotFolder();
                var snapshot = AddressTellerSnapshotService.Capture(settings, "Before Clear All Addresses & Labels");
                var path = SnapshotFileHelper.ResolveUniquePath(folder, DateTime.Now);

                File.WriteAllText(path, snapshot.ToJson());
                SnapshotFileHelper.RefreshIfInsideAssets(path);

                error = null;
                return path;
            }
            catch (Exception e)
            {
                error = $"Failed to save the Clear snapshot: {e}";
                return null;
            }
        }
    }
}
