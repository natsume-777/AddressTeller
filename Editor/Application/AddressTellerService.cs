using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System.Linq;

namespace Natsume777.AddressTeller.Editor
{
    public static class AddressTellerService
    {
        // Menu/CLI から ApplyAll() を呼んだ場合、その内部の Addressables 設定変更が
        // Postprocessor.OnPostprocessAllAssets を再トリガーし、そこから ApplyAll() が
        // 再度呼ばれて無限ループになるのを防ぐガード。
        // Postprocessor.s_isApplying は Postprocessor 自身の再入を防ぐ別ガードで、
        // Menu 経由の呼び出しではそちらは false のままのため、両方が必要。
        private static bool s_isApplying;

        /// <summary>
        /// 収集した全ルールを全アセットに適用し、問題のあった結果（衝突・グループ未検出・ルール例外など）を返す。
        /// settings が null の場合はプロジェクトのデフォルト設定を使う。Addressables 未設定の場合は空リストを返す。
        /// </summary>
        public static IReadOnlyList<ValidationResult> ApplyAll(AddressableAssetSettings settings = null)
        {
            if (s_isApplying) return Array.Empty<ValidationResult>();
            s_isApplying = true;
            try
            {
                settings ??= AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null) return Array.Empty<ValidationResult>();

                var rules = RuleCollector.CollectRules();
                var entries = GetOrderedEntries(rules);
                var configFolder = settings.ConfigFolder;
                var managedGroups = new HashSet<string>(entries.Select(e => e.GroupName));

                var issues = new List<ValidationResult>();

                foreach (var path in AssetDatabase.GetAllAssetPaths())
                {
                    var ctx = BuildContext(path);
                    if (ctx == null) continue;
                    if (AssetFilter.ShouldExclude(ctx, configFolder)) continue;

                    var resolution = RuleEvaluator.Evaluate(ctx, entries);
                    AddRuleErrors(ctx, resolution, issues);

                    var result = AddressTellerApplier.Apply(ctx, resolution, settings, managedGroups);
                    if (!result.IsOk)
                        issues.Add(result);
                }

                return issues;
            }
            finally
            {
                s_isApplying = false;
            }
        }

        /// <summary>
        /// 全ルールを全アセットに対して検証し、問題のある結果を返す。
        /// settings が null の場合はプロジェクトのデフォルト設定を使う。Addressables 未設定の場合は空リストを返す。
        /// </summary>
        public static IReadOnlyList<ValidationResult> ValidateAll(AddressableAssetSettings settings = null)
        {
            settings ??= AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return Array.Empty<ValidationResult>();

            var rules = RuleCollector.CollectRules();
            var entries = GetOrderedEntries(rules);
            var configFolder = settings.ConfigFolder;
            var groupNames = new HashSet<string>(settings.groups.Select(g => g.Name));

            var issues = new List<ValidationResult>();

            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                var ctx = BuildContext(path);
                if (ctx == null) continue;
                if (AssetFilter.ShouldExclude(ctx, configFolder)) continue;

                var resolution = RuleEvaluator.Evaluate(ctx, entries);
                AddRuleErrors(ctx, resolution, issues);

                var result = AddressTellerApplier.Validate(ctx, resolution, groupNames);
                if (!result.IsOk)
                    issues.Add(result);
            }

            return issues;
        }

        private static void AddRuleErrors(AssetContext ctx, AddressResolution resolution, List<ValidationResult> issues)
        {
            foreach (var error in resolution.Errors)
                issues.Add(new ValidationResult(
                    ctx,
                    ValidationStatus.RuleError,
                    $"{error.RuleSource} threw for '{ctx.Path}': {error.Message}"));
        }

        private static IReadOnlyList<AddressRuleEntry> GetOrderedEntries(IEnumerable<AddressRuleBase> rules)
        {
            var all = new List<AddressRuleEntry>();
            foreach (var rule in rules)
            {
                var builder = new AddressRuleBuilderImpl(rule.GetType().Name);
                rule.Configure(builder);
                all.AddRange(builder.Entries);
            }
            return all;
        }

        private static AssetContext BuildContext(string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return null;
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null) return null;
            return new AssetContext(guid, path, type);
        }
    }
}
