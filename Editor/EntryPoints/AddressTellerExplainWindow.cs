using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// 選択中のアセットについて、各ルールがマッチ／非マッチ／エラーになった理由と
    /// 最終的な検証結果（<see cref="AssetExplanation.Validation"/>）を表示するウィンドウ。
    /// 書き込みは行わない読み取り専用の表示。
    /// </summary>
    internal sealed class AddressTellerExplainWindow : EditorWindow
    {
        private List<AssetExplanation> _explanations = new();

        // アセットパスごとの Foldout 開閉状態。デフォルトは展開。
        private readonly Dictionary<string, bool> _foldoutStates = new();

        private Vector2 _scrollPosition;

        /// <summary>結果を渡してウィンドウを開く。既存ウィンドウがあれば再利用する。</summary>
        public static void ShowWindow(IReadOnlyList<AssetExplanation> explanations)
        {
            var window = GetWindow<AddressTellerExplainWindow>();
            window.titleContent = new GUIContent("AddressTeller - Explain");
            window.minSize = new Vector2(480, 320);
            window.SetExplanations(explanations);
            window.Show();
            window.Focus();
        }

        private void SetExplanations(IReadOnlyList<AssetExplanation> explanations)
        {
            _explanations = explanations?.ToList() ?? new List<AssetExplanation>();

            // 複数選択時はデフォルト展開。既存パスの開閉状態は保持する。
            foreach (var explanation in _explanations)
            {
                if (!_foldoutStates.ContainsKey(explanation.AssetPath))
                    _foldoutStates[explanation.AssetPath] = true;
            }

            // 今回の結果に存在しないパスの開閉状態は不要なので削除する（単調増加を防ぐ）。
            var currentPaths = new HashSet<string>(_explanations.Select(e => e.AssetPath));
            foreach (var staleKey in _foldoutStates.Keys.Where(k => !currentPaths.Contains(k)).ToList())
                _foldoutStates.Remove(staleKey);
        }

        private void OnGUI()
        {
            if (_explanations.Count == 0)
            {
                EditorGUILayout.HelpBox("選択中のアセットがありません。", MessageType.Info);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            foreach (var explanation in _explanations)
                DrawAsset(explanation);

            EditorGUILayout.EndScrollView();
        }

        private void DrawAsset(AssetExplanation explanation)
        {
            var foldoutKey = explanation.AssetPath;
            var expanded = _foldoutStates.TryGetValue(foldoutKey, out var v) && v;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var header = BuildHeaderContent(explanation);
            expanded = EditorGUILayout.Foldout(expanded, header, true);
            _foldoutStates[foldoutKey] = expanded;

            if (expanded)
            {
                EditorGUI.indentLevel++;

                if (explanation.IsExcluded)
                {
                    EditorGUILayout.LabelField("このパスはルール評価の対象外です");
                }
                else
                {
                    DrawConclusion(explanation.Validation, explanation.Explanation.Resolution);
                    DrawDetails(explanation.Explanation.Details);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>アイコン + パスのヘッダ表示を組み立てる。</summary>
        private static GUIContent BuildHeaderContent(AssetExplanation explanation)
        {
            var icon = AssetDatabase.GetCachedIcon(explanation.AssetPath);
            return new GUIContent(explanation.AssetPath, icon);
        }

        /// <summary>Validation.Status に応じて結論を色分け表示する。</summary>
        private static void DrawConclusion(ValidationResult validation, AddressResolution resolution)
        {
            var (text, color) = DescribeConclusion(validation, resolution);

            var originalColor = GUI.color;
            GUI.color = color;
            EditorGUILayout.LabelField("結論: " + text, EditorStyles.boldLabel);
            GUI.color = originalColor;
        }

        // テストから直接呼べるよう internal にしている。
        internal static (string text, Color color) DescribeConclusion(ValidationResult validation, AddressResolution resolution)
        {
            // ルールPredicateの例外は、他にマッチするルールが無い場合 Validation.Status が Skipped になり
            // 結論欄だけでは原因（ルール例外）が分からなくなる。Skipped より優先して表示する。
            if (resolution.Errors.Count > 0 && validation.Status == ValidationStatus.Skipped)
            {
                var ruleNames = string.Join(", ", resolution.Errors.Select(e => e.RuleSource));
                return ($"{resolution.Errors.Count}件のルールがエラー: {ruleNames}", Color.red);
            }

            switch (validation.Status)
            {
                case ValidationStatus.Ok:
                    var address = resolution.AddressCandidates.Count > 0 ? resolution.AddressCandidates[0].Address : "(unknown)";
                    return ($"アドレス \"{address}\" を採用", Color.green);
                case ValidationStatus.Skipped:
                    return ("マッチするルールなし（対象外）", Color.gray);
                case ValidationStatus.ConflictingAddress:
                    return ($"競合: {validation.Message}", Color.red);
                case ValidationStatus.GroupNotFound:
                    return ($"グループ未検出: {validation.Message}", Color.red);
                case ValidationStatus.InvalidAddress:
                    return ($"アドレス無効: {validation.Message}", Color.red);
                case ValidationStatus.RuleError:
                    return ($"ルールエラー: {validation.Message}", Color.red);
                default:
                    return (validation.Message ?? string.Empty, GUI.color);
            }
        }

        private static void DrawDetails(IReadOnlyList<RuleEvaluationDetail> details)
        {
            foreach (var detail in details)
            {
                switch (detail.Outcome)
                {
                    case RuleMatchOutcome.Matched:
                        DrawMatchedDetail(detail);
                        break;
                    case RuleMatchOutcome.NotMatched:
                        DrawNotMatchedDetail(detail);
                        break;
                    case RuleMatchOutcome.Errored:
                        DrawErroredDetail(detail);
                        break;
                }
            }
        }

        private static void DrawMatchedDetail(RuleEvaluationDetail detail)
        {
            var originalColor = GUI.color;
            GUI.color = Color.green;

            var title = string.IsNullOrEmpty(detail.Description)
                ? $"[Match] {detail.RuleSource}"
                : $"[Match] {detail.Description}";
            EditorGUILayout.LabelField(title);

            GUI.color = originalColor;

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"Group: {detail.GroupName}");

            if (!string.IsNullOrEmpty(detail.ProducedAddress))
                EditorGUILayout.LabelField($"Address: {detail.ProducedAddress}");

            if (detail.ProducedLabels.Count > 0)
                EditorGUILayout.LabelField($"Labels: {string.Join(", ", detail.ProducedLabels)}");

            EditorGUI.indentLevel--;
        }

        private static void DrawNotMatchedDetail(RuleEvaluationDetail detail)
        {
            var originalColor = GUI.color;
            GUI.color = Color.gray;

            var title = !string.IsNullOrEmpty(detail.Description)
                ? $"[No Match] {detail.Description}"
                : $"[No Match] {detail.RuleSource}";
            EditorGUILayout.LabelField(title);

            GUI.color = originalColor;
        }

        private static void DrawErroredDetail(RuleEvaluationDetail detail)
        {
            var originalColor = GUI.color;
            GUI.color = Color.red;

            EditorGUILayout.LabelField($"[Error] {detail.RuleSource}");

            GUI.color = originalColor;

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(detail.ErrorMessage ?? string.Empty);
            EditorGUI.indentLevel--;
        }
    }
}
