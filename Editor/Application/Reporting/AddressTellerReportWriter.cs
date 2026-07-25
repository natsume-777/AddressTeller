using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// <see cref="AddressTellerReport"/> を JSON / JUnit XML にシリアライズし、ファイルへ書き出す。
    /// CLI から呼ばれる想定で、Addressables には依存しない。
    /// </summary>
    public static class AddressTellerReportWriter
    {
        /// <summary>レポートを JSON 文字列に変換する。</summary>
        public static string ToJson(AddressTellerReport report) => report.ToJson();

        /// <summary>
        /// レポートを JUnit 形式の XML 文字列に変換する。
        /// testcase の粒度は観点ごと: drift 全体で1件、ValidationStatus 種別ごとに1件。
        /// </summary>
        public static string ToJUnitXml(AddressTellerReport report)
        {
            var testCases = new List<(string name, string classname, string failureMessage)>();

            // drift全体で1testcase
            string driftFailure = null;
            if (report.Drift.Count > 0)
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

                testCases.Add((group.Key, "AddressTeller.Validation", sb.ToString()));
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
        /// レポートをファイルに書き出す。出力先ディレクトリが無ければ作成する。
        /// </summary>
        /// <param name="path">出力先ファイルパス。</param>
        /// <param name="report">出力するレポート。</param>
        /// <param name="format">"json" または "junit"。それ以外は <see cref="ArgumentException"/>。</param>
        /// <returns>書き込みに成功したら true。失敗時は false（ログ出力済み）。</returns>
        public static bool WriteToFile(string path, AddressTellerReport report, string format)
        {
            string content;
            switch (format)
            {
                case "json":
                    content = ToJson(report);
                    break;
                case "junit":
                    content = ToJUnitXml(report);
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
