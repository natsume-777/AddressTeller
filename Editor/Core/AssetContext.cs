using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AddressTeller
{
    /// <summary>
    /// Information about a single asset passed to rule evaluation.
    /// </summary>
    public sealed class AssetContext
    {
        /// <summary>The asset's GUID.</summary>
        public string Guid { get; }

        /// <summary>Forward-slash path rooted at Assets/ (e.g. "Assets/Game/Player.prefab").</summary>
        public string Path { get; }

        /// <summary>The asset's type.</summary>
        public Type Type { get; }

        /// <summary>File name without extension (e.g. "Player").</summary>
        public string FileNameWithoutExtension => System.IO.Path.GetFileNameWithoutExtension(Path);

        /// <summary>File name including extension (e.g. "Player.prefab").</summary>
        public string FileName => System.IO.Path.GetFileName(Path);

        /// <summary>Directory path rooted at Assets/ (e.g. "Assets/Game").</summary>
        public string Directory => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? string.Empty;

        /// <summary>Lowercased extension (e.g. ".png"). Empty string if there is no extension.</summary>
        public string Extension => System.IO.Path.GetExtension(Path).ToLower(CultureInfo.InvariantCulture);

        /// <summary>Path split on "/". Empty segments are removed; casing and content are otherwise unchanged.</summary>
        public string[] PathSegments { get; }

        /// <summary>Creates an AssetContext for a single asset.</summary>
        /// <param name="guid">The asset's GUID.</param>
        /// <param name="path">Asset path rooted at Assets/. Backslashes are normalized to forward slashes.</param>
        /// <param name="type">The asset's type.</param>
        /// <exception cref="ArgumentException"><paramref name="guid"/> or <paramref name="path"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
        public AssetContext(string guid, string path, Type type)
        {
            if (string.IsNullOrEmpty(guid)) throw new ArgumentException("guid must not be empty.", nameof(guid));
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path must not be empty.", nameof(path));

            Guid = guid;
            Path = path.Replace('\\', '/');
            Type = type ?? throw new ArgumentNullException(nameof(type));

            // AssetContext はイミュータブルなので、Naming.ParentFolderName() 等から資産ごと・ルールごとに
            // 繰り返しアクセスされる PathSegments はコンストラクタで1回だけ計算してキャッシュする
            // （プロジェクト全体ループ内で毎回 Split/Where/ToArray を割り当てないようにするため）。
            PathSegments = Path.Split('/').Where(s => s.Length > 0).ToArray();
        }

        /// <summary>Returns whether this asset is under the given folder (recursively).</summary>
        public bool IsInFolder(string folder)
        {
            var normalized = NormalizeFolder(folder);
            return Path.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// If this asset is under <paramref name="root"/>, returns the path relative to root (no
        /// leading "/"). Otherwise returns Path unchanged.
        /// </summary>
        public string RelativePathFrom(string root)
        {
            var normalized = NormalizeFolder(root);
            var prefix = normalized + "/";
            if (Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Substring(prefix.Length);
            }

            return Path;
        }

        /// <summary>フォルダパスを "/" 区切りに正規化し、末尾の "/" を除去する。</summary>
        private static string NormalizeFolder(string folder)
        {
            var normalized = (folder ?? string.Empty).Replace('\\', '/');
            return normalized.TrimEnd('/');
        }
    }
}
