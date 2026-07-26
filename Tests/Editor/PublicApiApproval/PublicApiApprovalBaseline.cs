using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 公開APIサーフェス承認テストの対象アセンブリ列挙・承認ファイルパス解決を、
    /// テスト（<see cref="PublicApiApprovalTests"/>）と承認用メニュー（<see cref="PublicApiApprovalMenu"/>）の
    /// 双方から共通で使えるようにまとめたユーティリティ。プロダクトコードからは参照しない。
    /// </summary>
    internal static class PublicApiApprovalBaseline
    {
        private const string PackageName = "com.natsume777.addressteller";
        private const string TestAssemblyNameSuffix = ".Tests";

        /// <summary>
        /// パッケージ内の非テスト Editor アセンブリ名を Ordinal 昇順で列挙する。
        /// アセンブリ分割（asmdef の追加）が起きても、この列挙が自動的に対象を拾う。
        /// テストアセンブリの判定は名前規約（"*.Tests" サフィックス）によるもので、
        /// このリポジトリの asmdef 命名規約に依存している。
        /// </summary>
        public static IReadOnlyList<string> GetTargetAssemblyNames()
        {
            return CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                .Where(IsPackageProductAssembly)
                .Select(a => a.name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsPackageProductAssembly(UnityEditor.Compilation.Assembly assembly)
        {
            if (assembly.name.EndsWith(TestAssemblyNameSuffix, StringComparison.Ordinal)) return false;

            var asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly.name);
            if (string.IsNullOrEmpty(asmdefPath)) return false;

            var packageInfo = PackageInfo.FindForAssetPath(asmdefPath);
            return packageInfo != null && packageInfo.name == PackageName;
        }

        /// <summary>名前で指定した、現在ドメインにロード済みのアセンブリを取得する。</summary>
        public static System.Reflection.Assembly ResolveLoadedAssembly(string assemblyName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == assemblyName);
        }

        /// <summary>個別アセンブリの公開APIサーフェス承認ファイルのパス。</summary>
        public static string GetSurfaceApprovedFilePath(string assemblyName)
        {
            return Path.Combine(GetApprovalDirectory(), $"PublicAPI.{assemblyName}.approved.txt");
        }

        /// <summary>
        /// 承認対象アセンブリ名の集合そのものを承認するためのファイルパス。
        /// アセンブリの追加/削除によって承認対象から漏れることを防ぐためのガード。
        /// </summary>
        public static string GetAssemblyListApprovedFilePath()
        {
            return Path.Combine(GetApprovalDirectory(), "PublicAPI.AssemblyList.approved.txt");
        }

        public static string FormatAssemblyList(IReadOnlyList<string> assemblyNames)
        {
            return string.Join("\n", assemblyNames) + "\n";
        }

        /// <summary>改行コードを "\n" に正規化する（git の autocrlf 設定差異による誤検知を防ぐため）。</summary>
        public static string Normalize(string text) => text.Replace("\r\n", "\n");

        /// <summary>
        /// このテストアセンブリが属するパッケージの実体パス配下に承認ファイルを配置する。
        /// パッケージの導入形態（embedded/git 参照等）に関わらず resolvedPath はディスク上の実パスを指す。
        /// </summary>
        private static string GetApprovalDirectory()
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(PublicApiApprovalBaseline).Assembly);
            if (packageInfo == null)
                throw new InvalidOperationException("AddressTeller.Editor.Tests が属するパッケージ情報を解決できませんでした。");

            return Path.Combine(packageInfo.resolvedPath, "Tests", "Editor", "PublicApiApproval");
        }
    }
}
