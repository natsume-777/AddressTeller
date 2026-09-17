using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;

namespace AddressTeller.Editor
{
    /// <summary>
    /// When <see cref="AddressTellerSettings.PostprocessEnabled"/> is on, automatically applies rules to
    /// imported/moved assets and removes entries for deleted assets.
    /// </summary>
    public sealed class AddressTellerPostprocessor : AssetPostprocessor
    {
        // 自身が ApplyAll() で書き込んだ変更が OnPostprocessAllAssets を再トリガーしても
        // 無限ループにならないようにするための再入ガード。
        // Service.s_isApplying は Menu/CLI 経由の呼び出しでの再入を防ぐ別ガードで、
        // Postprocessor から呼ばれた場合はそちらが false のままのため、両方が必要。
        private static bool s_isApplying;

        /// <summary>
        /// テストアセンブリの実行中だけ本体の発火を止めるための抑止スイッチ。
        /// <see cref="AddressTellerSettings.PostprocessEnabled"/>（ProjectSettings/AddressTellerSettings.asset
        /// に永続化される設定）とは意図的に無関係にしている。テスト側がこのスイッチを立てる目的で
        /// PostprocessEnabled を操作すると、他のテストが CleanupStaleEntries 等の別プロパティを変更した際の
        /// AddressTellerSettings.* セッターの SaveChanges() が ScriptableSingleton.Save()
        /// （変更したプロパティだけでなくオブジェクト全体を書き出す）を呼ぶため、一時的に無効化した
        /// PostprocessEnabled の値までディスクへ巻き添えで永続化されてしまう（実際に発生した事象）。
        /// このフィールドはプロセスメモリ上だけで完結する static bool であり、いかなる .asset ファイルとも
        /// 一切接続していないため、この巻き添え永続化の経路が構造的に存在しない。
        /// s_isApplying・Service.s_isApplying とは役割が異なる別ガードである
        /// （それらは「自分自身が書き込んだ変更で再トリガーされる」のを防ぐ再入防止、
        /// こちらは「テストアセンブリの実行中は本体を一切発火させない」というテスト専用の外部抑止）。
        /// AddressTellerAddressablesPollutionGuard（Tests/Editor 配下の [SetUpFixture]）だけが操作する。
        /// </summary>
        internal static bool SuppressForTests;

        /// <summary>Postprocess order, taken from <see cref="AddressTellerSettings.PostprocessOrder"/>.</summary>
        public override int GetPostprocessOrder() => AddressTellerSettings.PostprocessOrder;

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssets)
        {
            if (s_isApplying) return;
            if (SuppressForTests) return;
            if (!AddressTellerSettings.PostprocessEnabled) return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            // settings.ConfigFolder は Windows では "Assets\AddressableAssetsData" のようにバックスラッシュ
            // 区切りで返ることがある。changedPaths（AssetPostprocessor から渡されるパス）は常にフォワードスラッシュ
            // 区切りのため、正規化しないと ShouldSkip の前方一致判定が Windows で常に外れてしまう
            // （RuleEvaluationPipeline.BuildSetup の正規化と同じ理由。読み取り箇所ごとに正規化する必要がある）。
            var configFolder = settings.ConfigFolder?.Replace('\\', '/');
            var changed = importedAssets.Concat(deletedAssets).Concat(movedAssets).ToArray();
            if (ShouldSkip(configFolder, changed)) return;

            s_isApplying = true;
            try
            {
                // movedFromAssets（移動・リネーム前のパス）は処理しない。
                // エントリは GUID ベースで管理されているため、movedAssets 側（新パス）を
                // Apply するだけで CreateOrMoveEntry によりエントリが更新され、
                // 新パスがどのルールにもマッチしなければ Skipped としてクリーンアップ対象になる。
                var targetPaths = importedAssets.Concat(movedAssets);
                var issues = AddressTellerService.ApplyAll(targetPaths, settings);
                AddressTellerIssueLogger.LogAll(issues);

                if (deletedAssets.Length > 0)
                {
                    var deletedGuids = ResolveDeletedGuids(
                        deletedAssets,
                        p => AssetDatabase.AssetPathToGUID(p, AssetPathToGUIDOptions.IncludeRecentlyDeletedAssets));

                    // 削除された各エントリは AddressTellerApplier 側で個別に Warning ログ済みのため、
                    // ここでは戻り値（削除済みエントリ一覧）を意図的に破棄する。
                    _ = AddressTellerService.RemoveEntriesForDeletedAssets(deletedGuids, settings);
                }
            }
            finally
            {
                s_isApplying = false;
            }
        }

        /// <summary>
        /// 変更されたパス群（import/delete/move の全パスをまとめたもの）が ConfigFolder 配下のみ、
        /// または変更が0件かどうかを判定する。true の場合、OnPostprocessAllAssets は処理をスキップする
        /// （Addressables 設定ファイル自体の変更のみでは Apply を走らせる必要がないため）。
        /// </summary>
        internal static bool ShouldSkip(string configFolder, IReadOnlyList<string> changedPaths)
        {
            if (changedPaths.Count == 0) return true;

            // configFolder は呼び出し元（OnPostprocessAllAssets）で settings.ConfigFolder?.Replace(...) を
            // 経由するため null になりうる（settings.ConfigFolder 自体が null を返す異常系）。
            // 「ConfigFolder 配下かどうか」を判定できない以上、安全側（スキップしない＝Apply を走らせる）に倒す。
            if (configFolder == null) return false;

            // "/" 境界込みで比較する。これは早期スキップ用の独自の保守的なヒューリスティックであり、
            // AssetFilter.ShouldExcludeByPath（Addressables 本体に揃えた境界なしの前方一致）とは
            // 意図的に異なる。ここで隣接フォルダ（例: AddressableAssetsData_Backup）まで
            // ConfigFolder 配下扱いにして誤ってスキップしてしまうと、そのフォルダ内の変更に対して
            // Apply が一切走らなくなる方が実害が大きいため、スキップ判定側は安全に倒して境界を厳格にしている。
            // 実際に評価対象から除外するかどうかは、スキップしなかった場合に呼ばれる AssetFilter 側の判定に委ねる。
            var configFolderPrefix = configFolder.TrimEnd('/') + "/";
            return changedPaths.All(p => p.StartsWith(configFolderPrefix, StringComparison.Ordinal));
        }

        /// <summary>
        /// 削除されたアセットパス群から、GUID解決に成功したもの（空文字・null でないもの）だけを抽出する。
        /// pathToGuid は実運用では AssetDatabase.AssetPathToGUID(..., IncludeRecentlyDeletedAssets) を渡すが、
        /// テストでは差し替え可能にすることで AssetDatabase に依存せずフィルタリングロジックのみを検証できる。
        /// </summary>
        internal static IEnumerable<string> ResolveDeletedGuids(IReadOnlyList<string> deletedAssets, Func<string, string> pathToGuid)
        {
            foreach (var path in deletedAssets)
            {
                var guid = pathToGuid(path);
                if (!string.IsNullOrEmpty(guid))
                    yield return guid;
            }
        }
    }
}
