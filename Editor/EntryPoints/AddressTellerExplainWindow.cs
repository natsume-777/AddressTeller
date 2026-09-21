using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AddressTeller.Editor
{
    /// <summary>
    /// 選択中のアセットについて、各ルールがマッチ／非マッチ／エラーになった理由と
    /// 最終的な検証結果（<see cref="AssetExplanation.Validation"/>）を表示するウィンドウ。
    /// 書き込みは行わない読み取り専用の表示。
    /// </summary>
    internal sealed class AddressTellerExplainWindow : EditorWindow
    {
        private List<AssetExplanation> _explanations = new();
        private IReadOnlyList<ValidationResult> _configureFailures = Array.Empty<ValidationResult>();

        /// <summary>結果を渡してウィンドウを開く。既存ウィンドウがあれば再利用する。</summary>
        /// <param name="configureFailures">
        /// ルールクラスの Configure() が例外を送出した場合の失敗一覧（<see cref="RuleExplainService"/> の
        /// out 引数付き Explain オーバーロード参照）。1件以上あれば、ウィンドウ先頭に警告として提示する。
        /// </param>
        public static void ShowWindow(IReadOnlyList<AssetExplanation> explanations, IReadOnlyList<ValidationResult> configureFailures = null)
        {
            var window = GetWindow<AddressTellerExplainWindow>();
            window.titleContent = new GUIContent("AddressTeller - Explain");
            window.minSize = new Vector2(480, 320);
            window.SetExplanations(explanations, configureFailures);
            window.Show();
            window.Focus();
        }

        private void SetExplanations(IReadOnlyList<AssetExplanation> explanations, IReadOnlyList<ValidationResult> configureFailures)
        {
            _explanations = explanations?.ToList() ?? new List<AssetExplanation>();
            _configureFailures = configureFailures ?? Array.Empty<ValidationResult>();
            // データ更新時は UI を再構築する
            var root = rootVisualElement;
            root.Clear();
            BuildUI(root);
        }

        public void CreateGUI()
        {
            // USS ロード
            var commonSS = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.natsume777.addressteller/Editor/EntryPoints/StyleSheets/AddressTellerCommon.uss");
            var windowSS = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.natsume777.addressteller/Editor/EntryPoints/StyleSheets/AddressTellerExplainWindow.uss");
            if (commonSS != null) rootVisualElement.styleSheets.Add(commonSS);
            if (windowSS != null) rootVisualElement.styleSheets.Add(windowSS);

            BuildUI(rootVisualElement);
        }

        private void BuildUI(VisualElement root)
        {
            root.Clear();

            // ルールクラスの Configure() が例外を送出したルールはどのアセットの評価にも一切現れないため、
            // 個々のアセット行とは別に、ウィンドウ先頭でまとめて警告する。
            if (_configureFailures.Count > 0)
                root.Add(new HelpBox(
                    $"{_configureFailures.Count} rule(s) failed to configure and were skipped: "
                        + $"{string.Join("; ", _configureFailures.Select(f => f.Message))}",
                    HelpBoxMessageType.Warning));

            if (_explanations.Count == 0)
            {
                root.Add(new HelpBox("No asset is selected.", HelpBoxMessageType.Info));
                return;
            }

            var scroll = new ScrollView();
            root.Add(scroll);

            foreach (var explanation in _explanations)
                scroll.Add(BuildAssetElement(explanation));
        }

        private static VisualElement BuildAssetElement(AssetExplanation explanation)
        {
            var container = new VisualElement();
            container.AddToClassList("at-asset-box");

            // アセットアイコン付きの Foldout
            var foldout = new Foldout { text = explanation.AssetPath, value = true };
            var icon = AssetDatabase.GetCachedIcon(explanation.AssetPath);
            if (icon != null)
            {
                // Foldout のヘッダにアイコンを添える（UIElements ではカスタム header content で代用）
                foldout.style.backgroundImage = null; // アイコンは別要素
            }
            container.Add(foldout);

            if (explanation.IsExcluded)
            {
                foldout.Add(new Label("This path is excluded from rule evaluation.")
                    { style = { marginLeft = 16 } });
                return container;
            }

            // 結論行
            var (text, cssClass) = DescribeConclusion(explanation.Validation, explanation.Explanation.Resolution);
            var conclusionLabel = new Label("Conclusion: " + text);
            conclusionLabel.AddToClassList("at-conclusion");
            conclusionLabel.AddToClassList(cssClass);
            foldout.Add(conclusionLabel);

            // 詳細行。Order による優先順位判定（採用/敗北）はアドレス候補群にしか意味を持たないため、
            // 候補が1件も無い（AddressSelector を持つルールがそもそも無い/どれもマッチしなかった）場合は
            // null のままにし、Matched の行に注記を付けない。
            var minOrder = MinOrderOrNull(explanation.Explanation.Resolution.AddressCandidates);
            var isConflict = explanation.Validation.Status == ValidationStatus.ConflictingAddress;
            foreach (var detail in explanation.Explanation.Details)
                foldout.Add(BuildDetailElement(detail, minOrder, isConflict));

            return container;
        }

        /// <summary>候補群のうち最小の Order を返す。候補が無ければ null。</summary>
        private static int? MinOrderOrNull(IReadOnlyList<AddressCandidate> candidates)
        {
            if (candidates.Count == 0) return null;
            var min = candidates[0].Order;
            for (var i = 1; i < candidates.Count; i++)
                if (candidates[i].Order < min) min = candidates[i].Order;
            return min;
        }

        private static VisualElement BuildDetailElement(RuleEvaluationDetail detail, int? minOrder, bool isConflict)
        {
            switch (detail.Outcome)
            {
                case RuleMatchOutcome.Matched:
                    return BuildMatchedElement(detail, minOrder, isConflict);
                case RuleMatchOutcome.NotMatched:
                    return BuildNotMatchedElement(detail);
                case RuleMatchOutcome.Errored:
                    return BuildErroredElement(detail);
                case RuleMatchOutcome.Skipped:
                    return BuildSkippedElement(detail);
                default:
                    return new VisualElement();
            }
        }

        private static VisualElement BuildMatchedElement(RuleEvaluationDetail detail, int? minOrder, bool isConflict)
        {
            var container = new VisualElement();
            container.AddToClassList("at-detail-indent");

            var title = string.IsNullOrEmpty(detail.Description)
                ? $"[Match] {detail.RuleSource}"
                : $"[Match] {detail.Description}";

            // このルールがアドレス候補を発行した場合のみ、Order による優先順位の結果を注記する。
            // 採用（最小 Order を単独で持つ）／同点で衝突中／他ルールに優先度で敗北、の3パターン。
            if (!string.IsNullOrEmpty(detail.ProducedAddress) && minOrder.HasValue)
            {
                title += detail.Order == minOrder.Value
                    ? (isConflict ? " (tied for priority)" : " (adopted)")
                    : " (superseded by a higher-priority rule)";
            }

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("at-match-label");
            container.Add(titleLabel);

            var inner = new VisualElement();
            inner.AddToClassList("at-detail-indent-inner");
            inner.Add(new Label($"Group: {AddressRuleBuilderImpl.DisplayGroupName(detail.GroupName)}"));

            if (!string.IsNullOrEmpty(detail.ProducedAddress))
                inner.Add(new Label($"Address: {detail.ProducedAddress}"));

            if (detail.ProducedLabels.Count > 0)
                inner.Add(new Label($"Labels: {string.Join(", ", detail.ProducedLabels)}"));

            container.Add(inner);
            return container;
        }

        private static VisualElement BuildNotMatchedElement(RuleEvaluationDetail detail)
        {
            var title = !string.IsNullOrEmpty(detail.Description)
                ? $"[No Match] {detail.Description}"
                : $"[No Match] {detail.RuleSource}";

            var label = new Label(title);
            label.AddToClassList("at-nomatch-label");
            label.AddToClassList("at-detail-indent");
            return label;
        }

        private static VisualElement BuildSkippedElement(RuleEvaluationDetail detail)
        {
            var title = !string.IsNullOrEmpty(detail.Description)
                ? $"[Skipped] {detail.Description}"
                : $"[Skipped] {detail.RuleSource}";

            var container = new VisualElement();
            container.AddToClassList("at-detail-indent");

            var label = new Label(title);
            label.AddToClassList("at-nomatch-label");
            container.Add(label);

            if (!string.IsNullOrEmpty(detail.ErrorMessage))
            {
                var reasonLabel = new Label(detail.ErrorMessage);
                reasonLabel.AddToClassList("at-detail-indent-inner");
                container.Add(reasonLabel);
            }

            return container;
        }

        private static VisualElement BuildErroredElement(RuleEvaluationDetail detail)
        {
            var container = new VisualElement();
            container.AddToClassList("at-detail-indent");

            var errLabel = new Label($"[Error] {detail.RuleSource}");
            errLabel.AddToClassList("at-error-label");
            container.Add(errLabel);

            var msgLabel = new Label(detail.ErrorMessage ?? string.Empty);
            msgLabel.AddToClassList("at-detail-indent-inner");
            container.Add(msgLabel);
            return container;
        }

        // テストから直接呼べるよう internal にしている。
        // 戻り値の第2要素は USS クラス名（at-conclusion--ok / --error / --warning / --muted）。
        internal static (string text, string cssClass) DescribeConclusion(ValidationResult validation, AddressResolution resolution)
        {
            // ルールPredicateの例外は、他にマッチするルールが無い場合 Validation.Status が Skipped になり
            // 結論欄だけでは原因（ルール例外）が分からなくなる。Skipped より優先して表示する。
            if (resolution.Errors.Count > 0 && validation.Status == ValidationStatus.Skipped)
            {
                var ruleNames = string.Join(", ", resolution.Errors.Select(e => e.RuleSource));
                return ($"{resolution.Errors.Count} rule error(s): {ruleNames}", "at-conclusion--error");
            }

            switch (validation.Status)
            {
                case ValidationStatus.Ok:
                    var address = DescribeWinningAddress(resolution);
                    return ($"Address \"{address}\" assigned", "at-conclusion--ok");
                case ValidationStatus.Skipped:
                    return ("No matching rule (excluded)", "at-conclusion--muted");
                case ValidationStatus.LabelsOnly:
                    return ("Labels only (no address rule matched)", "at-conclusion--muted");
                case ValidationStatus.ConflictingAddress:
                    return ($"Conflict: {validation.Message}", "at-conclusion--error");
                case ValidationStatus.GroupNotFound:
                    return ($"Group not found: {validation.Message}", "at-conclusion--error");
                case ValidationStatus.InvalidAddress:
                    return ($"Invalid address: {validation.Message}", "at-conclusion--error");
                case ValidationStatus.RuleError:
                    return ($"Rule error: {validation.Message}", "at-conclusion--error");
                case ValidationStatus.GroupWillBeCreated:
                    var createdAddress = DescribeWinningAddress(resolution);
                    return ($"Address \"{createdAddress}\" assigned (group will be created: {validation.Message})", "at-conclusion--warning");
                case ValidationStatus.GroupCreationFailed:
                    return ($"Group creation failed: {validation.Message}", "at-conclusion--error");
                case ValidationStatus.DefaultGroupUnavailable:
                    return ($"DefaultGroup unavailable: {validation.Message}", "at-conclusion--error");
                default:
                    return (validation.Message ?? string.Empty, "at-conclusion--muted");
            }
        }

        /// <summary>
        /// Ok/GroupWillBeCreated（=単独勝者が確定済み）のときに採用されたアドレスを返す。
        /// index 0 が常に最小 Order とは限らないため、明示的に最小値を探す
        /// （AddressTellerApplier.TrySelectWinningCandidate と同じ判定をここでも独立に行う）。
        /// </summary>
        private static string DescribeWinningAddress(AddressResolution resolution)
        {
            if (resolution.AddressCandidates.Count == 0) return "(unknown)";

            var winner = resolution.AddressCandidates[0];
            for (var i = 1; i < resolution.AddressCandidates.Count; i++)
                if (resolution.AddressCandidates[i].Order < winner.Order) winner = resolution.AddressCandidates[i];

            return winner.Address;
        }
    }
}
