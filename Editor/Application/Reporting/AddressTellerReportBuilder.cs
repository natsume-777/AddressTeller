using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor
{
    /// <summary>
    /// <see cref="DryRunResult"/> から CI 向け構造化レポート <see cref="AddressTellerReport"/> への変換と、
    /// exit code の判定を行う。<see cref="Build(DryRunResult)"/> は dry-run の計算結果のみから組み立てる純粋関数だが、
    /// <see cref="Build(DryRunResult, AddressableAssetSettings)"/> は論理バンドル分布サマリのために
    /// <see cref="AddressableAssetSettings"/> から各グループの BundleMode を読み取る。
    /// </summary>
    internal static class AddressTellerReportBuilder
    {
        /// <summary>
        /// 論理バンドル分布サマリに付与する固定の注記文言。
        /// この分布が Predict 結果と BundleMode から算出した論理推定であり、実ビルドのバンドル数を保証しないことを明記する。
        /// </summary>
        public const string BundleDistributionDisclaimer =
            "This distribution is a logical estimate calculated from Predict results and each group's BundleMode; it does not guarantee the actual bundle count produced by an Addressables build." +
            " Known approximation differences: per-scene bundle splitting for PackTogether, folder-level grouping for PackSeparately, and label concatenation behavior for PackTogetherByLabel are not reflected.";

        /// <summary>
        /// <see cref="DryRunResult"/> を <see cref="AddressTellerReport"/> に変換する。
        /// drift / issues はいずれも path の Ordinal 順で決定的に並べる。
        /// </summary>
        public static AddressTellerReport Build(DryRunResult result)
        {
            var report = new AddressTellerReport();
            var diff = result.Diff;

            foreach (var entry in diff.Added)
            {
                report.Drift.Add(new AddressTellerReportDrift
                {
                    Guid = entry.Guid,
                    Path = AssetPathOf(entry.Guid),
                    ChangeType = "Added",
                    Before = new AddressTellerReportEntry(),
                    After = ToReportEntry(entry),
                });
            }

            foreach (var entry in diff.Removed)
            {
                report.Drift.Add(new AddressTellerReportDrift
                {
                    Guid = entry.Guid,
                    Path = AssetPathOf(entry.Guid),
                    ChangeType = "Removed",
                    Before = ToReportEntry(entry),
                    After = new AddressTellerReportEntry(),
                });
            }

            foreach (var (before, after) in diff.Changed)
            {
                report.Drift.Add(new AddressTellerReportDrift
                {
                    Guid = after.Guid,
                    Path = AssetPathOf(after.Guid),
                    ChangeType = "Changed",
                    Before = ToReportEntry(before),
                    After = ToReportEntry(after),
                });
            }

            report.Drift = report.Drift
                .OrderBy(d => d.Path, StringComparer.Ordinal)
                .ThenBy(d => d.Guid, StringComparer.Ordinal)
                .ToList();

            foreach (var issue in result.Issues)
            {
                report.Issues.Add(new AddressTellerReportIssue
                {
                    Path = issue.Context?.Path ?? string.Empty,
                    Status = issue.Status.ToString(),
                    Message = issue.Message,
                });
            }

            report.Issues = report.Issues
                .OrderBy(i => i.Path, StringComparer.Ordinal)
                .ThenBy(i => i.Status, StringComparer.Ordinal)
                .ThenBy(i => i.Message, StringComparer.Ordinal)
                .ToList();

            report.Summary.Added = diff.Added.Count;
            report.Summary.Removed = diff.Removed.Count;
            report.Summary.Changed = diff.Changed.Count;
            report.Summary.Issues = result.Issues.Count;
            report.Summary.ExitCode = DetermineExitCode(result);

            return report;
        }

        /// <summary>
        /// <see cref="Build(DryRunResult)"/> に加えて、<paramref name="settings"/> から各グループの BundleMode を読み取り、
        /// 論理バンドル分布サマリ（<see cref="AddressTellerReport.BundleDistribution"/>）を算出して格納する。
        /// <see cref="DryRunResult.After"/> が null（テスト構築等）の場合は分布サマリを付与しない。
        /// </summary>
        public static AddressTellerReport Build(DryRunResult result, AddressableAssetSettings settings)
        {
            var report = Build(result);

            if (result.After == null || settings == null) return report;

            try
            {
                var distribution = BundleDistributionSummarizer.Build(result.After, settings, out var warnings);

                foreach (var warning in warnings)
                    UnityEngine.Debug.LogWarning($"[AddressTeller] {warning}");

                report.BundleDistribution = ToBundleDistributionReport(distribution);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[AddressTeller] Failed to calculate logical bundle distribution summary; it will be omitted from the report: {ex.Message}");
            }

            return report;
        }

        /// <summary><see cref="BundleDistribution"/> を JsonUtility 向け DTO に変換する。</summary>
        private static BundleDistributionReport ToBundleDistributionReport(BundleDistribution distribution)
        {
            var bundles = distribution.Bundles
                .Select(b => new LogicalBundleDto
                {
                    GroupName = b.GroupName,
                    Mode = b.Mode.ToString(),
                    SplitKey = b.SplitKey,
                    AssetCount = b.AssetCount,
                })
                .ToArray();

            return new BundleDistributionReport
            {
                Bundles = bundles,
                TotalLogicalBundleCount = distribution.Bundles.Count(b => b.Mode != BundleModeKind.Unknown),
                UnknownGroupCount = distribution.Bundles.Count(b => b.Mode == BundleModeKind.Unknown),
                Disclaimer = BundleDistributionDisclaimer,
            };
        }

        /// <summary>
        /// exit code を判定する。
        /// 0 = 差分なし・問題なし、1 = ドリフトあり（Validation エラーなし）、2 = Validation エラーあり。
        /// 実行環境エラー（3）はここでは判定しない（CLI 側で扱う）。
        /// <see cref="DryRunResult.Issues"/> は本来 IsOk=false の結果のみを想定するが、
        /// 任意のリストを受け取れる public 関数であるため <see cref="ValidationResult.IsOk"/> で判定する。
        /// </summary>
        public static int DetermineExitCode(DryRunResult result)
        {
            if (result.Issues.Any(issue => !issue.IsOk)) return 2;
            if (!result.Diff.IsEmpty) return 1;
            return 0;
        }

        /// <summary>
        /// Apply/Validate を実際に実行した CLI 向けの exit code 判定。
        /// <paramref name="dryRun"/> による判定（<see cref="DetermineExitCode(DryRunResult)"/>）を基本としつつ、
        /// 実行後に得られた issues（<paramref name="executionIssues"/>）にエラー（IsOk=false）が
        /// 含まれる場合は 2 に昇格させる（dry-run 時点では検出できなかった問題を取り逃さないため）。
        /// </summary>
        public static int DetermineExitCode(DryRunResult dryRun, IReadOnlyList<ValidationResult> executionIssues)
        {
            var exitCode = DetermineExitCode(dryRun);
            if (exitCode < 2 && executionIssues.Any(issue => !issue.IsOk))
                exitCode = 2;

            return exitCode;
        }

        private static AddressTellerReportEntry ToReportEntry(SnapshotEntry entry) => new()
        {
            Address = entry.Address,
            GroupName = entry.GroupName,
            Labels = new List<string>(entry.Labels ?? Enumerable.Empty<string>()),
        };

        /// <summary>
        /// GUID からアセットパスを解決する。AssetDatabase への依存はこの1箇所に閉じ込める。
        /// </summary>
        private static string AssetPathOf(string guid) => UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
    }
}
