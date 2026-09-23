using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>Menu items under Tools/AddressTeller/Snapshot. Saves, restores, and compares snapshots.</summary>
    public static class AddressTellerSnapshotMenu
    {
        /// <summary>Captures the current Addressables state and writes it to a timestamped file under SnapshotFolder.</summary>
        [MenuItem("Tools/AddressTeller/Snapshot/Save Snapshot")]
        public static void SaveSnapshot()
        {
            if (!AddressTellerSettings.EnsureLoaded()) return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found. Please initialize Addressables.");
                return;
            }

            SaveSnapshot(settings);
        }

        /// <summary>
        /// <see cref="SaveSnapshot()"/> のコア処理。settings を注入可能にしたオーバーロード（internal）。
        /// テストから、本番の <see cref="AddressableAssetSettingsDefaultObject.Settings"/> に一切触れずに
        /// 重複 guid 検出による中止経路を検証できるようにする（<see cref="AddressTellerMenu.ClearAll(AddressableAssetSettings, System.Collections.Generic.IReadOnlyList{AddressRuleBase})"/>
        /// と同じ意図）。
        /// </summary>
        internal static void SaveSnapshot(AddressableAssetSettings settings)
        {
            // 同一 guid が2つ以上のグループにまたがって存在する状態のまま保存すると、Entries に同じ guid が
            // 複数件入ったファイルができる。AddressTellerSnapshotService.LoadFromFile は重複 guid を
            // 明示的に拒否するため、そのファイルは以後 Restore にも Undo Last Apply にも使えない
            // （「読めないファイルを作らない」ため、保存自体をここで止める）。
            if (DuplicateAssetEntryDetector.LogAndReturnTrueIfDuplicates(settings, "Save Snapshot"))
                return;

            var snapshot = AddressTellerSnapshotService.Capture(settings);

            if (!TryWriteSnapshotFile(snapshot, out var path, out var error, out var exception))
            {
                // フォルダ作成（無効な SnapshotFolder 設定等）・ファイル書き込みいずれの失敗も
                // メニュー操作の未処理例外として漏らさず、ダイアログとログの両方で明確に伝える。
                // ダイアログには要点（対象フォルダ・簡潔な理由）、ログには調査用にスタックトレース込みの全体を出す。
                EditorUtility.DisplayDialog(
                    "AddressTeller - Save Snapshot",
                    $"Failed to save snapshot.\n\n{error}",
                    "OK");
                Debug.LogError($"[AddressTeller] Save Snapshot: Failed to write snapshot: {exception}");
                return;
            }

            Debug.Log($"[AddressTeller] Snapshot saved: {path} ({snapshot.Entries.Count} entries)");
        }

        /// <summary>
        /// スナップショットをファイルへ書き込む。フォルダ作成（<see cref="GetSnapshotFolder"/> 経由の
        /// Directory.CreateDirectory）・ファイル書き込みのいずれの失敗も例外として外へ漏らさず、
        /// false と <paramref name="error"/>（対象フォルダを含む簡潔な理由）・<paramref name="exception"/>
        /// （呼び出し側がログにスタックトレース込みで出すための元例外）に理由を返す。SaveSnapshot() から
        /// ダイアログ表示込みで呼ばれるほか、UI（EditorUtility.DisplayDialog）を経由せずテストから
        /// 直接検証できるように分離する。
        /// </summary>
        internal static bool TryWriteSnapshotFile(AddressTellerSnapshot snapshot, out string path, out string error, out Exception exception)
        {
            // GetSnapshotFolderAbsolutePath() 自体が Path.GetFullPath/Path.Combine を経由するため、
            // SnapshotFolder が不正な文字列の場合は catch 節内で再呼び出ししても同じ例外を投げうる
            // （Try...と名乗りながら例外を外に漏らしてしまう）。folder を try 内で確定できた場合のみ使い、
            // 確定できなかった場合は catch 内で生の設定値（SnapshotFolder）にフォールバックする。
            string folder = null;
            try
            {
                folder = GetSnapshotFolder();
                // 同じ秒に2回保存されても上書きされないよう、Auto/Clear スナップショットと同じ一意化ロジックを使う。
                path = SnapshotFileHelper.ResolveUniquePath(folder, DateTime.Now);
                File.WriteAllText(path, snapshot.ToJson());
                // 兄弟実装(AddressTellerAutoSnapshotService.CaptureAndSave等)と対称に、try 内で行う
                // （AssetDatabase.Refresh 自体が投げうる例外も同じ catch でエラーとして扱うため）。
                SnapshotFileHelper.RefreshIfInsideAssets(path);
                error = null;
                exception = null;
                return true;
            }
            catch (Exception e)
            {
                path = null;
                error = $"Failed to save snapshot to '{folder ?? AddressTellerSettings.SnapshotFolder}': {e.Message}";
                exception = e;
                return false;
            }
        }

        /// <summary>
        /// Restores the Addressables state from the latest automatic snapshot (saved immediately before
        /// running the Apply All / Apply with Validate menu items). Undoes address changes and label
        /// assignments made by Apply, as well as removing entries that Apply newly added (limited to
        /// entries in groups AddressTeller owns; manually created entries in groups it does not own are
        /// excluded from removal by the ownership check, and their count is shown in the confirmation
        /// dialog). Uses the dedicated path that also removes entries
        /// (<see cref="AddressTellerSnapshotService.RestoreExactWithRemoval"/>).
        /// </summary>
        [MenuItem("Tools/AddressTeller/Undo Last Apply")]
        public static void UndoLastApply()
        {
            if (!AddressTellerSettings.EnsureLoaded()) return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found. Please initialize Addressables.");
                return;
            }

            UndoLastApply(settings);
        }

        /// <summary>
        /// <see cref="UndoLastApply()"/> のコア処理。settings を注入可能にしたオーバーロード（internal）。
        /// テストから、本番の <see cref="AddressableAssetSettingsDefaultObject.Settings"/> に一切触れずに
        /// 重複 guid 検出による中止経路を検証できるようにする（<see cref="SaveSnapshot(AddressableAssetSettings)"/>
        /// と同じ意図）。
        /// </summary>
        internal static void UndoLastApply(AddressableAssetSettings settings)
        {
            // 同一 guid が2つ以上のグループにまたがって存在する状態のまま Diff/RestoreExactWithRemoval まで
            // 進めると、settings.FindAssetEntry の先勝ちで片方だけに作用してしまう（Diff 自体は重複耐性化済みだが、
            // それは「例外を出さない」だけで、この状態のまま復元してよいという意味ではない）。ダイアログより前に
            // 検出して中止する。
            if (DuplicateAssetEntryDetector.LogAndReturnTrueIfDuplicates(settings, "Undo Last Apply"))
                return;

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

            // 削除追従の所有権判定（ownedGroups）は、有効/無効に関わらず全ルールを対象にする。
            // ルールが Off でも過去に付与されたエントリの所属グループは一貫して所有下として扱うため
            // （RemoveEntriesForDeletedAssets と同じ考え方）。
            var setup = RuleEvaluationPipeline.BuildSetup(settings, RuleCollector.CollectRules());
            var ownedGroups = setup.OwnedGroups;
            var removable = diff.Removed.Where(e => ownedGroups.Contains(e.GroupName)).ToList();
            var keptCount = diff.Removed.Count - removable.Count;

            var message =
                $"Restore state before the last Apply ({Path.GetFileName(path)}).\n\n" +
                $"Entries to add: {diff.Added.Count}\n" +
                $"Entries to remove: {removable.Count}\n" +
                $"Entries to change: {diff.Changed.Count}\n\n" +
                (keptCount > 0 ? $"{keptCount} entry/entries in unowned groups will be kept.\n\n" : "") +
                // ルールの Configure() が1件でも失敗していると ownedGroups が不完全な可能性がある
                // （本来所有しているはずのグループが「未検出」として扱われうる）ことをユーザーに明示する。
                // RuleCollector.CollectRules() は無効化中のルールも含むため、Project Settings で無効化しても
                // この警告は解除されない（ルールの Configure() 自体を修正する必要がある）旨も添える。
                (setup.ConfigureFailures.Count > 0
                    ? $"Warning: {setup.ConfigureFailures.Count} rule(s) failed to configure; some groups may not be recognized as managed.\n"
                        + $"{string.Join("; ", setup.ConfigureFailures.Select(f => f.Message))}\n"
                        + "Disabling the rule in Project Settings will not resolve this; fix the rule's Configure() instead.\n\n"
                    : "") +
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
