using System;
using System.IO;

namespace Natsume777.AddressTeller
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

        public AssetContext(string guid, string path, Type type)
        {
            if (string.IsNullOrEmpty(guid)) throw new ArgumentException("guid must not be empty.", nameof(guid));
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path must not be empty.", nameof(path));

            Guid = guid;
            Path = path.Replace('\\', '/');
            Type = type ?? throw new ArgumentNullException(nameof(type));
        }
    }
}
