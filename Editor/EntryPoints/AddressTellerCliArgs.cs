using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Command-line arguments for the AddressTeller CLI (e.g. <see cref="AddressTellerMenu.CheckCLI"/>).
    /// Pass an array such as `Environment.GetCommandLineArgs()` to parse it. Arguments not starting with
    /// "-addressTeller" are ignored (Unity itself passes many unrelated arguments); an argument that does
    /// start with "-addressTeller" but does not match a known flag is a parse error (see
    /// <see cref="TryParse"/>).
    /// </summary>
    public sealed class AddressTellerCliArgs
    {
        /// <summary>Report output path specified via -addressTellerReport. Null if not specified.</summary>
        public string ReportPath { get; private set; }

        /// <summary>
        /// Report format.
        /// If -addressTellerReportFormat is omitted, this is inferred from <see cref="ReportPath"/>'s
        /// extension (".xml" -&gt; <see cref="AddressTeller.Editor.ReportFormat.Junit"/>, otherwise
        /// <see cref="AddressTeller.Editor.ReportFormat.Json"/>). Null if <see cref="ReportPath"/> is also unspecified.
        /// </summary>
        public ReportFormat? ReportFormat { get; private set; }

        /// <summary>
        /// Full names of rule classes specified via -addressTellerDisableRules (comma-separated, trimmed,
        /// empty entries removed). Empty list if not specified. Used for a temporary CLI-run-only
        /// exclusion (unioned with <see cref="AddressTellerSettings.DisabledRuleClassNames"/>).
        /// </summary>
        public IReadOnlyList<string> DisableRuleFullNames { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// Whether -addressTellerConfirmClear was specified. Used to confirm execution of
        /// <see cref="AddressTellerMenu.ClearCLI"/> (if not specified, treated as an intentional refusal
        /// and the run exits with an error).
        /// </summary>
        public bool ConfirmClear { get; private set; }

        /// <summary>
        /// Scope of the clear operation specified via -addressTellerClearScope. Defaults to
        /// <see cref="ClearScope.Managed"/> if not specified.
        /// </summary>
        public ClearScope ClearScope { get; private set; } = ClearScope.Managed;

        /// <summary>全フラグに共通のプレフィックス。未知引数の判定（<see cref="TryParse"/> の switch の default 節）
        /// と、この XML doc コメント冒頭の説明の、2箇所で重複していた文字列リテラルをここに集約する。</summary>
        private const string FlagPrefix = "-addressTeller";

        private const string ReportPathFlag = FlagPrefix + "Report";
        private const string ReportFormatFlag = FlagPrefix + "ReportFormat";
        private const string DisableRulesFlag = FlagPrefix + "DisableRules";
        private const string ConfirmClearFlag = FlagPrefix + "ConfirmClear";
        private const string ClearScopeFlag = FlagPrefix + "ClearScope";

        /// <summary>エラーメッセージに列挙する既知フラグ名一覧（<see cref="TryParse"/> の未知フラグエラー用）。</summary>
        private static readonly string[] KnownFlags =
        {
            ReportPathFlag, ReportFormatFlag, DisableRulesFlag, ConfirmClearFlag, ClearScopeFlag,
        };

        /// <summary>
        /// Parses an argument array.
        /// </summary>
        /// <param name="args">The argument array to parse (typically <c>Environment.GetCommandLineArgs()</c>).</param>
        /// <param name="result">The parsed result on success. Null on failure.</param>
        /// <param name="error">Error message on parse failure. Null on success.</param>
        /// <returns>True if parsing succeeded.</returns>
        public static bool TryParse(string[] args, out AddressTellerCliArgs result, out string error)
        {
            result = null;
            error = null;

            if (args == null)
            {
                error = "args must not be null.";
                return false;
            }

            string reportPath = null;
            ReportFormat? reportFormat = null;
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
                            error = $"No value specified for {ReportPathFlag}.";
                            return false;
                        }
                        reportPath = args[++i];
                        break;

                    case ReportFormatFlag:
                        if (i + 1 >= args.Length)
                        {
                            error = $"No value specified for {ReportFormatFlag}.";
                            return false;
                        }
                        var reportFormatValue = args[++i];
                        switch (reportFormatValue)
                        {
                            case "json":
                                reportFormat = AddressTeller.Editor.ReportFormat.Json;
                                break;
                            case "junit":
                                reportFormat = AddressTeller.Editor.ReportFormat.Junit;
                                break;
                            default:
                                error = $"Invalid value for {ReportFormatFlag} (must be 'json' or 'junit'): {reportFormatValue}";
                                return false;
                        }
                        break;

                    case DisableRulesFlag:
                        if (i + 1 >= args.Length)
                        {
                            error = $"No value specified for {DisableRulesFlag}.";
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
                            error = $"No value specified for {ClearScopeFlag}.";
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
                                error = $"Invalid value for {ClearScopeFlag} (must be 'all' or 'managed'): {clearScopeValue}";
                                return false;
                        }
                        break;

                    default:
                        // "-addressTeller" で始まらない引数は Unity 自身が多数渡してくるため無視する。
                        // "-addressTeller" で始まるのに上記いずれの case にも一致しない引数は、
                        // フラグ名の typo である可能性が高い。値の typo（上記の各 default 節）と対称に、
                        // 黙って無視せずエラーにする。
                        // プレフィックスの判定だけ OrdinalIgnoreCase にする（switch 本体のフラグ名一致は
                        // 大文字小文字を区別したまま）。フラグ名の先頭を誤って大文字にする（例:
                        // -AddressTellerReport）typo は、プレフィックス自体が一致しなくなり default にも
                        // 入らず黙って無視される最悪の失敗パターンになるため、ここだけ緩めて確実に捕まえる。
                        // Unity 自身が "-AddressTeller" で始まる引数を渡すことはないため誤爆リスクは無い。
                        if (args[i].StartsWith(FlagPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            error = $"Unknown AddressTeller CLI flag (must be one of: {string.Join(", ", KnownFlags)}): {args[i]}";
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

        /// <summary>拡張子からレポート形式を推定する。".xml" → <see cref="AddressTeller.Editor.ReportFormat.Junit"/>、それ以外 → <see cref="ReportFormat.Json"/>。</summary>
        private static ReportFormat InferFormatFromPath(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase)
                ? AddressTeller.Editor.ReportFormat.Junit
                : AddressTeller.Editor.ReportFormat.Json;
        }
    }
}
