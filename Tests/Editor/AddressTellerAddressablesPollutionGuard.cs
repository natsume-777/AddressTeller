using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// EditMode テストの実行中、本番の Addressables 設定
    /// （AddressableAssetSettings.asset / AddressableAssetGroupSortSettings.asset / 各グループ .asset の
    /// entries を含む）および ProjectSettings/AddressTellerSettings.json が変更されていないかを
    /// アセンブリ全体で監視する安全網であり、あわせてテストアセンブリの実行中は AddressTellerPostprocessor
    /// （Auto-apply on import）を無効化する。
    /// テストが Assets/ 配下に実アセットを作成する際（AssetDatabase.CreateAsset / PrefabUtility 等）、
    /// そのインポートで本番の AddressTellerPostprocessor が発火し、本番の管理グループに対して
    /// ApplyAll（CleanupStaleEntries 等）が走ってしまうことを防ぐ。
    /// テストアセンブリ内のどこかで isPersisted: true な AddressableAssetSettings.Create を
    /// 呼んでしまった場合などに、本番設定への副作用（sortOrder への余分な GUID 追加、
    /// m_GroupAssets への {fileID: 0} 残骸、m_currentHash のリセット、グループ内 entries の増減、
    /// ProjectSettings/AddressTellerSettings.json への意図しない書き込み等）を検出する。
    /// </summary>
    [SetUpFixture]
    public sealed class AddressTellerAddressablesPollutionGuard
    {
        private string _settingsJsonBefore;
        private string _sortSettingsJsonBefore;
        private readonly Dictionary<AddressableAssetGroup, string> _groupJsonBefore = new();

        private string _addressTellerSettingsPath;
        private byte[] _addressTellerSettingsBytesBefore;
        private string _addressTellerSettingsTempOverridePath;

        [OneTimeSetUp]
        public void CaptureBaselineAndDisablePostprocessor()
        {
            // AddressTellerPostprocessor.SuppressForTests はプロセスメモリ上だけで完結する static bool
            // であり、いかなる設定ファイルとも接続していない。このメソッドの最初の1行に置く（防御的措置）。
            AddressTellerPostprocessor.SuppressForTests = true;

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

            // 本番の AddressTeller 設定ファイルをバイト列として退避する（不在なら null）。
            _addressTellerSettingsPath = AddressTellerSettingsAsset.AbsoluteFilePath;
            _addressTellerSettingsBytesBefore = File.Exists(_addressTellerSettingsPath)
                ? File.ReadAllBytes(_addressTellerSettingsPath)
                : null;

            // テスト実行中は設定ファイルの読み書き先をテスト専用の一時ファイルへ切り替える。これにより、
            // 個々のテストが AddressTellerSettings.* の setter を正規の経路で呼んでも、本番の
            // ProjectSettings/AddressTellerSettings.json には一切触れない
            // （AddressTellerApplyFlow.s_notifyApplyAborted / AddressTellerPostprocessor.SuppressForTests と
            // 同じ「テスト用シーム」の考え方）。
            _addressTellerSettingsTempOverridePath = Path.Combine(
                Path.GetTempPath(), $"AddressTellerSettings_PollutionGuard_{Guid.NewGuid():N}.json");
            AddressTellerSettingsAsset.FilePathOverride = _addressTellerSettingsTempOverridePath;
            AddressTellerSettingsAsset.ResetInMemoryState();
        }

        [OneTimeTearDown]
        public void RestorePostprocessorAndAssertNoPollution()
        {
            AddressTellerPostprocessor.SuppressForTests = false;

            // テスト専用の一時ファイルを片付け、パスを本番へ戻してメモリを読み直させる。
            try
            {
                if (File.Exists(_addressTellerSettingsTempOverridePath))
                    File.Delete(_addressTellerSettingsTempOverridePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AddressTeller] テスト用の設定ファイル一時パスの削除に失敗した " +
                    $"('{_addressTellerSettingsTempOverridePath}'): {ex.Message}");
            }

            AddressTellerSettingsAsset.FilePathOverride = null;
            AddressTellerSettingsAsset.ResetInMemoryState();

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

            var addressTellerSettingsPolluted = CheckAndRestoreAddressTellerSettingsFile();

            var addressablesPolluted = settingsPolluted || sortSettingsPolluted || groupsPolluted;

            if (addressablesPolluted || addressTellerSettingsPolluted)
            {
                var causes = new List<string>();
                if (addressablesPolluted)
                {
                    causes.Add("本番 Addressables 設定（AddressableAssetSettings.asset / " +
                        "AddressableAssetGroupSortSettings.asset / グループ .asset の entries を含む）");
                }
                if (addressTellerSettingsPolluted)
                {
                    causes.Add("ProjectSettings/AddressTellerSettings.json の内容");
                }

                var message = "EditMode テストが" + string.Join(" または ", causes) + "を変更した。" +
                    "原因となったテストクラスを確認する必要がある。";

                if (addressablesPolluted)
                {
                    message += " 本番 Addressables 設定はベースラインへ復元済み。" +
                        "isPersisted: true で AddressableAssetSettings.Create を呼んでいるテストクラスや、" +
                        "本番設定に対して ApplyAll 等を呼んでいるテストクラスがないか確認せよ。";
                }
                if (addressTellerSettingsPolluted)
                {
                    message += " ProjectSettings/AddressTellerSettings.json はベースラインへ復元済み。" +
                        "AddressTellerSettingsAsset.FilePathOverride を切り替えずに本番パスへ直接書き込んでいる" +
                        "テストクラスがないか確認せよ。";
                }

                Assert.Fail(message);
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
        private static bool CheckAndRestore(UnityEngine.Object target, string jsonBefore, string assetLabel)
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
        /// 本番の ProjectSettings/AddressTellerSettings.json のバイト列（または不在という状態）を、
        /// ベースラインへそのまま退避・復元するだけの安全網。テスト実行中は
        /// <see cref="AddressTellerSettingsAsset.FilePathOverride"/> により本番パスへの読み書きは経路として
        /// 発生しないはずなので、ここで差分が見つかること自体が「そのシームを経由せず本番パスへ直接
        /// 触れたテストがある」ことを意味する。JSON 化に伴い、値の往復シリアライズによる比較・復元は
        /// 不要になった——ファイルの中身（またはファイルが元々存在しなかったという事実）をそのまま
        /// バイト列で退避し、差分があればそのまま書き戻す（削除されていた場合は削除された状態へ戻す）。
        /// </summary>
        /// <returns>ベースラインとバイト列が一致しなかった場合 true。</returns>
        private bool CheckAndRestoreAddressTellerSettingsFile()
        {
            byte[] bytesAfter;
            try
            {
                bytesAfter = File.Exists(_addressTellerSettingsPath)
                    ? File.ReadAllBytes(_addressTellerSettingsPath)
                    : null;
            }
            catch (Exception ex)
            {
                Debug.LogError("[AddressTeller] ProjectSettings/AddressTellerSettings.json の読み取りに失敗した: " +
                    $"{ex.Message}");
                return true;
            }

            if (BytesEqual(bytesAfter, _addressTellerSettingsBytesBefore))
                return false;

            Debug.LogWarning("[AddressTeller] EditMode テストの実行により ProjectSettings/AddressTellerSettings.json " +
                "が変更されたため、ベースラインに復元します。");

            try
            {
                if (_addressTellerSettingsBytesBefore == null)
                {
                    if (File.Exists(_addressTellerSettingsPath))
                        File.Delete(_addressTellerSettingsPath);
                }
                else
                {
                    File.WriteAllBytes(_addressTellerSettingsPath, _addressTellerSettingsBytesBefore);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[AddressTeller] ProjectSettings/AddressTellerSettings.json の復元処理自体が失敗した: " +
                    $"{ex.Message}");
            }

            return true;
        }

        /// <summary>2つのバイト列の内容が完全に一致するかどうかを返す。両方 null なら true。</summary>
        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null)
                return a == b;
            if (a.Length != b.Length)
                return false;
            for (var i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}
