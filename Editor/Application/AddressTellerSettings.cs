using System.IO;
using UnityEditor;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// EditorPrefs に永続化される AddressTeller の設定値。
    /// Project Settings UI（予定）からこれらの値を切り替えられるようにする。
    /// </summary>
    public static class AddressTellerSettings
    {
        private const string CleanupStaleEntriesKey = "AddressTeller.CleanupStaleEntries";
        private const string PostprocessEnabledKey = "AddressTeller.PostprocessEnabled";
        private const string SnapshotFolderKey = "AddressTeller.SnapshotFolder";
        private const string DefaultSnapshotFolder = "AddressTellerSnapshots";

        /// <summary>
        /// true の場合、ApplyAll 実行時にどのルールにもマッチしなくなったアセットの
        /// エントリを、AddressTeller が管理するグループ（いずれかのルールの GroupName）から削除する。
        /// 削除されるのは Addressables のエントリ（アドレス）のみで、過去にルールが付与した
        /// ラベルはプロジェクト全体で共有されるため剥がされない。
        /// </summary>
        public static bool CleanupStaleEntries
        {
            get => EditorPrefs.GetBool(CleanupStaleEntriesKey, true);
            set => EditorPrefs.SetBool(CleanupStaleEntriesKey, value);
        }

        /// <summary>
        /// false の場合、AssetPostprocessor によるインポート時の自動 ApplyAll を行わない。
        /// Tools/AddressTeller/Apply All からの手動実行には影響しない。
        /// </summary>
        public static bool PostprocessEnabled
        {
            get => EditorPrefs.GetBool(PostprocessEnabledKey, true);
            set => EditorPrefs.SetBool(PostprocessEnabledKey, value);
        }

        /// <summary>
        /// スナップショットの保存先フォルダ。プロジェクトルート（Assets の親ディレクトリ）からの相対パス。
        /// 既定値は "AddressTellerSnapshots"（Assets 外、Unity にインポートされない）。
        /// "Assets/..." を指定すると Project ウィンドウにも表示される。
        /// </summary>
        public static string SnapshotFolder
        {
            get => EditorPrefs.GetString(SnapshotFolderKey, DefaultSnapshotFolder);
            set => EditorPrefs.SetString(SnapshotFolderKey, value);
        }

        /// <summary>SnapshotFolder をプロジェクトルートからの絶対パスに解決する。</summary>
        public static string GetSnapshotFolderAbsolutePath()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot, SnapshotFolder));
        }
    }
}
