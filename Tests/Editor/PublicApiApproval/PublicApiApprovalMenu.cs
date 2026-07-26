using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 公開APIサーフェス承認テスト（<see cref="PublicApiApprovalTests"/>）の承認済みベースラインを、
    /// 現在のサーフェスで明示的に上書きするためのメニュー。
    /// 「.txtファイルを削除してテストを再実行する」運用は、削除の都度 .meta の GUID が振り直され
    /// 無意味な差分が乗るうえ、中身を確認せず機械的に通す方向に流れやすいため、
    /// このメニューでの明示的な上書きに一本化する。プロダクトコードからは参照しない。
    /// </summary>
    internal static class PublicApiApprovalMenu
    {
        [MenuItem("Tools/AddressTeller/Approve Public API Surface")]
        public static void ApproveCurrentSurface()
        {
            var summary = new StringBuilder();
            var assemblyNames = PublicApiApprovalBaseline.GetTargetAssemblyNames();

            foreach (var assemblyName in assemblyNames)
            {
                var assembly = PublicApiApprovalBaseline.ResolveLoadedAssembly(assemblyName);
                if (assembly == null)
                {
                    Debug.LogWarning($"[AddressTeller] アセンブリ {assemblyName} をロード済みアセンブリ一覧から解決できなかったため承認をスキップしました。");
                    continue;
                }

                var actual = PublicApiApprovalBaseline.Normalize(PublicApiSurfaceFormatter.Format(assembly));
                var approvedPath = PublicApiApprovalBaseline.GetSurfaceApprovedFilePath(assemblyName);
                WriteApprovedFile(approvedPath, actual);
                summary.Append("  - ").Append(approvedPath).Append('\n');
            }

            var assemblyListActual = PublicApiApprovalBaseline.Normalize(
                PublicApiApprovalBaseline.FormatAssemblyList(assemblyNames));
            var assemblyListPath = PublicApiApprovalBaseline.GetAssemblyListApprovedFilePath();
            WriteApprovedFile(assemblyListPath, assemblyListActual);
            summary.Append("  - ").Append(assemblyListPath).Append('\n');

            Debug.Log("[AddressTeller] 公開APIサーフェスの承認済みベースラインを更新しました。差分をレビューしてからコミットしてください:\n" + summary);
        }

        /// <summary>
        /// 既存ファイルへの上書きを基本とする（削除→再生成にすると.metaのGUIDが振り直されるため）。
        /// </summary>
        private static void WriteApprovedFile(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, content);
        }
    }
}
