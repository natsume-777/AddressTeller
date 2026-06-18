using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AddressTeller.Editor
{
    /// <summary>
    /// SnapshotFolder 配下のスナップショット一覧を表示し、復元・比較を行う管理ウィンドウ。
    /// </summary>
    internal sealed class AddressTellerSnapshotManagerWindow : EditorWindow
    {
        // Collect/LoadFromFile はファイル I/O を伴うため、CreateGUI/Refresh 以外では呼ばない。
        // Open時・Refresh押下時・トグル変更時・検索キーワード変更時のみ再構築してここにキャッシュする。
        private List<SnapshotFileInfo> _items = new();

        private bool _showAuto;
        private string _searchKeyword = "";

        // 選択は index ではなく Path で保持する。Refresh で一覧の並びが変わっても選択対象がズレないようにするため。
        private string _selectedPath;

        // 「2件目選択モード」：trueの間は一覧クリックで比較対象を選び、Diffを表示してモードを終了する。
        private bool _compareMode;

        // UI 要素への参照（Refresh 時に更新するため保持）
        private ListView _listView;
        private HelpBox _compareModeHelpBox;
        private Button _restoreAdditiveBtn;
        private Button _restoreExactBtn;
        private Button _compareWithCurrentBtn;
        private Button _compareWithAnotherBtn;

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

        public void CreateGUI()
        {
            var root = rootVisualElement;

            // ---- Toolbar ----
            var toolbar = new Toolbar();

            var refreshBtn = new ToolbarButton(Refresh) { text = "Refresh" };
            toolbar.Add(refreshBtn);

            var showAutoToggle = new ToolbarToggle { text = "Show auto snapshots", value = _showAuto };
            showAutoToggle.RegisterValueChangedCallback(e =>
            {
                _showAuto = e.newValue;
                Refresh();
            });
            toolbar.Add(showAutoToggle);

            // ツールバー右端に検索フィールド
            var spacer = new ToolbarSpacer { style = { flexGrow = 1 } };
            toolbar.Add(spacer);

            var searchField = new ToolbarSearchField { value = _searchKeyword, style = { width = 200 } };
            searchField.RegisterValueChangedCallback(e =>
            {
                _searchKeyword = e.newValue;
                Refresh();
            });
            toolbar.Add(searchField);

            root.Add(toolbar);

            // Compare モード中の案内（初期は非表示）
            _compareModeHelpBox = new HelpBox("Select a second snapshot from the list to compare.", HelpBoxMessageType.Info);
            _compareModeHelpBox.style.display = DisplayStyle.None;
            root.Add(_compareModeHelpBox);

            // ---- ListView ----
            _listView = new ListView
            {
                makeItem = () => new Label { style = { paddingLeft = 4, paddingTop = 2, paddingBottom = 2 } },
                bindItem = (element, index) =>
                {
                    var label = (Label)element;
                    var item = _items[index];
                    if (item.LoadError != null)
                    {
                        label.text = $"{item.FileName}  -  Load failed: {item.LoadError}";
                        label.style.color = Color.red;
                    }
                    else
                    {
                        label.text =
                            $"{item.FileName}    " +
                            $"{(string.IsNullOrEmpty(item.CapturedAtIso) ? "(unknown)" : item.CapturedAtIso)}    " +
                            $"{item.Comment}    schema={item.SchemaVersion}    entries={item.EntryCount}";
                        label.style.color = StyleKeyword.Null; // デフォルト色に戻す
                    }
                },
                selectionType = SelectionType.Single,
                style = { flexGrow = 1 },
            };

            _listView.onSelectionChange += OnListSelectionChange;
            root.Add(_listView);

            // ---- アクションボタン ----
            var actionsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4, marginLeft = 4, marginRight = 4 } };

            _restoreAdditiveBtn = new Button(() => RestoreSelected(SnapshotRestoreMode.Additive)) { text = "Restore (Additive)" };
            _restoreExactBtn = new Button(() => RestoreSelected(SnapshotRestoreMode.Exact)) { text = "Restore (Exact)" };
            _compareWithCurrentBtn = new Button(CompareWithCurrent) { text = "Compare with Current" };

            actionsRow.Add(_restoreAdditiveBtn);
            actionsRow.Add(_restoreExactBtn);
            actionsRow.Add(_compareWithCurrentBtn);
            root.Add(actionsRow);

            _compareWithAnotherBtn = new Button(ToggleCompareMode)
            {
                text = "Compare with another snapshot...",
                style = { marginLeft = 4, marginRight = 4, marginBottom = 4 }
            };
            root.Add(_compareWithAnotherBtn);

            RebuildList();
            UpdateActionButtons();
        }

        /// <summary>SnapshotFolder を再列挙し、現在のフィルタ条件で一覧をキャッシュし直す。</summary>
        private void Refresh()
        {
            var folder = AddressTellerSettings.GetSnapshotFolderAbsolutePath();
            var collected = SnapshotFileCatalog.Collect(folder, _showAuto);
            var filtered = SnapshotFileCatalog.Filter(collected, _searchKeyword);
            _items = SnapshotFileCatalog.SortByCapturedDesc(filtered).ToList();

            if (_selectedPath != null && !_items.Any(i => i.Path == _selectedPath))
                _selectedPath = null;

            RebuildList();
            UpdateActionButtons();
        }

        /// <summary>_items の内容を ListView に反映する。</summary>
        private void RebuildList()
        {
            if (_listView == null) return;

            _listView.itemsSource = _items;
            _listView.Rebuild();

            // 選択状態を復元する
            var selectedIndex = _selectedPath == null ? -1 : _items.FindIndex(i => i.Path == _selectedPath);
            if (selectedIndex >= 0)
            {
                _listView.SetSelectionWithoutNotify(new[] { selectedIndex });
            }
            else
            {
                _listView.ClearSelection();
                _selectedPath = null;
            }
        }

        private void OnListSelectionChange(IEnumerable<object> selection)
        {
            var selected = selection.FirstOrDefault() as SnapshotFileInfo;
            if (selected == null) return;

            // ロードエラーのある行は選択不可
            if (selected.LoadError != null)
            {
                _listView.ClearSelection();
                return;
            }

            if (_compareMode)
            {
                // 同じスナップショットを2件目として選択した場合はキャンセル
                if (selected.Path == _selectedPath)
                {
                    SetCompareMode(false);
                    return;
                }

                var first = _items.FirstOrDefault(i => i.Path == _selectedPath);
                if (first != null)
                    CompareSelectedWith(first, selected);

                SetCompareMode(false);
                return;
            }

            _selectedPath = selected.Path;
            UpdateActionButtons();
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

        private void UpdateActionButtons()
        {
            if (_restoreAdditiveBtn == null) return;

            var hasSelection = SelectedIndex >= 0;
            var notComparing = !_compareMode;

            _restoreAdditiveBtn.SetEnabled(hasSelection && notComparing);
            _restoreExactBtn.SetEnabled(hasSelection && notComparing);
            _compareWithCurrentBtn.SetEnabled(hasSelection && notComparing);
            _compareWithAnotherBtn.SetEnabled(hasSelection || _compareMode);
            _compareWithAnotherBtn.text = _compareMode ? "Cancel compare" : "Compare with another snapshot...";

            if (_compareModeHelpBox != null)
                _compareModeHelpBox.style.display = _compareMode ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ToggleCompareMode()
        {
            SetCompareMode(!_compareMode);
        }

        private void SetCompareMode(bool value)
        {
            _compareMode = value;
            UpdateActionButtons();
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
