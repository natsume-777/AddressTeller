using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor
{
    /// <summary>
    /// 開発時専用の包含フィルタ実行。「このルールだけ」「このアセットだけ」「このグループだけ」を
    /// dry-run で確認できるようにする。いずれも <see cref="AddressTellerResultWindow"/> をプレビュー表示のみで開き、
    /// 即時 Apply は行わない。
    /// </summary>
    internal static class AddressTellerScopedPreview
    {
        /// <summary>
        /// 指定した1ルールのみを適用した場合の dry-run プレビューを表示する。
        /// 全アセットを対象とする（無効化中のルールでも、明示的に選択された場合は適用対象に含める）。
        /// </summary>
        public static void RunRulePreview(AddressableAssetSettings settings, AddressRuleBase rule)
        {
            var paths = AssetDatabase.GetAllAssetPaths();
            var rules = new[] { rule };

            var dryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths, rules);

            var title = AddressTellerScopeBuilder.BuildScopeTitle(
                "AddressTeller Preview", $"Rule: {rule.GetType().Name}");
            var notice = AddressTellerScopeBuilder.BuildScopeNotice(isRuleFiltered: true);

            AddressTellerResultWindow.Show(dryRun, title, settings, notice: notice);
        }

        /// <summary>
        /// 選択アセット（フォルダ含む）に対し、有効な全ルールを適用した場合の dry-run プレビューを表示する。
        /// フォルダは配下アセットへ再帰展開する。
        /// </summary>
        public static void RunAssetPreview(AddressableAssetSettings settings, IReadOnlyList<string> selectedPaths)
        {
            var paths = AddressTellerScopeBuilder.ExpandFolders(
                selectedPaths,
                AssetDatabase.IsValidFolder,
                folder => AssetDatabase.FindAssets("", new[] { folder })
                    .Select(AssetDatabase.GUIDToAssetPath));

            var rules = RuleCollector.CollectEnabledRules();

            var dryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths, rules);

            var scopeLabel = paths.Count == 1 ? $"Asset: {paths[0]}" : $"Assets: {paths.Count}";
            var title = AddressTellerScopeBuilder.BuildScopeTitle("AddressTeller Preview", scopeLabel);

            AddressTellerResultWindow.Show(dryRun, title, settings);
        }

        /// <summary>
        /// 指定グループの現メンバー起点で、有効な全ルールを適用した場合の dry-run プレビューを表示する。
        /// グループにフォルダ・サブアセットのエントリが含まれる場合も <see cref="AddressTellerScopeBuilder.ExpandFolders"/> で
        /// 配下アセットへ安全に展開する。
        /// </summary>
        public static void RunGroupPreview(AddressableAssetSettings settings, AddressableAssetGroup group)
        {
            var memberPaths = group.entries
                .Select(e => AssetDatabase.GUIDToAssetPath(e.guid))
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            var paths = AddressTellerScopeBuilder.ExpandFolders(
                memberPaths,
                AssetDatabase.IsValidFolder,
                folder => AssetDatabase.FindAssets("", new[] { folder })
                    .Select(AssetDatabase.GUIDToAssetPath));

            var rules = RuleCollector.CollectEnabledRules();

            var dryRun = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, paths, rules);

            var title = AddressTellerScopeBuilder.BuildScopeTitle(
                "AddressTeller Preview", $"Group: {group.Name} (current members)");

            AddressTellerResultWindow.Show(dryRun, title, settings);
        }
    }
}
