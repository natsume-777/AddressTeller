using System;
using System.Collections.Generic;

namespace AddressTeller
{
    /// <summary>
    /// AssetContext に対してルールエントリを評価し AddressResolution を返す。
    /// Addressables API に依存しないモデル層。
    /// ソート・競合チェック・書き込みは呼び出し側の責務。
    /// </summary>
    internal static class RuleEvaluator
    {
        public static AddressResolution Evaluate(AssetContext context, IEnumerable<AddressRuleEntry> entries)
        {
            var candidates = new List<AddressCandidate>();
            var labels = new HashSet<string>();
            var errors = new List<RuleEvaluationError>();

            foreach (var entry in entries)
            {
                // フォルダは、ルールが IncludeFolders() で明示的に opt-in していない限り評価対象外。
                // Predicate すら呼ばない（フォルダを想定していない既存ルールの Predicate を壊さないため）。
                if (context.IsFolder && !entry.IncludesFolders)
                    continue;

                // アドレス候補を出したエントリのラベルは、その候補が勝ったときだけ書くため候補に持たせる。
                // ラベル専用エントリ（AddressSelector == null）のラベルは勝敗と無関係に書くため labels に集める。
                string address = null;
                var addressProduced = false;
                try
                {
                    if (!entry.Predicate(context))
                        continue;

                    if (entry.AddressSelector != null)
                    {
                        address = entry.AddressSelector(context);
                        addressProduced = true;
                    }

                    // 候補を出すエントリに限って配列化する（毎アセット評価されるため、ラベルが無ければ割り当てない）。
                    List<string> candidateLabels = null;
                    foreach (var labelSelector in entry.LabelSelectors)
                    {
                        var label = labelSelector(context);
                        if (string.IsNullOrEmpty(label))
                            continue;

                        if (!addressProduced)
                            labels.Add(label);
                        else if (candidateLabels == null)
                            candidateLabels = new List<string> { label };
                        else if (!candidateLabels.Contains(label))
                            candidateLabels.Add(label); // 重複除去（評価順を保つ。ラベル数は少ないため線形探索で足りる）
                    }

                    if (addressProduced)
                        candidates.Add(new AddressCandidate(entry.GroupName, address, entry.SourceClass, entry.Description, entry.RuleIndex, entry.Order, candidateLabels));
                }
                catch (Exception ex)
                {
                    // アドレス産出後に LabelSelector が例外を送出した場合も候補は残す（勝者になりうるルールの
                    // 例外が BlockedByRuleError の判定対象から落ちないようにするため）。この候補のラベルは、
                    // 勝てば BlockedByRuleError で書き込まれず、負ければ使われないため、持たせない。
                    if (addressProduced)
                        candidates.Add(new AddressCandidate(entry.GroupName, address, entry.SourceClass, entry.Description, entry.RuleIndex, entry.Order));

                    var source = AddressRuleEntry.DescribeSource(entry.SourceClass, entry.Description, entry.RuleIndex);
                    errors.Add(new RuleEvaluationError(source, ex.Message, entry.Order, entry.AddressSelector != null));
                }
            }

            return new AddressResolution(candidates, labels, errors);
        }

        /// <summary>
        /// 1アセットに対して全ルールを評価し、ルールごとの判定理由（<see cref="RuleEvaluationDetail"/>）と
        /// <see cref="Evaluate"/> と同一の集計結果（<see cref="AddressResolution"/>）を合わせて返す。
        /// Explain 機能向けの読み取り専用処理であり、Apply/Validate/Predict には使用しない。
        /// </summary>
        public static RuleExplanation Explain(AssetContext context, IReadOnlyList<AddressRuleEntry> entries)
        {
            var details = new List<RuleEvaluationDetail>(entries.Count);
            var candidates = new List<AddressCandidate>();
            var labels = new HashSet<string>();
            var errors = new List<RuleEvaluationError>();

            foreach (var entry in entries)
            {
                var ruleSource = AddressRuleEntry.DescribeSource(entry.SourceClass, entry.Description, entry.RuleIndex);

                // フォルダは、ルールが IncludeFolders() で明示的に opt-in していない限り評価対象外。
                // Predicate すら呼ばずスキップし、Explain 上でもその旨を明示する。
                if (context.IsFolder && !entry.IncludesFolders)
                {
                    details.Add(new RuleEvaluationDetail(
                        ruleSource, entry.GroupName, entry.Description,
                        RuleMatchOutcome.Skipped, null, null, "Folder asset; this rule does not call IncludeFolders().", entry.Order));
                    continue;
                }

                // Evaluate と同じ集計（候補ごとのラベル／ラベル専用ラベル）を保つ。
                string producedAddress = null;
                var addressProduced = false;
                try
                {
                    // Predicate は1回だけ評価する（副作用・コストの二重実行を避ける）。
                    if (!entry.Predicate(context))
                    {
                        details.Add(new RuleEvaluationDetail(
                            ruleSource, entry.GroupName, entry.Description,
                            RuleMatchOutcome.NotMatched, null, null, null, entry.Order));
                        continue;
                    }

                    if (entry.AddressSelector != null)
                    {
                        producedAddress = entry.AddressSelector(context);
                        addressProduced = true;
                    }

                    // producedLabels は Explain 表示用（このルールが出したラベル全部）。候補に持たせるラベルは
                    // インスタンスを共有しないよう別に集める（重複除去・評価順）。
                    var producedLabels = new List<string>();
                    List<string> candidateLabels = null;
                    foreach (var labelSelector in entry.LabelSelectors)
                    {
                        var label = labelSelector(context);
                        if (string.IsNullOrEmpty(label))
                            continue;

                        producedLabels.Add(label);
                        if (!addressProduced)
                            labels.Add(label);
                        else if (candidateLabels == null)
                            candidateLabels = new List<string> { label };
                        else if (!candidateLabels.Contains(label))
                            candidateLabels.Add(label);
                    }

                    if (addressProduced)
                        candidates.Add(new AddressCandidate(entry.GroupName, producedAddress, entry.SourceClass, entry.Description, entry.RuleIndex, entry.Order, candidateLabels));

                    details.Add(new RuleEvaluationDetail(
                        ruleSource, entry.GroupName, entry.Description,
                        RuleMatchOutcome.Matched, producedAddress, producedLabels, null, entry.Order));
                }
                catch (Exception ex)
                {
                    // Evaluate と同じ理由で、アドレス産出後の例外でも候補は残す（ラベルは持たせない）。
                    if (addressProduced)
                        candidates.Add(new AddressCandidate(entry.GroupName, producedAddress, entry.SourceClass, entry.Description, entry.RuleIndex, entry.Order));
                    errors.Add(new RuleEvaluationError(ruleSource, ex.Message, entry.Order, entry.AddressSelector != null));
                    details.Add(new RuleEvaluationDetail(
                        ruleSource, entry.GroupName, entry.Description,
                        RuleMatchOutcome.Errored, null, null, ex.Message, entry.Order));
                }
            }

            var resolution = new AddressResolution(candidates, labels, errors);
            return new RuleExplanation(context, details, resolution);
        }
    }
}
