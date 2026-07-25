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

            // 詳細行
            foreach (var detail in explanation.Explanation.Details)
                foldout.Add(BuildDetailElement(detail));

            return container;
        }

        private static VisualElement BuildDetailElement(RuleEvaluationDetail detail)
        {
            switch (detail.Outcome)
            {
                case RuleMatchOutcome.Matched:
                    return BuildMatchedElement(detail);
                case RuleMatchOutcome.NotMatched:
                    return BuildNotMatchedElement(detail);
                case RuleMatchOutcome.Errored:
                    return BuildErroredElement(detail);
                default:
                    return new VisualElement();
            }
        }

        private static VisualElement BuildMatchedElement(RuleEvaluationDetail detail)
        {
            var container = new VisualElement();
            container.AddToClassList("at-detail-indent");

            var title = string.IsNullOrEmpty(detail.Description)
                ? $"[Match] {detail.RuleSource}"
                : $"[Match] {detail.Description}";

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
                    var address = resolution.AddressCandidates.Count > 0 ? resolution.AddressCandidates[0].Address : "(unknown)";
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
                    var createdAddress = resolution.AddressCandidates.Count > 0 ? resolution.AddressCandidates[0].Address : "(unknown)";
                    return ($"Address \"{createdAddress}\" assigned (group will be created: {validation.Message})", "at-conclusion--warning");
                case ValidationStatus.GroupCreationFailed:
                    return ($"Group creation failed: {validation.Message}", "at-conclusion--error");
                case ValidationStatus.DefaultGroupUnavailable:
                    return ($"DefaultGroup unavailable: {validation.Message}", "at-conclusion--error");
                default:
                    return (validation.Message ?? string.Empty, "at-conclusion--muted");
            }
        }
    }
}
