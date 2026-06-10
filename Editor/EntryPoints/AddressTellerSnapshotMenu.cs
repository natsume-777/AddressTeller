using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
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
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。Addressables を初期化してください。");
                return;
            }

            var snapshot = AddressTellerSnapshotService.Capture(settings);
            var fileName = $"AddressTellerSnapshot_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var folder = GetSnapshotFolder();
            var path = Path.Combine(folder, fileName);

            File.WriteAllText(path, snapshot.ToJson());
            RefreshIfInsideAssets(path);

            Debug.Log($"[AddressTeller] スナップショットを保存しました: {path}（{snapshot.Entries.Count} 件）");
        }

        [MenuItem("Tools/AddressTeller/Snapshot/Restore Snapshot (Additive)...")]
        public static void RestoreSnapshotAdditive() => RestoreSnapshot(SnapshotRestoreMode.Additive);

        [MenuItem("Tools/AddressTeller/Snapshot/Restore Snapshot (Exact)...")]
        public static void RestoreSnapshotExact() => RestoreSnapshot(SnapshotRestoreMode.Exact);

        private static void RestoreSnapshot(SnapshotRestoreMode mode)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。Addressables を初期化してください。");
                return;
            }

            var path = EditorUtility.OpenFilePanel("Restore AddressTeller Snapshot", GetSnapshotFolder(), "json");
            if (string.IsNullOrEmpty(path)) return;

            var snapshot = AddressTellerSnapshot.FromJson(File.ReadAllText(path));
            var issues = AddressTellerSnapshotService.Restore(snapshot, settings, mode);

            foreach (var issue in issues)
                Debug.LogWarning($"[AddressTeller] {issue}");

            Debug.Log($"[AddressTeller] スナップショットを復元しました（{mode}）: {path}（{snapshot.Entries.Count} 件、問題 {issues.Count} 件）");
        }

        [MenuItem("Tools/AddressTeller/Snapshot/Compare with Current State...")]
        public static void CompareWithCurrent()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。Addressables を初期化してください。");
                return;
            }

            var path = EditorUtility.OpenFilePanel("Select Snapshot to Compare", GetSnapshotFolder(), "json");
            if (string.IsNullOrEmpty(path)) return;

            var before = AddressTellerSnapshot.FromJson(File.ReadAllText(path));
            var after = AddressTellerSnapshotService.Capture(settings);

            LogDiff(AddressTellerSnapshotService.Diff(before, after));
        }

        [MenuItem("Tools/AddressTeller/Snapshot/Compare Two Snapshots...")]
        public static void CompareTwoSnapshots()
        {
            var folder = GetSnapshotFolder();
            var beforePath = EditorUtility.OpenFilePanel("Select Older Snapshot", folder, "json");
            if (string.IsNullOrEmpty(beforePath)) return;

            var afterPath = EditorUtility.OpenFilePanel("Select Newer Snapshot", folder, "json");
            if (string.IsNullOrEmpty(afterPath)) return;

            var before = AddressTellerSnapshot.FromJson(File.ReadAllText(beforePath));
            var after = AddressTellerSnapshot.FromJson(File.ReadAllText(afterPath));

            LogDiff(AddressTellerSnapshotService.Diff(before, after));
        }

        private static void LogDiff(SnapshotDiff diff)
        {
            if (diff.IsEmpty)
            {
                Debug.Log("[AddressTeller] 差分はありません。");
                return;
            }

            foreach (var entry in diff.Added)
                Debug.Log($"[AddressTeller] + [{entry.GroupName}] {entry.Address} ({entry.Guid})");

            foreach (var entry in diff.Removed)
                Debug.Log($"[AddressTeller] - [{entry.GroupName}] {entry.Address} ({entry.Guid})");

            foreach (var (before, after) in diff.Changed)
                Debug.Log($"[AddressTeller] ~ [{before.GroupName}] {before.Address} → [{after.GroupName}] {after.Address} ({after.Guid})");

            Debug.Log($"[AddressTeller] 差分: 追加 {diff.Added.Count} 件 / 削除 {diff.Removed.Count} 件 / 変更 {diff.Changed.Count} 件");
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
