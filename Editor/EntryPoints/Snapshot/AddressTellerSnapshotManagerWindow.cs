using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// SnapshotFolder 配下のスナップショット一覧を表示し、復元・比較を行う管理ウィンドウ。
    /// </summary>
    internal sealed class AddressTellerSnapshotManagerWindow : EditorWindow
    {
        // Collect/LoadFromFile はファイル I/O を伴うため、OnGUI では呼ばない。
        // Open時・Refresh押下時・トグル変更時・検索キーワード変更時のみ再構築してここにキャッシュする。
        private List<SnapshotFileInfo> _items = new();

        private bool _showAuto;
        private string _searchKeyword = "";

        // 選択は index ではなく Path で保持する。Refresh で一覧の並びが変わっても選択対象がズレないようにするため。
        private string _selectedPath;
        private Vector2 _scroll;

        // 「2件目選択モード」：trueの間は一覧クリックで比較対象を選び、Diffを表示してモードを終了する。
        private bool _compareMode;

        // 行描画用スタイル。OnGUI 毎回 new しないようキャッシュする。
        private GUIStyle _normalRowStyle;
        private GUIStyle _errorRowStyle;

        [MenuItem("Tools/AddressTeller/Snapshot/Manage Snapshots...")]
        public static void Open()
        {
            var window = GetWindow<AddressTellerSnapshotManagerWindow>();
            window.titleContent = new GUIContent("AddressTeller - Snapshots");
            window.minSize = new Vector2(640, 360);
            window.Refresh();
            window.Show();
            window.Focus();
        }

        private void OnEnable() => Refresh();

        /// <summary>SnapshotFolder を再列挙し、現在のフィルタ条件で一覧をキャッシュし直す。</summary>
        private void Refresh()
        {
            var folder = AddressTellerSettings.GetSnapshotFolderAbsolutePath();
            var collected = SnapshotFileCatalog.Collect(folder, _showAuto);
            var filtered = SnapshotFileCatalog.Filter(collected, _searchKeyword);
            _items = SnapshotFileCatalog.SortByCapturedDesc(filtered).ToList();

            if (_selectedPath != null && !_items.Any(i => i.Path == _selectedPath))
                _selectedPath = null;
        }

        private int SelectedIndex => _selectedPath == null ? -1 : _items.FindIndex(i => i.Path == _selectedPath);

        private SnapshotFileInfo SelectedItem
        {
            get
            {
                var index = SelectedIndex;
                return index < 0 ? null : _items[index];
            }
        }

        private void OnGUI()
        {
            _normalRowStyle ??= new GUIStyle(EditorStyles.label);
            _errorRowStyle ??= new GUIStyle(EditorStyles.label) { normal = { textColor = Color.red } };

            DrawToolbar();
            EditorGUILayout.Space(2);
            DrawList();
            EditorGUILayout.Space(2);
            DrawActions();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                Refresh();

            EditorGUI.BeginChangeCheck();
            _showAuto = GUILayout.Toggle(_showAuto, "Show auto snapshots", EditorStyles.toolbarButton);
            if (EditorGUI.EndChangeCheck())
                Refresh();

            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            _searchKeyword = EditorGUILayout.TextField(_searchKeyword, EditorStyles.toolbarSearchField, GUILayout.Width(200));
            if (EditorGUI.EndChangeCheck())
                Refresh();

            EditorGUILayout.EndHorizontal();

            if (_compareMode)
                EditorGUILayout.HelpBox("Select a second snapshot from the list to compare.", MessageType.Info);
        }

        private void DrawList()
        {
            if (_items.Count == 0)
            {
                EditorGUILayout.HelpBox("No snapshots found.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var isSelected = item.Path == _selectedPath;

                var label = item.LoadError != null
                    ? $"{item.FileName}  -  Load failed: {item.LoadError}"
                    : $"{item.FileName}    " +
                      $"{(string.IsNullOrEmpty(item.CapturedAtIso) ? "(unknown)" : item.CapturedAtIso)}    " +
                      $"{item.Comment}    schema={item.SchemaVersion}    entries={item.EntryCount}";

                var style = item.LoadError != null ? _errorRowStyle : _normalRowStyle;

                var rect = GUILayoutUtility.GetRect(0, 100000, EditorGUIUtility.singleLineHeight, EditorGUIUtility.singleLineHeight);

                if (isSelected)
                    EditorGUI.DrawRect(rect, new Color(0.24f, 0.37f, 0.59f, 0.5f));

                EditorGUI.LabelField(rect, label, style);

                if (item.LoadError == null && Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    OnRowClicked(i);
                    Event.current.Use();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>一覧行クリック時の処理。通常選択と「2件目選択モード」での比較実行を振り分ける。</summary>
        private void OnRowClicked(int index)
        {
            if (_compareMode)
            {
                var first = SelectedItem;
                if (first == null || _items[index].Path == first.Path)
                {
                    _compareMode = false;
                    return;
                }

                CompareSelectedWith(first, _items[index]);
                _compareMode = false;
                return;
            }

            _selectedPath = _items[index].Path;
            Repaint();
        }

        private void DrawActions()
        {
            using (new EditorGUI.DisabledScope(SelectedIndex < 0 || _compareMode))
            {
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Restore (Additive)"))
                    RestoreSelected(SnapshotRestoreMode.Additive);

                if (GUILayout.Button("Restore (Exact)"))
                    RestoreSelected(SnapshotRestoreMode.Exact);

                if (GUILayout.Button("Compare with Current"))
                    CompareWithCurrent();

                EditorGUILayout.EndHorizontal();
            }

            var compareLabel = _compareMode ? "Cancel compare" : "Compare with another snapshot...";
            using (new EditorGUI.DisabledScope(SelectedIndex < 0 && !_compareMode))
            {
                if (GUILayout.Button(compareLabel))
                    _compareMode = !_compareMode;
            }
        }

        private AddressableAssetSettings GetSettingsOrLogError()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                EditorUtility.DisplayDialog("AddressTeller", "AddressableAssetSettings not found. Please initialize Addressables.", "OK");

            return settings;
        }

        private void RestoreSelected(SnapshotRestoreMode mode)
        {
            var item = SelectedItem;
            if (item == null) return;

            var settings = GetSettingsOrLogError();
            if (settings == null) return;

            var message = mode == SnapshotRestoreMode.Exact
                ? $"Restore state from '{item.FileName}'.\n\n" +
                  "Exact mode: labels added after the snapshot was taken will be removed. This operation cannot be undone."
                : $"Restore state from '{item.FileName}' (Additive).\n\n" +
                  "The Address/Group/Label values recorded in the snapshot will be written. Labels added after the snapshot was taken are preserved.";

            if (!EditorUtility.DisplayDialog($"Restore Snapshot ({mode})", message, "Restore", "Cancel"))
                return;

            if (!AddressTellerSnapshotService.LoadFromFile(item.Path, out var snapshot, out var error))
            {
                EditorUtility.DisplayDialog("Restore Snapshot", error, "OK");
                return;
            }

            var issues = AddressTellerSnapshotService.Restore(snapshot, settings, mode);
            foreach (var issue in issues)
                Debug.LogWarning($"[AddressTeller] {issue}");

            Debug.Log($"[AddressTeller] Snapshot restored ({mode}): {item.FileName} ({snapshot.Entries.Count} entries, {issues.Count} issue(s))");

            var summary = $"Restored {snapshot.Entries.Count} entry/entries from '{item.FileName}'.";
            if (issues.Count > 0)
                summary += $"\n\n{issues.Count} issue(s) (see Console for details):\n" + string.Join("\n", issues);

            EditorUtility.DisplayDialog("Restore Snapshot", summary, "OK");
        }

        private void CompareWithCurrent()
        {
            var item = SelectedItem;
            if (item == null) return;

            var settings = GetSettingsOrLogError();
            if (settings == null) return;

            if (!AddressTellerSnapshotService.LoadFromFile(item.Path, out var before, out var error))
            {
                EditorUtility.DisplayDialog("Compare with Current", error, "OK");
                return;
            }

            var after = AddressTellerSnapshotService.Capture(settings);
            var diff = AddressTellerSnapshotService.Diff(before, after);
            AddressTellerResultWindow.Show(new DryRunResult(diff, Array.Empty<ValidationResult>()), "AddressTeller - Compare with Current");
        }

        private void CompareSelectedWith(SnapshotFileInfo first, SnapshotFileInfo second)
        {
            if (!AddressTellerSnapshotService.LoadFromFile(first.Path, out var before, out var beforeError))
            {
                EditorUtility.DisplayDialog("Compare Snapshots", beforeError, "OK");
                return;
            }

            if (!AddressTellerSnapshotService.LoadFromFile(second.Path, out var after, out var afterError))
            {
                EditorUtility.DisplayDialog("Compare Snapshots", afterError, "OK");
                return;
            }

            var diff = AddressTellerSnapshotService.Diff(before, after);
            AddressTellerResultWindow.Show(new DryRunResult(diff, Array.Empty<ValidationResult>()), "AddressTeller - Compare Snapshots");
        }
    }
}
