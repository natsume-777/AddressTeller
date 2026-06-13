using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

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
            return ApplyAll(settings, NullProgressReporter.Instance);
        }

        /// <summary>
        /// <see cref="ApplyAll(AddressableAssetSettings)"/> に進捗報告とキャンセルを追加したオーバーロード。
        /// progress が false を返した時点までの結果を返して中断する。
        /// </summary>
        /// <remarks>
        /// キャンセル時、その時点までに処理済みのアセットへの書き込みは既に完了している（部分適用）。
        /// 中断後に Addressables の状態を巻き戻すことはしない。
        /// </remarks>
        public static IReadOnlyList<ValidationResult> ApplyAll(AddressableAssetSettings settings, IProgressReporter progress)
        {
            return ApplyAll(AssetDatabase.GetAllAssetPaths(), settings, progress);
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
            return ApplyAll(paths, settings, NullProgressReporter.Instance);
        }

        /// <summary>
        /// <see cref="ApplyAll(IEnumerable{string}, AddressableAssetSettings)"/> に進捗報告とキャンセルを追加したオーバーロード。
        /// progress が false を返した時点までの結果を返して中断する。
        /// </summary>
        /// <remarks>
        /// キャンセル時、その時点までに処理済みのアセットへの書き込みは既に完了している（部分適用）。
        /// 中断後に Addressables の状態を巻き戻すことはしない。
        /// </remarks>
        public static IReadOnlyList<ValidationResult> ApplyAll(IEnumerable<string> paths, AddressableAssetSettings settings, IProgressReporter progress)
        {
            if (s_isApplying) return Array.Empty<ValidationResult>();
            s_isApplying = true;
            try
            {
                settings ??= AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null) return Array.Empty<ValidationResult>();

                progress ??= NullProgressReporter.Instance;

                var rules = RuleCollector.CollectEnabledRules();
                RuleEvaluationPipeline.WarnOnDuplicateOrders(rules);
                var setup = RuleEvaluationPipeline.BuildSetup(settings, rules);
                // ManagedGroups（CleanupStaleEntriesの対象判定）は有効化されているルールのグループのみが対象。
                // 無効化中のルールが管理するグループのエントリはApplyAllでは掃除対象外（managed外扱い）になるが、
                // 資産削除時のRemoveEntriesForDeletedAssetsは全ルール対象で掃除するため、両者の間に非対称が存在する。

                var issues = new List<ValidationResult>();

                var pathList = paths as IList<string> ?? paths.ToList();
                var total = pathList.Count;

                var cancelled = false;
                for (var i = 0; i < total; i++)
                {
                    var path = pathList[i];

                    if (!progress.Report(i, total, path))
                    {
                        cancelled = true;
                        break;
                    }

                    var ctx = RuleEvaluationPipeline.BuildContext(path);
                    if (ctx == null) continue;
                    if (AssetFilter.ShouldExclude(ctx, setup.ConfigFolder)) continue;

                    var resolution = RuleEvaluator.Evaluate(ctx, setup.Entries);
                    RuleEvaluationPipeline.AddRuleErrors(ctx, resolution, issues);

                    var result = AddressTellerApplier.Apply(ctx, resolution, settings, setup.ExistingGroupNames, setup.ManagedGroups);
                    if (!result.IsOk)
                        issues.Add(result);
                }

                // キャンセル時は完了通知を呼ばない（中断したのに「完了」を通知すると意味的に矛盾するため）。
                if (!cancelled)
                    progress.Report(total, total, string.Empty);

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

            // ルールの On/Off 設定に関わらず、削除追従の所有権判定（managedGroups）は全ルールを対象にする。
            // 無効化されたルールが過去に作ったエントリも、設定の有無に関係なく一貫して掃除対象として認識する必要があるため。
            var rules = RuleCollector.CollectRules();
            var setup = RuleEvaluationPipeline.BuildSetup(settings, rules);

            foreach (var guid in deletedGuids)
            {
                if (string.IsNullOrEmpty(guid)) continue;
                AddressTellerApplier.RemoveEntryForDeletedAsset(guid, settings, setup.ManagedGroups);
            }
        }

        /// <summary>
        /// 全ルールを全アセットに対して検証し、問題のある結果を返す。
        /// settings が null の場合はプロジェクトのデフォルト設定を使う。Addressables 未設定の場合は空リストを返す。
        /// </summary>
        public static IReadOnlyList<ValidationResult> ValidateAll(AddressableAssetSettings settings = null)
        {
            return ValidateAll(settings, NullProgressReporter.Instance);
        }

        /// <summary>
        /// <see cref="ValidateAll(AddressableAssetSettings)"/> に進捗報告とキャンセルを追加したオーバーロード。
        /// progress が false を返した時点までの結果を返して中断する（Validate は書き込みを行わないため、
        /// 中断による副作用はない）。
        /// </summary>
        public static IReadOnlyList<ValidationResult> ValidateAll(AddressableAssetSettings settings, IProgressReporter progress)
        {
            settings ??= AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return Array.Empty<ValidationResult>();

            progress ??= NullProgressReporter.Instance;

            var rules = RuleCollector.CollectEnabledRules();
            RuleEvaluationPipeline.WarnOnDuplicateOrders(rules);
            var setup = RuleEvaluationPipeline.BuildSetup(settings, rules);

            var issues = new List<ValidationResult>();

            var pathList = AssetDatabase.GetAllAssetPaths();
            var total = pathList.Length;

            var cancelled = false;
            for (var i = 0; i < total; i++)
            {
                var path = pathList[i];

                if (!progress.Report(i, total, path))
                {
                    cancelled = true;
                    break;
                }

                var ctx = RuleEvaluationPipeline.BuildContext(path);
                if (ctx == null) continue;
                if (AssetFilter.ShouldExclude(ctx, setup.ConfigFolder)) continue;

                var resolution = RuleEvaluator.Evaluate(ctx, setup.Entries);
                RuleEvaluationPipeline.AddRuleErrors(ctx, resolution, issues);

                var result = AddressTellerApplier.Validate(ctx, resolution, setup.ExistingGroupNames);
                if (!result.IsOk)
                    issues.Add(result);
            }

            // キャンセル時は完了通知を呼ばない（中断したのに「完了」を通知すると意味的に矛盾するため）。
            if (!cancelled)
                progress.Report(total, total, string.Empty);

            return issues;
        }
    }
}
