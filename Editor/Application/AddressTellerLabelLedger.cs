using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// AddressTeller が Addressables のエントリへ書いたラベルの名前を記録する台帳
    /// （ProjectSettings/AddressTellerLabelLedger.json）。<see cref="AddressTellerSettings.RemoveStaleManagedLabels"/>
    /// が「このラベルは AddressTeller が付けたもの」と判別するための唯一の根拠で、台帳にある名前だけが
    /// 除去の対象になる（台帳に無いラベルには触れない）。台帳に載るのは名前だけで、どのエントリに付けたかは
    /// 記録しない——接頭辞のような命名規約を利用者に課さずに「AddressTeller が書いたラベル名」を特定するための割り切りで、
    /// 同名のラベルを利用者が手で付けた場合も、AddressTeller の書き込み対象エントリ上では除去の対象になる。
    /// 読み込みは設定ファイル（<see cref="AddressTellerSettingsAsset"/>）と同じゲート方針で、ファイルが存在しなければ
    /// 空の台帳、存在して読めない／マーカーが一致しなければ失敗として呼び出し元を止める
    /// （壊れた台帳の上に書き足さない）。ファイルは名前を Ordinal 順に並べて書き、版管理での差分を安定させる。
    /// </summary>
    internal static class AddressTellerLabelLedger
    {
        /// <summary>
        /// このファイルが AddressTeller のラベル台帳であることを示すマーカー値。<see cref="Data._marker"/> に
        /// フィールド初期化子を持たせない理由は <see cref="AddressTellerSettingsAsset.MarkerValue"/> と同じ。
        /// </summary>
        internal const string MarkerValue = "addressteller-label-ledger-v1";

        /// <summary>保存対象の POCO。</summary>
        [Serializable]
        internal sealed class Data
        {
            [SerializeField] internal string _marker;
            // 初期化子を持たせない（欠落を壊れた台帳として弾くため。_marker と同じ理由）。
            [SerializeField] internal List<string> labels;
        }

        // 台帳の中身（Ordinal 比較）。ゲート（EnsureLoaded）が成功するたびにファイルの内容へ揃う。
        private static HashSet<string> s_labels = new(StringComparer.Ordinal);

        // 直近の EnsureLoaded でファイルを実際に読み込んだ時点の (更新日時, サイズ)。null は未読込またはファイル不在。
        private static (DateTime lastWriteUtc, long length)? s_loadedStamp;

        // このドメインで遅延ロードを既に試みたか（AddressTellerSettingsAsset.Current と同じ考え方。
        // ゲートを経由しない直接呼び出しでも、最初の1回は台帳の内容を反映させる）。
        private static bool s_attemptedInitialLoadThisDomain;

        /// <summary>
        /// テスト用シーム。台帳ファイルの絶対パスを差し替える。null なら既定のプロジェクトパスを使う
        /// （<see cref="AddressTellerSettingsAsset.FilePathOverride"/> と同じ考え方）。
        /// </summary>
        internal static string FilePathOverride;

        /// <summary>
        /// テスト用シーム。台帳ファイルの書き込み処理（パス, 内容）。書き込み失敗（read-only の Perforce 管理下など）を
        /// 注入して検証するために差し替える。テストは差し替え後、必ず既定値へ戻すこと。
        /// </summary>
        internal static Action<string, string> WriteFile = (path, text) => File.WriteAllText(path, text);

        // --- ラン単位の記録 ---
        // ApplyAll の1回のランの間は、除去判定に使う「管理済み集合」をラン開始時の台帳で固定し（s_runBaseline）、
        // そのランで書いたラベル名は s_runPending に溜めてラン末尾に1回だけファイルへ書く。固定しないと、同じランで
        // 先に処理したアセットが新たに台帳入りさせた名前が、後続のアセットで除去対象になってしまい（走査順に依存し、
        // 台帳を更新しない Predict とも食い違う）。
        private static bool s_inRun;
        private static HashSet<string> s_runBaseline;
        private static HashSet<string> s_runPending;

        // 台帳へ書けるかどうかは、除去の正しさと独立している（除去はラン開始時の台帳だけに基づく）。書けなかった場合に
        // 失うのは「今回初めて書いた名前の記録」だけで、それは除去が減る側（安全側）なので、除去は止めない。

        /// <summary>台帳ファイルの絶対パス。<see cref="FilePathOverride"/> が優先される。</summary>
        internal static string AbsoluteFilePath
        {
            get
            {
                if (FilePathOverride != null) return FilePathOverride;

                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                return Path.GetFullPath(Path.Combine(projectRoot, "ProjectSettings", "AddressTellerLabelLedger.json"));
            }
        }

        /// <summary>メモリ上の台帳と読み込み状態を初期値へ戻す。<see cref="FilePathOverride"/> の切り替え直後に呼ぶ。</summary>
        internal static void ResetInMemoryState()
        {
            s_labels = new HashSet<string>(StringComparer.Ordinal);
            s_loadedStamp = null;
            s_attemptedInitialLoadThisDomain = false;
            s_inRun = false;
            s_runBaseline = null;
            s_runPending = null;
        }

        /// <summary>
        /// ラン（ApplyAll 1回）を開始する。ここでの台帳の内容を、このランの除去判定に使う管理済み集合として固定し、
        /// 以降の <see cref="Record"/> はラン末尾の <see cref="EndRun"/> まで溜める。
        /// </summary>
        internal static void BeginRun()
        {
            LoadOnceIfNeeded();
            s_runBaseline = new HashSet<string>(s_labels, StringComparer.Ordinal);
            s_runPending = new HashSet<string>(StringComparer.Ordinal);
            s_inRun = true;
        }

        /// <summary>
        /// ランを終え、溜めたラベル名を台帳ファイルへ1回で書く。ラン中でなければ何もしない。
        /// 書けなかった場合は例外にせず、理由を返す（呼び出し元が blocking な結果として報告する）。成功時は null。
        /// 書き込みはファイル側の最新内容との和集合になる（<see cref="Mutate"/>）。
        /// </summary>
        internal static string EndRun()
        {
            if (!s_inRun) return null;

            var pending = s_runPending;
            s_inRun = false;
            s_runBaseline = null;
            s_runPending = null;

            if (pending.Count == 0) return null;

            try
            {
                Mutate(set =>
                {
                    foreach (var label in pending)
                        set.Add(label);
                });
                return null;
            }
            catch (Exception ex)
            {
                return $"AddressTeller could not record the labels it wrote ({string.Join(", ", pending.OrderBy(l => l, StringComparer.Ordinal))}) " +
                    $"in its label ledger at '{AbsoluteFilePath}' ({ex.GetType().Name}: {ex.Message}). " +
                    "The labels were written to the entries, but they are not remembered as AddressTeller's yet, so they will not be removed as stale until a later run records them. Make the file writable (for example, check it out in your version control system) and run Apply again.";
            }
        }

        /// <summary>
        /// ラベル名が台帳にあるか（AddressTeller が書いたことがあるか）。ゲートを経由しない呼び出しでも、
        /// このドメインでの最初の1回は台帳を読み込む。読み込みに失敗した場合は空の台帳として扱う
        /// （何も除去しない側に倒れる）。失敗の報告はゲート（<see cref="EnsureLoaded"/>）の責務。
        /// </summary>
        internal static bool IsManaged(string label)
        {
            LoadOnceIfNeeded();
            return (s_inRun ? s_runBaseline : s_labels).Contains(label);
        }

        /// <summary>台帳のラベル名を Ordinal 順で返す（呼び出しごとに新しい配列）。</summary>
        internal static string[] GetAll()
        {
            LoadOnceIfNeeded();
            return s_labels.OrderBy(l => l, StringComparer.Ordinal).ToArray();
        }

        private static void LoadOnceIfNeeded()
        {
            if (s_attemptedInitialLoadThisDomain) return;
            s_attemptedInitialLoadThisDomain = true;
            // 失敗時のログはここでは出さない（バッチ入口のゲートに一本化する。AddressTellerSettingsAsset.Current と同じ）。
            EnsureLoaded();
        }

        /// <summary>
        /// 台帳ファイルが前回の読み込みから変化していれば読み直す（読み込み段。失敗はログしない）。
        /// ファイルが存在しなければ空の台帳として成功を返す。存在して読めない、またはマーカー／必須フィールドが
        /// 不正な場合は失敗を返し、メモリ上の台帳は変更しない（呼び出し元が止まるため、古い内容のまま
        /// 破壊的操作が進むことはない）。失敗時はスタンプを更新しないので、壊れたままの間は毎回読み直す。
        /// </summary>
        internal static SettingsGateResult EnsureLoaded()
        {
            s_attemptedInitialLoadThisDomain = true;

            var info = new FileInfo(AbsoluteFilePath);

            if (!info.Exists)
            {
                s_labels = new HashSet<string>(StringComparer.Ordinal);
                s_loadedStamp = null;
                return new SettingsGateResult(success: true, error: null, fileExists: false, default, 0);
            }

            var stamp = (info.LastWriteTimeUtc, info.Length);
            if (s_loadedStamp.HasValue && s_loadedStamp.Value == stamp)
                return new SettingsGateResult(success: true, error: null, fileExists: true, stamp.LastWriteTimeUtc, stamp.Length);

            string text;
            try
            {
                text = File.ReadAllText(info.FullName);
            }
            catch (Exception ex)
            {
                var error = $"AddressTeller could not read its label ledger at '{info.FullName}' " +
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

            // labels の欠落は、JsonUtility が空リストを補うかどうかに依らず検出したいので、テキストにキーがあることも確かめる
            // （キーが無いファイルは AddressTeller が書いたものではなく、空の台帳として読むと名前を黙って失うため）。
            if (parsed == null || parsed._marker != MarkerValue || parsed.labels == null || !text.Contains("\"labels\""))
            {
                var error = $"'{info.FullName}' does not look like an AddressTeller label ledger, or is corrupted. " +
                    "Fix or delete it (deleting it makes AddressTeller forget which labels it wrote, so it will not " +
                    "remove any existing label until it writes them again).";
                return new SettingsGateResult(success: false, error, fileExists: true, info.LastWriteTimeUtc, info.Length);
            }

            s_labels = new HashSet<string>(parsed.labels.Where(l => !string.IsNullOrEmpty(l)), StringComparer.Ordinal);
            s_loadedStamp = stamp;
            return new SettingsGateResult(success: true, error: null, fileExists: true, stamp.LastWriteTimeUtc, stamp.Length);
        }

        /// <summary>
        /// AddressTeller がこれから書くラベルを台帳へ記録する。呼び出し側はエントリへ書く<b>前</b>に呼ぶ
        /// （書いたのに台帳に無い、という状態を作らないため）。全ラベルが既に台帳にあれば何もせず、
        /// ファイルにも触れない（毎アセット呼ばれるため）。差分があるときだけ1回で書き込む。
        /// </summary>
        /// <exception cref="InvalidOperationException">台帳が読み込めない状態（壊れたファイルの上に書き足さない）。</exception>
        internal static void Record(IEnumerable<string> labels)
        {
            LoadOnceIfNeeded();

            if (s_inRun)
            {
                // ラン中は溜めるだけ（ファイルへはラン末尾の EndRun が書く。除去判定の管理済み集合も変えない）。
                foreach (var label in labels)
                    if (!string.IsNullOrEmpty(label) && !s_runBaseline.Contains(label))
                        s_runPending.Add(label);
                return;
            }

            List<string> missing = null;
            foreach (var label in labels)
            {
                if (string.IsNullOrEmpty(label) || s_labels.Contains(label)) continue;
                missing ??= new List<string>();
                missing.Add(label);
            }

            if (missing == null) return;

            Mutate(set =>
            {
                foreach (var label in missing)
                    set.Add(label);
            });
        }

        /// <summary>
        /// 台帳から名前を取り除く（そのラベル定義を Addressables から削除したときに呼ぶ）。
        /// 台帳に無い名前は無視する。
        /// </summary>
        /// <exception cref="InvalidOperationException">台帳が読み込めない状態。</exception>
        internal static void Forget(IEnumerable<string> labels)
        {
            LoadOnceIfNeeded();
            Mutate(set =>
            {
                foreach (var label in labels)
                    set.Remove(label);
            });
        }

        /// <summary>
        /// 台帳への唯一の書き込み口。ゲートを通し、現在の内容の複製へ変更を適用して、変化があるときだけ
        /// ファイルへ書く。書き込みに成功した場合のみメモリ上の台帳を差し替える
        /// （<see cref="AddressTellerSettingsAsset.Mutate"/> と同じ契約）。
        /// </summary>
        private static void Mutate(Action<HashSet<string>> mutate)
        {
            var gate = EnsureLoaded();
            if (!gate.Success)
                throw new InvalidOperationException(
                    $"AddressTeller's label ledger could not be loaded, so this change was not saved: {gate.Error}");

            var next = new HashSet<string>(s_labels, StringComparer.Ordinal);
            mutate(next);
            if (next.SetEquals(s_labels)) return;

            var data = new Data { _marker = MarkerValue, labels = next.OrderBy(l => l, StringComparer.Ordinal).ToList() };

            var path = AbsoluteFilePath;
            try
            {
                WriteFile(path, JsonUtility.ToJson(data, true));
            }
            catch
            {
                // 書き込みに失敗してもメモリは変更前のまま。次回の EnsureLoaded が実際の状態を読み直せるようスタンプを無効化する。
                s_loadedStamp = null;
                throw;
            }

            s_labels = next;
            var info = new FileInfo(path);
            s_loadedStamp = (info.LastWriteTimeUtc, info.Length);
        }
    }
}
