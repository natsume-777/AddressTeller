using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// ProjectSettings/AddressTellerSettings.asset の内容が、実際にメモリへロードされている設定と食い違って
    /// いないかを検出する診断。検出方式は「メモリを書き出してディスクのテキストと比較する」であり、逆
    /// （ディスクを読み込んでメモリと比べる）ではない。<see cref="AddressTellerSettingsAsset.SaveCurrentInstanceToTempFileAndReadText"/>
    /// は既存インスタンスをそのまま再シリアライズするだけで、逆シリアライズや新規インスタンス生成を一切
    /// 発生させない（2個目のインスタンスを作らずに済む）。
    /// </summary>
    /// <remarks>
    /// この診断の抑止フラグを <see cref="AddressTellerSettingsAsset"/> 側（ProjectSettings/AddressTellerSettings.asset
    /// のシリアライズフィールド）に持たせてはならない。この警告はまさに「そのファイルが読めていない」状態
    /// （型識別子が解決できず全フィールドが既定値化している状態）で鳴るものであり、抑止フラグ自体もその
    /// ファイルの一部である以上、読めない状態では抑止フラグも一緒に既定値へ戻ってしまい、意味をなさない
    /// （自己矛盾）。将来この診断に対する抑止設定を追加する必要が生じた場合は、CLI引数
    /// （本ファイルが呼び出す <c>-addressTellerFailOnSettingsMismatch</c> のように、設定アセットの外側にある
    /// 仕組み）や、EditorPrefs のようなプロジェクト設定ファイルの外側にあるユーザーローカルな保存先を検討
    /// すること。
    /// </remarks>
    internal static class AddressTellerSettingsLoadDiagnostics
    {
        /// <summary>
        /// [InitializeOnLoadMethod] はドメインリロードのたびに呼ばれるため、SessionState
        /// （ドメインリロードをまたいで同一 Editor セッション内で保持される）でセッションにつき1回だけに
        /// 制限する。実測で SessionState がドメインリロードをまたいで保持されることを確認済み。
        /// </summary>
        private const string DiagnosedSessionStateKey = "AddressTeller.SettingsLoadDiagnosed";

        /// <summary>
        /// ProjectSettings/AddressTellerSettings.asset のディスク上のテキストと、現在メモリにある設定を
        /// 再シリアライズしたテキストを比較し、差分があれば警告メッセージ（<c>[AddressTeller]</c> プレフィックス
        /// なし）を返す。
        /// </summary>
        /// <remarks>
        /// 以下のいずれの場合も、例外を投げず、不一致を報告する目的のログは一切出さず、<c>null</c>
        /// （「報告すべきことは無い」の意）を返す: ファイルがまだ存在しない（初回保存前の新規プロジェクト等）、
        /// ファイルが読み取れない（権限不足等）、メモリ側の再シリアライズに失敗した
        /// （<see cref="AddressTellerSettingsAsset.SaveCurrentInstanceToTempFileAndReadText"/> が <c>null</c> を
        /// 返した場合）、比較の結果フィールドの差分が1件も無かった場合。これにより、対話的でない実行
        /// （CLI/バッチモード）からこのメソッドを無条件に呼んでも、偽の不一致警告を出すことがない契約になっている。
        /// ただし例外が1つある: 上記いずれの場合であっても、メモリ側の再シリアライズが使う一時ファイルの
        /// 削除に失敗した場合に限り、<see cref="AddressTellerSettingsAsset.SaveCurrentInstanceToTempFileAndReadText"/>
        /// 自身が best-effort な <c>Debug.LogWarning</c> を1回出すことがある（戻り値やこのメソッドの契約には
        /// 影響しない。単に一時ファイルが削除されずに残るだけ）。これは「設定の不一致を報告する」ためのログ
        /// ではなく、上記の無言契約はこの警告には及ばない。
        /// </remarks>
        internal static string DiagnoseMismatch()
        {
            string diskText;
            try
            {
                diskText = File.ReadAllText(AddressTellerSettingsAsset.GetAbsoluteFilePath());
            }
            catch (Exception)
            {
                return null;
            }

            var memoryText = AddressTellerSettingsAsset.SaveCurrentInstanceToTempFileAndReadText();
            if (memoryText == null) return null;

            var (diffs, typeIdentifierMatches) = AddressTellerSettingsTextDiff.Compare(diskText, memoryText);
            if (diffs.Count == 0) return null;

            return BuildMessage(diffs, typeIdentifierMatches);
        }

        /// <summary>
        /// variant A（<c>m_Script</c> が不一致。このファイル分割に伴う BREAKING に該当する無言リセット）と
        /// variant B（<c>m_Script</c> は一致するが値だけ食い違う。外部編集・VCS 更新・書き込み失敗などが
        /// 疑われる）で文面を切り替える。この判定は文面選択だけに使い、呼び出し元が警告するかどうかの
        /// 決定には一切影響しない（<paramref name="diffs"/> が空でない時点で呼び出し元は既に警告すると
        /// 決めている）。
        /// </summary>
        private static string BuildMessage(IReadOnlyList<SettingsFieldDiff> diffs, bool typeIdentifierMatches)
        {
            // 複数要素のブロックリスト（例: _disabledRuleClassNames）は DiskValue/MemoryValue が改行区切りの
            // 複数行になりうる。1行の警告メッセージへそのまま混入させると読みにくくなるため、表示用にだけ
            // 改行を ", " へ畳み込む（SettingsFieldDiff 自体の値は変更しない）。
            var diffText = string.Join(", ", diffs.Select(d =>
                $"{d.FieldName} (file: {FlattenForMessage(d.DiskValue)}, in use: {FlattenForMessage(d.MemoryValue)})"));

            if (!typeIdentifierMatches)
            {
                return "ProjectSettings/AddressTellerSettings.asset was written in a form this version of AddressTeller cannot read, " +
                    $"so every setting fell back to its default value. Values that differ — {diffText}. The file still holds those " +
                    "previous values right now, but the next settings change overwrites it: copy them out first, then re-enter them " +
                    "under Project Settings > AddressTeller. See the \"Settings Asset\" section of the compatibility documentation.";
            }

            return "ProjectSettings/AddressTellerSettings.asset does not match the settings currently in use, most likely because the " +
                $"file changed after the Editor loaded it. Values that differ — {diffText}. Call AddressTellerSettings.ReloadFromDisk() " +
                "to take the file's values, or AddressTellerSettings.SaveToDisk() to overwrite the file with the values in use.";
        }

        /// <summary>1行の警告メッセージに埋め込むための表示用整形。改行を ", " へ畳み込むだけで、値そのものは変えない。</summary>
        private static string FlattenForMessage(string value) => value.Replace("\n", ", ");

        /// <summary>
        /// Editor 起動時（および各ドメインリロード後）にセッションにつき1回だけ診断を実行し、差分が見つかれば
        /// Warning を1本ログ出力する。
        /// AssetPostprocessor 側には意図的にフックを追加していない——毎 import ごとにファイル読み込みと
        /// 一時ファイル書き出しを行うことになり、import のたびに変化しない計算を避けるという方針に反するため。
        /// また <see cref="EditorApplication.delayCall"/> による遅延も採用していない——
        /// <c>-batchmode -executeMethod</c> では CLI 側のメソッドが自前で <see cref="EditorApplication.Exit"/> を
        /// 呼ぶため、delayCall が発火する前にプロセスが終了しうる。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void RunOnceOnStartup()
        {
            // セッションにつき1回であることを保証するため、診断の成否に関わらず先にフラグを立てる。
            if (SessionState.GetBool(DiagnosedSessionStateKey, false)) return;
            SessionState.SetBool(DiagnosedSessionStateKey, true);

            var message = DiagnoseMismatch();
            if (message != null)
                Debug.LogWarning($"[AddressTeller] {message}");
        }
    }
}
