using System;
using System.Collections.Generic;
using System.Linq;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// <see cref="AssetExplanation"/> のリストから CI/外部ツール向け構造化レポート
    /// <see cref="AddressTellerExplainReport"/> への変換を行う。
    /// Addressables / AssetDatabase に依存しない純粋関数として、Explain の計算結果のみから組み立てる。
    /// </summary>
    public static class AddressTellerExplainReportBuilder
    {
        /// <summary>
        /// <paramref name="explanations"/> を <see cref="AddressTellerExplainReport"/> に変換する。
        /// Assets は Path の Ordinal 順で決定的に並べる。
        /// </summary>
        public static AddressTellerExplainReport Build(IReadOnlyList<AssetExplanation> explanations)
        {
            var report = new AddressTellerExplainReport();

            foreach (var explanation in explanations)
            {
                var asset = new AddressTellerExplainAsset
                {
                    Path = explanation.AssetPath,
                    IsExcluded = explanation.IsExcluded,
                    ValidationStatus = explanation.Validation != null ? explanation.Validation.Status.ToString() : string.Empty,
                    ValidationMessage = explanation.Validation?.Message ?? string.Empty,
                };

                if (explanation.Explanation != null)
                {
                    foreach (var detail in explanation.Explanation.Details)
                    {
                        asset.Rules.Add(new AddressTellerExplainRule
                        {
                            RuleSource = detail.RuleSource,
                            GroupName = detail.GroupName,
                            Description = detail.Description ?? string.Empty,
                            Outcome = detail.Outcome.ToString(),
                            ProducedAddress = detail.ProducedAddress ?? string.Empty,
                            ProducedLabels = new List<string>(detail.ProducedLabels),
                            ErrorMessage = detail.ErrorMessage ?? string.Empty,
                        });
                    }
                }

                report.Assets.Add(asset);
            }

            report.Assets = report.Assets
                .OrderBy(a => a.Path, StringComparer.Ordinal)
                .ToList();

            return report;
        }
    }
}
