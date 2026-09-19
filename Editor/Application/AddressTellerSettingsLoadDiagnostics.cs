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

        /// <summary>AddressTeller 側の都合（再シリアライズ失敗、再シリアライズ結果を自身が解釈できない、
        /// パスの形状が不正、または診断処理そのものの想定外の失敗）で診断そのものが成立しなかった。
        /// ファイル側の問題ではない。</summary>
        DiagnosticUnavailable,
    }

    /// <summary>
    /// 設定ロード診断（<see cref="AddressTellerSettingsLoadDiagnostics.Diagnose"/>）の結果。
    /// </summary>
    /// <remarks>
    /// 不変条件: <see cref="Message"/> が <c>null</c> になるのは <see cref="Kind"/> が
    /// <see cref="SettingsLoadDiagnosisKind.Match"/> または <see cref="SettingsLoadDiagnosisKind.FileAbsent"/>
    /// の場合のみ。それ以外の <see cref="Kind"/> は必ず非 <c>null</c> の <see cref="Message"/> を持つ
    /// （各ファクトリメソッドが <c>null</c> を渡された場合に <see cref="ArgumentNullException"/> で弾く）。
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

        internal static SettingsLoadDiagnosis Mismatch(string message) =>
            new(SettingsLoadDiagnosisKind.Mismatch, message ?? throw new ArgumentNullException(nameof(message)));

        internal static SettingsLoadDiagnosis FileUnreadable(string message) =>
            new(SettingsLoadDiagnosisKind.FileUnreadable, message ?? throw new ArgumentNullException(nameof(message)));

        internal static SettingsLoadDiagnosis FileUnparsable(string message) =>
            new(SettingsLoadDiagnosisKind.FileUnparsable, message ?? throw new ArgumentNullException(nameof(message)));

        internal static SettingsLoadDiagnosis DiagnosticUnavailable(string message) =>
            new(SettingsLoadDiagnosisKind.DiagnosticUnavailable, message ?? throw new ArgumentNullException(nameof(message)));
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
        /// このフラグの意味は「診断を試行済み」であり「報告すべきことをすでに報告した」ではない
        /// （下記 <see cref="s_diagnosisThisDomain"/> とは役割が異なる二重のガード——詳細は
        /// <see cref="RunOnceOnStartup"/> のコメントを参照）。
        /// </summary>
        private const string DiagnosisAttemptedSessionStateKey = "AddressTeller.SettingsLoadDiagnosed";

        /// <summary>
        /// このドメインの寿命内で下した診断結果のキャッシュ。<see cref="GetOrDiagnoseForThisDomain"/> 参照。
        /// スレッド安全ではない（<c>??=</c> はアトミックではない）。実際の呼び出し元
        /// （<see cref="RunOnceOnStartup"/> の <c>[InitializeOnLoadMethod]</c>、CLI の <c>-executeMethod</c>）
        /// はいずれもメインスレッドからのみ呼ばれるため現状は問題にならないが、これは
        /// <see cref="AddressTellerSettings.ReloadFromDisk"/> の XML doc が「call it from the main thread」と
        /// 明記しているのと同じ前提に乗っているだけであり、この前提が崩れる呼び出し方をした場合は保護されない。
        /// </summary>
        private static SettingsLoadDiagnosis? s_diagnosisThisDomain;

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
        /// このメソッドは例外を投げない契約——内部の各段階（パス解決・ファイル読み取り・再シリアライズ・
        /// 比較）を個別に保護した上で、さらにメソッド全体を外側の try/catch で包んでおり、個別の保護が
        /// 想定していない失敗（例えば <c>instance</c> の初回アクセスが内部で予期しない例外を投げた場合等）
        /// もすべて <see cref="SettingsLoadDiagnosisKind.DiagnosticUnavailable"/> として拾う。呼び出し元
        /// （<see cref="RunOnceOnStartup"/> や CLI 側）はこの契約に依存してよい。
        /// ただし例外が1つある: いずれの <see cref="SettingsLoadDiagnosisKind"/> であっても、メモリ側の
        /// 再シリアライズが使う一時ファイルの削除に失敗した場合に限り、
        /// <see cref="AddressTellerSettingsAsset.SaveCurrentInstanceToTempFileAndReadText"/> 自身が
        /// best-effort な <c>Debug.LogWarning</c> を1回出すことがある（戻り値やこのメソッドの契約には
        /// 影響しない。単に一時ファイルが削除されずに残るだけ）。これは「設定の不一致を報告する」ための
        /// ログではなく、上記の契約はこの警告には及ばない。
        /// </remarks>
        internal static SettingsLoadDiagnosis Diagnose()
        {
            try
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
                    // ClassifyReadException は3値（FileAbsent/FileUnreadable/DiagnosticUnavailable）を
                    // 明示的に列挙し、default では例外を投げない（Diagnose() 自身が「例外を投げない」契約を
                    // 持つため）。_ アームは、ClassifyReadException が将来 Kind を追加した場合に到達する経路。
                    // C# の switch 式は enum に対する非網羅を実際に CS8509 で検出するが、この _ アームを
                    // 置いたことで、その検出自体を自分で無効化している（Diagnose() の非 throw 契約を優先した
                    // 意図的なトレードオフ）。将来 _ を消せばコンパイル時に検出できる余地は残っているため、
                    // 気づく手段はレビューとテストに限られる。
                    return ClassifyReadException(ex) switch
                    {
                        SettingsLoadDiagnosisKind.FileAbsent => SettingsLoadDiagnosis.FileAbsent(),
                        SettingsLoadDiagnosisKind.DiagnosticUnavailable => SettingsLoadDiagnosis.DiagnosticUnavailable(BuildPathUnusableMessage(ex)),
                        _ => SettingsLoadDiagnosis.FileUnreadable(BuildFileUnreadableMessage(ex)),
                    };
                }

                var memoryText = AddressTellerSettingsAsset.SaveCurrentInstanceToTempFileAndReadText();
                if (memoryText == null)
                    return SettingsLoadDiagnosis.DiagnosticUnavailable(ReserializeFailedMessage);

                var comparison = AddressTellerSettingsTextDiff.Compare(diskText, memoryText);
                return Classify(comparison, diskText.Length);
            }
            catch (Exception ex)
            {
                // 上記の個別 catch のどれにも該当しない、想定外の失敗を拾う最後の砦。個別 catch は
                // 「どの段階で失敗したか」という区別可能な観測事実を保持しており、ここに畳み込むと
                // その区別が失われるため、個別 catch は削除せず維持する。ここに来た時点で観測できるのは
                // 「診断処理のどこかで予期しない例外が起きた」という事実のみで、ファイル側の問題だと
                // 決めつける根拠は無いため DiagnosticUnavailable にする。
                return SettingsLoadDiagnosis.DiagnosticUnavailable(BuildDiagnosticFailedMessage(ex));
            }
        }

        /// <summary>
        /// SettingsTextComparison の結果だけから診断を下す純粋関数（ファイル I/O・例外処理を一切伴わない）。
        /// 分類の判定順序（MemoryTextRecognized を ComparableFieldCount より先に見る、等）をここに集約する
        /// ことで、I/O なしにテストできる。
        /// </summary>
        /// <param name="comparison">ディスク側テキストとメモリ側テキストの比較結果。</param>
        /// <param name="diskTextLength">
        /// ディスク側テキストの文字数のみを受け取る（テキスト本体は受け取らない）。FileUnparsable の文面が
        /// 使う唯一の観測値がこれであり、テキスト本体を渡せるようにすると、この関数の中でもう一度パースを
        /// したくなる誘惑を生む。意図的に長さだけに絞ることで、この関数の責務を「比較結果からの分類」だけに
        /// 保っている。
        /// </param>
        /// <returns>
        /// <see cref="SettingsLoadDiagnosisKind.DiagnosticUnavailable"/> / <see cref="SettingsLoadDiagnosisKind.FileUnparsable"/> /
        /// <see cref="SettingsLoadDiagnosisKind.Match"/> / <see cref="SettingsLoadDiagnosisKind.Mismatch"/> の4値のみ。
        /// <see cref="SettingsLoadDiagnosisKind.FileAbsent"/> / <see cref="SettingsLoadDiagnosisKind.FileUnreadable"/> は
        /// 返さない——これらは「ファイルを読めたかどうか」という I/O 層の判断であり、この関数が呼ばれる時点で
        /// 読み取りは既に成功している。
        /// </returns>
        internal static SettingsLoadDiagnosis Classify(SettingsTextComparison comparison, int diskTextLength)
        {
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
                return SettingsLoadDiagnosis.FileUnparsable(BuildFileUnparsableMessage(diskTextLength));

            if (comparison.Diffs.Count == 0)
                return SettingsLoadDiagnosis.Match();

            return SettingsLoadDiagnosis.Mismatch(BuildMessage(comparison.Diffs, comparison.TypeIdentifierMatches));
        }

        /// <summary>
        /// ProjectSettings/AddressTellerSettings.asset の読み取りで発生した例外を、「まだファイルが無い」
        /// （正常）・「存在するが読めない」（異常・ファイル側の問題）・「AddressTeller 側の都合で読み取りに
        /// 使えなかった」（異常・ファイル側の問題ではない）の3値に分類する純粋関数。実際に ACL を操作する
        /// 等の実ファイル操作を伴わずにテストできるよう、例外の型だけを引数に取る形にしている。
        /// </summary>
        /// <remarks>
        /// 判定順序は .NET の継承関係に依存するため固定する: <see cref="PathTooLongException"/> /
        /// <see cref="FileNotFoundException"/> / <see cref="DirectoryNotFoundException"/> はいずれも
        /// <see cref="IOException"/> の派生であり、先に <see cref="IOException"/> で大づかみに拾うと以下の
        /// 分岐が機能しなくなる。
        /// ①不在系（<see cref="FileNotFoundException"/> / <see cref="DirectoryNotFoundException"/>）→
        /// <see cref="SettingsLoadDiagnosisKind.FileAbsent"/>。
        /// ②パスの形状自体が理由で読み取れなかったケース（<see cref="ArgumentException"/>
        /// ——<see cref="ArgumentNullException"/> / <see cref="ArgumentOutOfRangeException"/> を含む——、
        /// <see cref="PathTooLongException"/>、<see cref="NotSupportedException"/>）→
        /// <see cref="SettingsLoadDiagnosisKind.DiagnosticUnavailable"/>。
        /// <see cref="AddressTellerSettingsAsset.GetAbsoluteFilePath"/> が例外を投げずに不正な形状のパスを
        /// 返した場合、<see cref="File.ReadAllText(string)"/> 側がこれらの例外を投げうる。これは環境や
        /// ファイルの問題ではなく AddressTeller 側のパス組み立ての事情であるため、ファイルのせいにする
        /// <see cref="SettingsLoadDiagnosisKind.FileUnreadable"/> ではなく
        /// <see cref="SettingsLoadDiagnosisKind.DiagnosticUnavailable"/> へ倒す（
        /// <see cref="ArgumentNullException"/> / <see cref="ArgumentOutOfRangeException"/> も AddressTeller
        /// 側の呼び出しミスに起因するため同じ扱いで妥当）。
        /// 未確認事項: <see cref="Path.GetFullPath(string)"/>（<see cref="AddressTellerSettingsAsset.GetAbsoluteFilePath"/>
        /// が内部で呼ぶ）が先に <see cref="PathTooLongException"/> を投げるかどうか（Unity Mono での挙動）は
        /// 未実測。ただし <see cref="AddressTellerSettingsAsset.GetAbsoluteFilePath"/> 内で投げれば
        /// <c>Diagnose()</c> の既存 catch が <see cref="SettingsLoadDiagnosisKind.DiagnosticUnavailable"/> に、
        /// ここまで到達して投げても同じ <see cref="SettingsLoadDiagnosisKind.DiagnosticUnavailable"/> になる
        /// ——どちらでも最終分類は同じで、変わるのは文面（<see cref="BuildPathUnavailableMessage"/> か
        /// <see cref="BuildPathUnusableMessage"/> か）だけのため、この未実測の点は結果に影響しない。
        /// ③既定（それ以外すべて。<see cref="UnauthorizedAccessException"/>、パス形状以外の
        /// <see cref="IOException"/>、<see cref="System.Security.SecurityException"/> 等）→
        /// <see cref="SettingsLoadDiagnosisKind.FileUnreadable"/>。.NET のドキュメント上、読み取り権限が
        /// 無い場合は <see cref="UnauthorizedAccessException"/> を投げるとされているが、これも未実測。
        /// 既定を <see cref="SettingsLoadDiagnosisKind.FileUnreadable"/> のまま置くこと自体が判断——未知の
        /// 例外型を <see cref="SettingsLoadDiagnosisKind.DiagnosticUnavailable"/> へ倒すと「読めないファイルが
        /// あるのに <c>-addressTellerFailOnSettingsMismatch</c> 指定時も CI を失敗させない」という抜け穴になり、
        /// この診断が「ファイル側の問題を見逃さず報告する」ことを目的にしている点に反する。ここに来る例外は
        /// <see cref="File.ReadAllText(string)"/> が（②で弾かれなかった、つまり形状としては妥当な）解決済み
        /// パスに対して投げたものであり、ファイル側が主語である方が確からしい。
        /// </remarks>
        internal static SettingsLoadDiagnosisKind ClassifyReadException(Exception exception)
        {
            if (exception is FileNotFoundException || exception is DirectoryNotFoundException)
                return SettingsLoadDiagnosisKind.FileAbsent;

            if (exception is ArgumentException || exception is PathTooLongException || exception is NotSupportedException)
                return SettingsLoadDiagnosisKind.DiagnosticUnavailable;

            return SettingsLoadDiagnosisKind.FileUnreadable;
        }

        /// <summary>設定ファイルの絶対パスが取得できなかった場合の文面。原因（権限・環境）は推測になるため書かない。</summary>
        private static string BuildPathUnavailableMessage(Exception ex) =>
            "AddressTeller could not determine the location of ProjectSettings/AddressTellerSettings.asset " +
            $"({ex.GetType().Name}: {ex.Message}), so it skipped its check of that file. This says nothing about whether that " +
            "file matches the settings in use; neither the file nor the settings in use were changed.";

        /// <summary>
        /// 場所（パス）自体は得られたが、その場所が読み取りに使えなかった場合の文面。
        /// <see cref="BuildPathUnavailableMessage"/> とは観測事実が異なる——こちらは「パスは取得できたが、
        /// そのパスの形状が理由で読み取れなかった」であり、原因（AddressTeller 側のパス組み立て・環境）は
        /// 推測になるため書かない。
        /// </summary>
        private static string BuildPathUnusableMessage(Exception ex) =>
            "AddressTeller determined a location for ProjectSettings/AddressTellerSettings.asset, but that location could not " +
            $"be used to read the file ({ex.GetType().Name}: {ex.Message}), so it skipped its check of that file. This says " +
            "nothing about whether that file matches the settings in use; neither the file nor the settings in use were changed.";

        /// <summary>
        /// ファイルは存在するが読み取りで例外が発生した場合の文面。実際に観測したのは「不在系以外の例外が
        /// 発生した」ことのみであり、「ファイルが存在する」ことそのものを確認したわけではない（例外の型名と
        /// メッセージが唯一の観測事実）。原因（権限拒否・ロック等）は推測になるため書かない。
        /// </summary>
        private static string BuildFileUnreadableMessage(Exception ex) =>
            "Reading ProjectSettings/AddressTellerSettings.asset raised an exception other than the kind AddressTeller " +
            $"treats as \"the file does not exist\" ({ex.GetType().Name}: {ex.Message}), so AddressTeller could not check it " +
            "against the settings currently in use. Once the file can be read again, call " +
            "AddressTellerSettings.ReloadFromDisk() to load its values, or AddressTellerSettings.SaveToDisk() to overwrite it " +
            "with the values in use — but if this file also failed to load the same way when Unity itself read it at startup, " +
            "the settings currently in use are likely still at their default values, and the file may still hold different, " +
            "previous values right now: any settings change (not just SaveToDisk() — every property setter here persists on " +
            "change too) overwrites it with whatever is currently in use.";

        /// <summary>
        /// ファイルは読めたが比較可能な鍵が1件も無かった場合の文面。「破損している」と断定はしない
        /// （バイナリ化・切り詰め・別型のアセットのいずれもありうるため）。実際に観測したのは「現行の
        /// 抽出規則（2スペースインデントのトップレベル鍵検出）で認識できる位置に、現行スキーマの鍵が
        /// 1件も見つからなかった」ことのみであり、「フィールドを1つも含んでいない」と断定するのは
        /// 抽出規則の限界（例: インデント幅が異なる等）を無視した言い過ぎになる。読めた文字数は実観測なので入れる。
        /// </summary>
        private static string BuildFileUnparsableMessage(int characterCount) =>
            $"ProjectSettings/AddressTellerSettings.asset was read ({characterCount} characters), but none of the settings " +
            "fields this version writes were found in a position AddressTeller's extraction rule recognizes, so the file " +
            "could not be checked against the settings currently in use. The settings currently in use are unchanged by this " +
            "check. To overwrite the file with the settings currently in use, call AddressTellerSettings.SaveToDisk() — but if " +
            "this file also failed to load the same way when Unity itself read it at startup, the settings currently in use " +
            "are likely still at their default values, and the file may still hold different, previous values right now: any " +
            "settings change (not just SaveToDisk() — every property setter here persists on change too) overwrites it with " +
            "whatever is currently in use.";

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

        /// <summary>
        /// この診断処理そのものが、個別の catch のどれにも当たらない想定外の理由で失敗した場合の文面。
        /// <see cref="ReserializeFailedMessage"/> や <see cref="BuildPathUnavailableMessage"/> とはあえて
        /// 別の文面にする——「既知の段階で失敗した」ことと「想定外の場所で落ちた」ことはログ上で区別できる
        /// べきであり、後者を前者の文面に混ぜると、原因調査のときに「既知のはずの失敗」だと誤解させる。
        /// </summary>
        private static string BuildDiagnosticFailedMessage(Exception ex) =>
            "AddressTeller hit an unexpected error while checking ProjectSettings/AddressTellerSettings.asset against the " +
            $"settings currently in use ({ex.GetType().Name}: {ex.Message}), so it skipped that check. This says nothing " +
            "about whether that file matches the settings in use; neither the file nor the settings in use were changed.";

        /// <summary>1行の警告メッセージに埋め込むための表示用整形。改行を ", " へ畳み込むだけで、値そのものは変えない。</summary>
        private static string FlattenForMessage(string value) => value.Replace("\n", ", ");

        /// <summary>
        /// このドメインの寿命内で一度だけ <see cref="Diagnose"/> を実行し、結果をキャッシュして返す。
        /// Editor 起動時（<see cref="RunOnceOnStartup"/>）と CLI（<c>AddressTellerMenu.SettingsDiagnosticShouldAbortCli</c>）
        /// の両方がこのメソッドを呼ぶことで、同一ドメイン内で <see cref="Diagnose"/> が重複実行されるのを防ぐ
        /// （重複実行は一時ファイルの書き込み・削除とログの二重発生を招いていた）。
        /// </summary>
        /// <remarks>
        /// キャッシュは <see cref="SessionState"/> ではなく static フィールドに置く。理由は2つ:
        /// (1) <see cref="SessionState"/> は string/int/bool しか保持できず、<see cref="SettingsLoadDiagnosisKind"/> と
        /// メッセージ本文の組をシリアライズする専用形式を新設することになるが、それを消費するコードは無い。
        /// (2) ドメインリロードをまたいで結果を持ち回ると、「設定変更 → スクリプト変更 → ドメインリロード」の
        /// 後も古い結論を出し続けてしまう。ドメイン限りのキャッシュにすることで、結論が古くなりうる窓は
        /// 1ドメインの寿命に限定され、バッチモードで再利用したい窓（起動時の診断 → 同じドメイン内で
        /// <c>-executeMethod</c> が呼ぶ CLI の診断）とちょうど一致する。
        /// このキャッシュは <see cref="AddressTellerSettings.SaveToDisk"/> / <see cref="AddressTellerSettings.ReloadFromDisk"/>
        /// の呼び出し後に <see cref="InvalidateDomainCache"/> で明示的に無効化される（両メソッドとも、ファイルと
        /// メモリの関係を変えうるため）。それ以外の経路（設定プロパティの setter による自動保存等）では
        /// このキャッシュは無効化されない——setter 経由の保存はディスクとメモリを一致させる方向のため
        /// 偽陽性は起きにくいが、厳密には陳腐化しうる、という既知の割り切り。
        /// </remarks>
        internal static SettingsLoadDiagnosis GetOrDiagnoseForThisDomain()
        {
            s_diagnosisThisDomain ??= Diagnose();
            return s_diagnosisThisDomain.Value;
        }

        /// <summary>
        /// <see cref="GetOrDiagnoseForThisDomain"/> のドメインスコープキャッシュを無効化する。
        /// <see cref="AddressTellerSettings.SaveToDisk"/> / <see cref="AddressTellerSettings.ReloadFromDisk"/> の
        /// 呼び出し後に呼ぶことで、直したはずの不一致が古い診断結果のせいで
        /// <c>-addressTellerFailOnSettingsMismatch</c> 指定時に誤って中断させる偽陽性を防ぐ。
        /// </summary>
        internal static void InvalidateDomainCache() => s_diagnosisThisDomain = null;

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
            try
            {
                // このフラグの意味は「診断を試行済み」であり「報告すべきことをすでに報告した」ではない。
                // 例外時も含め、成否に関わらず先にフラグを立て、二度と再試行しない。
                // 理由: ここでの失敗（壊れたパス・権限・instance 生成失敗）は決定的なことがほとんどで、
                // 「成功時のみフラグを立てる」案だとスクリプトを触るたびに同じ長い警告と一時ファイル書き出しが
                // 再発し、新しい情報は増えずログノイズと I/O だけが増える。「例外時だけ再試行する」案も
                // 「最大2回まで」という中途半端な保証になり、「この警告は何回出るのか」に答えられなくなる。
                // 情報が失われるわけではない——例外時も catch が Warning を1本出す（下記）。「再試行しない」
                // ことと「黙る」ことは別。
                // なお、このフラグとは別に GetOrDiagnoseForThisDomain 側にもドメインスコープのキャッシュ
                // （s_diagnosisThisDomain）がある。役割は異なる: このフラグは「起動時の報告はこのセッション
                // （ドメインリロードをまたぐ）で済んだか」を管理し、s_diagnosisThisDomain は「このドメイン内で
                // 診断そのものを実行済みか」を管理する（起動時の1回と、同じドメイン内で CLI が呼ぶ1回とで
                // Diagnose() の実処理を共有するためのもの）。
                if (SessionState.GetBool(DiagnosisAttemptedSessionStateKey, false)) return;
                SessionState.SetBool(DiagnosisAttemptedSessionStateKey, true);

                var diagnosis = GetOrDiagnoseForThisDomain();
                if (diagnosis.Message != null)
                    Debug.LogWarning($"[AddressTeller] {diagnosis.Message}");
            }
            catch (Exception ex)
            {
                // [InitializeOnLoadMethod] はドメインロード中に他の初期化処理と並んで呼ばれる。ここで例外を
                // 投げっぱなしにすると、同じドメインロード中の他の初期化処理を巻き添えにしうる（Unity 側が
                // このメソッド単位で例外を隔離しているかどうかは未確認だが、隔離されていてもいなくても、
                // ここで止めておくべきことに変わりはない）。起動時は Error に昇格させない方針を保つため、
                // Warning のみに留め再スローしない。
                Debug.LogWarning("[AddressTeller] RunOnceOnStartup: unexpected error while running the settings load " +
                    $"diagnostic ({ex.GetType().Name}: {ex.Message}). This says nothing about the settings file itself.");
            }
        }
    }
}
