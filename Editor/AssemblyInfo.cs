using System.Runtime.CompilerServices;

// 結果ウィンドウの行データ変換ロジック（AddressTellerResultWindowRows 等）は
// 表示用 internal 型のまま EditMode テストから直接検証できるようにする。
[assembly: InternalsVisibleTo("AddressTeller.Editor.Tests")]
