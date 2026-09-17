using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditorInternal;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// EditMode テストの実行中、本番の Addressables 設定
    /// （AddressableAssetSettings.asset / AddressableAssetGroupSortSettings.asset / 各グループ .asset の
    /// entries を含む）および ProjectSettings/AddressTellerSettings.asset が変更されていないかを
    /// アセンブリ全体で監視する安全網であり、あわせてテストアセンブリの実行中は AddressTellerPostprocessor
    /// （Auto-apply on import）を無効化する。
    /// テストが Assets/ 配下に実アセットを作成する際（AssetDatabase.CreateAsset / PrefabUtility 等）、
    /// そのインポートで本番の AddressTellerPostprocessor が発火し、本番の管理グループに対して
    /// ApplyAll（CleanupStaleEntries 等）が走ってしまうことを防ぐ。
    /// テストアセンブリ内のどこかで isPersisted: true な AddressableAssetSettings.Create を
    /// 呼んでしまった場合などに、本番設定への副作用（sortOrder への余分な GUID 追加、
    /// m_GroupAssets への {fileID: 0} 残骸、m_currentHash のリセット、グループ内 entries の増減、
    /// ProjectSettings/AddressTellerSettings.asset への意図しない書き込み等）を検出する。
    /// </summary>
    [SetUpFixture]
    public sealed class AddressTellerAddressablesPollutionGuard
    {
        private string _settingsJsonBefore;
        private string _sortSettingsJsonBefore;
        private readonly Dictionary<AddressableAssetGroup, string> _groupJsonBefore = new();

        private string _addressTellerSettingsPath;
        private string _addressTellerSettingsJsonBefore;

        [OneTimeSetUp]
        public void CaptureBaselineAndDisablePostprocessor()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            _settingsJsonBefore = settings != null ? EditorJsonUtility.ToJson(settings) : null;

            var sort = AddressableAssetGroupSortSettings.GetSettings();
            _sortSettingsJsonBefore = sort != null ? EditorJsonUtility.ToJson(sort) : null;

            _groupJsonBefore.Clear();
            if (settings != null)
            {
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;
                    _groupJsonBefore[group] = EditorJsonUtility.ToJson(group);
                }
            }

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            _addressTellerSettingsPath = Path.Combine(projectRoot, "ProjectSettings", "AddressTellerSettings.asset");
            _addressTellerSettingsJsonBefore = ReadAddressTellerSettingsJsonFromDisk(_addressTellerSettingsPath);

            // AddressTellerPostprocessor.SuppressForTests はプロセスメモリ上だけで完結する static bool
            // であり、いかなる .asset ファイルとも接続していない。以前は
            // AddressTellerSettings.PostprocessEnabled（ProjectSettings/AddressTellerSettings.asset に
            // 永続化される設定）を直接操作する方式だったが、他のテストが別プロパティを正規のセッター経由で
            // 変更した際の SaveChanges()（ScriptableSingleton.Save() はオブジェクト全体を書き出す）に
            // 巻き添えでディスクへ書き出されてしまう事故が実際に起きたため、この方式へ乗り換えた。
            // 詳細は AddressTellerPostprocessor.cs の SuppressForTests の XML doc を参照。
            AddressTellerPostprocessor.SuppressForTests = true;
        }

        [OneTimeTearDown]
        public void RestorePostprocessorAndAssertNoPollution()
        {
            AddressTellerPostprocessor.SuppressForTests = false;

            var settingsPolluted = CheckAndRestore(
                AddressableAssetSettingsDefaultObject.Settings,
                _settingsJsonBefore,
                "AddressableAssetSettings.asset");

            var sortSettingsPolluted = CheckAndRestore(
                AddressableAssetGroupSortSettings.GetSettings(),
                _sortSettingsJsonBefore,
                "AddressableAssetGroupSortSettings.asset");

            var groupsPolluted = false;
            foreach (var kvp in _groupJsonBefore)
            {
                var group = kvp.Key;
                if (group == null)
                {
                    // UnityEngine.Object の == null はオーバーロードされており、破棄済み(Destroyed)の
                    // オブジェクトも null 相当になる。JSON 復元では元に戻せないため、検出のみ行う。
                    Debug.LogError("[AddressTeller] EditMode テストの実行中に本番の Addressables グループの1つが破棄された。" +
                        "復元できないため、原因となったテストクラスを確認し、本番 .asset の状態を目視確認すること。");
                    groupsPolluted = true;
                    continue;
                }

                if (CheckAndRestore(group, kvp.Value, $"group '{group.Name}' ({AssetDatabase.GetAssetPath(group)})"))
                    groupsPolluted = true;
            }

            var addressTellerSettingsPolluted = CheckAndRestoreAddressTellerSettings(_addressTellerSettingsPath, _addressTellerSettingsJsonBefore);

            if (settingsPolluted || sortSettingsPolluted || groupsPolluted || addressTellerSettingsPolluted)
            {
                Assert.Fail(
                    "EditMode テストが本番 Addressables 設定（AddressableAssetSettings.asset / " +
                    "AddressableAssetGroupSortSettings.asset / グループ .asset の entries を含む）、または " +
                    "ProjectSettings/AddressTellerSettings.asset を変更した。" +
                    "汚染された設定はベースラインに復元したが、原因となったテストクラスを確認する必要がある。" +
                    "isPersisted: true で AddressableAssetSettings.Create を呼んでいるテストクラスや、" +
                    "本番設定に対して ApplyAll 等を呼んでいるテストクラス、" +
                    "AddressTellerSettings.* の変更を TearDown で復元し忘れているテストクラスがないか確認せよ。");
            }
        }

        /// <summary>
        /// 対象オブジェクトをベースラインの JSON と比較する。差分があれば復元してログ警告を出し true を返す。
        /// 対象オブジェクトが null（Addressables 未構成環境）、またはベースライン未取得の場合は何もせず false を返す。
        /// 差分がない場合は対象オブジェクトに一切触れない。
        /// </summary>
        /// <remarks>
        /// この復元は best-effort であり、汚染を検出して報告することが本来の目的。
        /// <see cref="EditorJsonUtility.FromJsonOverwrite"/> はオブジェクト参照を含むフィールド
        /// （<c>m_GroupAssets</c> 等）まで完全に元の状態へ戻せるとは限らないため、
        /// Assert.Fail が出た場合は復元結果を過信せず、原因テストを特定したうえで
        /// 本番 .asset の状態を目視確認すること。
        /// </remarks>
        private static bool CheckAndRestore(Object target, string jsonBefore, string assetLabel)
        {
            if (target == null || jsonBefore == null)
                return false;

            var jsonAfter = EditorJsonUtility.ToJson(target);
            if (jsonAfter == jsonBefore)
                return false;

            Debug.LogWarning($"[AddressTeller] EditMode テストの実行により {assetLabel} が変更されたため、ベースラインに復元します。");

            EditorJsonUtility.FromJsonOverwrite(jsonBefore, target);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();

            return true;
        }

        /// <summary>
        /// ProjectSettings/AddressTellerSettings.asset をディスクから読み直し、
        /// <see cref="EditorJsonUtility.ToJson(object)"/> で値を正規化した文字列として返す。
        /// <see cref="InternalEditorUtility.LoadSerializedFileAndForget"/> で AssetDatabase・インポート
        /// パイプラインを経由せず一時オブジェクトとして読み込むため、現在エディタ内にロード済みの
        /// <c>AddressTellerSettingsAsset</c> シングルトン（メモリ上の値）には一切触れない。
        /// バイト列そのものではなく値の JSON を比較するのは、シリアライズ書式の些細な差異
        /// （将来のフィールド追加時のデフォルト値挿入・改行コード等）による偽陽性を避けるため。
        /// ファイルが存在しない、または読み込めない場合は null。
        /// </summary>
        private static string ReadAddressTellerSettingsJsonFromDisk(string path)
        {
            if (!File.Exists(path)) return null;

            var loaded = InternalEditorUtility.LoadSerializedFileAndForget(path);
            try
            {
                var asset = loaded?.OfType<AddressTellerSettingsAsset>().FirstOrDefault();
                return asset != null ? EditorJsonUtility.ToJson(asset) : null;
            }
            finally
            {
                if (loaded != null)
                    foreach (var obj in loaded)
                        if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        /// <summary>
        /// ProjectSettings/AddressTellerSettings.asset の内容（値の JSON）をベースラインと比較する。
        /// 差分があれば、(1) 現在エディタ内にロード済みの <c>AddressTellerSettingsAsset</c> シングルトン
        /// （メモリ上の値）をベースラインへ上書きし、(2) その状態を通常の保存経路
        /// （<c>AddressTellerSettingsAsset.SaveChanges()</c>）でディスクへ書き戻す。生のバイト列を
        /// 直接書き戻すのではなく、必ず「メモリを正しい値に戻してから正規の保存経路を通す」ことで、
        /// メモリとディスクの両方が矛盾なくベースラインへ揃う（ドメインリロードや Editor 再起動を
        /// 必要としない）。ベースライン未取得（ファイルが元々存在しなかった）の場合は何もせず false を返す。
        /// </summary>
        private static bool CheckAndRestoreAddressTellerSettings(string path, string jsonBefore)
        {
            if (jsonBefore == null)
                return false;

            var jsonAfter = ReadAddressTellerSettingsJsonFromDisk(path);
            if (jsonAfter == jsonBefore)
                return false;

            Debug.LogWarning("[AddressTeller] EditMode テストの実行により ProjectSettings/AddressTellerSettings.asset が変更されたため、ベースラインに復元します。");

            EditorJsonUtility.FromJsonOverwrite(jsonBefore, AddressTellerSettingsAsset.instance);
            AddressTellerSettingsAsset.instance.SaveChanges();

            return true;
        }
    }
}
