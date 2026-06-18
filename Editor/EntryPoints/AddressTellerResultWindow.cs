using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AddressTeller.Editor
{
    /// <summary>
    /// dry-run の差分（<see cref="DryRunResult"/>）や Apply/Validate の問題点
    /// （<see cref="ValidationResult"/>）を MultiColumnTreeView で表示する結果ウィンドウ。
    /// </summary>
    internal sealed class AddressTellerResultWindow : EditorWindow
    {
        private enum Tab
        {
            Diff,
            Issues,
            Distribution,
        }

        // _showDiffTab/_showDistributionTab はドメインリロード後にリセットされ、
        // タブ構成が Issues のみに縮退する（_diffRows 等の非シリアライズフィールドも空になるため、
        // リロード後の再表示は元々想定していない割り切り）。
        private Tab _currentTab;
        private bool _showDiffTab;
        private bool _showDistributionTab;

        // Apply実行用のコールバック。ドメインリロードを跨ぐとデリゲートは復元できないため、
        // リロード後は null（＝ボタン非表示）になることを許容する割り切り。
        private Action _onApply;

        // Show() 時に一度だけ構築し、UI 更新では再構築しない（GUIDToAssetPath 等の重い変換を避けるため）。
        private List<DiffRow> _diffRows = new();
        private List<IssueRow> _allIssueRows = new();
        private IReadOnlyList<string> _groupsToCreate = Array.Empty<string>();

        // Distribution タブの集計結果。Show() 時に一度だけ算出する。
        // 算出失敗・対象外（After が null 等）の場合は null（タブ自体を非表示にする）。
        private BundleDistribution _distribution;
        private DistributionSummary _distributionSummary;

        // Issues タブのステータス別フィルタ。キーは Enum.GetValues(typeof(ValidationStatus)) の全件、初期値は true（全件表示）。
        private readonly Dictionary<ValidationStatus, bool> _statusFilter = new();

        // タブ描画より前に HelpBox として表示する任意の注意文。スコープ付きプレビューでの案内に使う。
        private string _notice;

        // UI 要素への参照（データ更新時に再バインドするため保持）
        private AddressTellerDiffTreeView _diffTreeView;
        private AddressTellerIssueTreeView _issueTreeView;
        private VisualElement _diffTabContent;
        private VisualElement _issueTabContent;
        private VisualElement _distributionTabContent;
        private VisualElement _tabBar;
        private HelpBox _noticeBox;

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
            window._onApply = null;
            window._groupsToCreate = dryRun.GroupsToCreate;
            window._notice = notice;
            window.SetDiffRows(AddressTellerResultWindowRows.BuildDiffRows(dryRun.Diff));
            window.SetIssueRows(dryRun.Issues);
            window.SetDistribution(dryRun, settings);
            window.RebuildUI();
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
            window._notice = null;
            window.SetDiffRows(AddressTellerResultWindowRows.BuildDiffRows(dryRun.Diff));
            window.SetIssueRows(dryRun.Issues);
            window.SetDistribution(dryRun, settings);
            window.RebuildUI();
            window.Show();
            window.Focus();
        }

        /// <summary>Issues のみをタブ固定で表示する。ApplyAll/ValidateAll の結果表示用。</summary>
        public static void Show(IReadOnlyList<ValidationResult> issues, string title)
        {
            var window = GetOrCreateWindow(title);
            window._showDiffTab = false;
            window._currentTab = Tab.Issues;
            window._onApply = null;
            window._groupsToCreate = Array.Empty<string>();
            window._notice = null;
            window.SetDiffRows(new List<DiffRow>());
            window.SetIssueRows(issues);
            window.SetDistribution(default, null);
            window.RebuildUI();
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
        }

        private void SetIssueRows(IReadOnlyList<ValidationResult> issues)
        {
            _allIssueRows = AddressTellerResultWindowRows.BuildIssueRows(issues ?? Array.Empty<ValidationResult>());

            _statusFilter.Clear();
            foreach (ValidationStatus status in Enum.GetValues(typeof(ValidationStatus)))
                _statusFilter[status] = true;
        }

        /// <summary>
        /// 論理バンドル分布サマリを算出し、フィールドにキャッシュする。
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
            }
        }

        /// <summary>現在のステータスフィルタに基づき、Issue ツリービューを再バインドする。フィルタ変更時のみ呼ぶ。</summary>
        private void ApplyIssueFilter()
        {
            var filtered = _allIssueRows.Where(r => _statusFilter.TryGetValue(r.Status, out var enabled) && enabled).ToList();
            _issueTreeView?.SetRows(filtered);
        }

        public void CreateGUI()
        {
            // USS ロード
            var commonSS = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.natsume777.addressteller/Editor/EntryPoints/StyleSheets/AddressTellerCommon.uss");
            var windowSS = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.natsume777.addressteller/Editor/EntryPoints/StyleSheets/AddressTellerResultWindow.uss");
            if (commonSS != null) rootVisualElement.styleSheets.Add(commonSS);
            if (windowSS != null) rootVisualElement.styleSheets.Add(windowSS);

            RebuildUI();
        }

        /// <summary>Show() 呼び出し時・初回 CreateGUI 時に rootVisualElement を再構築する。</summary>
        private void RebuildUI()
        {
            var root = rootVisualElement;
            root.Clear();

            // 注意文
            _noticeBox = new HelpBox(_notice ?? string.Empty, HelpBoxMessageType.Info);
            _noticeBox.style.display = string.IsNullOrEmpty(_notice) ? DisplayStyle.None : DisplayStyle.Flex;
            root.Add(_noticeBox);

            // タブバー
            _tabBar = new VisualElement();
            _tabBar.AddToClassList("at-tab-bar");
            root.Add(_tabBar);

            // 各タブのコンテンツ領域
            _diffTabContent = new VisualElement();
            _diffTabContent.AddToClassList("at-tab-content");
            _issueTabContent = new VisualElement();
            _issueTabContent.AddToClassList("at-tab-content");
            _distributionTabContent = new VisualElement();
            _distributionTabContent.AddToClassList("at-tab-content");
            root.Add(_diffTabContent);
            root.Add(_issueTabContent);
            root.Add(_distributionTabContent);

            // Diff タブの内容
            BuildDiffTabContent();

            // Issues タブの内容
            BuildIssueTabContent();

            // Distribution タブの内容
            BuildDistributionTabContent();

            // タブボタンを並べる
            if (_showDiffTab)
            {
                AddTabButton("Diff", Tab.Diff);
                AddTabButton("Issues", Tab.Issues);
                if (_showDistributionTab)
                    AddTabButton("Distribution", Tab.Distribution);
            }
            else
            {
                // Diff タブなし：Issues タブのみ（ボタン不要、タブバー自体を非表示）
                _tabBar.style.display = DisplayStyle.None;
                _currentTab = Tab.Issues;
            }

            SwitchTab(_currentTab);
        }

        private void AddTabButton(string label, Tab tab)
        {
            var btn = new Button(() => SwitchTab(tab)) { text = label, name = $"tab-{tab}" };
            btn.style.marginRight = 2;
            _tabBar.Add(btn);
        }

        private void SwitchTab(Tab tab)
        {
            _currentTab = tab;

            _diffTabContent.style.display = tab == Tab.Diff ? DisplayStyle.Flex : DisplayStyle.None;
            _issueTabContent.style.display = tab == Tab.Issues ? DisplayStyle.Flex : DisplayStyle.None;
            _distributionTabContent.style.display = tab == Tab.Distribution ? DisplayStyle.Flex : DisplayStyle.None;

            // アクティブタブのボタンを太字にする（簡易ハイライト）
            foreach (var btn in _tabBar.Children().OfType<Button>())
                btn.style.unityFontStyleAndWeight = btn.name == $"tab-{tab}" ? FontStyle.Bold : FontStyle.Normal;
        }

        private void BuildDiffTabContent()
        {
            _diffTabContent.Clear();

            var summary = $"Added: {_diffRows.Count(r => r.Kind == DiffRowKind.Added)} / " +
                          $"Removed: {_diffRows.Count(r => r.Kind == DiffRowKind.Removed)} / " +
                          $"Changed: {_diffRows.Count(r => r.Kind == DiffRowKind.Changed)}";
            var summaryLabel = new Label(summary);
            summaryLabel.AddToClassList("at-summary-label");
            _diffTabContent.Add(summaryLabel);

            if (_groupsToCreate.Count > 0)
            {
                var groupsLabel = new Label(
                    $"Groups to be created: {_groupsToCreate.Count} ({string.Join(", ", _groupsToCreate)})");
                groupsLabel.AddToClassList("at-summary-label");
                _diffTabContent.Add(groupsLabel);
            }

            if (_diffRows.Count == 0)
            {
                _diffTabContent.Add(new HelpBox("No changes detected.", HelpBoxMessageType.Info));
            }
            else
            {
                _diffTreeView = new AddressTellerDiffTreeView();
                _diffTreeView.SetRows(_diffRows);
                _diffTabContent.Add(_diffTreeView);
            }

            // 確認ダイアログ経由で開かれた場合のみ Apply ボタンを表示する
            if (_onApply != null)
            {
                var applyBtn = new Button(() =>
                {
                    var apply = _onApply;
                    Close();
                    apply();
                }) { text = "Apply with this content" };
                applyBtn.AddToClassList("at-apply-btn");
                _diffTabContent.Add(applyBtn);
            }
        }

        private void BuildIssueTabContent()
        {
            _issueTabContent.Clear();

            // ステータスフィルタ行
            var filterRow = new VisualElement();
            filterRow.AddToClassList("at-filter-row");
            var filterLabel = new Label("Show:");
            filterLabel.AddToClassList("at-filter-label");
            filterRow.Add(filterLabel);

            foreach (ValidationStatus status in Enum.GetValues(typeof(ValidationStatus)))
            {
                var s = status; // クロージャ用
                var toggle = new ToolbarToggle
                {
                    text = status.ToString(),
                    value = _statusFilter.TryGetValue(status, out var enabled) && enabled,
                };
                toggle.RegisterValueChangedCallback(e =>
                {
                    _statusFilter[s] = e.newValue;
                    ApplyIssueFilter();
                });
                filterRow.Add(toggle);
            }
            _issueTabContent.Add(filterRow);

            if (_allIssueRows.Count == 0)
            {
                _issueTabContent.Add(new HelpBox("No issues found.", HelpBoxMessageType.Info));
                return;
            }

            _issueTreeView = new AddressTellerIssueTreeView();
            ApplyIssueFilter();
            _issueTabContent.Add(_issueTreeView);
        }

        private void BuildDistributionTabContent()
        {
            _distributionTabContent.Clear();

            if (_distribution == null || _distributionSummary == null)
            {
                _distributionTabContent.Add(new HelpBox("No distribution data.", HelpBoxMessageType.Info));
                return;
            }

            _distributionTabContent.Add(new Label($"Logical bundle count: {_distributionSummary.TotalLogicalBundleCount}"));
            _distributionTabContent.Add(new Label($"Groups with unknown BundleMode: {_distributionSummary.UnknownGroupCount}"));

            var largest = _distributionSummary.LargestBundle;
            if (largest != null)
            {
                _distributionTabContent.Add(new Label(
                    $"Largest consolidated bundle: {largest.GroupName} / {largest.SplitKey} ({largest.AssetCount} assets)"));
            }

            var disclaimerLabel = new Label(AddressTellerReportBuilder.BundleDistributionDisclaimer);
            disclaimerLabel.AddToClassList("at-detail-label");
            _distributionTabContent.Add(disclaimerLabel);

            var scroll = new ScrollView();
            scroll.AddToClassList("at-distribution-scroll");
            foreach (var bundle in _distribution.Bundles)
            {
                scroll.Add(new Label($"{bundle.GroupName} / {bundle.Mode} / {bundle.SplitKey} : {bundle.AssetCount}"));
            }
            _distributionTabContent.Add(scroll);

            var btnRow = new VisualElement();
            btnRow.AddToClassList("at-export-row");
            btnRow.Add(new Button(() => ExportDistribution("csv", "csv")) { text = "Export CSV..." });
            btnRow.Add(new Button(() => ExportDistribution("markdown", "md")) { text = "Export Markdown..." });
            _distributionTabContent.Add(btnRow);
        }

        /// <summary>算出済みの論理バンドル分布をユーザーが選択したファイルに書き出す。</summary>
        private void ExportDistribution(string format, string extension)
        {
            var path = EditorUtility.SaveFilePanel("Export Bundle Distribution", string.Empty, $"bundle-distribution.{extension}", extension);
            if (string.IsNullOrEmpty(path)) return;

            BundleDistributionSerializer.WriteToFile(path, _distribution, _distributionSummary, format);
        }
    }
}
