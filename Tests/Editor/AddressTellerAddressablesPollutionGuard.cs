using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor.Tests
{
    /// <summary>
    /// EditMode テストの実行中に、本番の Addressables 設定
    /// （AddressableAssetSettings.asset / AddressableAssetGroupSortSettings.asset）が
    /// 変更されていないかをアセンブリ全体で監視する安全網。
    /// テストアセンブリ内のどこかで isPersisted: true な AddressableAssetSettings.Create を
    /// 呼んでしまった場合などに、本番設定への副作用（sortOrder への余分な GUID 追加、
    /// m_GroupAssets への {fileID: 0} 残骸、m_currentHash のリセット等）を検出する。
    /// </summary>
    [SetUpFixture]
    public sealed class AddressTellerAddressablesPollutionGuard
    {
        private string _settingsJsonBefore;
        private string _sortSettingsJsonBefore;

        [OneTimeSetUp]
        public void CaptureBaseline()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            _settingsJsonBefore = settings != null ? EditorJsonUtility.ToJson(settings) : null;

            var sort = AddressableAssetGroupSortSettings.GetSettings();
            _sortSettingsJsonBefore = sort != null ? EditorJsonUtility.ToJson(sort) : null;
        }

        [OneTimeTearDown]
        public void AssertNoPollution()
        {
            var settingsPolluted = CheckAndRestore(
                AddressableAssetSettingsDefaultObject.Settings,
                _settingsJsonBefore,
                "AddressableAssetSettings.asset");

            var sortSettingsPolluted = CheckAndRestore(
                AddressableAssetGroupSortSettings.GetSettings(),
                _sortSettingsJsonBefore,
                "AddressableAssetGroupSortSettings.asset");

            if (settingsPolluted || sortSettingsPolluted)
            {
                Assert.Fail(
                    "EditMode テストが本番 Addressables 設定（AddressableAssetSettings.asset / " +
                    "AddressableAssetGroupSortSettings.asset）を変更した。汚染された設定はベースラインに" +
                    "復元したが、原因となったテストクラスを確認する必要がある。" +
                    "isPersisted: true で AddressableAssetSettings.Create を呼んでいるテストクラスがないか確認せよ。");
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
    }
}
