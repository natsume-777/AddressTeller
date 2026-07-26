using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;

namespace AddressTeller.Editor
{
    public sealed class AddressTellerPostprocessor : AssetPostprocessor
    {
        // 自身が ApplyAll() で書き込んだ変更が OnPostprocessAllAssets を再トリガーしても
        // 無限ループにならないようにするための再入ガード。
        // Service.s_isApplying は Menu/CLI 経由の呼び出しでの再入を防ぐ別ガードで、
        // Postprocessor から呼ばれた場合はそちらが false のままのため、両方が必要。
        private static bool s_isApplying;

        public override int GetPostprocessOrder() => AddressTellerSettings.PostprocessOrder;

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssets)
        {
            if (s_isApplying) return;
            if (!AddressTellerSettings.PostprocessEnabled) return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var configFolder = settings.ConfigFolder;
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

            // "/" 境界込みで比較する（AssetFilter.ShouldExcludeByPath と同様。隣接フォルダの誤除外を避けるため）。
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
