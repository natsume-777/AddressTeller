using System;

namespace AddressTeller
{
    /// <summary>
    /// Static helpers for building address-generating expressions to pass to Address()/Label() concisely.
    /// </summary>
    public static class Naming
    {
        /// <summary>Expression that returns the file name including extension.</summary>
        public static Func<AssetContext, string> FileName()
        {
            return ctx => ctx.FileName;
        }

        /// <summary>Expression that returns the file name without extension.</summary>
        public static Func<AssetContext, string> FileNameWithoutExtension()
        {
            return ctx => ctx.FileNameWithoutExtension;
        }

        /// <summary>
        /// Expression that returns the name of the folder directly containing the file.
        /// At the root (e.g. Assets/Foo.prefab, which has no parent folder), returns "Assets".
        /// </summary>
        public static Func<AssetContext, string> ParentFolderName()
        {
            return ctx =>
            {
                var segments = ctx.PathSegments;
                // PathSegments の末尾はファイル名そのものなので、親フォルダ名はその1つ前。
                // ルート直下（Assets/Foo.prefab のように要素数2）の場合は segments[0]="Assets" が返る
                // （<summary> の通り）。要素数2未満になるケースは実質存在しないが、フォールバックとして空文字を返す。
                return segments.Length >= 2 ? segments[segments.Length - 2] : string.Empty;
            };
        }

        /// <summary>Expression that returns the path relative to root, if under root (delegates to ctx.RelativePathFrom).</summary>
        public static Func<AssetContext, string> RelativePath(string root)
        {
            return ctx => ctx.RelativePathFrom(root);
        }
    }
}
