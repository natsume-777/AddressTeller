using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace AddressTeller.Editor
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
            Distribution,
        }

        private static readonly string[] TabLabelsWithDistribution = { "Diff", "Issues", "Distribution" };
        private static readonly string[] TabLabelsWithoutDistribution = { "Diff", "Issues" };

        [SerializeField] private TreeViewState<int> _diffTreeViewState;
        [SerializeField] private TreeViewState<int> _issueTreeViewState;
        [SerializeField] private MultiColumnHeaderState _diffHeaderState;
        [SerializeField] private MultiColumnHeaderState _issueHeaderState;

        private AddressTellerDiffTreeView _diffTreeView;
        private AddressTellerIssueTreeView _issueTreeView;

        // _showDiffTab/_showDistributionTab は非シリアライズのため、ドメインリロード後は
        // false にリセットされ、タブ構成が Issues のみに縮退する（_diffRows 等の非シリアライズ
        // フィールドも空になるため、リロード後の再表示は元々想定していない割り切り）。
        private Tab _currentTab;
        private bool _showDiffTab;
        private bool _showDistributionTab;

        // Apply実行用のコールバック。ドメインリロードを跨ぐとデリゲートは復元できないため非シリアライズとし、
        // リロード後は null（＝ボタン非表示）になることを許容する割り切り。
        private Action _onApply;

        // Show() 時に一度だけ構築し、OnGUI では再構築しない（GUIDToAssetPath 等の重い変換を避けるため）。
        private List<DiffRow> _diffRows = new();
        private List<IssueRow> _allIssueRows = new();
        private IReadOnlyList<string> _groupsToCreate = Array.Empty<string>();

        // Distribution タブの集計結果。Show() 時に一度だけ算出し、OnGUI では再計算しない。
        // 算出失敗・対象外（After が null 等）の場合は null（タブ自体を非表示にする）。
        private BundleDistribution _distribution;
        private DistributionSummary _distributionSummary;
        private Vector2 _distributionScroll;

        // Issues タブのステータス別フィルタ。キーは Enum.GetValues(typeof(ValidationStatus)) の全件、初期値は true（全件表示）。
        private readonly Dictionary<ValidationStatus, bool> _statusFilter = new();

        // タブ描画より前に HelpBox として表示する任意の注意文。スコープ付きプレビューでの
        // 「ルール絞り込み中は削除予測が縮小される」等の案内に使う。
        private string _notice;

        /// <summary>Diff/Issues の2タブでウィンドウを開く。dry-run の結果表示用。</summary>
        /// <param name="settings">
        /// Distribution タブの算出に使う。null または <paramref name="dryRun"/>.After が null の場合、Distribution タブは表示しない。
        /// </param>
        /// <param name="notice">非 null の場合、タブ描画前に HelpBox(Info) として表示する注意文。</param>
        public static void Show(DryRunResult dryRun, string title = "AddressTeller - Apply Preview", AddressableAssetSettings settings = null, string notice = null)
        {
            var window = GetOrCreateWindow(title);
            window._showDiffTab = true;
            window._currentTab = Tab.Diff;
            window._onApply = null; // ウィンドウ再利用時に前回の onApply が残らないようにクリア
            window._groupsToCreate = dryRun.GroupsToCreate;
            window._notice = notice;
            window.SetDiffRows(AddressTellerResultWindowRows.BuildDiffRows(dryRun.Diff));
            window.SetIssueRows(dryRun.Issues);
            window.SetDistribution(dryRun, settings);
            window.Show();
            window.Focus();
        }

        /// <summary>
        /// Diff/Issues の2タブでウィンドウを開き、Diff タブに「この内容で Apply」ボタンを表示する。
        /// 確認ダイアログの「詳細を見る」から開かれることを想定する。
        /// </summary>
        /// <param name="settings">
        /// Distribution タブの算出に使う。null または <paramref name="dryRun"/>.After が null の場合、Distribution タブは表示しない。
        /// </param>
        public static void Show(DryRunResult dryRun, string title, Action onApply, AddressableAssetSettings settings = null)
        {
            var window = GetOrCreateWindow(title);
            window._showDiffTab = true;
            window._currentTab = Tab.Diff;
            window._onApply = onApply;
            window._groupsToCreate = dryRun.GroupsToCreate;
            window._notice = null; // ウィンドウ再利用時に前回の notice が残らないようにクリア
            window.SetDiffRows(AddressTellerResultWindowRows.BuildDiffRows(dryRun.Diff));
            window.SetIssueRows(dryRun.Issues);
            window.SetDistribution(dryRun, settings);
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
            window._notice = null; // ウィンドウ再利用時に前回の notice が残らないようにクリア
            window.SetDiffRows(new List<DiffRow>());
            window.SetIssueRows(issues);
            window.SetDistribution(default, null);
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

        /// <summary>
        /// 論理バンドル分布サマリを算出し、フィールドにキャッシュする。OnGUI では再計算しない。
        /// settings が null、または dryRun.After が null の場合、Distribution タブは表示しない。
        /// </summary>
        private void SetDistribution(DryRunResult dryRun, AddressableAssetSettings settings)
        {
            _distribution = null;
            _distributionSummary = null;
            _showDistributionTab = false;

            if (settings == null || dryRun.After == null) return;

            try
            {
                _distribution = BundleDistributionSummarizer.Build(dryRun.After, settings);
                _distributionSummary = BundleDistributionSummarizer.Summarize(_distribution);
                _showDistributionTab = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressTeller] Failed to calculate logical bundle distribution summary; the Distribution tab will not be shown: {ex.Message}");
                _distribution = null;
                _distributionSummary = null;
                _showDistributionTab = false;
            }
        }

        private void EnsureDiffTreeView()
        {
            if (_diffTreeView != null) return;

            _diffTreeViewState ??= new TreeViewState<int>();
            _diffHeaderState ??= AddressTellerDiffTreeView.CreateHeaderState();

            var header = new MultiColumnHeader(_diffHeaderState);
            header.ResizeToFit();
            _diffTreeView = new AddressTellerDiffTreeView(_diffTreeViewState, header);
        }

        private void EnsureIssueTreeView()
        {
            if (_issueTreeView != null) return;

            _issueTreeViewState ??= new TreeViewState<int>();
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
            if (!string.IsNullOrEmpty(_notice))
                EditorGUILayout.HelpBox(_notice, MessageType.Info);

            if (_showDiffTab)
            {
                var labels = _showDistributionTab ? TabLabelsWithDistribution : TabLabelsWithoutDistribution;

                EditorGUILayout.Space(2);
                _currentTab = (Tab)GUILayout.Toolbar((int)_currentTab, labels);
                EditorGUILayout.Space(2);

                // Distribution タブが非表示のときに Tab.Distribution が選択されたままになるのを防ぐ。
                if (_currentTab == Tab.Distribution && !_showDistributionTab)
                    _currentTab = Tab.Diff;
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
                case Tab.Distribution:
                    DrawDistributionTab();
                    break;
            }
        }

        private void DrawDiffTab()
        {
            EnsureDiffTreeView();

            if (_diffRows.Count == 0)
            {
                EditorGUILayout.HelpBox("No changes detected.", MessageType.Info);
                DrawGroupsToCreateSummary();
                DrawApplyButton();
                return;
            }

            var summary = $"Added: {_diffRows.Count(r => r.Kind == DiffRowKind.Added)} / " +
                           $"Removed: {_diffRows.Count(r => r.Kind == DiffRowKind.Removed)} / " +
                           $"Changed: {_diffRows.Count(r => r.Kind == DiffRowKind.Changed)}";
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
                $"Groups to be created: {_groupsToCreate.Count} ({string.Join(", ", _groupsToCreate)})",
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
            if (GUILayout.Button("Apply with this content"))
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
                EditorGUILayout.HelpBox("No issues found.", MessageType.Info);
                return;
            }

            var rect = GUILayoutUtility.GetRect(0, 100000, 0, 100000);
            _issueTreeView.OnGUI(rect);
        }

        /// <summary>ステータスごとの表示トグルを描画する。トグル変更時のみフィルタを再適用する。</summary>
        private void DrawStatusFilterToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Show:", GUILayout.Width(40));

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

        /// <summary>論理バンドル分布サマリを表示する。算出済みの値（SetDistribution でキャッシュ済み）のみ参照する。</summary>
        private void DrawDistributionTab()
        {
            if (_distribution == null || _distributionSummary == null)
            {
                EditorGUILayout.HelpBox("No distribution data.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"Logical bundle count: {_distributionSummary.TotalLogicalBundleCount}");
            EditorGUILayout.LabelField($"Groups with unknown BundleMode: {_distributionSummary.UnknownGroupCount}");

            var largest = _distributionSummary.LargestBundle;
            if (largest != null)
            {
                EditorGUILayout.LabelField(
                    $"Largest consolidated bundle: {largest.GroupName} / {largest.SplitKey} ({largest.AssetCount} assets)");
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(AddressTellerReportBuilder.BundleDistributionDisclaimer, EditorStyles.miniLabel, GUILayout.MaxWidth(position.width - 10));

            EditorGUILayout.Space(4);
            _distributionScroll = EditorGUILayout.BeginScrollView(_distributionScroll);
            foreach (var bundle in _distribution.Bundles)
            {
                EditorGUILayout.LabelField(
                    $"{bundle.GroupName} / {bundle.Mode} / {bundle.SplitKey} : {bundle.AssetCount}");
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Export CSV..."))
                ExportDistribution("csv", "csv");
            if (GUILayout.Button("Export Markdown..."))
                ExportDistribution("markdown", "md");
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 算出済みの論理バンドル分布を、ユーザーが選択したファイルに書き出す。
        /// </summary>
        /// <param name="format">"csv" または "markdown"。<see cref="BundleDistributionSerializer.WriteToFile"/> に渡す。</param>
        /// <param name="extension">保存ダイアログのデフォルト拡張子。</param>
        private void ExportDistribution(string format, string extension)
        {
            var path = EditorUtility.SaveFilePanel("Export Bundle Distribution", string.Empty, $"bundle-distribution.{extension}", extension);
            if (string.IsNullOrEmpty(path)) return;

            BundleDistributionSerializer.WriteToFile(path, _distribution, _distributionSummary, format);
        }
    }
}
