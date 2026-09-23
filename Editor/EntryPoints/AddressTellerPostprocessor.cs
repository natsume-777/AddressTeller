using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;

namespace AddressTeller.Editor
{
    /// <summary>
    /// When <see cref="AddressTellerSettings.PostprocessEnabled"/> is on, automatically applies rules to
    /// imported/moved assets. When <see cref="AddressTellerSettings.CleanupStaleEntries"/> is also on, it
    /// additionally asks <see cref="AddressTellerService.RemoveEntriesForDeletedAssets"/> to remove, from
    /// groups AddressTeller owns, any entry left over from an asset that was deleted — in practice
    /// Addressables itself has usually already removed that entry on its own by the time this runs,
    /// regardless of any AddressTeller setting, so this is a best-effort follow-up rather than the thing
    /// that makes the entry disappear.
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
        /// <see cref="AddressTellerSettings.PostprocessEnabled"/>（ProjectSettings/AddressTellerSettings.json
        /// に永続化される設定）とは意図的に無関係にしている。テスト側がこのスイッチを立てる目的で
        /// PostprocessEnabled を操作すると、他のテストが CleanupStaleEntries 等の別プロパティを変更した際の
        /// AddressTellerSettings.* セッターが経由する AddressTellerSettingsAsset.Mutate() がファイル全体を
        /// 書き出すため、一時的に
        /// 無効化した PostprocessEnabled の値までディスクへ巻き添えで永続化されてしまう
        /// （実際に発生した事象）。このフィールドはプロセスメモリ上だけで完結する static bool であり、
        /// いかなる設定ファイルとも一切接続していないため、この巻き添え永続化の経路が構造的に存在しない。
        /// s_isApplying・Service.s_isApplying とは役割が異なる別ガードである
        /// （それらは「自分自身が書き込んだ変更で再トリガーされる」のを防ぐ再入防止、
        /// こちらは「テストアセンブリの実行中は本体を一切発火させない」というテスト専用の外部抑止）。
        /// AddressTellerAddressablesPollutionGuard（Tests/Editor 配下の [SetUpFixture]）だけが操作する。
        /// </summary>
        internal static bool SuppressForTests;

        /// <summary>
        /// 直近にログした重複 guid 状態のシグネチャ。内容が変わらない限り再ログしない
        /// （設定ゲートの <see cref="SettingsGateLogPolicy.OncePerDistinctFailure"/> と同じ考え方）。
        /// null は「まだ一度もログしていない、または直近の import で重複が解消された」ことを表す。
        /// テストからリセットできるよう internal にしている。
        /// </summary>
        internal static string s_lastLoggedDuplicateSignature;

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
            // Postprocessor は1 import ごとに毎回呼ばれるため、設定ファイルが壊れたままだと Always ポリシー
            // ではログが連発する。ファイルが直っていない限り最初の1回だけログする。
            if (!AddressTellerSettings.EnsureLoaded(SettingsGateLogPolicy.OncePerDistinctFailure)) return;
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

            // 同一 guid が2つ以上のグループにまたがって存在する状態では、この後の ApplyAll /
            // RemoveEntriesForDeletedAssets はいずれも内部で同じ検出にかかり何も書かないが、両方が
            // それぞれ独立にログすると（削除を伴う import では特に）1回の import で同じ内容が複数行・
            // 重複して出力される。ここで1回だけ検出し、書き込み側の呼び出し自体を丸ごとスキップすることで
            // 二重ログを避ける。ログ自体も、内容が変わらない間は最初の1回だけに抑える
            // （設定ゲートの OncePerDistinctFailure と同じ考え方。毎 import 連発すると気付きにくくなるため）。
            var duplicateAssetEntries = DuplicateAssetEntryDetector.Detect(settings);
            if (ShouldLogDuplicateAssetEntries(duplicateAssetEntries))
                foreach (var duplicate in duplicateAssetEntries)
                    UnityEngine.Debug.LogError($"[AddressTeller] {duplicate.Message}");
            if (duplicateAssetEntries.Count > 0) return;

            s_isApplying = true;
            try
            {
                // movedFromAssets（移動・リネーム前のパス）は処理しない。
                // エントリは GUID ベースで管理されているため、movedAssets 側（新パス）を
                // Apply するだけで CreateOrMoveEntry によりエントリが更新され、
                // 新パスがどのルールにもマッチしなければ Skipped としてクリーンアップ対象になる。
                // targetPaths（今回変更された資産のみ）に対する ApplyAll は、別アセット間のアドレス重複
                // （ValidationStatus.DuplicateAddress）を検出しない。重複判定にはプロジェクト全体のアドレス
                // 集合が必要だが、毎 import でプロジェクト全体を走査するのはコストが見合わない。
                // フルスキャンする ValidateAll / BuildPredictedSnapshot（Apply All・Apply with Validate・
                // 各 CLI が経由する dry-run）側でのみ検出する。
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
        /// <paramref name="duplicateAssetEntries"/>（<see cref="DuplicateAssetEntryDetector.Detect"/> の結果）を
        /// 今回ログすべきかどうかを判定し、<see cref="s_lastLoggedDuplicateSignature"/> を更新する。
        /// OnPostprocessAllAssets（Unity のインポートフックからしか発火せず EditMode テストから直接
        /// 再現できない）からロジックだけを抽出したもの（<see cref="ShouldSkip"/> と同じ意図）。
        /// 空リストの場合は記憶をリセットして false を返す（次に同じ内容の重複が起きたときに改めて
        /// ログできるようにするため）。空でない場合、直前にログした内容（Message の連結）と一致すれば
        /// false（再ログしない）、変化していれば記憶を更新して true を返す。
        /// </summary>
        internal static bool ShouldLogDuplicateAssetEntries(IReadOnlyList<ValidationResult> duplicateAssetEntries)
        {
            if (duplicateAssetEntries.Count == 0)
            {
                s_lastLoggedDuplicateSignature = null;
                return false;
            }

            var signature = string.Join("\u0001", duplicateAssetEntries.Select(d => d.Message));
            if (signature == s_lastLoggedDuplicateSignature) return false;

            s_lastLoggedDuplicateSignature = signature;
            return true;
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
