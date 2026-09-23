using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// テスト専用: 同一 guid のエントリを2つ以上のグループに存在させた状態を作るためのヘルパー。
    /// 公開 API（<see cref="AddressableAssetSettings.CreateOrMoveEntry"/>）は既存エントリを移動してしまうため、
    /// この状態を再現できない。<see cref="AddressableAssetGroup"/> の内部フィールド m_SerializeEntries を
    /// SerializedObject 経由で直接書き換え、<c>ApplyModifiedPropertiesWithoutUndo</c> が呼ぶ
    /// デシリアライズ（<c>ISerializationCallbackReceiver.OnAfterDeserialize</c>、内部で m_EntryMap を再構築する）
    /// を利用して、通常の API を経由せず group.entries にエントリを直接追加する。
    /// </summary>
    internal static class DuplicateAssetEntryTestInjector
    {
        /// <summary>
        /// <paramref name="group"/> に、<paramref name="guid"/>/<paramref name="address"/> を持つエントリを
        /// 通常の API を経由せず直接追加する。呼び出し前に別のグループへ同じ guid のエントリが
        /// （通常の <c>CreateOrMoveEntry</c> 経由で）既に存在していれば、Addressables 側の重複除去
        /// （グループ単位の内部処理）を経ずに2つのグループへ同じ guid が存在する状態が作れる。
        /// </summary>
        public static void InjectDuplicateEntry(AddressableAssetGroup group, string guid, string address)
        {
            var so = new SerializedObject(group);
            var entriesProp = so.FindProperty("m_SerializeEntries");
            var newIndex = entriesProp.arraySize;
            entriesProp.arraySize = newIndex + 1;

            var element = entriesProp.GetArrayElementAtIndex(newIndex);
            element.FindPropertyRelative("m_GUID").stringValue = guid;
            element.FindPropertyRelative("m_Address").stringValue = address;
            element.FindPropertyRelative("m_ReadOnly").boolValue = false;

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
