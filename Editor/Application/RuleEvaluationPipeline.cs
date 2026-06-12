using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
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

        public EvaluationSetup(IReadOnlyList<AddressRuleEntry> entries, string configFolder, HashSet<string> managedGroups, HashSet<string> existingGroupNames)
        {
            Entries = entries;
            ConfigFolder = configFolder;
            ManagedGroups = managedGroups;
            ExistingGroupNames = existingGroupNames;
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
        /// </summary>
        public static EvaluationSetup BuildSetup(AddressableAssetSettings settings, IReadOnlyList<AddressRuleBase> rules)
        {
            var entries = GetOrderedEntries(rules);
            var configFolder = settings.ConfigFolder;
            var managedGroups = new HashSet<string>(entries.Select(e => e.GroupName));
            // settings.groups の null 要素を除外する（Capture の if (group == null) continue; と対称にする）。
            var existingGroupNames = new HashSet<string>(settings.groups.Where(g => g != null).Select(g => g.Name));
            return new EvaluationSetup(entries, configFolder, managedGroups, existingGroupNames);
        }

        /// <summary>同一 Order のルールクラスが複数あれば警告を出す。</summary>
        public static void WarnOnDuplicateOrders(IReadOnlyList<AddressRuleBase> rules)
        {
            foreach (var group in RuleCollector.FindDuplicateOrders(rules))
            {
                var names = string.Join(", ", group.Select(r => r.GetType().Name));
                Debug.LogWarning($"[AddressTeller] Order={group.Key} のルールクラスが複数あります: {names}。評価順序が意図通りか確認してください。");
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

        /// <summary>Configure() を実行してルールエントリ一覧を構築する。</summary>
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
    }
}
