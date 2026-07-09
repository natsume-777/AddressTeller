using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AddressTeller
{
    /// <summary>
    /// ルール評価に渡すアセット1件分の情報。
    /// </summary>
    public sealed class AssetContext
    {
        /// <summary>アセットの GUID。</summary>
        public string Guid { get; }

        /// <summary>Assets/ 起点の前進スラッシュ区切りパス（例: "Assets/Game/Player.prefab"）。</summary>
        public string Path { get; }

        /// <summary>アセットの型。</summary>
        public Type Type { get; }

        /// <summary>拡張子なしのファイル名（例: "Player"）。</summary>
        public string FileNameWithoutExtension => System.IO.Path.GetFileNameWithoutExtension(Path);

        /// <summary>ファイル名（拡張子あり）（例: "Player.prefab"）。</summary>
        public string FileName => System.IO.Path.GetFileName(Path);

        /// <summary>Assets/ 起点のディレクトリパス（例: "Assets/Game"）。</summary>
        public string Directory => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? string.Empty;

        /// <summary>小文字化した拡張子（例: ".png"）。拡張子なしの場合は空文字。</summary>
        public string Extension => System.IO.Path.GetExtension(Path).ToLower(CultureInfo.InvariantCulture);

        /// <summary>Path を "/" で分割したセグメント配列。空セグメントは除去し、大文字小文字・表記は変換しない。</summary>
        public string[] PathSegments { get; }

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

        /// <summary>指定フォルダ配下（再帰的に含む）かどうかを判定する。</summary>
        public bool IsInFolder(string folder)
        {
            var normalized = NormalizeFolder(folder);
            return Path.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>root 配下なら root からの相対パス（先頭 "/" なし）を返す。配下でない場合は Path をそのまま返す。</summary>
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
