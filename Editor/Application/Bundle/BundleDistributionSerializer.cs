using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>File output format for the logical bundle distribution.</summary>
    public enum DistributionFormat
    {
        /// <summary>CSV format.</summary>
        Csv = 0,

        /// <summary>Markdown format.</summary>
        Markdown = 1,
    }

    /// <summary>
    /// Serializes a <see cref="BundleDistribution"/> / <see cref="DistributionSummary"/> to CSV / Markdown
    /// and writes it to a file. Follows the same approach as <see cref="AddressTellerReportWriter"/> (pure
    /// functions + WriteToFile). Inputs are expected to already be computed by
    /// <see cref="BundleDistributionSummarizer"/>; this class does not recompute them.
    /// </summary>
    public static class BundleDistributionSerializer
    {
        /// <summary>
        /// Converts the logical bundle distribution to CSV.
        /// Each row is GroupName,Mode,SplitKey,AssetCount. The summary and disclaimer are appended as
        /// comment lines (starting with #) at the end.
        /// Returns an empty string if <paramref name="distribution"/> / <paramref name="summary"/> is null.
        /// </summary>
        public static string ToCsv(BundleDistribution distribution, DistributionSummary summary)
        {
            if (distribution == null || summary == null) return string.Empty;

            var sb = new StringBuilder();
            sb.Append("GroupName,Mode,SplitKey,AssetCount\n");

            foreach (var bundle in distribution.Bundles)
            {
                sb.Append(CsvEscape(bundle.GroupName)).Append(',')
                  .Append(bundle.Mode).Append(',')
                  .Append(CsvEscape(bundle.SplitKey)).Append(',')
                  .Append(bundle.AssetCount).Append('\n');
            }

            sb.Append('\n');
            sb.Append("# Logical bundle count: ").Append(summary.TotalLogicalBundleCount).Append('\n');
            sb.Append("# Groups with unknown BundleMode: ").Append(summary.UnknownGroupCount).Append('\n');

            var largest = summary.LargestBundle;
            if (largest != null)
            {
                sb.Append("# Largest consolidated bundle: ")
                  .Append(largest.GroupName).Append(" / ")
                  .Append(largest.SplitKey).Append(" (")
                  .Append(largest.AssetCount).Append(" assets)\n");
            }

            // 免責文言は改行を含まないため、そのまま # コメント行として1行で出力できる。
            sb.Append("# ").Append(AddressTellerReportBuilder.BundleDistributionDisclaimer).Append('\n');

            return sb.ToString();
        }

        /// <summary>
        /// Converts the logical bundle distribution to Markdown.
        /// Outputs the summary, the distribution table, and the disclaimer (as a blockquote), in that order.
        /// Returns an empty string if <paramref name="distribution"/> / <paramref name="summary"/> is null.
        /// </summary>
        public static string ToMarkdown(BundleDistribution distribution, DistributionSummary summary)
        {
            if (distribution == null || summary == null) return string.Empty;

            var sb = new StringBuilder();
            sb.Append("# Bundle Distribution\n\n");

            sb.Append("- Logical bundle count: ").Append(summary.TotalLogicalBundleCount).Append('\n');
            sb.Append("- Groups with unknown BundleMode: ").Append(summary.UnknownGroupCount).Append('\n');

            var largest = summary.LargestBundle;
            if (largest != null)
            {
                sb.Append("- Largest consolidated bundle: ")
                  .Append(largest.GroupName).Append(" / ")
                  .Append(largest.SplitKey).Append(" (")
                  .Append(largest.AssetCount).Append(" assets)\n");
            }

            sb.Append('\n');
            sb.Append("| GroupName | Mode | SplitKey | AssetCount |\n");
            sb.Append("|---|---|---|---|\n");

            foreach (var bundle in distribution.Bundles)
            {
                sb.Append("| ").Append(MarkdownEscape(bundle.GroupName))
                  .Append(" | ").Append(bundle.Mode)
                  .Append(" | ").Append(MarkdownEscape(bundle.SplitKey))
                  .Append(" | ").Append(bundle.AssetCount).Append(" |\n");
            }

            sb.Append('\n');
            sb.Append("> ").Append(AddressTellerReportBuilder.BundleDistributionDisclaimer).Append('\n');

            return sb.ToString();
        }

        /// <summary>
        /// Writes the distribution to a file. Creates the destination directory if it does not exist.
        /// </summary>
        /// <param name="path">Destination file path.</param>
        /// <param name="distribution">The logical bundle distribution.</param>
        /// <param name="summary">Display-oriented aggregate for the distribution.</param>
        /// <param name="format">Output format. Throws <see cref="ArgumentException"/> for an undefined value.</param>
        /// <returns>True if the write succeeded; false on failure (a message is already logged).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="distribution"/> is null.</exception>
        public static bool WriteToFile(string path, BundleDistribution distribution, DistributionSummary summary, DistributionFormat format)
        {
            if (distribution == null) throw new ArgumentNullException(nameof(distribution));

            string content;
            switch (format)
            {
                case DistributionFormat.Csv:
                    content = ToCsv(distribution, summary);
                    break;
                case DistributionFormat.Markdown:
                    content = ToMarkdown(distribution, summary);
                    break;
                default:
                    throw new ArgumentException($"Unknown output format: {format}", nameof(format));
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
                Debug.LogError($"[AddressTeller] Failed to write bundle distribution ({path}): {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// CSV のフィールド値をエスケープする。カンマ・ダブルクォート・改行を含む場合はダブルクォートで囲む。
        /// </summary>
        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Markdown のテーブルセル値をエスケープする。パイプ文字をテーブル区切りと混同しないようエスケープする。
        /// </summary>
        private static string MarkdownEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

            return value.Replace("|", "\\|");
        }
    }
}
