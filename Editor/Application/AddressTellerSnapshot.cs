using System;
using System.Collections.Generic;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// Addressables のアドレス／ラベル／グループ割り当ての状態を表すスナップショット。
    /// JsonUtility でシリアライズするため public フィールドで構成する。
    /// </summary>
    [Serializable]
    public sealed class AddressTellerSnapshot
    {
        public List<SnapshotEntry> Entries = new();

        public string ToJson() => JsonUtility.ToJson(this, true);

        public static AddressTellerSnapshot FromJson(string json) => JsonUtility.FromJson<AddressTellerSnapshot>(json);
    }

    /// <summary>スナップショット中の1アセット分のエントリ。</summary>
    [Serializable]
    public sealed class SnapshotEntry
    {
        public string Guid;
        public string Address;
        public string GroupName;
        public List<string> Labels = new();
    }
}
