using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// AddressTellerSettings の実体。ProjectSettings/AddressTellerSettings.asset に
    /// シリアライズされ、プロジェクトを共有する開発者間でバージョン管理される。
    /// </summary>
    /// <remarks>
    /// 不変条件: <c>instance</c> をフィールドにキャッシュしてはならない。
    /// <see cref="AddressTellerSettings.ReloadFromDisk"/> は「既存インスタンスを破棄してから
    /// <c>instance</c> に再アクセスさせ、Unity 自身にディスクから読み直させる」方式（破棄→再取得）で
    /// 実装されており、この呼び出しのたびに <c>instance</c> が指すオブジェクトの参照そのものが
    /// 差し替わる。呼び出しのたびに <c>instance</c> を引き直さず、取得した参照をフィールドに
    /// 保持し続けるコード（例えば将来 <c>SerializedObject</c> ベースの Project Settings UI を
    /// 実装する場合、その生成元となるオブジェクト参照など）は、破棄済みの参照を握り続けることになり
    /// 破綻する。<see cref="AddressTellerSettings"/> の各プロパティは現在すべて呼び出しのたびに
    /// <c>instance</c> を引き直しており、この不変条件を満たしている。
    /// </remarks>
    [FilePath("ProjectSettings/AddressTellerSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class AddressTellerSettingsAsset : ScriptableSingleton<AddressTellerSettingsAsset>
    {
        [SerializeField] internal bool _cleanupStaleEntries = true;
        [SerializeField] internal bool _postprocessEnabled = true;
        [SerializeField] internal string _snapshotFolder = AddressTellerSettings.DefaultSnapshotFolder;
        [SerializeField] internal bool _autoSnapshotBeforeApplyAll = true;
        [SerializeField] internal int _autoSnapshotRetention = 10;
        [SerializeField] internal bool _autoCreateMissingGroups = false;
        [SerializeField] internal int _postprocessOrder = AddressTellerSettings.DefaultPostprocessOrder;
        [SerializeField] internal List<string> _disabledRuleClassNames = new();

        /// <summary>変更内容を ProjectSettings/AddressTellerSettings.asset へ書き出す。</summary>
        internal void SaveChanges() => Save(true);

        /// <summary>
        /// 永続化ファイルの絶対パスを返す。GetFilePath() は <see cref="FilePathAttribute"/> の
        /// <c>filepath</c> をそのまま返す仕様で、本クラスの属性指定（<see cref="FilePathAttribute.Location.ProjectFolder"/>）
        /// では相対パスになる。一方 <see cref="FilePathAttribute.Location.PreferencesFolder"/> を
        /// 指定した場合は絶対パスがそのまま返る仕様のため、<see cref="Path.IsPathRooted"/> で分岐して
        /// おく（本クラスの属性指定を変えた場合にも壊れないようにするための保険であり、Unity 側の
        /// 将来の仕様変更を見越したものではない）。
        /// </summary>
        internal static string GetAbsoluteFilePath()
        {
            var filePath = GetFilePath();
            if (Path.IsPathRooted(filePath)) return filePath;

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot, filePath));
        }

        /// <summary>
        /// 現在メモリ上にある設定（<c>instance</c> が指すオブジェクト）を、プロジェクト内の一意な
        /// 一時ファイルへそのまま再シリアライズし、書き出されたテキストを返す。既存のオブジェクトを
        /// <see cref="InternalEditorUtility.SaveToSerializedFileAndForget"/> へ渡すだけであり、
        /// 逆シリアライズや新規インスタンス生成は一切発生しない（<c>ScriptableSingleton</c> 内部の
        /// 唯一のインスタンス参照には一切触れない）。
        /// 一時パスには <see cref="FileUtil.GetUniqueTempPathInProject"/> を使う。
        /// <see cref="Path.GetTempFileName"/> は呼び出し時点で0バイトの空ファイルを先に作ってしまい、
        /// <see cref="InternalEditorUtility.SaveToSerializedFileAndForget"/> が（テキストモードでの）
        /// 既存内容の読み込みに失敗して "File is either empty or corrupted" という Error を出すため
        /// 使わない（実測確認済み）。
        /// 読み終えた一時ファイルは必ず削除を試みる。パス取得・書き出し・読み込みのいずれかで例外が
        /// 発生した場合は自前では例外を投げず null を返す（呼び出し元の
        /// <see cref="AddressTellerSettings.SaveToDisk"/> は bool を返す契約の公開 API であり、
        /// 権限不足やディスクフル等の I/O エラーをここから例外として漏らしてはならないため）。
        /// </summary>
        internal static string SaveCurrentInstanceToTempFileAndReadText()
        {
            string tempPath;
            try
            {
                tempPath = FileUtil.GetUniqueTempPathInProject();
            }
            catch (Exception)
            {
                return null;
            }

            try
            {
                InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[] { instance }, tempPath, true);
                return File.Exists(tempPath) ? File.ReadAllText(tempPath) : null;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    // 一時ファイルの削除失敗は戻り値の成否には影響させない（best-effort）。
                    // Temp フォルダにファイルが残ること自体は致命的ではないが、原因調査の手がかりとして
                    // 警告だけ残す。
                    Debug.LogWarning("[AddressTeller] SaveCurrentInstanceToTempFileAndReadText: failed to " +
                        $"delete the temporary file '{tempPath}' ({ex.GetType().Name}: {ex.Message}).");
                }
            }
        }
    }
}
