using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor
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
        /// <remarks>
        /// 戻り値には <see cref="ValidationStatus.GroupWillBeCreated"/>（IsOk=true、AutoCreateMissingGroups による
        /// グループ作成予定・実施の提示）が情報提供として含まれる場合がある。Apply の中止判定など「問題」として扱う場合は
        /// <c>!result.IsOk</c> でフィルタすること。
        /// </remarks>
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
            return ApplyAll(paths, settings, progress, RuleCollector.CollectEnabledRules());
        }

        /// <summary>
        /// <see cref="ApplyAll(IEnumerable{string}, AddressableAssetSettings, IProgressReporter)"/> に
        /// 評価対象ルールの注入を追加したオーバーロード。リフレクションによるルール収集
        /// （<see cref="RuleCollector.CollectEnabledRules()"/>）を経由せず、呼び出し側が用意した
        /// ルール一覧をそのまま評価に使う（テスト等での利用を想定）。
        /// </summary>
        public static IReadOnlyList<ValidationResult> ApplyAll(IEnumerable<string> paths, AddressableAssetSettings settings, IProgressReporter progress, IReadOnlyList<AddressRuleBase> rules)
        {
            if (s_isApplying) return Array.Empty<ValidationResult>();
            s_isApplying = true;
            try
            {
                settings ??= AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null) return Array.Empty<ValidationResult>();

                progress ??= NullProgressReporter.Instance;
                rules ??= Array.Empty<AddressRuleBase>();

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

                    if (AssetFilter.ShouldExcludeByPath(path, setup.ConfigFolder)) continue;

                    var ctx = RuleEvaluationPipeline.BuildContext(path);
                    if (ctx == null) continue;
                    if (AssetFilter.ShouldExclude(ctx, setup.ConfigFolder)) continue;

                    var resolution = RuleEvaluator.Evaluate(ctx, setup.Entries);
                    RuleEvaluationPipeline.AddRuleErrors(ctx, resolution, issues);

                    var result = AddressTellerApplier.Apply(ctx, resolution, settings, setup.ExistingGroupNames, setup.ManagedGroups, setup.AutoCreateMissingGroups);
                    // GroupWillBeCreated は IsOk=true（グループ自動作成が成功した）だが、
                    // 「作成された」ことを提示するため issues に情報として積む。
                    if (!result.IsOk || result.Status == ValidationStatus.GroupWillBeCreated)
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
            // ルールの On/Off 設定に関わらず、削除追従の所有権判定（managedGroups）は全ルールを対象にする。
            // 無効化されたルールが過去に作ったエントリも、設定の有無に関係なく一貫して掃除対象として認識する必要があるため。
            RemoveEntriesForDeletedAssets(deletedGuids, settings, RuleCollector.CollectRules());
        }

        /// <summary>
        /// <see cref="RemoveEntriesForDeletedAssets(IEnumerable{string}, AddressableAssetSettings)"/> に
        /// 評価対象ルールの注入を追加したオーバーロード。所有権判定（managedGroups）に使うルール一覧を
        /// 呼び出し側がそのまま指定する（テスト等での利用を想定）。収集方法（有効/無効の絞り込み）の判断は行わない。
        /// </summary>
        public static void RemoveEntriesForDeletedAssets(IEnumerable<string> deletedGuids, AddressableAssetSettings settings, IReadOnlyList<AddressRuleBase> rules)
        {
            settings ??= AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;
            rules ??= Array.Empty<AddressRuleBase>();

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
        /// <remarks>
        /// 戻り値には <see cref="ValidationStatus.GroupWillBeCreated"/>（IsOk=true、AutoCreateMissingGroups による
        /// グループ作成予定の提示）が情報提供として含まれる場合がある。Apply の中止判定など「問題」として扱う場合は
        /// <c>!result.IsOk</c> でフィルタすること。
        /// </remarks>
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
            return ValidateAll(settings, progress, RuleCollector.CollectEnabledRules());
        }

        /// <summary>
        /// <see cref="ValidateAll(AddressableAssetSettings, IProgressReporter)"/> に
        /// 評価対象ルールの注入を追加したオーバーロード。リフレクションによるルール収集
        /// （<see cref="RuleCollector.CollectEnabledRules()"/>）を経由せず、呼び出し側が用意した
        /// ルール一覧をそのまま評価に使う（テスト等での利用を想定）。
        /// </summary>
        public static IReadOnlyList<ValidationResult> ValidateAll(AddressableAssetSettings settings, IProgressReporter progress, IReadOnlyList<AddressRuleBase> rules)
        {
            settings ??= AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return Array.Empty<ValidationResult>();

            progress ??= NullProgressReporter.Instance;
            rules ??= Array.Empty<AddressRuleBase>();

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

                if (AssetFilter.ShouldExcludeByPath(path, setup.ConfigFolder)) continue;

                var ctx = RuleEvaluationPipeline.BuildContext(path);
                if (ctx == null) continue;
                if (AssetFilter.ShouldExclude(ctx, setup.ConfigFolder)) continue;

                var resolution = RuleEvaluator.Evaluate(ctx, setup.Entries);
                RuleEvaluationPipeline.AddRuleErrors(ctx, resolution, issues);

                var result = AddressTellerApplier.Validate(ctx, resolution, setup.ExistingGroupNames, setup.AutoCreateMissingGroups);
                // GroupWillBeCreated は IsOk=true（Apply をブロックしない）だが、
                // 「作成予定N件」を提示するため issues に情報として積む。
                if (!result.IsOk || result.Status == ValidationStatus.GroupWillBeCreated)
                    issues.Add(result);
            }

            // キャンセル時は完了通知を呼ばない（中断したのに「完了」を通知すると意味的に矛盾するため）。
            if (!cancelled)
                progress.Report(total, total, string.Empty);

            return issues;
        }
    }
}
