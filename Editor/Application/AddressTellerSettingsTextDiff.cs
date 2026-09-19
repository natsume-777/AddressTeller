using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AddressTeller.Editor
{
    /// <summary>1つのトップレベルフィールドについて、ディスク側テキストとメモリ側テキストで値が食い違っていたことを表す。</summary>
    internal readonly struct SettingsFieldDiff
    {
        internal SettingsFieldDiff(string fieldName, string diskValue, string memoryValue)
        {
            FieldName = fieldName;
            DiskValue = diskValue;
            MemoryValue = memoryValue;
        }

        /// <summary>UI ラベルではなく、実際にファイルへ書かれているシリアライズ名（例: <c>_postprocessOrder</c>）。</summary>
        internal string FieldName { get; }
        internal string DiskValue { get; }
        internal string MemoryValue { get; }
    }

    /// <summary>
    /// <see cref="AddressTellerSettingsAsset"/> が書き出す YAML テキスト2本（ディスク側・メモリ側）を、
    /// ファイル I/O やインスタンス生成を一切行わずに比較する純粋ロジック。
    /// AddressTellerSettingsLoadDiagnostics（オーケストレーション層）からのみ呼ばれる想定だが、
    /// テストからは本クラスへ直接テキストを渡すことで、ProjectSettings 配下の実ファイルに触れずに
    /// 比較ロジック単体を検証できる。
    /// </summary>
    internal static class AddressTellerSettingsTextDiff
    {
        /// <summary>
        /// 型の同定に使うヘッダ鍵。ディスク側・メモリ側でこの鍵が一致しているかどうかを、
        /// 警告メッセージの文面（「読めなかった」旨の説明か、「単に食い違っている」旨の説明か）の
        /// 選択に使う。警告を出すかどうかの判定には使わない
        /// （<see cref="Compare"/> の戻り値のうち <c>Diffs</c> が空でないことだけが警告の要否を決める）。
        /// <c>m_EditorClassIdentifier</c> ではなく <c>m_Script</c> のみを見る。Unity は <c>MonoScript</c>
        /// 参照（GUID）を実際に型解決へ使うのは <c>m_Script</c> 側であり、<c>m_EditorClassIdentifier</c> は
        /// 型の完全名・アセンブリ名を埋め込むだけの補助情報にすぎない。両方を要求すると、
        /// <c>m_Script</c> は一致しているのに <c>m_EditorClassIdentifier</c> だけ食い違う（型は解決できて
        /// いるはずの）ケースまで「読めなかった」側の文面に誤分類しうる。
        /// </summary>
        private const string TypeIdentityFieldName = "m_Script";

        /// <summary>
        /// Unity が MonoBehaviour ヘッダとして必ず出力する鍵。設定値そのものではないため値比較の対象から
        /// 除外する（このうち m_Script は type identity の判定に別枠で使う。m_EditorClassIdentifier は
        /// 値比較からは除外されるが、type identity の判定には使わない——上記 <see cref="TypeIdentityFieldName"/>
        /// のコメントを参照）。
        /// </summary>
        private static readonly HashSet<string> ExcludedFieldNames = new(StringComparer.Ordinal)
        {
            "m_ObjectHideFlags",
            "m_CorrespondingSourceObject",
            "m_PrefabInstance",
            "m_PrefabAsset",
            "m_GameObject",
            "m_Enabled",
            "m_EditorHideFlags",
            "m_Script",
            "m_Name",
            "m_EditorClassIdentifier",
        };

        // トップレベルフィールド行（2スペースインデント + 識別子 + ":"）を検出する。
        // 3文字目に識別子の先頭文字（英字またはアンダースコア）を要求しているため、3スペース以上の
        // インデント行（ネストした値やリスト継続行）は自動的にここへマッチせず、直前のトップレベル鍵の
        // 値へ連結される側（continuation）として扱われる。
        private static readonly Regex TopLevelFieldPattern = new(@"^  ([A-Za-z_][A-Za-z0-9_]*):(.*)$", RegexOptions.Compiled);

        /// <summary>
        /// ディスク側テキストとメモリ側テキストを比較する。
        /// 差分は、メモリ側テキストに現れたトップレベル鍵のうちヘッダ鍵を除いたものを対象に、
        /// ディスク側にも同じ鍵が存在する場合だけ値を比較する（両辺に無い鍵は無視。将来フィールドが
        /// 追加・削除されても誤検知しないようにするため）。
        /// </summary>
        /// <param name="diskText">ディスク上のファイルから読んだテキスト。</param>
        /// <param name="memoryText">現在メモリ上にあるインスタンスを再シリアライズしたテキスト。</param>
        /// <returns>
        /// フィールド名の昇順（Ordinal）に並んだ差分の一覧と、type identity（<c>m_Script</c>）が
        /// ディスク側・メモリ側の両方に存在し、かつ両方とも一致しているかどうか。
        /// </returns>
        internal static (IReadOnlyList<SettingsFieldDiff> Diffs, bool TypeIdentifierMatches) Compare(string diskText, string memoryText)
        {
            var diskFields = ExtractTopLevelFields(diskText);
            var memoryFields = ExtractTopLevelFields(memoryText);

            var typeIdentifierMatches =
                diskFields.TryGetValue(TypeIdentityFieldName, out var diskScript) &&
                memoryFields.TryGetValue(TypeIdentityFieldName, out var memoryScript) &&
                string.Equals(diskScript, memoryScript, StringComparison.Ordinal);

            var diffs = memoryFields.Keys
                .Where(key => !ExcludedFieldNames.Contains(key))
                .Where(diskFields.ContainsKey)
                .Where(key => !string.Equals(diskFields[key], memoryFields[key], StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.Ordinal)
                .Select(key => new SettingsFieldDiff(key, diskFields[key], memoryFields[key]))
                .ToList();

            return (diffs, typeIdentifierMatches);
        }

        /// <summary>
        /// YAML テキストからトップレベルフィールドを抽出する。改行コードは CRLF→LF に正規化し、各行の
        /// 行末空白は取り除く（インデントの判定に使う先頭側の空白はそのまま扱う）。
        /// トップレベル鍵にマッチしない行（<c>_disabledRuleClassNames:</c> の後に続く <c>- Foo</c> のような
        /// リスト継続行を含む）は、直前に見つかったトップレベル鍵の値へそのまま連結する。
        /// 最初のトップレベル鍵が見つかるまでの行（YAML ヘッダ・<c>MonoBehaviour:</c> 行等）は無視する。
        /// </summary>
        private static Dictionary<string, string> ExtractTopLevelFields(string text)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return fields;

            var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = normalized.Split('\n');

            string currentKey = null;
            var currentValue = new StringBuilder();

            void Flush()
            {
                if (currentKey != null)
                    fields[currentKey] = currentValue.ToString();
            }

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                var match = TopLevelFieldPattern.Match(line);
                if (match.Success)
                {
                    Flush();
                    currentKey = match.Groups[1].Value;
                    currentValue.Clear();
                    currentValue.Append(match.Groups[2].Value.Trim());
                    continue;
                }

                if (currentKey == null) continue; // まだトップレベル鍵が1つも見つかっていない（ヘッダ行等）。

                var continuation = line.Trim();
                if (continuation.Length == 0) continue;

                if (currentValue.Length > 0) currentValue.Append('\n');
                currentValue.Append(continuation);
            }

            Flush();

            return fields;
        }
    }
}
