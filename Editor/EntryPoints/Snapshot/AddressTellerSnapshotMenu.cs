using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>Tools/AddressTeller/Snapshot 以下のメニュー。スナップショットの保存・復元・比較を行う。</summary>
    public static class AddressTellerSnapshotMenu
    {
        [MenuItem("Tools/AddressTeller/Snapshot/Save Snapshot")]
        public static void SaveSnapshot()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found. Please initialize Addressables.");
                return;
            }

            var snapshot = AddressTellerSnapshotService.Capture(settings);
            var fileName = $"AddressTellerSnapshot_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var folder = GetSnapshotFolder();
            var path = Path.Combine(folder, fileName);

            File.WriteAllText(path, snapshot.ToJson());
            RefreshIfInsideAssets(path);

            Debug.Log($"[AddressTeller] Snapshot saved: {path} ({snapshot.Entries.Count} entries)");
        }

        /// <summary>
        /// 最新の自動スナップショット（Apply All / Apply with Validate メニューの実行直前に保存されたもの）から、
        /// Addressables の状態を Exact モードで復元する。Apply によるエントリ削除・アドレス変更等の Undo として使う。
        /// </summary>
        [MenuItem("Tools/AddressTeller/Undo Last Apply")]
        public static void UndoLastApply()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found. Please initialize Addressables.");
                return;
            }

            var path = AddressTellerAutoSnapshotService.FindLatestAuto();
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[AddressTeller] No auto snapshot found. Run Apply All or Apply with Validate once to create one.");
                return;
            }

            var snapshot = AddressTellerAutoSnapshotService.LoadAuto(path);
            if (snapshot == null) return;

            var current = AddressTellerSnapshotService.Capture(settings);
            var diff = AddressTellerSnapshotService.Diff(current, snapshot);
            if (diff.IsEmpty)
            {
                Debug.Log("[AddressTeller] No changes since the last Apply.");
                return;
            }

            var message =
                $"Restore state before the last Apply ({Path.GetFileName(path)}).\n\n" +
                $"Entries to add: {diff.Added.Count}\n" +
                $"Entries to remove: {diff.Removed.Count}\n" +
                $"Entries to change: {diff.Changed.Count}\n\n" +
                "Restores in Exact mode; labels added after the snapshot was taken may be removed.";

            if (!EditorUtility.DisplayDialog("Undo Last Apply", message, "Restore", "Cancel"))
                return;

            var issues = AddressTellerSnapshotService.Restore(snapshot, settings, SnapshotRestoreMode.Exact);
            foreach (var issue in issues)
                Debug.LogWarning($"[AddressTeller] {issue}");

            Debug.Log($"[AddressTeller] Last Apply undone ({Path.GetFileName(path)}): {snapshot.Entries.Count} entries, {issues.Count} issue(s)");
        }

        /// <summary>AddressTellerSettings.SnapshotFolder を絶対パスに解決し、フォルダがなければ作成する。</summary>
        private static string GetSnapshotFolder()
        {
            var path = AddressTellerSettings.GetSnapshotFolderAbsolutePath();
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>Assets 配下に保存した場合のみ AssetDatabase.Refresh() で Project ウィンドウに反映する。</summary>
        private static void RefreshIfInsideAssets(string absolutePath)
        {
            var dataPath = Path.GetFullPath(Application.dataPath);
            if (absolutePath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                AssetDatabase.Refresh();
        }
    }
}
