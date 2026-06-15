using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

        /// <summary>
        /// -addressTellerDisableRules で指定されたルールクラスのフルネーム一覧（カンマ区切り、trim済み、空要素除去）。
        /// 未指定なら空リスト。CLI実行限定の一時除外（<see cref="AddressTellerSettings.DisabledRuleClassNames"/> との和集合）に使う。
        /// </summary>
        public IReadOnlyList<string> DisableRuleFullNames { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// -addressTellerConfirmClear が指定されたかどうか。<see cref="AddressTellerMenu.ClearCLI"/> の実行確認に使う
        /// （未指定の場合は意図的な拒否としてエラー終了する）。
        /// </summary>
        public bool ConfirmClear { get; private set; }

        /// <summary>
        /// -addressTellerClearScope で指定されたクリア対象のスコープ。未指定なら <see cref="ClearScope.Managed"/>。
        /// </summary>
        public ClearScope ClearScope { get; private set; } = ClearScope.Managed;

        private const string ReportPathFlag = "-addressTellerReport";
        private const string ReportFormatFlag = "-addressTellerReportFormat";
        private const string DisableRulesFlag = "-addressTellerDisableRules";
        private const string ConfirmClearFlag = "-addressTellerConfirmClear";
        private const string ClearScopeFlag = "-addressTellerClearScope";

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
            IReadOnlyList<string> disableRuleFullNames = Array.Empty<string>();
            var confirmClear = false;
            var clearScope = ClearScope.Managed;

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

                    case DisableRulesFlag:
                        if (i + 1 >= args.Length)
                        {
                            error = $"{DisableRulesFlag} に値が指定されていません。";
                            return false;
                        }
                        disableRuleFullNames = args[++i]
                            .Split(',')
                            .Select(name => name.Trim())
                            .Where(name => name.Length > 0)
                            .ToList();
                        break;

                    case ConfirmClearFlag:
                        confirmClear = true;
                        break;

                    case ClearScopeFlag:
                        if (i + 1 >= args.Length)
                        {
                            error = $"{ClearScopeFlag} に値が指定されていません。";
                            return false;
                        }
                        var clearScopeValue = args[++i];
                        switch (clearScopeValue)
                        {
                            case "all":
                                clearScope = ClearScope.All;
                                break;
                            case "managed":
                                clearScope = ClearScope.Managed;
                                break;
                            default:
                                error = $"{ClearScopeFlag} の値が不正です（'all' または 'managed' を指定してください）: {clearScopeValue}";
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
                DisableRuleFullNames = disableRuleFullNames,
                ConfirmClear = confirmClear,
                ClearScope = clearScope,
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
