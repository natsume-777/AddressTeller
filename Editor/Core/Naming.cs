using System;

namespace AddressTeller
{
    /// <summary>
    /// Address()/Label() に渡すアドレス生成式を簡潔に組み立てるための静的ヘルパー群。
    /// </summary>
    public static class Naming
    {
        /// <summary>ファイル名（拡張子あり）を返す式。</summary>
        public static Func<AssetContext, string> FileName()
        {
            return ctx => ctx.FileName;
        }

        /// <summary>拡張子なしのファイル名を返す式。</summary>
        public static Func<AssetContext, string> FileNameWithoutExtension()
        {
            return ctx => ctx.FileNameWithoutExtension;
        }

        /// <summary>
        /// ファイルが直接置かれている親フォルダ名（1つ）を返す式。
        /// ルート直下（Assets/Foo.prefab のように親フォルダがない場合）は "Assets" を返す。
        /// </summary>
        public static Func<AssetContext, string> ParentFolderName()
        {
            return ctx =>
            {
                var segments = ctx.PathSegments;
                // PathSegments の末尾はファイル名そのものなので、親フォルダ名はその1つ前。
                // 末尾2要素未満（= Assets/Foo.prefab のようにルート直下）の場合は空文字。
                return segments.Length >= 2 ? segments[segments.Length - 2] : string.Empty;
            };
        }

        /// <summary>root 配下なら root からの相対パスを返す式（ctx.RelativePathFrom への委譲）。</summary>
        public static Func<AssetContext, string> RelativePath(string root)
        {
            return ctx => ctx.RelativePathFrom(root);
        }
    }
}
