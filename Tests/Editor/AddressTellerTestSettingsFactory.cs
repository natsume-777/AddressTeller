using System.Reflection;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// テスト専用の <see cref="AddressableAssetSettings"/> をディスクに永続化せずに生成するためのヘルパー。
    /// <see cref="AddressableAssetSettings.Create"/> を isPersisted: true で呼ぶと、ディスク上に .asset を
    /// 作成・削除する際に Addressables の内部処理が発火し、本番の AddressableAssetSettings.asset /
    /// AddressableAssetGroupSortSettings.asset に副作用（sortOrder への余分な GUID 追加など）が残る。
    /// このヘルパーは isPersisted: false で生成したうえで、ConfigFolder のキャッシュのみをリフレクションで
    /// 設定することで、ConfigFolder を参照するテスト対象コードに対応しつつ本番設定への副作用を避ける。
    /// </summary>
    internal static class AddressTellerTestSettingsFactory
    {
        /// <summary>
        /// 非永続（メモリ上のみ）の <see cref="AddressableAssetSettings"/> を生成する。
        /// </summary>
        /// <param name="configFolder">ConfigFolder として扱うパス（テスト対象が settings.ConfigFolder を参照する場合に使われる）。</param>
        /// <param name="configName">settings の名前。複数テストクラスで同時に非永続 settings を持つ場合の名前衝突を避けるため呼び出し側で指定する。</param>
        public static AddressableAssetSettings CreateInMemory(string configFolder, string configName = "AddressTellerInMemoryTestSettings")
        {
            var settings = AddressableAssetSettings.Create(configFolder, configName, createDefaultGroups: false, isPersisted: false);

            // ConfigFolder は m_CachedConfigFolder が空の場合に AssetPath（永続化されていないと例外）から
            // 導出されるため、リフレクションでキャッシュに直接書き込み、ConfigFolder 参照のみを満たす。
            var field = typeof(AddressableAssetSettings).GetField("m_CachedConfigFolder", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(settings, configFolder);

            return settings;
        }
    }
}
