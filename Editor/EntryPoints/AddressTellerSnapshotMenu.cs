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
                Debug.LogError("[AddressTeller] AddressableAssetSettings が見つかりません。Addressables を初期化してください。");
                return;
            }

            var path = AddressTellerAutoSnapshotService.FindLatestAuto();
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[AddressTeller] 自動スナップショットが見つかりません。Apply All / Apply with Validate を一度実行すると自動保存されます。");
                return;
            }

            var snapshot = AddressTellerAutoSnapshotService.LoadAuto(path);
            if (snapshot == null) return;

            var current = AddressTellerSnapshotService.Capture(settings);
            var diff = AddressTellerSnapshotService.Diff(current, snapshot);
            if (diff.IsEmpty)
            {
                Debug.Log("[AddressTeller] 直前の Apply からの変更はありません。");
                return;
            }

            var message =
                $"直前の Apply 実行前の状態（{Path.GetFileName(path)}）に復元します。\n\n" +
                $"追加されるエントリ: {diff.Added.Count} 件\n" +
                $"削除されるエントリ: {diff.Removed.Count} 件\n" +
                $"変更されるエントリ: {diff.Changed.Count} 件\n\n" +
                "Exact モードで復元するため、スナップショット保存後に付与されたラベルは剥がされる可能性があります。";

            if (!EditorUtility.DisplayDialog("Undo Last Apply", message, "復元する", "キャンセル"))
                return;

            var issues = AddressTellerSnapshotService.Restore(snapshot, settings, SnapshotRestoreMode.Exact);
            foreach (var issue in issues)
                Debug.LogWarning($"[AddressTeller] {issue}");

            Debug.Log($"[AddressTeller] 直前の Apply を取り消しました（{Path.GetFileName(path)}）: {snapshot.Entries.Count} 件、問題 {issues.Count} 件");
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
