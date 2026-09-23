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
        private HelpBox _settingsErrorHelpBox;

        /// <summary>
        /// 直近の Refresh でゲートが失敗した場合のエラー文。null なら成功（または未実行）。
        /// OnEnable 経由の初回 Refresh は CreateGUI より先に走り、その時点では _settingsErrorHelpBox が
        /// まだ null で表示に反映できないため、ここに理由を保持しておき、CreateGUI が
        /// _settingsErrorHelpBox を生成する時点でこれを読んで初期表示に反映する。
        /// </summary>
        private string _lastSettingsGateError;

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

            // USS ロード
            var commonSS = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.natsume777.addressteller/Editor/EntryPoints/StyleSheets/AddressTellerCommon.uss");
            var windowSS = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.natsume777.addressteller/Editor/EntryPoints/StyleSheets/AddressTellerSnapshotManagerWindow.uss");
            if (commonSS != null) root.styleSheets.Add(commonSS);
            if (windowSS != null) root.styleSheets.Add(windowSS);

            // ---- Toolbar ----
            var toolbar = new Toolbar();

            // 明示的な Refresh 操作なので毎回ログする（Always）。OnEnable・検索・トグル変更は
            // 既定の OncePerDistinctFailure に任せる（HelpBox で状態は分かるため、これらの暗黙的な
            // 再列挙のたびに Console を埋める必要はない）。
            var refreshBtn = new ToolbarButton(() => Refresh(SettingsGateLogPolicy.Always)) { text = "Refresh" };
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
            _compareModeHelpBox.AddToClassList("at-compare-hint");
            _compareModeHelpBox.style.display = DisplayStyle.None;
            root.Add(_compareModeHelpBox);

            // 設定ファイルの読み込みに失敗した場合の案内。初期は非表示だが、OnEnable 経由の初回 Refresh が
            // CreateGUI より先に走って既に失敗を記録していた場合は、ここでその内容を反映する
            // （_lastSettingsGateError のコメント参照）。
            _settingsErrorHelpBox = new HelpBox(_lastSettingsGateError ?? string.Empty, HelpBoxMessageType.Error);
            _settingsErrorHelpBox.style.display = _lastSettingsGateError != null ? DisplayStyle.Flex : DisplayStyle.None;
            root.Add(_settingsErrorHelpBox);

            // ---- ListView ----
            _listView = new ListView
            {
                makeItem = () =>
                {
                    var lbl = new Label();
                    lbl.style.paddingLeft = 4;
                    lbl.style.paddingTop = 2;
                    lbl.style.paddingBottom = 2;
                    return lbl;
                },
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
            };
            _listView.AddToClassList("at-list");

            _listView.selectionChanged += OnListSelectionChange;
            root.Add(_listView);

            // ---- アクションボタン ----
            var actionsRow = new VisualElement();
            actionsRow.AddToClassList("at-actions-row");

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
            };
            _compareWithAnotherBtn.AddToClassList("at-compare-btn-row");
            root.Add(_compareWithAnotherBtn);

            RebuildList();
            UpdateActionButtons();
        }

        /// <summary>
        /// SnapshotFolder を再列挙し、現在のフィルタ条件で一覧をキャッシュし直す。先頭で設定ファイルの
        /// 読み込みをゲートし、失敗していれば一覧を空にして HelpBox でエラーを示す
        /// （古い設定・既定値のまま一覧を組み立てて誤った SnapshotFolder を見せないため）。
        /// <paramref name="logPolicy"/> の既定は OncePerDistinctFailure——OnEnable・検索キーワード変更・
        /// 「Show auto snapshots」トグルなど、利用者が明示的に選んだわけではない暗黙的な再列挙のたびに
        /// Console へ Error を連発させないため（HelpBox で状態は分かる）。Refresh ボタン（明示操作）のみ
        /// Always を渡す。
        /// </summary>
        private void Refresh(SettingsGateLogPolicy logPolicy = SettingsGateLogPolicy.OncePerDistinctFailure)
        {
            var gate = AddressTellerSettings.EnsureLoaded(logPolicy);
            if (!gate.Success)
            {
                _items = new List<SnapshotFileInfo>();
                _selectedPath = null;
                ShowSettingsError(gate.Error);
                RebuildList();
                UpdateActionButtons();
                return;
            }

            HideSettingsError();

            var folder = AddressTellerSettings.GetSnapshotFolderAbsolutePath();
            var collected = SnapshotFileCatalog.Collect(folder, _showAuto);
            var filtered = SnapshotFileCatalog.Filter(collected, _searchKeyword);
            _items = SnapshotFileCatalog.SortByCapturedDesc(filtered).ToList();

            if (_selectedPath != null && !_items.Any(i => i.Path == _selectedPath))
                _selectedPath = null;

            RebuildList();
            UpdateActionButtons();
        }

        /// <summary>
        /// 設定ファイルの読み込み失敗を記録し、HelpBox が既に存在すれば表示する。CreateGUI 前（OnEnable
        /// 経由の初回 Refresh）では _settingsErrorHelpBox がまだ null のため、その場合は
        /// _lastSettingsGateError への記録のみ行う——CreateGUI が HelpBox 生成時にこれを読んで反映する
        /// （_lastSettingsGateError のコメント参照）。
        /// </summary>
        private void ShowSettingsError(string error)
        {
            _lastSettingsGateError = error;
            if (_settingsErrorHelpBox == null) return;
            _settingsErrorHelpBox.text = error;
            _settingsErrorHelpBox.style.display = DisplayStyle.Flex;
        }

        /// <summary>設定ファイルの読み込みエラー表示を消す。</summary>
        private void HideSettingsError()
        {
            _lastSettingsGateError = null;
            if (_settingsErrorHelpBox == null) return;
            _settingsErrorHelpBox.style.display = DisplayStyle.None;
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
            if (!AddressTellerSettings.EnsureLoaded()) return;

            var item = SelectedItem;
            if (item == null) return;

            var settings = GetSettingsOrLogError();
            if (settings == null) return;

            // 同一 guid が2つ以上のグループにまたがって存在する状態のまま Restore まで進めると、
            // settings.FindAssetEntry の先勝ちで片方だけに作用してしまう。ダイアログより前に検出して中止する。
            if (DuplicateAssetEntryDetector.LogAndReturnTrueIfDuplicates(settings, "Restore Snapshot"))
            {
                EditorUtility.DisplayDialog(
                    "Restore Snapshot",
                    "Aborted: the same asset has an entry in two or more Addressables groups at once. See the Console for details.",
                    "OK");
                return;
            }

            var message = mode == SnapshotRestoreMode.Exact
                ? $"Restore state from '{item.FileName}'.\n\n" +
                  "Exact mode: labels added after the snapshot was taken will be removed. This operation cannot be undone.\n" +
                  "Entries are not removed; only labels are reconciled."
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
