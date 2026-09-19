using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>診断が下せた結論の種類。</summary>
    internal enum SettingsLoadDiagnosisKind
    {
        /// <summary>比較が成立し、差分が0件だった（正常）。</summary>
        Match,

        /// <summary>ファイルがまだ存在しない（初回起動等。正常）。</summary>
        FileAbsent,

        /// <summary>比較が成立し、差分が1件以上あった（variant A/B）。</summary>
        Mismatch,

        /// <summary>ファイルは存在するが読み取りで例外が発生した（不在以外。権限拒否・ロック等）。</summary>
        FileUnreadable,

        /// <summary>ファイルは読めたが、比較可能な鍵が1件も無かった（解釈不能）。</summary>
        FileUnparsable,

        /// <summary>AddressTeller 側の都合（再シリアライズ失敗、または再シリアライズ結果を自身が
        /// 解釈できない）で診断そのものが成立しなかった。ファイル側の問題ではない。</summary>
        DiagnosticUnavailable,
    }

    /// <summary>
    /// 設定ロード診断（<see cref="AddressTellerSettingsLoadDiagnostics.Diagnose"/>）の結果。
    /// </summary>
    /// <remarks>
    /// 不変条件: <see cref="Message"/> が <c>null</c> になるのは <see cref="Kind"/> が
    /// <see cref="SettingsLoadDiagnosisKind.Match"/> または <see cref="SettingsLoadDiagnosisKind.FileAbsent"/>
    /// の場合のみ。それ以外の <see cref="Kind"/> は必ず非 <c>null</c> の <see cref="Message"/> を持つ。
    /// 呼び出し元はこの不変条件により、<see cref="Kind"/> ごとの分岐を書かずとも
    /// <c>Message != null</c> だけで「何か報告すべきことがあるか」を判定できる。
    /// </remarks>
    internal readonly struct SettingsLoadDiagnosis
    {
        private SettingsLoadDiagnosis(SettingsLoadDiagnosisKind kind, string message)
        {
            Kind = kind;
            Message = message;
        }

        internal SettingsLoadDiagnosisKind Kind { get; }

        /// <summary>
        /// ログに値する文面。<c>null</c> なのは <see cref="Kind"/> が
        /// <see cref="SettingsLoadDiagnosisKind.Match"/> または <see cref="SettingsLoadDiagnosisKind.FileAbsent"/>
        /// の場合のみ（上記クラス remarks 参照）。
        /// </summary>
        internal string Message { get; }

        /// <summary>
        /// この診断が「設定ファイル自体の問題」（<see cref="SettingsLoadDiagnosisKind.Mismatch"/> /
        /// <see cref="SettingsLoadDiagnosisKind.FileUnreadable"/> / <see cref="SettingsLoadDiagnosisKind.FileUnparsable"/>）
        /// かどうか。<see cref="SettingsLoadDiagnosisKind.DiagnosticUnavailable"/>（AddressTeller 側の都合）は
        /// 含まない。CLI エントリポイントが <c>-addressTellerFailOnSettingsMismatch</c> 指定時に実行を
        /// 中断すべきかどうかの判定に使う（両呼び出し元が同じ判断を共有できるよう、判定ロジックをここに
        /// 集約している）。
        /// </summary>
        internal bool IsSettingsFileProblem =>
            Kind == SettingsLoadDiagnosisKind.Mismatch ||
            Kind == SettingsLoadDiagnosisKind.FileUnreadable ||
            Kind == SettingsLoadDiagnosisKind.FileUnparsable;

        internal static SettingsLoadDiagnosis Match() => new(SettingsLoadDiagnosisKind.Match, null);
        internal static SettingsLoadDiagnosis FileAbsent() => new(SettingsLoadDiagnosisKind.FileAbsent, null);
        internal static SettingsLoadDiagnosis Mismatch(string message) => new(SettingsLoadDiagnosisKind.Mismatch, message);
        internal static SettingsLoadDiagnosis FileUnreadable(string message) => new(SettingsLoadDiagnosisKind.FileUnreadable, message);
        internal static SettingsLoadDiagnosis FileUnparsable(string message) => new(SettingsLoadDiagnosisKind.FileUnparsable, message);
        internal static SettingsLoadDiagnosis DiagnosticUnavailable(string message) => new(SettingsLoadDiagnosisKind.DiagnosticUnavailable, message);
    }

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

        private const string ReserializeFailedMessage =
            "AddressTeller could not re-serialize the settings currently in use for comparison, so it skipped its check of " +
            "ProjectSettings/AddressTellerSettings.asset. This says nothing about whether that file matches the settings in use; " +
            "neither the file nor the settings in use were changed.";

        private const string MemoryUnrecognizedMessage =
            "AddressTeller re-serialized the settings currently in use, but recognized no settings fields in the result, so it " +
            "skipped its check of ProjectSettings/AddressTellerSettings.asset. This is a problem on AddressTeller's side and says " +
            "nothing about that file; neither the file nor the settings in use were changed.";

        /// <summary>
        /// ProjectSettings/AddressTellerSettings.asset のディスク上のテキストと、現在メモリにある設定を
        /// 再シリアライズしたテキストを比較し、診断結果を返す。
        /// </summary>
        /// <remarks>
        /// 戻り値の6種類の <see cref="SettingsLoadDiagnosisKind"/> の意味は各メンバーの XML doc を参照。
        /// <see cref="SettingsLoadDiagnosis.Message"/> が <c>null</c> になるのは
        /// <see cref="SettingsLoadDiagnosisKind.Match"/> と <see cref="SettingsLoadDiagnosisKind.FileAbsent"/>
        /// の2つだけで、それ以外は必ず何か報告すべきことがある。これにより、対話的でない実行
        /// （CLI/バッチモード）からこのメソッドを無条件に呼んでも、初回起動時（ファイル未存在）に偽の
        /// 警告を出すことはない一方、ファイルが破損・権限拒否等で読めない場合は確実に報告される。
        /// ただし例外が1つある: いずれの <see cref="SettingsLoadDiagnosisKind"/> であっても、メモリ側の
        /// 再シリアライズが使う一時ファイルの削除に失敗した場合に限り、
        /// <see cref="AddressTellerSettingsAsset.SaveCurrentInstanceToTempFileAndReadText"/> 自身が
        /// best-effort な <c>Debug.LogWarning</c> を1回出すことがある（戻り値やこのメソッドの契約には
        /// 影響しない。単に一時ファイルが削除されずに残るだけ）。これは「設定の不一致を報告する」ための
        /// ログではなく、上記の契約はこの警告には及ばない。
        /// </remarks>
        internal static SettingsLoadDiagnosis Diagnose()
        {
            string filePath;
            try
            {
                filePath = AddressTellerSettingsAsset.GetAbsoluteFilePath();
            }
            catch (Exception ex)
            {
                // パス取得自体の失敗は「ファイルの問題」ではなく AddressTeller 側の問題として扱う。
                // ここを File.ReadAllText と同じ try に含めてしまうと、パス解決の失敗をファイルの
                // せいにして FileUnreadable（-addressTellerFailOnSettingsMismatch 指定時に Exit(3)）
                // へ倒してしまう。
                return SettingsLoadDiagnosis.DiagnosticUnavailable(BuildPathUnavailableMessage(ex));
            }

            string diskText;
            try
            {
                diskText = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                return ClassifyReadException(ex) == SettingsLoadDiagnosisKind.FileAbsent
                    ? SettingsLoadDiagnosis.FileAbsent()
                    : SettingsLoadDiagnosis.FileUnreadable(BuildFileUnreadableMessage(ex));
            }

            var memoryText = AddressTellerSettingsAsset.SaveCurrentInstanceToTempFileAndReadText();
            if (memoryText == null)
                return SettingsLoadDiagnosis.DiagnosticUnavailable(ReserializeFailedMessage);

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, memoryText);

            // メモリ側の判定を必ずディスク側の判定より先に行う。メモリ側テキストに比較対象になりうる
            // 鍵が1件も認識できないなら ComparableFieldCount も必然的に0になるため、順序を逆にすると
            // AddressTeller 自身の再シリアライズ結果を自身が解釈できていないという AddressTeller 側の
            // 不調を、「利用者のファイルが壊れている」（FileUnparsable）と誤って報告してしまう。
            if (!comparison.MemoryTextRecognized)
                return SettingsLoadDiagnosis.DiagnosticUnavailable(MemoryUnrecognizedMessage);

            // 「解釈できない」の判定基準は「抽出できた鍵がゼロ件」ではなく「両辺で比較可能な鍵がゼロ件」。
            // ディスク側に鍵は抽出できていても、そのどれもが現行スキーマの鍵と1つも名前が重ならない場合
            // （バイナリ化・途中で切り詰められたファイル・全く別の型のアセット、のいずれであっても）は、
            // 診断からは原因を区別する観測手段が無い同一カテゴリ（「ファイルをメモリと照合できなかった」）
            // として扱う。なお、このバージョンより前の旧形式ファイル（m_Script が旧識別子）はこれに該当
            // しない — 旧形式でも `_` 始まりの設定フィールド名自体は現行と完全に同名のため
            // ComparableFieldCount は0にならず、従来どおり Mismatch（variant A）に分類される
            // （AddressTellerSettingsTextDiffTests の旧形式ケースで固定済み）。
            if (comparison.ComparableFieldCount == 0)
                return SettingsLoadDiagnosis.FileUnparsable(BuildFileUnparsableMessage(diskText.Length));

            if (comparison.Diffs.Count == 0)
                return SettingsLoadDiagnosis.Match();

            return SettingsLoadDiagnosis.Mismatch(BuildMessage(comparison.Diffs, comparison.TypeIdentifierMatches));
        }

        /// <summary>
        /// ProjectSettings/AddressTellerSettings.asset の読み取りで発生した例外を、「まだファイルが無い」
        /// （正常）と「存在するが読めない」（異常）に分類する純粋関数。実際に ACL を操作する等の実ファイル
        /// 操作を伴わずにテストできるよう、例外の型だけを引数に取る形にしている。
        /// </summary>
        /// <remarks>
        /// .NET のドキュメント上、読み取り権限が無い場合は <see cref="UnauthorizedAccessException"/> を
        /// 投げるとされているが、これは未実測。ここでは個々の例外型を網羅的にホワイトリストするのではなく
        /// 「不在系（<see cref="FileNotFoundException"/> / <see cref="DirectoryNotFoundException"/>）か
        /// どうか」だけで二分するため、この未実測の点は結果に影響しない
        /// （不在系以外はすべて一律 <see cref="SettingsLoadDiagnosisKind.FileUnreadable"/> になる）。
        /// </remarks>
        internal static SettingsLoadDiagnosisKind ClassifyReadException(Exception exception)
        {
            return exception is FileNotFoundException || exception is DirectoryNotFoundException
                ? SettingsLoadDiagnosisKind.FileAbsent
                : SettingsLoadDiagnosisKind.FileUnreadable;
        }

        /// <summary>設定ファイルの絶対パスが取得できなかった場合の文面。原因（権限・環境）は推測になるため書かない。</summary>
        private static string BuildPathUnavailableMessage(Exception ex) =>
            "AddressTeller could not determine the location of ProjectSettings/AddressTellerSettings.asset " +
            $"({ex.GetType().Name}: {ex.Message}), so it skipped its check of that file. This says nothing about whether that " +
            "file matches the settings in use; neither the file nor the settings in use were changed.";

        /// <summary>
        /// ファイルは存在するが読み取りで例外が発生した場合の文面。例外の型名とメッセージが唯一の観測事実
        /// のため必ず埋める。原因（権限拒否・ロック等）は推測になるため書かない。
        /// </summary>
        private static string BuildFileUnreadableMessage(Exception ex) =>
            "ProjectSettings/AddressTellerSettings.asset exists but could not be read " +
            $"({ex.GetType().Name}: {ex.Message}), so AddressTeller could not check it against the settings currently in use. " +
            "Once the file can be read again, call AddressTellerSettings.ReloadFromDisk() to load its values, or " +
            "AddressTellerSettings.SaveToDisk() to overwrite it with the values in use.";

        /// <summary>
        /// ファイルは読めたが比較可能な鍵が1件も無かった場合の文面。「破損している」と断定はしない
        /// （バイナリ化・切り詰め・別型のアセットのいずれもありうるため）。読めた文字数は実観測なので入れる。
        /// </summary>
        private static string BuildFileUnparsableMessage(int characterCount) =>
            $"ProjectSettings/AddressTellerSettings.asset was read ({characterCount} characters), but it contains none of the " +
            "settings fields this version of AddressTeller writes, so the file could not be checked against the settings " +
            "currently in use. The settings currently in use are unchanged by this check. To overwrite the file with the " +
            "settings currently in use, call AddressTellerSettings.SaveToDisk().";

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
        /// Editor 起動時（および各ドメインリロード後）にセッションにつき1回だけ診断を実行し、
        /// 報告すべきことがあれば（<see cref="SettingsLoadDiagnosis.Message"/> が非 <c>null</c>）
        /// Warning を1本ログ出力する。ファイル自体の問題（Mismatch/FileUnreadable/FileUnparsable）と
        /// AddressTeller 側の都合（DiagnosticUnavailable）のいずれも、起動時は区別せず Warning に統一する
        /// （フラグを指定していない利用者にとって Error は挙動変化であり、実行を失敗させる手段は
        /// -addressTellerFailOnSettingsMismatch に一本化している）。
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

            var diagnosis = Diagnose();
            if (diagnosis.Message != null)
                Debug.LogWarning($"[AddressTeller] {diagnosis.Message}");
        }
    }
}
