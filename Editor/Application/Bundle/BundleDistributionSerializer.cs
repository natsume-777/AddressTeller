using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// <see cref="BundleDistribution"/> / <see cref="DistributionSummary"/> を CSV / Markdown にシリアライズし、
    /// ファイルへ書き出す。<see cref="AddressTellerReportWriter"/> と同じ流儀（純粋関数 + WriteToFile）。
    /// 入力は <see cref="BundleDistributionSummarizer"/> で算出済みのものを渡す想定で、ここでは再計算しない。
    /// </summary>
    public static class BundleDistributionSerializer
    {
        /// <summary>
        /// 論理バンドル分布を CSV に変換する。
        /// 各行は GroupName,Mode,SplitKey,AssetCount。末尾にサマリと免責文言をコメント行（# 始まり）で付与する。
        /// <paramref name="distribution"/> / <paramref name="summary"/> が null の場合は空文字列を返す。
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
            sb.Append("# 論理バンドル数: ").Append(summary.TotalLogicalBundleCount).Append('\n');
            sb.Append("# BundleMode 未判定のグループ数: ").Append(summary.UnknownGroupCount).Append('\n');

            var largest = summary.LargestBundle;
            if (largest != null)
            {
                sb.Append("# 最大集約バンドル: ")
                  .Append(largest.GroupName).Append(" / ")
                  .Append(largest.SplitKey).Append(" (")
                  .Append(largest.AssetCount).Append(" アセット)\n");
            }

            // 免責文言は改行を含まないため、そのまま # コメント行として1行で出力できる。
            sb.Append("# ").Append(AddressTellerReportBuilder.BundleDistributionDisclaimer).Append('\n');

            return sb.ToString();
        }

        /// <summary>
        /// 論理バンドル分布を Markdown に変換する。
        /// サマリ・分布テーブル・免責文言（引用ブロック）の順に出力する。
        /// <paramref name="distribution"/> / <paramref name="summary"/> が null の場合は空文字列を返す。
        /// </summary>
        public static string ToMarkdown(BundleDistribution distribution, DistributionSummary summary)
        {
            if (distribution == null || summary == null) return string.Empty;

            var sb = new StringBuilder();
            sb.Append("# Bundle Distribution\n\n");

            sb.Append("- 論理バンドル数: ").Append(summary.TotalLogicalBundleCount).Append('\n');
            sb.Append("- BundleMode 未判定のグループ数: ").Append(summary.UnknownGroupCount).Append('\n');

            var largest = summary.LargestBundle;
            if (largest != null)
            {
                sb.Append("- 最大集約バンドル: ")
                  .Append(largest.GroupName).Append(" / ")
                  .Append(largest.SplitKey).Append(" (")
                  .Append(largest.AssetCount).Append(" アセット)\n");
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
        /// 分布をファイルに書き出す。出力先ディレクトリが無ければ作成する。
        /// </summary>
        /// <param name="path">出力先ファイルパス。</param>
        /// <param name="distribution">論理バンドル分布。</param>
        /// <param name="summary">分布の表示用集計。</param>
        /// <param name="format">"csv" または "markdown"。それ以外は <see cref="ArgumentException"/>。</param>
        /// <returns>書き込みに成功したら true。失敗時は false（ログ出力済み）。</returns>
        public static bool WriteToFile(string path, BundleDistribution distribution, DistributionSummary summary, string format)
        {
            string content;
            switch (format)
            {
                case "csv":
                    content = ToCsv(distribution, summary);
                    break;
                case "markdown":
                    content = ToMarkdown(distribution, summary);
                    break;
                default:
                    throw new ArgumentException($"未知の出力形式です: {format}", nameof(format));
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
                Debug.LogError($"AddressTeller: バンドル分布の書き込みに失敗しました ({path}): {e.Message}");
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
