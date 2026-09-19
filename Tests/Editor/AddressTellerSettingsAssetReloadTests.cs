using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettingsAsset (ScriptableSingleton) の読み直し方式（既存インスタンスを破棄してから
    /// <c>instance</c> に再アクセスし、Unity 自身にディスクから読み直させる方式）が実際に成立することを
    /// 検証する回帰テスト。
    /// 以前はこの方式が本当に成立するかどうかを実測で確かめる計測目的のテストだった。現在は本番実装
    /// （<see cref="AddressTellerSettingsAsset"/> / <see cref="AddressTellerSettings.ReloadFromDisk"/>）が
    /// この方式へ差し替わっているため、以後の回帰を検知する固定テストとして残す。
    /// </summary>
    public class AddressTellerSettingsAssetReloadTests
    {
        private string _path;
        private string _backupPath;
        private string _originalDiskText;
        private int _originalPostprocessOrder;

        [SetUp]
        public void SetUp()
        {
            _path = AddressTellerSettingsAsset.GetAbsoluteFilePath();
            // このテストクラス専用の接尾辞にする。AddressTellerSettingsPersistenceTests.cs も同じ
            // ProjectSettings/AddressTellerSettings.asset に対してバックアップ方式の後始末を行っており、
            // 接尾辞が同じだと片方が残した .bak を他方が誤って掴んだり、File.Move の宛先重複で
            // 分かりにくい例外を起こしたりしうるため、クラスごとに分ける。
            _backupPath = _path + ".reload.bak";

            if (File.Exists(_backupPath))
            {
                // 前回のこのテストクラスの実行が TearDown を通らずに終わった痕跡（クラッシュ・強制終了・
                // ドメインリロード等による中断）。バックアップから復元してから続行する。
                Debug.LogWarning("[AddressTeller] 前回のテスト実行が正常終了しなかった痕跡" +
                    $"（バックアップファイル '{_backupPath}' の残存）を検出したため、そこから復元します。");
                File.Copy(_backupPath, _path, true);
                File.Delete(_backupPath);
            }

            if (!File.Exists(_path))
            {
                // ProjectSettings/AddressTellerSettings.asset は ScriptableSingleton の初回 Save まで
                // 書き出されないため、一度も設定を保存していない新規プロジェクト（クリーンな clone の
                // CI 等）には存在しない。環境要因のためスキップする（本リポジトリの既存の慣習。
                // Tests/Editor/AddressTellerMenuTests.cs 等を参照）。
                Assert.Ignore("ProjectSettings/AddressTellerSettings.asset がまだ保存されていない環境の" +
                    "ため、このテストはスキップします（初回保存前の新規プロジェクト等）。");
            }

            // テスト実行の中断（クラッシュ・強制終了・ドメインリロード等）に備え、元の内容をディスク上の
            // バックアップファイルとしても残しておく。_originalDiskText（プロセスメモリ上の変数）だけに
            // 頼ると、TearDown が実行される前にプロセスが落ちた場合、元の内容を完全に失ってしまう。
            // 元のファイルは削除せずそのまま残し、バックアップはコピーとして別に作る（File.Move で
            // 一時的にファイルを消す方式は、その間だけ実ファイルが存在しない状態を作ってしまうため
            // 採らない）。
            File.Copy(_path, _backupPath, true);

            _originalDiskText = File.ReadAllText(_path);
            _originalPostprocessOrder = AddressTellerSettingsAsset.instance._postprocessOrder;
        }

        [TearDown]
        public void TearDown()
        {
            if (_originalDiskText == null)
            {
                // _originalDiskText が null のまま TearDown に入るケースは2通りある。
                // (a) SetUp が Assert.Ignore で早期終了した場合（_backupPath はまだ作られていない）。
                // (b) SetUp が File.Copy でバックアップを作った後、File.ReadAllText 等で例外を投げて
                //     終わった場合（バックアップだけが残る）。NUnit は SetUp が例外で終わっても
                //     TearDown を実行するため、ここに来る。
                // (a) は後始末不要だが、(b) はバックアップを放置すると開発者の ProjectSettings/ に
                // ".reload.bak" ファイルが残り続けてしまう（次回 SetUp の自己修復に頼らない限り）。
                if (_backupPath != null && File.Exists(_backupPath))
                {
                    if (!File.Exists(_path))
                    {
                        // 元ファイルが失われている（何らかの理由で）。バックアップから復元する。
                        File.Copy(_backupPath, _path, true);
                    }
                    File.Delete(_backupPath);
                }

                return;
            }

            // ディスクは書き換え前のテキストへバイト単位で完全に戻す。
            File.WriteAllText(_path, _originalDiskText);

            // 復元そのものに破棄→再取得方式を使う。これは
            // Instance_AfterDestroyImmediateAndReaccess_ReloadsPostprocessOrderFromDisk() で検証している
            // 方式と同じだが、万一この方式が機能していなかった場合に備え、フィールドの直接代入による
            // フォールバックを用意し、かつその事実をログに残す（サイレントに握りつぶさない）。
            // このフォールバックは _postprocessOrder フィールドのみを対象とする。このテストクラスの
            // 各テストが書き換えるフィールドが _postprocessOrder だけであることを前提にしており、
            // 他のフィールドをこのクラスが直接変更することはないため、フォールバック対象にも含めていない。
            UnityEngine.Object.DestroyImmediate(AddressTellerSettingsAsset.instance);
            var restored = AddressTellerSettingsAsset.instance;

            if (restored._postprocessOrder != _originalPostprocessOrder)
            {
                Debug.LogWarning("[AddressTeller] TearDown: 破棄→再取得による復元が期待値と一致しなかった" +
                    $"（期待={_originalPostprocessOrder}, 実際={restored._postprocessOrder}）。" +
                    "フィールドを直接書き戻してフォールバックする。ディスクは既に正しいテキストへ戻して" +
                    "いるため、この直接代入だけで復元は完了する（SaveChanges() は呼ばない＝追加の書き込みを" +
                    "発生させない）。この警告が出ること自体が、破棄→再取得の再読み込み方式に関する" +
                    "計測結果として重要な情報になる。");
                restored._postprocessOrder = _originalPostprocessOrder;
            }

            // ここまで正常に復元できたので、バックアップファイルはもう不要。
            if (File.Exists(_backupPath))
                File.Delete(_backupPath);
        }

        /// <summary>YAML テキスト中の "_postprocessOrder: N" を書き換える。</summary>
        private static string ReplacePostprocessOrder(string yaml, int value)
            => Regex.Replace(yaml, @"_postprocessOrder: -?\d+", $"_postprocessOrder: {value}");

        /// <summary>
        /// このプロジェクトの NUnit（com.unity.test-framework 1.6.0 同梱）には Assert.Multiple が
        /// 存在しないため、代わりに失敗内容をリストへ集めておき、最後に1回だけ Assert.Fail でまとめて
        /// 報告する。これにより、値のチェックとログのチェックのどちらが失敗しても、もう片方の計測結果を
        /// 失わずに済む（片方の Assert で例外が飛んで残りが評価されない、という事態を避ける）。
        /// </summary>
        private static void FailIfAny(List<string> failures)
        {
            if (failures.Count > 0)
                Assert.Fail(string.Join("\n", failures));
        }

        /// <summary>
        /// LogAssert.NoUnexpectedReceived() は失敗時に例外を投げるため、そのまま呼ぶと後続の
        /// チェックが評価されなくなる。例外を捕まえて <paramref name="failures"/> に文字列として
        /// 積むことで、他のチェックと同列に扱えるようにする。
        /// </summary>
        private static void CheckNoUnexpectedLogs(List<string> failures)
        {
            try
            {
                LogAssert.NoUnexpectedReceived();
            }
            catch (Exception ex)
            {
                failures.Add($"予期しないログが検出された: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Instance_AfterDestroyImmediateAndReaccess_ReloadsPostprocessOrderFromDisk（最重要）:
        // 破棄→再取得がディスクを読み直すことの検証
        // ------------------------------------------------------------------

        [Test]
        public void Instance_AfterDestroyImmediateAndReaccess_ReloadsPostprocessOrderFromDisk()
        {
            const int diskValue = 777;
            Assert.AreNotEqual(_originalPostprocessOrder, diskValue,
                "テストの前提: 元値と書き換え値が衝突しないこと。");
            Assert.AreNotEqual(AddressTellerSettings.DefaultPostprocessOrder, diskValue,
                "テストの前提: 既定値へのフォールバックと区別できる値であること。");

            // ディスク上の値だけを、メモリに一切触れずに書き換える（ここではまだ instance は元のまま）。
            var mutatedText = ReplacePostprocessOrder(_originalDiskText, diskValue);
            File.WriteAllText(_path, mutatedText);

            UnityEngine.Object.DestroyImmediate(AddressTellerSettingsAsset.instance);
            var reloaded = AddressTellerSettingsAsset.instance;

            // 参考情報: ファイルから読めた場合と CreateInstance() 経路とで hideFlags に差があるかを見る。
            TestContext.Out.WriteLine($"[計測] reloaded.hideFlags = {reloaded.hideFlags}");
            TestContext.Out.WriteLine($"[計測] reloaded._postprocessOrder = {reloaded._postprocessOrder} " +
                $"(元値={_originalPostprocessOrder}, 既定値={AddressTellerSettings.DefaultPostprocessOrder}, " +
                $"ディスクへ書き込んだ値={diskValue})");

            // 値のチェックとログのチェックの両方を必ず評価させる（片方の失敗でもう片方の情報を失わない）。
            var failures = new List<string>();

            if (reloaded._postprocessOrder != diskValue)
            {
                failures.Add("破棄→再取得でディスクの値が反映されなかった。" +
                    $"実際に取得された値={reloaded._postprocessOrder} " +
                    $"（既定値 {AddressTellerSettings.DefaultPostprocessOrder} と一致するなら " +
                    "CreateInstance() で新規生成されたことを示唆し、" +
                    $"元値 {_originalPostprocessOrder} と一致するならキャッシュを返していることを示唆する）。");
            }

            CheckNoUnexpectedLogs(failures);

            FailIfAny(failures);
        }

        // ------------------------------------------------------------------
        // ReloadFromDisk_AfterExternalDiskEdit_AppliesValueWithoutUnexpectedLogs:
        // 外部からの書き換えが ReloadFromDisk() で取り込まれることの検証（回帰テスト）
        // ------------------------------------------------------------------

        /// <summary>
        /// ReloadFromDisk() が破棄→再取得方式で実装されていることの回帰テスト。
        /// 以前の「同じファイルを2個目のオブジェクトとして読む」方式では、ScriptableSingleton の
        /// コンストラクタが "already exists" という Debug.LogError を出しており、その頃はこのテストは
        /// 失敗していた。以後この LogError が再発しないこと、および外部からの書き換えが正しくメモリへ
        /// 取り込まれることを固定的に検証する。
        /// </summary>
        [Test]
        public void ReloadFromDisk_AfterExternalDiskEdit_AppliesValueWithoutUnexpectedLogs()
        {
            const int diskValue = 888;
            Assert.AreNotEqual(_originalPostprocessOrder, diskValue,
                "テストの前提: 元値と書き換え値が衝突しないこと。");
            Assert.AreNotEqual(AddressTellerSettings.DefaultPostprocessOrder, diskValue,
                "テストの前提: 既定値へのフォールバックと区別できる値であること。");

            var mutatedText = ReplacePostprocessOrder(_originalDiskText, diskValue);
            File.WriteAllText(_path, mutatedText);

            var result = AddressTellerSettings.ReloadFromDisk();

            TestContext.Out.WriteLine($"[計測] ReloadFromDisk() の戻り値={result}, " +
                $"適用後の PostprocessOrder={AddressTellerSettings.PostprocessOrder}");

            var failures = new List<string>();

            if (!result)
                failures.Add("ReloadFromDisk() が false を返した。");

            var actualPostprocessOrder = AddressTellerSettings.PostprocessOrder;
            if (actualPostprocessOrder != diskValue)
            {
                failures.Add("ReloadFromDisk() 後の値が外部書き換え結果と一致しない。" +
                    $"実際の値={actualPostprocessOrder}");
            }

            CheckNoUnexpectedLogs(failures);

            FailIfAny(failures);
        }
    }
}
