using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// ルール評価のループに入る前に1回だけ構築すればよいセットアップ情報。
    /// </summary>
    internal readonly struct EvaluationSetup
    {
        public IReadOnlyList<AddressRuleEntry> Entries { get; }
        public string ConfigFolder { get; }
        public HashSet<string> ManagedGroups { get; }
        public HashSet<string> ExistingGroupNames { get; }

        /// <summary>
        /// true の場合、Apply は未存在グループを DefaultGroup のスキーマ構成を複製して自動作成し、
        /// Validate/Predict は作成せず <see cref="ValidationStatus.GroupWillBeCreated"/> として提示する。
        /// <see cref="AddressTellerSettings.AutoCreateMissingGroups"/> を全エントリポイントに一貫供給するためのフィールド。
        /// </summary>
        public bool AutoCreateMissingGroups { get; }

        /// <summary>
        /// true の場合、GroupDefault() を使うルールが存在するが AddressableAssetSettings.DefaultGroup が
        /// 取得できなかった。該当エントリのグループ名はセンチネルのまま残っており、
        /// AddressTellerApplier.Validate が <see cref="ValidationStatus.DefaultGroupUnavailable"/> を返す。
        /// </summary>
        public bool DefaultGroupUnavailable { get; }

        /// <summary>
        /// Configure() が例外を送出したルールごとの失敗一覧（<see cref="ValidationStatus.RuleConfigureFailed"/>、
        /// Context は null）。ApplyAll/ValidateAll/BuildPredictedSnapshot 等、issues リストを返す呼び出し側は
        /// これをそのまま結果に含めること。ここに含まれるルールは <see cref="Entries"/> に一切寄与しない。
        /// </summary>
        public IReadOnlyList<ValidationResult> ConfigureFailures { get; }

        public EvaluationSetup(IReadOnlyList<AddressRuleEntry> entries, string configFolder, HashSet<string> managedGroups, HashSet<string> existingGroupNames, bool autoCreateMissingGroups, bool defaultGroupUnavailable = false, IReadOnlyList<ValidationResult> configureFailures = null)
        {
            Entries = entries;
            ConfigFolder = configFolder;
            ManagedGroups = managedGroups;
            ExistingGroupNames = existingGroupNames;
            AutoCreateMissingGroups = autoCreateMissingGroups;
            DefaultGroupUnavailable = defaultGroupUnavailable;
            ConfigureFailures = configureFailures ?? Array.Empty<ValidationResult>();
        }
    }

    /// <summary>
    /// ApplyAll/ValidateAll/BuildPredictedSnapshot が共有する前処理（セットアップ構築・コンテキスト生成・
    /// ルール例外の集約）をまとめたパイプライン。
    /// </summary>
    internal static class RuleEvaluationPipeline
    {
        /// <summary>
        /// ループ外で1回だけ構築するセットアップ情報をまとめて返す。
        /// GroupDefault() を使うルールがある場合のみ settings.DefaultGroup を1回取得し、
        /// そのグループ名へ正規化する（センチネル文字列はここで解消する。以降は実名のみを扱う）。
        /// DefaultGroup が取得できない場合は該当エントリのグループ名をセンチネルのまま残し、
        /// <see cref="EvaluationSetup.DefaultGroupUnavailable"/> を true にする
        /// （AddressTellerApplier.Validate がセンチネルを検出して <see cref="ValidationStatus.DefaultGroupUnavailable"/> を返す）。
        /// </summary>
        public static EvaluationSetup BuildSetup(AddressableAssetSettings settings, IReadOnlyList<AddressRuleBase> rules)
        {
            var entries = GetOrderedEntries(rules, out var configureFailures);
            var configFolder = settings.ConfigFolder;
            var defaultGroupUnavailable = false;

            // センチネルを使うルールが1件もなければ DefaultGroup を取得しない（不要な Addressables アクセスを避ける）。
            if (entries.Any(e => e.GroupName == AddressRuleBuilderImpl.DefaultGroupSentinel))
            {
                // settings.DefaultGroup は通常 null を返さず未設定時は自動作成するが、
                // その自動作成自体が失敗する異常系（CreateGroup の例外等）に備えて try/catch する。
                AddressableAssetGroup defaultGroup = null;
                try
                {
                    defaultGroup = settings.DefaultGroup;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AddressTeller] Failed to retrieve AddressableAssetSettings.DefaultGroup: {ex.Message}. Rules using GroupDefault() will be skipped.");
                }

                if (defaultGroup != null)
                {
                    entries = entries
                        .Select(e => e.GroupName == AddressRuleBuilderImpl.DefaultGroupSentinel
                            ? new AddressRuleEntry(defaultGroup.Name, e.Predicate, e.AddressSelector, e.LabelSelectors, e.SourceClass, e.Description, e.RuleIndex)
                            : e)
                        .ToArray();
                }
                else
                {
                    defaultGroupUnavailable = true;
                    Debug.LogWarning("[AddressTeller] AddressableAssetSettings.DefaultGroup could not be retrieved. Rules using GroupDefault() will be skipped.");
                }
            }

            // AnyGroup() 由来のエントリは GroupName が null、未解決のセンチネルも除外する。
            var managedGroups = new HashSet<string>(entries
                .Where(e => e.GroupName != null && e.GroupName != AddressRuleBuilderImpl.DefaultGroupSentinel)
                .Select(e => e.GroupName));
            // settings.groups の null 要素を除外する（Capture の if (group == null) continue; と対称にする）。
            var existingGroupNames = new HashSet<string>(settings.groups.Where(g => g != null).Select(g => g.Name));
            return new EvaluationSetup(entries, configFolder, managedGroups, existingGroupNames, AddressTellerSettings.AutoCreateMissingGroups, defaultGroupUnavailable, configureFailures);
        }

        /// <summary>
        /// 同一 Order のルールクラスが複数あれば警告を出す。
        /// Project Settings で無効化中のルールも含む全ルールが対象（無効化しても警告は消えない）。
        /// </summary>
        public static void WarnOnDuplicateOrders(IReadOnlyList<AddressRuleBase> rules)
        {
            foreach (var group in RuleCollector.FindDuplicateOrders(rules))
            {
                var names = string.Join(", ", group.Select(r => r.GetType().Name));
                Debug.LogWarning($"[AddressTeller] Multiple rule classes share Order={group.Key} (all rules including disabled ones are considered): {names}. Verify that the evaluation order is intentional.");
            }
        }

        /// <summary>パスから AssetContext を構築する。</summary>
        public static AssetContext BuildContext(string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return null;
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null) return null;
            return new AssetContext(guid, path, type);
        }

        /// <summary>ルールの評価エラーを issues へ追加する。</summary>
        public static void AddRuleErrors(AssetContext ctx, AddressResolution resolution, List<ValidationResult> issues)
        {
            foreach (var error in resolution.Errors)
                issues.Add(new ValidationResult(
                    ctx,
                    ValidationStatus.RuleError,
                    $"{error.RuleSource} threw for '{ctx.Path}': {error.Message}"));
        }

        /// <summary>
        /// Configure() を実行してルールエントリ一覧を構築する。Configure() はルールごとに try/catch し、
        /// 例外が発生したルールは他のルールの評価をブロックせずスキップする（該当ルールのエントリは0件）。
        /// 失敗したルールは <see cref="ValidationStatus.RuleConfigureFailed"/> の <see cref="ValidationResult"/>
        /// として <paramref name="configureFailures"/> に毎回まとめる（ClearAll/UndoLastApply 等、issues リストを
        /// 持たない呼び出し側でも失敗が可視化されるようにするため）。コンソールへのエラー出力（スタックトレース込み）も
        /// 呼び出しのたびに毎回行う。同じ失敗が import のたびに繰り返しログされうるが、原因調査の容易さを優先する。
        /// </summary>
        private static IReadOnlyList<AddressRuleEntry> GetOrderedEntries(IEnumerable<AddressRuleBase> rules, out IReadOnlyList<ValidationResult> configureFailures)
        {
            var all = new List<AddressRuleEntry>();
            List<ValidationResult> failures = null;

            foreach (var rule in rules)
            {
                var builder = new AddressRuleBuilderImpl(rule.GetType().Name);
                try
                {
                    rule.Configure(builder);
                }
                catch (Exception ex)
                {
                    var message = $"{rule.GetType().Name}.Configure() threw and will be skipped: {ex.GetType().Name}: {ex.Message}";
                    // ValidationResult.Message は簡潔なままにし、ログ側にのみスタックトレースを含める。
                    // 呼び出しのたびに毎回出す（ログが増える代わりに、原因特定に必要な情報を常に確保する）。
                    Debug.LogError($"[AddressTeller] {message}\n{ex}");
                    failures ??= new List<ValidationResult>();
                    failures.Add(new ValidationResult(null, ValidationStatus.RuleConfigureFailed, message));
                    continue;
                }

                all.AddRange(builder.Entries);
            }

            configureFailures = (IReadOnlyList<ValidationResult>)failures ?? Array.Empty<ValidationResult>();
            return all;
        }
    }
}
