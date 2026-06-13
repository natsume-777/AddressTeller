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

        /// <summary>取得日時（UTC、ISO 8601 形式）。旧形式の JSON では空文字になる。</summary>
        public string CapturedAtIso = "";

        /// <summary>ユーザーが付与する任意のコメント。</summary>
        public string Comment = "";

        /// <summary>取得時の Unity バージョン（<see cref="Application.unityVersion"/>）。</summary>
        public string UnityVersion = "";

        /// <summary>取得時の AddressTeller パッケージバージョン。取得できない場合は空文字。</summary>
        public string PackageVersion = "";

        /// <summary>
        /// スナップショットのスキーマバージョン。<see cref="AddressTellerSnapshotService.Capture"/> で
        /// <see cref="AddressTellerSnapshotService.CurrentSchemaVersion"/> が設定される。
        /// このフィールドが存在しない旧形式の JSON を読み込んだ場合や、初期化直後は 0 になる。
        /// </summary>
        public int SchemaVersion = 0;

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
