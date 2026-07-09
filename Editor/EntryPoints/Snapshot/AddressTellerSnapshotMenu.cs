using System;
using System.IO;
using System.Linq;
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
            var folder = GetSnapshotFolder();
            // 同じ秒に2回保存されても上書きされないよう、Auto/Clear スナップショットと同じ一意化ロジックを使う。
            var path = SnapshotFileHelper.ResolveUniquePath(folder, DateTime.Now);

            try
            {
                File.WriteAllText(path, snapshot.ToJson());
            }
            catch (Exception e)
            {
                // 読み取り専用フォルダ等での書き込み失敗をメニュー操作の未処理例外として漏らさず、
                // ダイアログとログの両方で明確に伝える。
                EditorUtility.DisplayDialog(
                    "AddressTeller - Save Snapshot",
                    $"Failed to save snapshot to '{path}'.\n\n{e.Message}",
                    "OK");
                Debug.LogError($"[AddressTeller] Save Snapshot: Failed to write '{path}': {e}");
                return;
            }

            SnapshotFileHelper.RefreshIfInsideAssets(path);

            Debug.Log($"[AddressTeller] Snapshot saved: {path} ({snapshot.Entries.Count} entries)");
        }

        /// <summary>
        /// 最新の自動スナップショット（Apply All / Apply with Validate メニューの実行直前に保存されたもの）から、
        /// Addressables の状態を復元する。Apply によるアドレス変更・ラベル付与に加え、Apply が新規追加した
        /// エントリの削除まで含めて Undo する（AddressTeller 管理下のグループのエントリに限る。管理外グループの
        /// 手動エントリは所有権判定により削除対象から除外し、確認ダイアログにも件数を明示する）。
        /// エントリ削除を伴う専用パス（<see cref="AddressTellerSnapshotService.RestoreExactWithRemoval"/>）を使う。
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

            // 削除追従の所有権判定（managedGroups）は、有効/無効に関わらず全ルールを対象にする。
            // ルールが Off でも過去に付与されたエントリの所属グループは一貫して管理下として扱うため
            // （RemoveEntriesForDeletedAssets と同じ考え方）。
            var managedGroups = RuleEvaluationPipeline.BuildSetup(settings, RuleCollector.CollectRules()).ManagedGroups;
            var removable = diff.Removed.Where(e => managedGroups.Contains(e.GroupName)).ToList();
            var keptCount = diff.Removed.Count - removable.Count;

            var message =
                $"Restore state before the last Apply ({Path.GetFileName(path)}).\n\n" +
                $"Entries to add: {diff.Added.Count}\n" +
                $"Entries to remove: {removable.Count}\n" +
                $"Entries to change: {diff.Changed.Count}\n\n" +
                (keptCount > 0 ? $"{keptCount} entry/entries in unmanaged groups will be kept.\n\n" : "") +
                "Restores in Exact mode; labels added after the snapshot was taken may be removed.";

            if (!EditorUtility.DisplayDialog("Undo Last Apply", message, "Restore", "Cancel"))
                return;

            var issues = AddressTellerSnapshotService.RestoreExactWithRemoval(snapshot, settings, removable.Select(e => e.Guid));
            foreach (var issue in issues)
                Debug.LogWarning($"[AddressTeller] {issue}");

            Debug.Log($"[AddressTeller] Last Apply undone ({Path.GetFileName(path)}): {snapshot.Entries.Count} entries, {removable.Count} removed, {issues.Count} issue(s)");
        }

        /// <summary>AddressTellerSettings.SnapshotFolder を絶対パスに解決し、フォルダがなければ作成する。</summary>
        private static string GetSnapshotFolder()
        {
            var path = AddressTellerSettings.GetSnapshotFolderAbsolutePath();
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
