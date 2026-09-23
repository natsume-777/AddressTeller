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
                    Blocking = issue.IsBlocking,
                    Ok = issue.IsOk,
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
            report.SchemaVersion = AddressTellerReport.CurrentSchemaVersion;

            return report;
        }

        /// <summary>
        /// <see cref="Build(DryRunResult)"/> に加えて、<paramref name="settings"/> から各グループの BundleMode を読み取り、
        /// 論理バンドル分布サマリ（<see cref="AddressTellerReport.BundleDistribution"/>）を算出して格納する。
        /// <see cref="DryRunResult.After"/> が null（テスト構築等）の場合、<paramref name="settings"/> が null の場合、
        /// または算出処理自体が例外を投げた場合（内部でキャッチし警告ログのみ出力）は、
        /// <see cref="AddressTellerReport.BundleDistribution"/> は null のまま代入されない
        /// （JsonUtility の制約上、JSON 出力上は全フィールドが既定値のオブジェクトとして現れる）。
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
                UnityEngine.Debug.LogWarning($"[AddressTeller] Failed to calculate logical bundle distribution summary; report.BundleDistribution will remain null (serialized as an all-defaults object in the JSON output): {ex.Message}");
            }

            return report;
        }

        /// <summary><see cref="BundleDistribution"/> を JsonUtility 向け DTO に変換する。</summary>
        private static BundleDistributionReport ToBundleDistributionReport(BundleDistribution distribution)
        {
            var bundles = distribution.Bundles
                .Select(b => new BundleDistributionReportEntry
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
        /// 1件の <see cref="ValidationResult"/> が <see cref="AddressTellerMenu.CheckCLI"/> の exit code 判定
        /// （<see cref="DetermineExitCode(DryRunResult)"/>）で問題として数えられるかどうか。CheckCLI は
        /// 読み取り専用で <see cref="ValidationStatus.DuplicateAddress"/> のような書き込み専用の例外を
        /// 設けないため、単純に <see cref="ValidationResult.IsOk"/> の否定で判定する
        /// （Apply 系の基準は <see cref="ValidationResult.IsBlocking"/> を参照——DuplicateAddress の
        /// 扱いが異なる）。
        /// </summary>
        internal static bool IsCheckCliFailing(ValidationResult issue) => !issue.IsOk;

        /// <summary>
        /// <paramref name="issues"/> から、<see cref="IsCheckCliFailing"/> を満たす（＝CheckCLI の exit code
        /// 判定に反映される）Status 名の集合を作る。<see cref="AddressTellerMenu.CheckCLI"/> が JUnit 出力
        /// （<see cref="AddressTellerReportWriter.ToJUnitXml(AddressTellerReport, bool, IReadOnlyCollection{string})"/>）
        /// の failure 対象を絞り込むために使う。
        /// </summary>
        internal static IReadOnlyList<string> BuildCheckCliFailingStatusNames(IReadOnlyList<ValidationResult> issues) =>
            issues.Where(IsCheckCliFailing).Select(i => i.Status.ToString()).Distinct().ToList();

        /// <summary>
        /// <paramref name="issues"/> から、<see cref="ValidationResult.IsBlocking"/> を満たす
        /// （＝Apply 系の exit code 判定に反映される）Status 名の集合を作る。
        /// <see cref="AddressTellerMenu.ApplyAllCLI"/> / <see cref="AddressTellerMenu.ApplyWithValidateCLI"/>
        /// が JUnit 出力の failure 対象を絞り込むために使う。
        /// </summary>
        internal static IReadOnlyList<string> BuildApplyFailingStatusNames(IReadOnlyList<ValidationResult> issues) =>
            issues.Where(i => i.IsBlocking).Select(i => i.Status.ToString()).Distinct().ToList();

        /// <summary>
        /// <see cref="AddressTellerMenu.CheckCLI"/> 向けの exit code 判定。
        /// 0 = 差分なし・問題なし、1 = ドリフトあり（Validation エラーなし）、2 = Validation エラーあり。
        /// 実行環境エラー（3）はここでは判定しない（CLI 側で扱う）。
        /// <see cref="DryRunResult.Issues"/> は本来 IsOk=false の結果のみを想定するが、
        /// 任意のリストを受け取れる public 関数であるため <see cref="IsCheckCliFailing"/> で判定する。
        /// CheckCLI は読み取り専用（書き込みを行わない）ため、差分の有無自体が意味のある報告内容であり、
        /// exit code 1（ドリフトあり）を返してよい。書き込みを行う Apply 系の判定は
        /// <see cref="DetermineApplyExitCode"/> を参照（差分の有無を理由に exit code を変えない）。
        /// </summary>
        public static int DetermineExitCode(DryRunResult result)
        {
            if (result.Issues.Any(IsCheckCliFailing)) return 2;
            if (!result.Diff.IsEmpty) return 1;
            return 0;
        }

        /// <summary>
        /// <see cref="AddressTellerMenu.ApplyAllCLI"/> / <see cref="AddressTellerMenu.ApplyWithValidateCLI"/>
        /// 向けの exit code 判定。実行後に得られた issues（<paramref name="executionIssues"/>）に
        /// 「書き込みを見送るべき問題」（<see cref="ValidationResult.IsBlocking"/>）が
        /// 含まれれば 2、なければ 0 を返す。単純に <c>!issue.IsOk</c> では判定しない——
        /// <see cref="ValidationStatus.DuplicateAddress"/> は <see cref="ValidationResult.HasWritableDuplicate"/>
        /// が true でも IsOk=false（Error 扱い）になりうるが、書き込みを止めない報告専用ステータスであるため
        /// exit code には反映しない（<see cref="ValidationResult.IsBlocking"/> のXMLドキュメント参照）。
        /// <see cref="DetermineExitCode(DryRunResult)"/>（CheckCLI 用）と異なり、dry-run の差分の有無は
        /// 見ない——Apply は実際に変更を書き込んで完了しているため、差分があったこと自体を失敗として
        /// 扱う（exit code 1 を返す）と、`set -e` の下で正常な適用が毎回失敗になってしまう。
        /// 差分の検出用途には <see cref="AddressTellerMenu.CheckCLI"/> を使うこと。
        /// </summary>
        public static int DetermineApplyExitCode(IReadOnlyList<ValidationResult> executionIssues)
        {
            return executionIssues.Any(i => i.IsBlocking) ? 2 : 0;
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
