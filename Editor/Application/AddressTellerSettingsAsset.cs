using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AddressTeller.Editor
{
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
            [SerializeField] internal bool _cleanupStaleEntries = true;
            [SerializeField] internal bool _postprocessEnabled = true;
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
        /// （<see cref="EnsureLoaded(out string)"/>）の両方から使う——基準を2箇所に持つと将来どちらかだけが
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
                    // EnsureLoaded(out string) は s_loadedStamp を更新しないため、後で実際にバッチ入口を
                    // 通った際には同じ失敗が再度検出され、そこで正しく報告される。
                    EnsureLoaded(out _);
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
        /// 設定ファイルが前回の読み込みから変化していれば読み直す。判定は (LastWriteTimeUtc, Length) の
        /// 比較のみで行う（内容のハッシュ等は取らない）。バッチ（Apply/Validate/Preview/Explain/各 CLI
        /// コマンド/Postprocessor の1回の OnPostprocessAllAssets/Project Settings ページの activate）
        /// ごとに1回、入口で呼ぶことを想定している。
        /// ファイルが存在しない場合は初回起動（または読み込み後の削除）として無言で true を返し、
        /// <see cref="s_data"/> を既定値へ揃える（設計上「ファイル不在＝既定値」で一貫させるため）。
        /// ファイルは存在するが読めない、またはマーカーが一致しない場合は false を返し、
        /// <paramref name="error"/> に理由を設定する——この場合 <see cref="s_data"/> は変更しない
        /// （呼び出し元がこの戻り値を見て処理を中止するため、古い値のまま破壊的操作が進むことはない）。
        /// </summary>
        internal static bool EnsureLoaded(out string error)
        {
            error = null;

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
                return true;
            }

            var stamp = (info.LastWriteTimeUtc, info.Length);
            if (s_loadedStamp.HasValue && s_loadedStamp.Value == stamp)
                return true; // 前回読み込み時から変化なし。再読み込み不要。

            string text;
            try
            {
                text = File.ReadAllText(info.FullName);
            }
            catch (Exception ex)
            {
                error = $"AddressTeller could not read its settings file at '{info.FullName}' " +
                    $"({ex.GetType().Name}: {ex.Message}).";
                return false;
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
                error = $"'{info.FullName}' does not look like an AddressTeller settings file, or is " +
                    "corrupted. Fix or delete it, then reopen Project Settings > AddressTeller to " +
                    "re-enter your values.";
                return false;
            }

            // ファイルは手編集され得る外部入力のため、setter のクランプ（Mathf.Max(1, value)）を経由せずに
            // 下限未満の値がそのまま読み込まれる余地がある。ここでメモリ上の値だけを正規化し、
            // ファイルへは書き戻さない（この読み込みが副作用としてファイルを書き換えないため）。
            parsed._autoSnapshotRetention = NormalizeAutoSnapshotRetention(parsed._autoSnapshotRetention, warnIfChanged: true);

            s_data = parsed;
            s_loadedStamp = stamp;
            return true;
        }

        /// <summary>
        /// 現在メモリにある値を JSON として書き出す。一時ファイル経由の置換（書き込み後のリネーム等）は
        /// 行わない——設定ファイルは高々1KB程度で、書き込み途中でプロセスが落ちた場合の損失は値を
        /// 入れ直すだけで済むため、そのための機構は設けない。書き込みで例外が発生した場合はそのまま
        /// 呼び出し元（各設定プロパティの setter）へ伝播させる。
        /// </summary>
        internal static void SaveChanges()
        {
            s_data._marker = MarkerValue;
            var path = AbsoluteFilePath;

            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(s_data, true));
            }
            catch
            {
                // 書き込みに失敗した時点で s_data は既に呼び出し元（各設定プロパティの setter）が
                // 新しい値へ書き換え済みだが、ディスク上のファイルはそれを反映できていない
                // （書き込み前の内容のまま、または中途半端な内容）。スタンプをここで無効化しないと、
                // 次回の EnsureLoaded() が「前回読み込み時からファイルは変化していない」と誤判定し、
                // このセッションが終わるまでメモリとディスクの乖離に気づけなくなる。無効化しておけば、
                // 次回 EnsureLoaded() が必ずファイルの実際の状態を読み直す。
                s_loadedStamp = null;
                throw;
            }

            // 自分で書いた変更なので、次回 EnsureLoaded が同じ内容を無駄に読み直さないよう
            // スタンプを更新しておく。
            var info = new FileInfo(path);
            s_loadedStamp = (info.LastWriteTimeUtc, info.Length);
        }
    }
}
