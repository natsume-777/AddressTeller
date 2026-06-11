using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System.Linq;
using UnityEngine;

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
        /// 収集した全ルールをプロジェクト全アセットに適用し、問題のあった結果（衝突・グループ未検出・ルール例外など）を返す。
        /// settings が null の場合はプロジェクトのデフォルト設定を使う。Addressables 未設定の場合は空リストを返す。
        /// </summary>
        public static IReadOnlyList<ValidationResult> ApplyAll(AddressableAssetSettings settings = null)
        {
            return ApplyAll(AssetDatabase.GetAllAssetPaths(), settings);
        }

        /// <summary>
        /// 収集した全ルールを <paramref name="paths"/> で指定したアセットのみに適用し、
        /// 問題のあった結果（衝突・グループ未検出・ルール例外など）を返す。
        /// settings が null の場合はプロジェクトのデフォルト設定を使う。Addressables 未設定の場合は空リストを返す。
        /// </summary>
        /// <remarks>
        /// 「どのルールにもマッチしなくなった」エントリのクリーンアップ判定は、
        /// このメソッドに渡されたアセット自身が変更された場合のみ行われる。
        /// プロジェクト全体の整合性チェックは引数なしの <see cref="ApplyAll(AddressableAssetSettings)"/>
        /// （Menu/CLI のフル走査）が引き続き担う。
        /// </remarks>
        public static IReadOnlyList<ValidationResult> ApplyAll(IEnumerable<string> paths, AddressableAssetSettings settings = null)
        {
            if (s_isApplying) return Array.Empty<ValidationResult>();
            s_isApplying = true;
            try
            {
                settings ??= AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null) return Array.Empty<ValidationResult>();

                var rules = RuleCollector.CollectRules();
                WarnOnDuplicateOrders(rules);
                var entries = GetOrderedEntries(rules);
                var configFolder = settings.ConfigFolder;
                var managedGroups = new HashSet<string>(entries.Select(e => e.GroupName));
                var groupNames = new HashSet<string>(settings.groups.Select(g => g.Name));

                var issues = new List<ValidationResult>();

                foreach (var path in paths)
                {
                    var ctx = BuildContext(path);
                    if (ctx == null) continue;
                    if (AssetFilter.ShouldExclude(ctx, configFolder)) continue;

                    var resolution = RuleEvaluator.Evaluate(ctx, entries);
                    AddRuleErrors(ctx, resolution, issues);

                    var result = AddressTellerApplier.Apply(ctx, resolution, settings, groupNames, managedGroups);
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
        /// 削除されたアセットの GUID（<see cref="AssetPathToGUIDOptions.IncludeRecentlyDeletedAssets"/> で取得したもの）に対応する
        /// エントリが AddressTeller 管理下のグループに属している場合のみ削除する。ルール評価は行わない。
        /// settings が null の場合はプロジェクトのデフォルト設定を使う。Addressables 未設定の場合は何もしない。
        /// </summary>
        /// <remarks>
        /// ConfigFolder 配下のパスはここでは除外していない（削除済みのため判定材料がパスでなく GUID のみ）。
        /// ただし削除はエントリの所属グループが managedGroups に含まれる場合に限られる（資産単位の所有権判定）ため、
        /// ConfigFolder 内資産が誤って削除される実害はない。
        /// </remarks>
        public static void RemoveEntriesForDeletedAssets(IEnumerable<string> deletedGuids, AddressableAssetSettings settings = null)
        {
            settings ??= AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var rules = RuleCollector.CollectRules();
            var entries = GetOrderedEntries(rules);
            var managedGroups = new HashSet<string>(entries.Select(e => e.GroupName));

            foreach (var guid in deletedGuids)
            {
                if (string.IsNullOrEmpty(guid)) continue;
                AddressTellerApplier.RemoveEntryForDeletedAsset(guid, settings, managedGroups);
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
            WarnOnDuplicateOrders(rules);
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

        private static void WarnOnDuplicateOrders(IReadOnlyList<AddressRuleBase> rules)
        {
            foreach (var group in RuleCollector.FindDuplicateOrders(rules))
            {
                var names = string.Join(", ", group.Select(r => r.GetType().Name));
                Debug.LogWarning($"[AddressTeller] Order={group.Key} のルールクラスが複数あります: {names}。評価順序が意図通りか確認してください。");
            }
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
