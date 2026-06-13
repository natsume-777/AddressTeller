using System;
using System.Collections.Generic;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// 1アセットに対する Explain の結果。除外対象かどうかと、評価詳細・最終的な検証結果を持つ。
    /// </summary>
    public sealed class AssetExplanation
    {
        public string AssetPath { get; }

        /// <summary>AssetFilter.ShouldExclude により評価対象外と判定された場合 true。</summary>
        public bool IsExcluded { get; }

        /// <summary>ルールごとの評価詳細。除外時は null。</summary>
        public RuleExplanation Explanation { get; }

        /// <summary>Apply/Validate と同一ロジックによる結論。除外時は null。</summary>
        public ValidationResult Validation { get; }

        public AssetExplanation(string assetPath, bool isExcluded, RuleExplanation explanation, ValidationResult validation)
        {
            AssetPath = assetPath;
            IsExcluded = isExcluded;
            Explanation = explanation;
            Validation = validation;
        }
    }

    /// <summary>
    /// 指定したアセットについて、どのルールがマッチ／非マッチ／エラーになったかと
    /// 最終的な検証結果（ConflictingAddress, GroupNotFound 等）をまとめて返す。
    /// 書き込みは行わない読み取り専用処理。
    /// </summary>
    public static class RuleExplainService
    {
        /// <summary>
        /// settings が null の場合はプロジェクトのデフォルト設定を使う。Addressables 未設定の場合は空リストを返す。
        /// </summary>
        public static IReadOnlyList<AssetExplanation> Explain(IReadOnlyList<string> assetPaths, AddressableAssetSettings settings = null)
        {
            return Explain(assetPaths, settings, RuleCollector.CollectEnabledRules());
        }

        /// <summary>
        /// <see cref="Explain(IReadOnlyList{string}, AddressableAssetSettings)"/> に
        /// 評価対象ルールの注入を追加したオーバーロード。リフレクションによるルール収集
        /// （<see cref="RuleCollector.CollectEnabledRules()"/>）を経由せず、呼び出し側が用意した
        /// ルール一覧をそのまま評価に使う（テスト等での利用を想定）。
        /// </summary>
        public static IReadOnlyList<AssetExplanation> Explain(IReadOnlyList<string> assetPaths, AddressableAssetSettings settings, IReadOnlyList<AddressRuleBase> rules)
        {
            settings ??= AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return Array.Empty<AssetExplanation>();
            rules ??= Array.Empty<AddressRuleBase>();

            var setup = RuleEvaluationPipeline.BuildSetup(settings, rules);

            var results = new List<AssetExplanation>(assetPaths.Count);

            foreach (var path in assetPaths)
            {
                if (AssetFilter.ShouldExcludeByPath(path, setup.ConfigFolder))
                {
                    // パス上は除外対象だが、BuildContext が null（GUID/型が取得不能=解決不能パス）の場合は
                    // 旧経路と同じく結果に含めない（「除外」として積まない）。
                    var earlyCtx = RuleEvaluationPipeline.BuildContext(path);
                    if (earlyCtx == null) continue;

                    results.Add(new AssetExplanation(path, isExcluded: true, explanation: null, validation: null));
                    continue;
                }

                var ctx = RuleEvaluationPipeline.BuildContext(path);
                if (ctx == null) continue;

                if (AssetFilter.ShouldExclude(ctx, setup.ConfigFolder))
                {
                    results.Add(new AssetExplanation(path, isExcluded: true, explanation: null, validation: null));
                    continue;
                }

                var explanation = RuleEvaluator.Explain(ctx, setup.Entries);
                var validation = AddressTellerApplier.Validate(ctx, explanation.Resolution, setup.ExistingGroupNames, setup.AutoCreateMissingGroups);

                results.Add(new AssetExplanation(path, isExcluded: false, explanation, validation));
            }

            return results;
        }
    }
}
