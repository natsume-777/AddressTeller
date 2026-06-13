using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// dry-run の差分（<see cref="DryRunResult"/>）や Apply/Validate の問題点
    /// （<see cref="ValidationResult"/>）を TreeView で表示する結果ウィンドウ。
    /// Step 12-c の確認ダイアログから「詳細を見る」として開かれることを想定するが、
    /// このステップでは単体のウィンドウとして完成させる（既存メニューへの組み込みは別ステップ）。
    /// </summary>
    internal sealed class AddressTellerResultWindow : EditorWindow
    {
        private enum Tab
        {
            Diff,
            Issues,
        }

        private static readonly string[] TabLabels = { "Diff", "Issues" };

        [SerializeField] private TreeViewState _diffTreeViewState;
        [SerializeField] private TreeViewState _issueTreeViewState;
        [SerializeField] private MultiColumnHeaderState _diffHeaderState;
        [SerializeField] private MultiColumnHeaderState _issueHeaderState;

        private AddressTellerDiffTreeView _diffTreeView;
        private AddressTellerIssueTreeView _issueTreeView;

        private Tab _currentTab;
        private bool _showDiffTab;

        // Apply実行用のコールバック。ドメインリロードを跨ぐとデリゲートは復元できないため非シリアライズとし、
        // リロード後は null（＝ボタン非表示）になることを許容する割り切り。
        private Action _onApply;

        // Show() 時に一度だけ構築し、OnGUI では再構築しない（GUIDToAssetPath 等の重い変換を避けるため）。
        private List<DiffRow> _diffRows = new();
        private List<IssueRow> _allIssueRows = new();
        private IReadOnlyList<string> _groupsToCreate = Array.Empty<string>();

        // Issues タブのステータス別フィルタ。キーは Enum.GetValues(typeof(ValidationStatus)) の全件、初期値は true（全件表示）。
        private readonly Dictionary<ValidationStatus, bool> _statusFilter = new();

        /// <summary>Diff/Issues の2タブでウィンドウを開く。dry-run の結果表示用。</summary>
        public static void Show(DryRunResult dryRun, string title = "AddressTeller - Apply Preview")
        {
            var window = GetOrCreateWindow(title);
            window._showDiffTab = true;
            window._currentTab = Tab.Diff;
            window._onApply = null; // ウィンドウ再利用時に前回の onApply が残らないようにクリア
            window._groupsToCreate = dryRun.GroupsToCreate;
            window.SetDiffRows(AddressTellerResultWindowRows.BuildDiffRows(dryRun.Diff));
            window.SetIssueRows(dryRun.Issues);
            window.Show();
            window.Focus();
        }

        /// <summary>
        /// Diff/Issues の2タブでウィンドウを開き、Diff タブに「この内容で Apply」ボタンを表示する。
        /// 確認ダイアログの「詳細を見る」から開かれることを想定する。
        /// </summary>
        public static void Show(DryRunResult dryRun, string title, Action onApply)
        {
            var window = GetOrCreateWindow(title);
            window._showDiffTab = true;
            window._currentTab = Tab.Diff;
            window._onApply = onApply;
            window._groupsToCreate = dryRun.GroupsToCreate;
            window.SetDiffRows(AddressTellerResultWindowRows.BuildDiffRows(dryRun.Diff));
            window.SetIssueRows(dryRun.Issues);
            window.Show();
            window.Focus();
        }

        /// <summary>Issues のみをタブ固定で表示する。ApplyAll/ValidateAll の結果表示用。</summary>
        public static void Show(IReadOnlyList<ValidationResult> issues, string title)
        {
            var window = GetOrCreateWindow(title);
            window._showDiffTab = false;
            window._currentTab = Tab.Issues;
            window._onApply = null; // ウィンドウ再利用時に前回の onApply が残らないようにクリア
            window._groupsToCreate = Array.Empty<string>();
            window.SetDiffRows(new List<DiffRow>());
            window.SetIssueRows(issues);
            window.Show();
            window.Focus();
        }

        /// <summary>
        /// 既存ウィンドウがあれば再利用してフォーカスし、なければ新規作成する。
        /// Apply実行を複数回起動した際にウィンドウが積み上がらないようにするため。
        /// </summary>
        private static AddressTellerResultWindow GetOrCreateWindow(string title)
        {
            var window = GetWindow<AddressTellerResultWindow>();
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(640, 360);
            return window;
        }

        private void SetDiffRows(List<DiffRow> rows)
        {
            _diffRows = rows ?? new List<DiffRow>();
            EnsureDiffTreeView();
            _diffTreeView.SetRows(_diffRows);
        }

        private void SetIssueRows(IReadOnlyList<ValidationResult> issues)
        {
            _allIssueRows = AddressTellerResultWindowRows.BuildIssueRows(issues ?? Array.Empty<ValidationResult>());

            // フィルタの初期状態は全ステータス表示。
            _statusFilter.Clear();
            foreach (ValidationStatus status in Enum.GetValues(typeof(ValidationStatus)))
                _statusFilter[status] = true;

            EnsureIssueTreeView();
            ApplyIssueFilter();
        }

        private void EnsureDiffTreeView()
        {
            if (_diffTreeView != null) return;

            _diffTreeViewState ??= new TreeViewState();
            _diffHeaderState ??= AddressTellerDiffTreeView.CreateHeaderState();

            var header = new MultiColumnHeader(_diffHeaderState);
            header.ResizeToFit();
            _diffTreeView = new AddressTellerDiffTreeView(_diffTreeViewState, header);
        }

        private void EnsureIssueTreeView()
        {
            if (_issueTreeView != null) return;

            _issueTreeViewState ??= new TreeViewState();
            _issueHeaderState ??= AddressTellerIssueTreeView.CreateHeaderState();

            var header = new MultiColumnHeader(_issueHeaderState);
            header.ResizeToFit();
            _issueTreeView = new AddressTellerIssueTreeView(_issueTreeViewState, header);
        }

        /// <summary>現在のステータスフィルタに基づき、表示行を再構築する。フィルタ変更時のみ呼ぶ。</summary>
        private void ApplyIssueFilter()
        {
            var filtered = _allIssueRows.Where(r => _statusFilter.TryGetValue(r.Status, out var enabled) && enabled).ToList();
            EnsureIssueTreeView();
            _issueTreeView.SetRows(filtered);
        }

        private void OnGUI()
        {
            if (_showDiffTab)
            {
                EditorGUILayout.Space(2);
                _currentTab = (Tab)GUILayout.Toolbar((int)_currentTab, TabLabels);
                EditorGUILayout.Space(2);
            }
            else
            {
                _currentTab = Tab.Issues;
            }

            switch (_currentTab)
            {
                case Tab.Diff:
                    DrawDiffTab();
                    break;
                case Tab.Issues:
                    DrawIssuesTab();
                    break;
            }
        }

        private void DrawDiffTab()
        {
            EnsureDiffTreeView();

            if (_diffRows.Count == 0)
            {
                EditorGUILayout.HelpBox("差分はありません。", MessageType.Info);
                DrawGroupsToCreateSummary();
                DrawApplyButton();
                return;
            }

            var summary = $"追加 {_diffRows.Count(r => r.Kind == DiffRowKind.Added)} 件 / " +
                           $"削除 {_diffRows.Count(r => r.Kind == DiffRowKind.Removed)} 件 / " +
                           $"変更 {_diffRows.Count(r => r.Kind == DiffRowKind.Changed)} 件";
            EditorGUILayout.LabelField(summary, EditorStyles.miniLabel);
            DrawGroupsToCreateSummary();

            var rect = GUILayoutUtility.GetRect(0, 100000, 0, 100000);
            _diffTreeView.OnGUI(rect);

            DrawApplyButton();
        }

        /// <summary>AutoCreateMissingGroups が有効で新規作成予定のグループがある場合、その件数と名前を表示する。</summary>
        private void DrawGroupsToCreateSummary()
        {
            if (_groupsToCreate.Count == 0) return;

            EditorGUILayout.LabelField(
                $"新規作成されるグループ: {_groupsToCreate.Count} 件 ({string.Join(", ", _groupsToCreate)})",
                EditorStyles.miniLabel);
        }

        /// <summary>
        /// 確認ダイアログ経由で開かれた場合のみ「この内容で Apply」ボタンを表示する。
        /// 押下時に実行される Apply は、このウィンドウを開いた時点（dry-run計算時）の
        /// 対象パス・ルールに基づく。ウィンドウを開いてからアセットやルールが変化した場合、
        /// 表示中の差分と実際の Apply 結果がずれる可能性があるが、意図的な割り切りとする（M-1）。
        /// </summary>
        private void DrawApplyButton()
        {
            if (_onApply == null) return;

            EditorGUILayout.Space(2);
            if (GUILayout.Button("この内容で Apply"))
            {
                var apply = _onApply;
                Close();
                apply();
            }
        }

        private void DrawIssuesTab()
        {
            EnsureIssueTreeView();

            DrawStatusFilterToolbar();

            if (_allIssueRows.Count == 0)
            {
                EditorGUILayout.HelpBox("問題はありません。", MessageType.Info);
                return;
            }

            var rect = GUILayoutUtility.GetRect(0, 100000, 0, 100000);
            _issueTreeView.OnGUI(rect);
        }

        /// <summary>ステータスごとの表示トグルを描画する。トグル変更時のみフィルタを再適用する。</summary>
        private void DrawStatusFilterToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("表示:", GUILayout.Width(40));

            EditorGUI.BeginChangeCheck();

            foreach (ValidationStatus status in Enum.GetValues(typeof(ValidationStatus)))
            {
                var current = _statusFilter.TryGetValue(status, out var enabled) && enabled;
                _statusFilter[status] = GUILayout.Toggle(current, status.ToString(), EditorStyles.toolbarButton);
            }

            if (EditorGUI.EndChangeCheck())
                ApplyIssueFilter();

            EditorGUILayout.EndHorizontal();
        }
    }
}
