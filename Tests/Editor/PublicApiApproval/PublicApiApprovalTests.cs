using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// パッケージ内の各 Editor アセンブリの公開APIサーフェス（public/protected型・メンバー）を
    /// 承認済みベースライン（PublicAPI.&lt;アセンブリ名&gt;.approved.txt）と突き合わせ、
    /// 意図しない変更を検知する回帰テスト。対象アセンブリ自体は
    /// <see cref="PublicApiApprovalBaseline.GetTargetAssemblyNames"/> が動的に列挙するため、
    /// 将来アセンブリ分割が起きても自動的に対象へ加わる。
    ///
    /// ベースラインの更新手順:
    /// 1. このテストが失敗した際のメッセージで、消えた行/増えた行を確認する。
    /// 2. 意図した公開API変更であれば、Unity Editor メニュー
    ///    「Tools/AddressTeller/Approve Public API Surface」を実行し、承認済みファイルを上書きする
    ///    （既存の.txtファイルを削除してからの再生成は、meta の GUID が振り直されて無意味な差分を生むため行わないこと）。
    /// 3. 上書き後の内容が意図した変更を反映していることを確認したうえで、そのままコミットする。
    /// 4. 再度テストを実行し、green になることを確認する。
    ///
    /// なお、初回（承認ファイルが存在しない）の場合に限り、テスト自身がファイルを新規生成して意図的に失敗する。
    /// </summary>
    public class PublicApiApprovalTests
    {
        private static IEnumerable<string> GetTargetAssemblyNames() => PublicApiApprovalBaseline.GetTargetAssemblyNames();

        [TestCaseSource(nameof(GetTargetAssemblyNames))]
        public void PublicApiSurface_MatchesApprovedBaseline(string assemblyName)
        {
            var assembly = PublicApiApprovalBaseline.ResolveLoadedAssembly(assemblyName);
            Assert.IsNotNull(assembly, $"アセンブリ {assemblyName} をロード済みアセンブリ一覧から解決できませんでした。");

            var actual = PublicApiApprovalBaseline.Normalize(PublicApiSurfaceFormatter.Format(assembly));
            var approvedPath = PublicApiApprovalBaseline.GetSurfaceApprovedFilePath(assemblyName);

            AssertMatchesBaseline(actual, approvedPath);
        }

        [Test]
        public void TargetAssemblyNames_MatchesApprovedBaseline()
        {
            // 承認対象アセンブリの集合そのものを承認対象にすることで、新規アセンブリ追加時に
            // 個別サーフェスのベースライン作成が漏れたまま検知網から抜け落ちることを防ぐ。
            var actual = PublicApiApprovalBaseline.Normalize(
                PublicApiApprovalBaseline.FormatAssemblyList(PublicApiApprovalBaseline.GetTargetAssemblyNames()));
            var approvedPath = PublicApiApprovalBaseline.GetAssemblyListApprovedFilePath();

            AssertMatchesBaseline(actual, approvedPath);
        }

        private static void AssertMatchesBaseline(string actual, string approvedPath)
        {
            if (!File.Exists(approvedPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(approvedPath));
                File.WriteAllText(approvedPath, actual);
                Assert.Fail(
                    $"承認済みベースラインファイルが存在しなかったため新規生成しました: {approvedPath}\n" +
                    "内容を確認し、現在のサーフェスとして問題なければそのままコミットしてテストを再実行してください。");
            }

            var approved = PublicApiApprovalBaseline.Normalize(File.ReadAllText(approvedPath));
            if (approved == actual) return;

            Assert.Fail(BuildMismatchMessage(approved, actual, approvedPath));
        }

        /// <summary>
        /// 承認済みベースラインと現在のサーフェスの差分を行単位（集合差分）で報告する。
        /// 行の並び替えは検知しない簡易的な比較だが、失敗時にどの行が消えた/増えたかを
        /// 一目で確認できるようにするための診断情報であり、正確なパッチ生成が目的ではない。
        /// </summary>
        private static string BuildMismatchMessage(string approved, string actual, string approvedPath)
        {
            var approvedLines = new HashSet<string>(approved.Split('\n'));
            var actualLines = new HashSet<string>(actual.Split('\n'));

            var removed = approvedLines.Except(actualLines).OrderBy(l => l, StringComparer.Ordinal).ToArray();
            var added = actualLines.Except(approvedLines).OrderBy(l => l, StringComparer.Ordinal).ToArray();

            var sb = new StringBuilder();
            sb.Append("公開APIサーフェスが承認済みベースラインと一致しません（").Append(approvedPath).Append("）。\n");
            sb.Append("意図した変更であれば「Tools/AddressTeller/Approve Public API Surface」メニューで承認してください")
                .Append("（.txtファイルを削除しての再生成は、GUIDが振り直されて無意味なdiffを生むため行わないでください）。\n");

            if (removed.Length > 0)
            {
                sb.Append("承認済みベースラインにあり、現在のサーフェスには無くなった行:\n");
                foreach (var line in removed) sb.Append("  - ").Append(line).Append('\n');
            }

            if (added.Length > 0)
            {
                sb.Append("現在のサーフェスにあり、承認済みベースラインには無い行:\n");
                foreach (var line in added) sb.Append("  + ").Append(line).Append('\n');
            }

            return sb.ToString();
        }
    }
}
