using System;
using System.IO;

namespace AddressTeller.Editor
{
    /// <summary>
    /// AddressTeller CLI（<see cref="AddressTellerMenu.CheckCLI"/> など）のコマンドライン引数。
    /// `Environment.GetCommandLineArgs()` 等の配列を渡してパースする。
    /// </summary>
    public sealed class AddressTellerCliArgs
    {
        /// <summary>-addressTellerReport で指定されたレポート出力先パス。未指定なら null。</summary>
        public string ReportPath { get; private set; }

        /// <summary>
        /// レポート形式（"json" または "junit"）。
        /// -addressTellerReportFormat が省略された場合は <see cref="ReportPath"/> の拡張子から推定する
        /// （".xml" → "junit"、それ以外 → "json"）。<see cref="ReportPath"/> も未指定なら null。
        /// </summary>
        public string ReportFormat { get; private set; }

        private const string ReportPathFlag = "-addressTellerReport";
        private const string ReportFormatFlag = "-addressTellerReportFormat";

        /// <summary>
        /// 引数配列をパースする。
        /// </summary>
        /// <param name="args">パース対象の引数配列（<c>Environment.GetCommandLineArgs()</c> 想定）。</param>
        /// <param name="result">パース成功時の結果。失敗時は null。</param>
        /// <param name="error">パース失敗時のエラーメッセージ。成功時は null。</param>
        /// <returns>パースに成功したら true。</returns>
        public static bool TryParse(string[] args, out AddressTellerCliArgs result, out string error)
        {
            result = null;
            error = null;

            string reportPath = null;
            string reportFormat = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case ReportPathFlag:
                        if (i + 1 >= args.Length)
                        {
                            error = $"{ReportPathFlag} に値が指定されていません。";
                            return false;
                        }
                        reportPath = args[++i];
                        break;

                    case ReportFormatFlag:
                        if (i + 1 >= args.Length)
                        {
                            error = $"{ReportFormatFlag} に値が指定されていません。";
                            return false;
                        }
                        reportFormat = args[++i];
                        if (reportFormat != "json" && reportFormat != "junit")
                        {
                            error = $"{ReportFormatFlag} の値が不正です（'json' または 'junit' を指定してください）: {reportFormat}";
                            return false;
                        }
                        break;
                }
            }

            if (reportFormat == null && reportPath != null)
                reportFormat = InferFormatFromPath(reportPath);

            result = new AddressTellerCliArgs
            {
                ReportPath = reportPath,
                ReportFormat = reportFormat,
            };
            return true;
        }

        /// <summary>拡張子からレポート形式を推定する。".xml" → "junit"、それ以外 → "json"。</summary>
        private static string InferFormatFromPath(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase) ? "junit" : "json";
        }
    }
}
