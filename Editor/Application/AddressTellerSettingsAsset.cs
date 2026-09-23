using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// 設定読み込みゲート（<see cref="AddressTellerSettingsAsset.EnsureLoaded"/> /
    /// <see cref="AddressTellerSettings.EnsureLoaded"/>）の結果。成功/失敗だけでなく、失敗理由
    /// （<see cref="Error"/>）とファイルがそもそも存在したか（<see cref="FileExists"/>）を呼び出し側が
    /// 参照できるようにする。<c>bool</c> への暗黙変換を持つため、<c>if (!EnsureLoaded()) return;</c> と
    /// 書ける。
    /// </summary>
    internal readonly struct SettingsGateResult
    {
        /// <summary>読み込みに成功したか（ファイルが存在しない場合も既定値として成功扱い）。</summary>
        internal bool Success { get; }

        /// <summary>失敗理由。<see cref="Success"/> が true の場合は null。</summary>
        internal string Error { get; }

        /// <summary>この判定を行った時点で設定ファイルが存在したか。</summary>
        internal bool FileExists { get; }

        /// <summary>
        /// <see cref="FileExists"/> が true の場合、この判定時点でのファイルの更新日時（UTC）。
        /// <see cref="AddressTellerSettings.EnsureLoaded"/> の OncePerDistinctFailure ログ抑制が、
        /// 失敗のたびにファイルを再 stat せずこの値をそのまま重複排除キーに使うために持たせている。
        /// <see cref="FileExists"/> が false の場合は既定値（未使用）。
        /// </summary>
        internal DateTime FileLastWriteUtc { get; }

        /// <summary>
        /// <see cref="FileExists"/> が true の場合、この判定時点でのファイルサイズ。用途は
        /// <see cref="FileLastWriteUtc"/> と同じ。<see cref="FileExists"/> が false の場合は既定値（未使用）。
        /// </summary>
        internal long FileLength { get; }

        internal SettingsGateResult(bool success, string error, bool fileExists, DateTime fileLastWriteUtc, long fileLength)
        {
            Success = success;
            Error = error;
            FileExists = fileExists;
            FileLastWriteUtc = fileLastWriteUtc;
            FileLength = fileLength;
        }

        public static implicit operator bool(SettingsGateResult result) => result.Success;
    }

    /// <summary>
    /// AddressTellerSettings の実体。ProjectSettings/AddressTellerSettings.json に JSON として
    /// シリアライズされ、プロジェクトを共有する開発者間でバージョン管理される。
    /// </summary>
    internal static class AddressTellerSettingsAsset
    {
        /// <summary>
        /// このファイルが AddressTeller の設定ファイルであることを示すマーカー値。書き込み時に必ず
        /// <see cref="Data._marker"/> へ設定する。<see cref="Data._marker"/> にフィールド初期化子を
        /// 持たせていないのは意図的——JsonUtility.FromJson は、JSON テキストに存在しないキーに対応する
        /// フィールドをフィールド初期化子の値のまま残す（実測で確認済み）ため、初期化子を与えてしまうと
        /// マーカー不在（＝AddressTeller の設定ファイルではない、または壊れている）を検出できなくなる。
        /// </summary>
        internal const string MarkerValue = "addressteller-settings-v1";

        /// <summary>
        /// 保存対象の POCO。現在9個（設定8個 + マーカー1個）。増減した場合は
        /// Documentation~/operations.md/.ja.md の設定一覧表、Documentation~/compatibility.md/.ja.md の
        /// JSON キー表、Tests/Editor/AddressTellerSettingsPersistenceTests.cs のフィールド数 assert を
        /// 同時に更新すること。
        /// </summary>
        [Serializable]
        internal sealed class Data
        {
            // JsonUtility は Unity のシリアライズシステムを経由するため、internal フィールドであっても
            // [SerializeField] を明示しないとシリアライズ対象にならない（public にしない理由は、
            // このクラス自体が internal であり外部公開する必要が無いため）。
            // 既定は両方 OFF。安全に導入するなら手動の Apply All から始めるのが自然な流れであり、それを
            // 既定にした。自動化（インポート時自動適用・不要エントリの自動削除）は、手動 Apply の結果に
            // 納得した利用者が段階的に有効化するものという位置付け。詳細は
            // Documentation~/design-decisions.md の「Deletions Are Determined by Per-Asset Ownership」節を参照。
            [SerializeField] internal bool _cleanupStaleEntries = false;
            [SerializeField] internal bool _postprocessEnabled = false;
            [SerializeField] internal string _snapshotFolder = AddressTellerSettings.DefaultSnapshotFolder;
            [SerializeField] internal bool _autoSnapshotBeforeApplyAll = true;
            [SerializeField] internal int _autoSnapshotRetention = 10;
            [SerializeField] internal bool _autoCreateMissingGroups = false;
            [SerializeField] internal int _postprocessOrder = AddressTellerSettings.DefaultPostprocessOrder;
            [SerializeField] internal List<string> _disabledRuleClassNames = new();

            // マーカー。初期化子を持たせない（クラス remarks 参照）。
            [SerializeField] internal string _marker;
        }

        private static Data s_data = new();

        /// <summary>
        /// <see cref="AddressTellerSettings.AutoSnapshotRetention"/> の下限（1）への正規化を1箇所に集約する。
        /// setter（<see cref="AddressTellerSettings.AutoSnapshotRetention"/>）と、設定ファイル読み込み直後
        /// （<see cref="EnsureLoaded"/>）の両方から使う——基準を2箇所に持つと将来どちらかだけが
        /// 追従せず食い違う恐れがあるため。<paramref name="warnIfChanged"/> が true で正規化が働いた場合のみ
        /// Warning を1本出す。setter からの呼び出しは false（利用者が直接指定した値をその場でクランプする
        /// 通常の挙動であり、毎回警告を出すのは過剰）。読み込み時は true（設定ファイルという外部入力に
        /// 想定外の値が入っていたことを知らせる）。
        /// </summary>
        internal static int NormalizeAutoSnapshotRetention(int value, bool warnIfChanged)
        {
            if (value >= 1) return value;

            if (warnIfChanged)
            {
                Debug.LogWarning($"[AddressTeller] AutoSnapshotRetention in the settings file was {value}, which is below the minimum of 1. Using 1 instead; this load does not rewrite the file, so the value on disk stays {value} until something else saves a change to the settings.");
            }

            return 1;
        }

        /// <summary>
        /// <see cref="AddressTellerSettings.SnapshotFolder"/> の空文字・空白のみの値を既定値
        /// （<see cref="AddressTellerSettings.DefaultSnapshotFolder"/>）へ正規化する処理を1箇所に集約する。
        /// setter（<see cref="AddressTellerSettings.SnapshotFolder"/>）と、設定ファイル読み込み直後
        /// （<see cref="EnsureLoaded"/>）の両方から使う——<see cref="NormalizeAutoSnapshotRetention"/> と同じ
        /// 理由（基準を2箇所に持つと将来どちらかだけが追従せず食い違う恐れがある）。空文字・空白のみを
        /// 正規化対象とするのは、それ以外の値（相対パス・絶対パス）はプロジェクトルート外への解決を
        /// <see cref="AddressTellerSettings.GetSnapshotFolderAbsolutePath"/> 側で既に防いでおり、ここで
        /// 追加の検証は不要なため。<paramref name="warnIfChanged"/> が true で正規化が働いた場合のみ
        /// Warning を1本出す。setter からの呼び出しは false（利用者が直接指定した値をその場で正規化する
        /// 通常の挙動であり、毎回警告を出すのは過剰）。読み込み時は true（設定ファイルという外部入力に
        /// 想定外の値が入っていたことを知らせる）。
        /// </summary>
        internal static string NormalizeSnapshotFolder(string value, bool warnIfChanged)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value;

            if (warnIfChanged)
            {
                Debug.LogWarning($"[AddressTeller] SnapshotFolder in the settings file was empty or whitespace-only. Using '{AddressTellerSettings.DefaultSnapshotFolder}' instead; this load does not rewrite the file, so the value on disk stays unchanged until something else saves a change to the settings.");
            }

            return AddressTellerSettings.DefaultSnapshotFolder;
        }

        /// <summary>
        /// 直近の <see cref="EnsureLoaded"/> 呼び出しでファイルを実際に読み込んだ時点の
        /// (更新日時, サイズ)。null は「まだ一度もファイルから読み込んでいない」
        /// （ファイル不在、またはドメインリロード直後で未読込）ことを表す。
        /// </summary>
        private static (DateTime lastWriteUtc, long length)? s_loadedStamp;

        /// <summary>
        /// このドメインで <see cref="Current"/> 経由の遅延ロードを既に試みたかどうか。
        /// <see cref="EnsureLoaded"/> を明示的に呼ぶバッチ入口を経由しない読み取り（例えば利用者が
        /// <c>AddressTellerService</c> の公開 API を独自のエディタ拡張から直接呼ぶ経路）でも、
        /// ドメインリロード直後の初回アクセスでは設定ファイルの内容を反映させるためのフラグ。
        /// </summary>
        private static bool s_attemptedInitialLoadThisDomain;

        /// <summary>
        /// テスト用シーム。設定ファイルの絶対パスを差し替える。null なら既定のプロジェクトパスを使う。
        /// AddressTellerAddressablesPollutionGuard（Tests/Editor 配下の [SetUpFixture]）がテスト実行中
        /// だけ一時フォルダへ切り替えることで、テストが本番の ProjectSettings/AddressTellerSettings.json
        /// に一切触れないようにする（AddressTellerApplyFlow.s_notifyApplyAborted と同じ
        /// 「テスト用シーム」の考え方）。
        /// </summary>
        internal static string FilePathOverride;

        /// <summary>設定ファイルの絶対パス。<see cref="FilePathOverride"/> が優先される。</summary>
        internal static string AbsoluteFilePath
        {
            get
            {
                if (FilePathOverride != null) return FilePathOverride;

                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                return Path.GetFullPath(Path.Combine(projectRoot, "ProjectSettings", "AddressTellerSettings.json"));
            }
        }

        /// <summary>
        /// 現在メモリ上にある設定値。<see cref="EnsureLoaded"/> を経由しないアクセスでも、このドメインで
        /// 一度も読み込みを試みていなければ、ここで最初の1回だけ遅延ロードを試みる——「バッチ入口で
        /// 1回」という設計は*再読み込みの契機*の話であり、*最初の1回*まで呼び出し元任せにする必要は
        /// ない。ドメインリロード直後、まだどのバッチ入口も通っていない状態で
        /// <c>AddressTellerService</c> の公開 API を直接呼ばれた場合に既定値のまま動いてしまう
        /// （設定ファイルの内容を無視してしまう）事故を防ぐ。
        /// </summary>
        internal static Data Current
        {
            get
            {
                if (!s_attemptedInitialLoadThisDomain)
                {
                    s_attemptedInitialLoadThisDomain = true;
                    // 失敗時のログはここでは出さない。ここでの責務は「ゲートを経由しない経路でも
                    // 初回だけは読み込みを試みる」ことに限定し、エラー報告はバッチ入口のゲート
                    // （AddressTellerSettings.EnsureLoaded()）に一本化する。読み込みに失敗した場合、
                    // EnsureLoaded() は s_loadedStamp を更新しないため、後で実際にバッチ入口を
                    // 通った際には同じ失敗が再度検出され、そこで正しく報告される。
                    EnsureLoaded();
                }
                return s_data;
            }
        }

        /// <summary>
        /// メモリ上の値と読み込み状態を初期値へ戻す。<see cref="FilePathOverride"/> の切り替え直後に
        /// 呼び、次回のアクセス（<see cref="Current"/> 経由の遅延ロード、または明示的な
        /// <see cref="EnsureLoaded"/> 呼び出し）でファイルを無条件に読み直させる。
        /// </summary>
        internal static void ResetInMemoryState()
        {
            s_data = new Data();
            s_loadedStamp = null;
            s_attemptedInitialLoadThisDomain = false;
        }

        /// <summary>
        /// 設定ファイルが前回の読み込みから変化していれば読み直す（読み込み段。読み込み失敗自体はログしない
        /// ——ログ段は呼び出し元の <see cref="AddressTellerSettings.EnsureLoaded"/> の責務。ただし読み込みに
        /// 成功した値を正規化する過程で <see cref="NormalizeAutoSnapshotRetention"/>・
        /// <see cref="NormalizeSnapshotFolder"/> が Warning を1本出すことがある——これは「読み込み失敗の
        /// 報告」ではなく「読み込んだ値そのものへの是正の通知」であり、ログ段とは責務が異なる）。判定は
        /// (LastWriteTimeUtc, Length) の比較のみで行う（内容のハッシュ等は取らない）。バッチ
        /// （Apply/Validate/Preview/Explain/各 CLI コマンド/Postprocessor の1回の
        /// OnPostprocessAllAssets/Project Settings ページの activate）ごとに1回、入口で呼ぶことを
        /// 想定している。
        /// ファイルが存在しない場合は初回起動（または読み込み後の削除）として <see cref="s_data"/> を
        /// 既定値へ揃え、<see cref="SettingsGateResult.Success"/>=true・<see cref="SettingsGateResult.FileExists"/>=false
        /// を返す（設計上「ファイル不在＝既定値」で一貫させるため）。
        /// ファイルは存在するが読めない、またはマーカーが一致しない場合は Success=false・
        /// <see cref="SettingsGateResult.Error"/> に理由を設定して返す——この場合 <see cref="s_data"/> は
        /// 変更しない（呼び出し元がこの戻り値を見て処理を中止するため、古い値のまま破壊的操作が進むことはない）。
        /// 失敗時は <see cref="s_loadedStamp"/> も更新しないため、ファイルの内容が壊れたままである限り、
        /// この呼び出し（＝ファイルの読み込み・パース自体）は毎回実際に発生する——「変化なしなら
        /// 読み直さない」ショートカットは成功時にしか効かない。上位の
        /// <see cref="AddressTellerSettings.EnsureLoaded"/> が OncePerDistinctFailure で抑制するのは
        /// あくまで*ログの出力*だけであり、この読み込み段自体は毎回実行される。
        /// </summary>
        internal static SettingsGateResult EnsureLoaded()
        {
            // AbsoluteFilePath は Application.dataPath から組み立てるライブラリ内部完結の値であり、
            // FilePathOverride も内部のテスト専用シームで利用者入力ではないため、
            // ここを利用者向けエラーに変換する防御は設けない（万一の異常はスタックトレースの方が
            // 情報量が多い）。
            var info = new FileInfo(AbsoluteFilePath);

            if (!info.Exists)
            {
                // ファイルがまだ無い（初回起動、または一度読み込んだ後にファイルが削除された）。
                // 「ファイル不在＝既定値」で一貫させるため、既存の値を保持せず既定値へ揃える。
                s_data = new Data();
                s_loadedStamp = null;
                return new SettingsGateResult(success: true, error: null, fileExists: false, default, 0);
            }

            var stamp = (info.LastWriteTimeUtc, info.Length);
            if (s_loadedStamp.HasValue && s_loadedStamp.Value == stamp)
                // 前回読み込み時から変化なし。再読み込み不要。
                return new SettingsGateResult(success: true, error: null, fileExists: true, stamp.LastWriteTimeUtc, stamp.Length);

            string text;
            try
            {
                text = File.ReadAllText(info.FullName);
            }
            catch (Exception ex)
            {
                var error = $"AddressTeller could not read its settings file at '{info.FullName}' " +
                    $"({ex.GetType().Name}: {ex.Message}).";
                return new SettingsGateResult(success: false, error, fileExists: true, info.LastWriteTimeUtc, info.Length);
            }

            Data parsed;
            try
            {
                parsed = JsonUtility.FromJson<Data>(text);
            }
            catch (Exception)
            {
                parsed = null;
            }

            if (parsed == null || parsed._marker != MarkerValue)
            {
                var error = $"'{info.FullName}' does not look like an AddressTeller settings file, or is " +
                    "corrupted. Fix or delete it, then reopen Project Settings > AddressTeller to " +
                    "re-enter your values.";
                return new SettingsGateResult(success: false, error, fileExists: true, info.LastWriteTimeUtc, info.Length);
            }

            // ファイルは手編集され得る外部入力のため、setter のクランプ（Mathf.Max(1, value)）を経由せずに
            // 下限未満の値がそのまま読み込まれる余地がある。ここでメモリ上の値だけを正規化し、
            // ファイルへは書き戻さない（この読み込みが副作用としてファイルを書き換えないため）。
            parsed._autoSnapshotRetention = NormalizeAutoSnapshotRetention(parsed._autoSnapshotRetention, warnIfChanged: true);
            parsed._snapshotFolder = NormalizeSnapshotFolder(parsed._snapshotFolder, warnIfChanged: true);

            s_data = parsed;
            s_loadedStamp = stamp;
            return new SettingsGateResult(success: true, error: null, fileExists: true, stamp.LastWriteTimeUtc, stamp.Length);
        }

        /// <summary>
        /// 設定値への全ての変更が経由する唯一の書き込み口。ゲート（<see cref="EnsureLoaded"/>）を通し、
        /// 成功していれば現在値の複製に <paramref name="mutate"/> を適用してからディスクへ書き込み、
        /// 書き込みが成功した場合のみメモリ上の値をその複製へ差し替える。
        /// 複製に対して変更を行うため、書き込みが失敗した場合（<see cref="File.WriteAllText"/> が例外を
        /// 投げた場合）でも <see cref="s_data"/>（呼び出し元から見える現在値）は変更前のまま残る——
        /// 「ディスクは書き込み前のまま、メモリだけ新しい値が残る」という食い違いを構造的に起こさせない。
        /// ゲートが失敗している場合（ファイルが存在するが読めない、またはマーカー不一致）は
        /// <see cref="InvalidOperationException"/> を投げ、変更を一切適用しない——壊れたファイルの上に
        /// メモリ上の値だけを書き足して上書きしてしまう事故を防ぐため。
        /// <paramref name="mutate"/> の適用結果が変更前の値と完全に一致する場合（JSON テキストとして比較）
        /// は書き込み自体を省略する——「同じ値を再代入しても書き込まれない」という各プロパティ setter の
        /// 既存契約を、個々の setter ではなくここに1箇所へ集約するため。この比較は <see cref="Data._marker"/>
        /// を <see cref="s_data"/> と揃えた状態（<see cref="Clone"/> がそのままコピーする）で行い、
        /// <see cref="MarkerValue"/> の確定は比較の後・書き込みの直前に行う——先に確定させてしまうと、
        /// 設定ファイルがまだ存在しない状態（<c>s_data._marker == null</c>）で全ての値を既定値のまま
        /// 代入しても、マーカーの有無だけで「変更あり」と誤検出し、変更が無いのにファイルを作ってしまう
        /// （「ファイルが無ければ既定値のまま動き、実際に値を変えた時だけファイルを作る」という設計上の
        /// 契約に反する）。
        /// </summary>
        internal static void Mutate(Action<Data> mutate)
        {
            if (mutate == null) throw new ArgumentNullException(nameof(mutate));

            var gate = EnsureLoaded();
            if (!gate.Success)
                throw new InvalidOperationException(
                    $"AddressTeller settings could not be loaded, so this change was not saved: {gate.Error}");

            var before = JsonUtility.ToJson(s_data);

            var next = Clone(s_data);
            mutate(next);

            if (JsonUtility.ToJson(next) == before) return;

            next._marker = MarkerValue;

            var path = AbsoluteFilePath;
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(next, true));
            }
            catch
            {
                // 書き込みに失敗した場合、next は s_data とは別のインスタンスであり、ここまで s_data を
                // 差し替えていないため、メモリ上の値は変更前のまま保たれる。次回の EnsureLoaded() が
                // ファイルの実際の状態を確実に読み直せるよう、念のためスタンプは無効化しておく。
                s_loadedStamp = null;
                throw;
            }

            s_data = next;
            var info = new FileInfo(path);
            s_loadedStamp = (info.LastWriteTimeUtc, info.Length);
        }

        /// <summary>
        /// <see cref="Data"/> のフィールド単位の複製を返す（参照型フィールドである
        /// <see cref="Data._disabledRuleClassNames"/> も独立したリストへコピーする）。フィールドを
        /// 増減した場合は <see cref="Mutate"/> の remarks 参照先である Documentation~/operations.md/.ja.md
        /// の設定一覧表、Tests/Editor/AddressTellerSettingsPersistenceTests.cs のフィールド数 assert と
        /// 同時にここも更新すること。
        /// </summary>
        private static Data Clone(Data source) => new()
        {
            _cleanupStaleEntries = source._cleanupStaleEntries,
            _postprocessEnabled = source._postprocessEnabled,
            _snapshotFolder = source._snapshotFolder,
            _autoSnapshotBeforeApplyAll = source._autoSnapshotBeforeApplyAll,
            _autoSnapshotRetention = source._autoSnapshotRetention,
            _autoCreateMissingGroups = source._autoCreateMissingGroups,
            _postprocessOrder = source._postprocessOrder,
            _disabledRuleClassNames = new List<string>(source._disabledRuleClassNames),
            _marker = source._marker,
        };

        /// <summary>
        /// Project Settings の「壊れたファイルを .bak 退避して既定値で作り直す」ボタンから呼ばれる復旧処理。
        /// ファイルがそもそも存在しない場合は復旧の必要が無いため何もせず false を返す（呼び出し元の
        /// Project Settings はゲート失敗中のみボタンを表示するため、通常この分岐には来ない防御）——
        /// 「ファイル不在＝既定値」は壊れた状態ではないため、これはゲート（<see cref="EnsureLoaded"/>）の
        /// 再確認より先に判定する。
        /// ファイルが存在する場合は、ゲートを再確認し、既に成功する状態（＝ボタン表示後、クリックまでの
        /// 間に誰か・何かがファイルを直した）であれば、ファイルには一切触れず true を返す。
        /// それ以外の場合は、現在のファイルをタイムスタンプ付きの名前（
        /// "&lt;元のパス&gt;.&lt;yyyyMMdd-HHmmss-fff&gt;.bak"）へコピーしてから、既定値の新しい設定
        /// ファイルへ書き直す。タイムスタンプ付きにしているのは、復旧ボタンを複数回押した場合に
        /// 1回目の退避ファイルを2回目が上書きして消してしまわないようにするため。
        /// <see cref="Mutate"/> と異なりゲートを要求しない——この関数自体がゲート失敗中（壊れたファイル）を
        /// 復旧するためのものであるため。
        /// </summary>
        internal static bool TryRecoverFromBrokenFile(out string error)
        {
            var path = AbsoluteFilePath;
            if (!File.Exists(path))
            {
                error = "No settings file exists to recover.";
                return false;
            }

            // ボタンが表示されてからクリックされるまでの間に、既に壊れたファイルが直っている
            // （他のツール・手動編集等）可能性がある。その場合は壊れていないファイルへ触れる理由が
            // 無いため、何もせず「既に読める」として成功を返す。ファイルが存在することを確認した
            // *後*にこの再判定を行う——EnsureLoaded はファイル不在でも Success=true を返す
            // （「ファイル不在＝既定値」で正常扱い）ため、先に判定してしまうとファイルが無い場合まで
            // 「復旧不要」として誤って true を返してしまう。
            if (EnsureLoaded())
            {
                error = null;
                return true;
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
            var backupPath = $"{path}.{timestamp}.bak";
            try
            {
                File.Copy(path, backupPath, overwrite: false);
            }
            catch (Exception ex)
            {
                error = $"Failed to back up the broken settings file to '{backupPath}' ({ex.GetType().Name}: {ex.Message}).";
                return false;
            }

            var fresh = new Data { _marker = MarkerValue };
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(fresh, true));
            }
            catch (Exception ex)
            {
                error = $"Backed up the broken file to '{backupPath}', but failed to write a fresh settings file ({ex.GetType().Name}: {ex.Message}).";
                return false;
            }

            s_data = fresh;
            var info = new FileInfo(path);
            s_loadedStamp = (info.LastWriteTimeUtc, info.Length);
            error = null;
            return true;
        }
    }
}
