using System;
using System.Collections.Generic;
using System.Linq;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// <see cref="DryRunResult"/> から CI 向け構造化レポート <see cref="AddressTellerReport"/> への変換と、
    /// exit code の判定を行う。Addressables / AssetDatabase に依存しない純粋関数として、
    /// dry-run の計算結果のみから組み立てる。
    /// </summary>
    public static class AddressTellerReportBuilder
    {
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
