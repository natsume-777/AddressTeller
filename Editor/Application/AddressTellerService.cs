using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System.Linq;

namespace Natsume777.AddressTeller.Editor
{
    public static class AddressTellerService
    {
        // 実行中に Addressables 設定変更 → Postprocessor 再トリガー → 再帰を防ぐ
        private static bool s_isApplying;

        /// <summary>
        /// 収集した全ルールを全アセットに適用する。
        /// settings が null の場合はプロジェクトのデフォルト設定を使う。
        /// </summary>
        public static void ApplyAll(AddressableAssetSettings settings = null)
        {
            if (s_isApplying) return;
            s_isApplying = true;
            try
            {
                settings ??= AddressableAssetSettingsDefaultObject.Settings;
                var rules = RuleCollector.CollectRules();
                var entries = GetOrderedEntries(rules);
                var configFolder = settings.ConfigFolder;

                foreach (var path in AssetDatabase.GetAllAssetPaths())
                {
                    var ctx = BuildContext(path);
                    if (ctx == null) continue;
                    if (AssetFilter.ShouldExclude(ctx, configFolder)) continue;

                    var resolution = RuleEvaluator.Evaluate(ctx, entries);
                    AddressTellerApplier.Apply(ctx, resolution, settings);
                }
            }
            finally
            {
                s_isApplying = false;
            }
        }

        /// <summary>
        /// 全ルールを全アセットに対して検証し、問題のある結果を返す。
        /// </summary>
        public static IReadOnlyList<ValidationResult> ValidateAll(AddressableAssetSettings settings = null)
        {
            settings ??= AddressableAssetSettingsDefaultObject.Settings;
            var rules = RuleCollector.CollectRules();
            var entries = GetOrderedEntries(rules);
            var configFolder = settings.ConfigFolder;
            var groupNames = settings.groups.Select(g => g.Name);

            var issues = new List<ValidationResult>();

            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                var ctx = BuildContext(path);
                if (ctx == null) continue;
                if (AssetFilter.ShouldExclude(ctx, configFolder)) continue;

                var resolution = RuleEvaluator.Evaluate(ctx, entries);
                var result = AddressTellerApplier.Validate(ctx, resolution, groupNames);
                if (!result.IsOk)
                    issues.Add(result);
            }

            return issues;
        }

        private static IReadOnlyList<AddressRuleEntry> GetOrderedEntries(IEnumerable<AddressRuleBase> rules)
        {
            var all = new List<AddressRuleEntry>();
            foreach (var rule in rules)
            {
                var builder = new AddressRuleBuilderImpl();
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
