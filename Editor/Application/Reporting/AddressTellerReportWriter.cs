using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>File output format for a report.</summary>
    public enum ReportFormat
    {
        /// <summary>JSON format.</summary>
        Json = 0,

        /// <summary>JUnit-style XML.</summary>
        Junit = 1,
    }

    /// <summary>
    /// Serializes an <see cref="AddressTellerReport"/> to JSON / JUnit XML and writes it to a file.
    /// Intended to be called from the CLI; does not depend on Addressables.
    /// </summary>
    public static class AddressTellerReportWriter
    {
        /// <summary>Converts the report to a JSON string.</summary>
        public static string ToJson(AddressTellerReport report) => report.ToJson();

        /// <summary>
        /// Converts the report to a JUnit-style XML string.
        /// Testcase granularity is per aspect: one for drift as a whole, and one per ValidationStatus kind.
        /// </summary>
        /// <param name="report">The report to convert.</param>
        /// <param name="treatDriftAsFailure">
        /// Whether the drift testcase should be emitted with a nested &lt;failure&gt; element when
        /// <see cref="AddressTellerReport.Drift"/> is non-empty. Defaults to true, matching
        /// <see cref="AddressTellerMenu.CheckCLI"/>'s read-only dry-run, where drift is itself the thing
        /// being detected. Pass false for a report built from <see cref="AddressTellerMenu.ApplyAllCLI"/> /
        /// <see cref="AddressTellerMenu.ApplyWithValidateCLI"/>: those already wrote the drift successfully
        /// by the time the report is produced, so flagging it as a JUnit failure would make a CI job that
        /// ingests this file fail on every successful apply that changed anything — the same problem
        /// exit code 1 used to cause for those two methods (see <see cref="AddressTellerReportBuilder.DetermineApplyExitCode"/>).
        /// </param>
        /// <remarks>
        /// Depending on the caller, some <see cref="AddressTellerReport.Issues"/> statuses do not affect that
        /// entry point's exit code (for example a report-only <see cref="ValidationStatus.DuplicateAddress"/>
        /// notice that does not abort Apply). This overload always marks every ValidationStatus testcase with
        /// a &lt;failure&gt; element; callers that need the &lt;failure&gt; presence to track the exit code
        /// exactly (as <see cref="AddressTellerMenu.CheckCLI"/> / <see cref="AddressTellerMenu.ApplyAllCLI"/> /
        /// <see cref="AddressTellerMenu.ApplyWithValidateCLI"/> do) use an internal overload that limits
        /// &lt;failure&gt; to the statuses that are actually blocking.
        /// </remarks>
        public static string ToJUnitXml(AddressTellerReport report, bool treatDriftAsFailure = true) =>
            ToJUnitXml(report, treatDriftAsFailure, failingStatusNames: null);

        /// <summary>
        /// <see cref="ToJUnitXml(AddressTellerReport, bool)"/> に、failure を付ける対象 Status を絞り込む
        /// <paramref name="failingStatusNames"/> を追加したオーバーロード。testcase の構成（1 Status = 1 testcase）
        /// 自体は変えない——failure の有無だけを、呼び出し元のエントリポイントが「exit code に反映する issue」と
        /// して数えたかどうかに揃える。<paramref name="failingStatusNames"/> が null の場合は全 Status を failure
        /// 対象とする（公開オーバーロードと同じ挙動）。
        /// </summary>
        internal static string ToJUnitXml(AddressTellerReport report, bool treatDriftAsFailure, IReadOnlyCollection<string> failingStatusNames)
        {
            var testCases = new List<(string name, string classname, string failureMessage)>();

            // drift全体で1testcase
            string driftFailure = null;
            if (treatDriftAsFailure && report.Drift.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append("Drift detected: ").Append(report.Drift.Count).Append(" entr").Append(report.Drift.Count == 1 ? "y" : "ies");
                foreach (var d in report.Drift)
                    sb.Append('\n').Append(d.Path).Append(" (").Append(d.ChangeType).Append(')');
                driftFailure = sb.ToString();
            }
            testCases.Add(("drift", "AddressTeller.Drift", driftFailure));

            // Issues を Status でグルーピングし、種別ごとに1testcase
            var issuesByStatus = report.Issues
                .GroupBy(i => i.Status)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            foreach (var group in issuesByStatus)
            {
                var sb = new StringBuilder();
                var ordered = group
                    .OrderBy(i => i.Path, StringComparer.Ordinal)
                    .ThenBy(i => i.Message, StringComparer.Ordinal)
                    .ToList();

                sb.Append(ordered.Count).Append(" issue").Append(ordered.Count == 1 ? "" : "s").Append(" with status ").Append(group.Key);
                foreach (var issue in ordered)
                    sb.Append('\n').Append(issue.Path).Append(": ").Append(issue.Message);

                // failingStatusNames が null なら常に failure（従来どおり）。集合が渡された場合は、
                // その Status がエントリポイントの exit code に反映される issue かどうかで failure の有無を決める。
                var isFailingStatus = failingStatusNames == null || failingStatusNames.Contains(group.Key);
                testCases.Add((group.Key, "AddressTeller.Validation", isFailingStatus ? sb.ToString() : null));
            }

            int totalTests = testCases.Count;
            int totalFailures = testCases.Count(tc => tc.failureMessage != null);

            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            xml.Append("<testsuite name=\"AddressTeller\" tests=\"").Append(totalTests)
               .Append("\" failures=\"").Append(totalFailures).Append("\">\n");

            foreach (var (name, classname, failureMessage) in testCases)
            {
                xml.Append("  <testcase name=\"").Append(Escape(name))
                   .Append("\" classname=\"").Append(Escape(classname)).Append('"');

                if (failureMessage == null)
                {
                    xml.Append(" />\n");
                }
                else
                {
                    xml.Append(">\n");
                    xml.Append("    <failure message=\"").Append(Escape(FirstLine(failureMessage))).Append("\">")
                       .Append(Escape(failureMessage)).Append("</failure>\n");
                    xml.Append("  </testcase>\n");
                }
            }

            xml.Append("</testsuite>\n");
            return xml.ToString();
        }

        /// <summary>
        /// Writes the report to a file. Creates the destination directory if it does not exist.
        /// </summary>
        /// <param name="path">Destination file path.</param>
        /// <param name="report">The report to write.</param>
        /// <param name="format">Output format. Throws <see cref="ArgumentException"/> for an undefined value.</param>
        /// <param name="treatDriftAsFailure">
        /// Only meaningful for <see cref="ReportFormat.Junit"/>; ignored for <see cref="ReportFormat.Json"/>.
        /// See <see cref="ToJUnitXml(AddressTellerReport, bool)"/> for what this controls and why the two apply CLI entry points pass false.
        /// </param>
        /// <returns>True if the write succeeded; false on failure (a message is already logged).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="report"/> is null.</exception>
        public static bool WriteToFile(string path, AddressTellerReport report, ReportFormat format, bool treatDriftAsFailure = true) =>
            WriteToFile(path, report, format, treatDriftAsFailure, failingStatusNames: null);

        /// <summary>
        /// <see cref="WriteToFile(string, AddressTellerReport, ReportFormat, bool)"/> に、JUnit 出力の failure を
        /// 絞り込む <paramref name="failingStatusNames"/> を追加したオーバーロード。<see cref="ReportFormat.Json"/>
        /// の場合は無視される（JSON 出力は failure の概念を持たないため挙動不変）。
        /// </summary>
        internal static bool WriteToFile(string path, AddressTellerReport report, ReportFormat format, bool treatDriftAsFailure, IReadOnlyCollection<string> failingStatusNames)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            string content;
            switch (format)
            {
                case ReportFormat.Json:
                    content = ToJson(report);
                    break;
                case ReportFormat.Junit:
                    content = ToJUnitXml(report, treatDriftAsFailure, failingStatusNames);
                    break;
                default:
                    throw new ArgumentException($"Unknown report format: {format}", nameof(format));
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(path, content);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AddressTeller] Failed to write report ({path}): {e.Message}");
                return false;
            }
        }

        /// <summary>XML属性・要素値として安全な文字列にエスケープする。</summary>
        private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

        /// <summary>メッセージの1行目を取り出す（failure の message 属性用）。</summary>
        private static string FirstLine(string text)
        {
            var index = text.IndexOf('\n');
            return index < 0 ? text : text.Substring(0, index);
        }
    }
}
