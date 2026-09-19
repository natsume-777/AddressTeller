using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// EditMode テストの実行中、本番の Addressables 設定
    /// （AddressableAssetSettings.asset / AddressableAssetGroupSortSettings.asset / 各グループ .asset の
    /// entries を含む）および ProjectSettings/AddressTellerSettings.asset が変更されていないかを
    /// アセンブリ全体で監視する安全網であり、あわせてテストアセンブリの実行中は AddressTellerPostprocessor
    /// （Auto-apply on import）を無効化する。
    /// テストが Assets/ 配下に実アセットを作成する際（AssetDatabase.CreateAsset / PrefabUtility 等）、
    /// そのインポートで本番の AddressTellerPostprocessor が発火し、本番の管理グループに対して
    /// ApplyAll（CleanupStaleEntries 等）が走ってしまうことを防ぐ。
    /// テストアセンブリ内のどこかで isPersisted: true な AddressableAssetSettings.Create を
    /// 呼んでしまった場合などに、本番設定への副作用（sortOrder への余分な GUID 追加、
    /// m_GroupAssets への {fileID: 0} 残骸、m_currentHash のリセット、グループ内 entries の増減、
    /// ProjectSettings/AddressTellerSettings.asset への意図しない書き込み等）を検出する。
    /// </summary>
    [SetUpFixture]
    public sealed class AddressTellerAddressablesPollutionGuard
    {
        private string _settingsJsonBefore;
        private string _sortSettingsJsonBefore;
        private readonly Dictionary<AddressableAssetGroup, string> _groupJsonBefore = new();

        private string _addressTellerSettingsPath;
        private byte[] _addressTellerSettingsBytesBefore;
        private string _addressTellerSettingsValueJsonBefore;
        private bool _addressTellerSettingsHadPreExistingDrift;

        [OneTimeSetUp]
        public void CaptureBaselineAndDisablePostprocessor()
        {
            // AddressTellerPostprocessor.SuppressForTests はプロセスメモリ上だけで完結する static bool
            // であり、いかなる .asset ファイルとも接続していない。以前は
            // AddressTellerSettings.PostprocessEnabled（ProjectSettings/AddressTellerSettings.asset に
            // 永続化される設定）を直接操作する方式だったが、他のテストが別プロパティを正規のセッター経由で
            // 変更した際の SaveChanges()（ScriptableSingleton.Save() はオブジェクト全体を書き出す）に
            // 巻き添えでディスクへ書き出されてしまう事故が実際に起きたため、この方式へ乗り換えた。
            // 詳細は AddressTellerPostprocessor.cs の SuppressForTests の XML doc を参照。
            // このメソッドの最初の1行に置く（防御的措置）。以降のベースライン取得処理は本番 Addressables
            // 設定・ProjectSettings/AddressTellerSettings.asset を読むだけで Postprocessor を発火させる
            // ような書き込みは行わない想定だが、万一の巻き込みを避けるため抑止を最優先で有効化しておく。
            AddressTellerPostprocessor.SuppressForTests = true;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            _settingsJsonBefore = settings != null ? EditorJsonUtility.ToJson(settings) : null;

            var sort = AddressableAssetGroupSortSettings.GetSettings();
            _sortSettingsJsonBefore = sort != null ? EditorJsonUtility.ToJson(sort) : null;

            _groupJsonBefore.Clear();
            if (settings != null)
            {
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;
                    _groupJsonBefore[group] = EditorJsonUtility.ToJson(group);
                }
            }

            _addressTellerSettingsPath = AddressTellerSettingsAsset.GetAbsoluteFilePath();
            // バイト列として控える（生テキストの string ではなく）。File.ReadAllText → File.WriteAllText
            // の往復は、ベースラインが BOM 付きだった場合に BOM を落としてしまう（ReadAllText が剥がし、
            // WriteAllText は BOM なし UTF-8 で書く）ため、「バイト列として完全に元へ戻す」という本来の
            // 目的にはバイト単位の読み書きが必要。
            _addressTellerSettingsBytesBefore = File.Exists(_addressTellerSettingsPath)
                ? File.ReadAllBytes(_addressTellerSettingsPath)
                : null;

            // 値ベースのベースラインも控えておく。現在メモリ上にある唯一のインスタンスをそのまま
            // JSON化するだけであり、ディスクを読むために2個目のインスタンスを作ることはない。
            // これは意図的にディスクではなくメモリ（エディタセッション中の実際の値。未保存の変更を
            // 含みうる）を正とする。理由は DetectPreExistingDrift のコメントを参照。
            _addressTellerSettingsValueJsonBefore = EditorJsonUtility.ToJson(AddressTellerSettingsAsset.instance);

            // このガードは「EditMode テストの実行中に汚染が起きたか」だけを判定する責務に限定しており、
            // 実行開始前から既にディスクとメモリがずれていた場合の是非までは判断しない。ずれを検出した
            // ことだけ控えておき、CheckAndRestoreAddressTellerSettings 側で復元後検証（
            // restoreVerificationFailed）を無効化するために使う。
            _addressTellerSettingsHadPreExistingDrift = DetectPreExistingDrift(
                _addressTellerSettingsBytesBefore, _addressTellerSettingsValueJsonBefore);
        }

        [OneTimeTearDown]
        public void RestorePostprocessorAndAssertNoPollution()
        {
            AddressTellerPostprocessor.SuppressForTests = false;

            var settingsPolluted = CheckAndRestore(
                AddressableAssetSettingsDefaultObject.Settings,
                _settingsJsonBefore,
                "AddressableAssetSettings.asset");

            var sortSettingsPolluted = CheckAndRestore(
                AddressableAssetGroupSortSettings.GetSettings(),
                _sortSettingsJsonBefore,
                "AddressableAssetGroupSortSettings.asset");

            var groupsPolluted = false;
            foreach (var kvp in _groupJsonBefore)
            {
                var group = kvp.Key;
                if (group == null)
                {
                    // UnityEngine.Object の == null はオーバーロードされており、破棄済み(Destroyed)の
                    // オブジェクトも null 相当になる。JSON 復元では元に戻せないため、検出のみ行う。
                    Debug.LogError("[AddressTeller] EditMode テストの実行中に本番の Addressables グループの1つが破棄された。" +
                        "復元できないため、原因となったテストクラスを確認し、本番 .asset の状態を目視確認すること。");
                    groupsPolluted = true;
                    continue;
                }

                if (CheckAndRestore(group, kvp.Value, $"group '{group.Name}' ({AssetDatabase.GetAssetPath(group)})"))
                    groupsPolluted = true;
            }

            var addressTellerSettingsPolluted = CheckAndRestoreAddressTellerSettings(
                _addressTellerSettingsPath, _addressTellerSettingsBytesBefore, _addressTellerSettingsValueJsonBefore,
                _addressTellerSettingsHadPreExistingDrift, out var addressTellerSettingsRestoreFailed,
                out var addressTellerSettingsRestoreActuallyPerformed);

            var addressablesPolluted = settingsPolluted || sortSettingsPolluted || groupsPolluted;

            // addressTellerSettingsRestoreFailed は addressTellerSettingsPolluted（値の汚染）とは独立に
            // 立ちうる（値の汚染は無かったが、復元後の再検証に失敗したケース）。これを見逃して
            // Assert.Fail に繋がらない、ということがないよう条件に含める。
            if (addressablesPolluted || addressTellerSettingsPolluted || addressTellerSettingsRestoreFailed)
            {
                // 原因が Addressables 側なのか ProjectSettings/AddressTellerSettings.asset 側なのかが
                // 分かるよう、該当する方の説明だけを組み立てる。
                var causes = new List<string>();
                if (addressablesPolluted)
                {
                    causes.Add("本番 Addressables 設定（AddressableAssetSettings.asset / " +
                        "AddressableAssetGroupSortSettings.asset / グループ .asset の entries を含む）");
                }
                if (addressTellerSettingsPolluted)
                {
                    causes.Add("ProjectSettings/AddressTellerSettings.asset の値");
                }

                var message = causes.Count > 0
                    ? "EditMode テストが" + string.Join(" または ", causes) + "を変更した。" +
                      "原因となったテストクラスを確認する必要がある。"
                    : "ProjectSettings/AddressTellerSettings.asset の値そのものの汚染は検出されなかったが、" +
                      "復元後の再検証に失敗した。";

                if (addressablesPolluted)
                {
                    message += " 本番 Addressables 設定はベースラインへ復元済み。" +
                        "isPersisted: true で AddressableAssetSettings.Create を呼んでいるテストクラスや、" +
                        "本番設定に対して ApplyAll 等を呼んでいるテストクラスがないか確認せよ。";
                }
                if (addressTellerSettingsPolluted)
                {
                    // restoreActuallyPerformed が false の場合、CheckAndRestoreAddressTellerSettings 内で
                    // 復元処理自体（File.Delete / File.WriteAllBytes 等）が例外で失敗しており、ディスクが
                    // 実際にベースラインへ戻せているかどうか保証できない。「復元済み」と断定して調査者を
                    // 誤誘導しないよう、その場合は文言を変える。
                    message += addressTellerSettingsRestoreActuallyPerformed
                        ? " ProjectSettings/AddressTellerSettings.asset はベースラインへ復元済み。" +
                          "AddressTellerSettings.* の変更を TearDown で復元し忘れているテストクラスが" +
                          "ないか確認せよ。"
                        : " ProjectSettings/AddressTellerSettings.asset の復元処理自体が例外で失敗した。" +
                          "ディスクは復元できていない可能性がある。実ファイルを直接確認せよ。";
                }

                // causes が空（addressTellerSettingsPolluted も addressablesPolluted も false）の場合は、
                // causes.Count == 0 側の message（上の三項演算子）が既に「復元後の再検証に失敗した」という
                // 同じ内容を述べているため、ここで重複して足さない。
                // なお addressTellerSettingsRestoreFailed が true になるのは、CheckAndRestoreAddressTellerSettings
                // 内で File.WriteAllBytes による書き戻し自体は例外なく完了した後、その復元後の値の検証で
                // 不一致が見つかった場合のみである（書き戻し自体が例外で失敗した場合は
                // addressTellerSettingsRestoreActuallyPerformed が false になり、上のメッセージで別途扱う）。
                // つまり restoreFailed が true の時点でディスクへの書き戻し自体は完了している。
                if (addressTellerSettingsRestoreFailed && causes.Count > 0)
                {
                    message +=
                        " さらに、ProjectSettings/AddressTellerSettings.asset の復元後、値がベースラインと" +
                        "一致しなかった。AddressTellerSettings.ReloadFromDisk() による再読み込みに問題がある" +
                        "可能性がある。ディスクは最終的にベースラインのバイト列へ強制的に書き戻し済みのため" +
                        "ファイル自体は汚染されたまま残っていないはずだが、原因を確認すること。";
                }

                Assert.Fail(message);
            }
        }

        /// <summary>
        /// 対象オブジェクトをベースラインの JSON と比較する。差分があれば復元してログ警告を出し true を返す。
        /// 対象オブジェクトが null（Addressables 未構成環境）、またはベースライン未取得の場合は何もせず false を返す。
        /// 差分がない場合は対象オブジェクトに一切触れない。
        /// </summary>
        /// <remarks>
        /// この復元は best-effort であり、汚染を検出して報告することが本来の目的。
        /// <see cref="EditorJsonUtility.FromJsonOverwrite"/> はオブジェクト参照を含むフィールド
        /// （<c>m_GroupAssets</c> 等）まで完全に元の状態へ戻せるとは限らないため、
        /// Assert.Fail が出た場合は復元結果を過信せず、原因テストを特定したうえで
        /// 本番 .asset の状態を目視確認すること。
        /// </remarks>
        private static bool CheckAndRestore(UnityEngine.Object target, string jsonBefore, string assetLabel)
        {
            if (target == null || jsonBefore == null)
                return false;

            var jsonAfter = EditorJsonUtility.ToJson(target);
            if (jsonAfter == jsonBefore)
                return false;

            Debug.LogWarning($"[AddressTeller] EditMode テストの実行により {assetLabel} が変更されたため、ベースラインに復元します。");

            EditorJsonUtility.FromJsonOverwrite(jsonBefore, target);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();

            return true;
        }

        /// <summary>
        /// ProjectSettings/AddressTellerSettings.asset の変化を、検出用の生バイト列と、判定用の値
        /// （JSON）の2段構えでチェックする。
        /// <list type="bullet">
        /// <item><description>
        /// 復元するかどうかの判定（走査のトリガー）は生バイト列の完全一致で行う。書式だけの差異
        /// （改行コード・フィールド順・コメント・BOM 等）であっても、バイト列が変わっていればディスクを
        /// ベースラインへ書き戻す。ワーキングツリーに git 差分を残さない保証はここで担保する。
        /// テキスト（<see cref="File.ReadAllText(string)"/>）ではなくバイト列（
        /// <see cref="File.ReadAllBytes(string)"/>）で扱うのは、テキストの往復では BOM 付きファイルの
        /// BOM が失われる（<c>ReadAllText</c> が剥がし、<c>WriteAllText</c> は BOM なし UTF-8 で書く）ため、
        /// 「バイト列として完全に元へ戻す」という目的をテキスト経由では厳密に果たせないため。
        /// </description></item>
        /// <item><description>
        /// 一方、<see cref="Assert.Fail"/> まで繋げるべき「汚染」と判定する条件は値の JSON 比較で行う。
        /// 生バイト列の完全一致を条件にすると、値は変わっていなくても現行シリアライザの出力書式
        /// （<c>m_Script</c> の書式・改行コード等）がベースライン取得時と異なるだけで不一致となり、
        /// 旧形式の .asset をベースラインとして持つ環境（他のコントリビュータ・CI・過去バージョンからの
        /// 移行者等）で恒常的に失敗する（実測で確認済み）。復元は無条件に実行されワーキングツリーは
        /// 汚れないため、書式だけの差異を失敗として扱う必要はない。値の JSON 比較であれば書式の影響を
        /// 受けず、「値が本当に変わったか」だけを判定できる。
        /// </description></item>
        /// </list>
        /// <see cref="EditorJsonUtility.ToJson(object)"/> は現在メモリ上にある唯一のインスタンスを
        /// そのまま JSON化するだけであり、ディスクを読むために2個目のインスタンスを作ることはない
        /// （<c>InternalEditorUtility.LoadSerializedFileAndForget</c> で一時オブジェクトとして読み込む
        /// 旧方式は、ScriptableSingleton の内部実装と衝突し、テスト本体が1件も実行されるより前に
        /// このガード自身の OneTimeSetUp／OneTimeTearDown が呼ばれた時点でテストアセンブリ全体が
        /// 即失敗する事故を過去に起こしているため、採用しない）。
        /// 手順: (0) ベースライン取得時にファイルが存在しなかった場合、テストの実行によってファイルが
        /// 新規に作られたまま残っていないか確認する。存在していれば、削除する前に
        /// <see cref="AddressTellerSettings.ReloadFromDisk"/> でその内容をメモリへ読み込み、値がベースライン
        /// （<paramref name="valueJsonBefore"/>）と一致するかどうかで汚染かどうかを判定する（(1) の分岐と
        /// 判定基準を揃えるため。存在すること自体を汚染とはしない）。
        /// <c>AddressTellerSettingsPersistenceTests.TearDown</c> のように無条件で <c>SaveToDisk()</c> を呼ぶ
        /// テストや、既定値のまま設定を保存するだけの操作がこの分岐を踏んでも、値が既定値（＝メモリの
        /// ベースラインと一致）である限り汚染とは見なさない。一致すればファイルを削除してメモリも
        /// ベースラインへ戻したうえで、<see cref="TestContext.Out"/> への情報出力に留めて false を返す
        /// （初回保存前の新規プロジェクトを明示的にサポートするテストが実在するため、値が変わっていない
        /// 後始末漏れだけでアセンブリ全体を落とすのは避ける）。一致しない場合のみ true を返す。
        /// (1) バイト列が変わっていれば、まだ復元する前のディスクの内容を
        /// <see cref="AddressTellerSettings.ReloadFromDisk"/>（破棄→再取得方式）でメモリへ読み込み、
        /// 値がベースラインと一致するかを見る。一致しなければ「値の汚染」として戻り値を true にする。
        /// ファイルが削除された場合（<see cref="AddressTellerSettings.ReloadFromDisk"/> は不在時に早期
        /// return しメモリに触れない）や、何らかの理由で再読み込み自体が失敗した場合も、値の比較を待たず
        /// 汚染として扱う（さもないと「メモリは削除前のまま＝ベースラインと一致＝汚染なし」と誤判定する）。
        /// 一致する場合はバイト列の差異が書式のみであることを意味するため、<see cref="TestContext.Out"/>
        /// への情報出力に留め、戻り値は false のままにする。(2) 値の判定結果に関わらず、ディスクを
        /// ベースラインのバイト列へ無条件に書き戻し、メモリも <see cref="AddressTellerSettings.ReloadFromDisk"/>
        /// で揃える。(3) 揃えた後の値がベースラインの値 JSON と一致するかを検証し、一致しない場合のみ
        /// <paramref name="restoreVerificationFailed"/> に true を設定する（呼び出し側で「復元自体に問題が
        /// あった」ことを明示的に扱うため）。ただし <paramref name="hadPreExistingDrift"/> が true の場合
        /// （OneTimeSetUp の時点で既にディスクとメモリがずれていた場合）はこの検証を行わない。
        /// <paramref name="bytesBefore"/>（ディスク由来）と <paramref name="valueJsonBefore"/>（メモリ由来）
        /// の出所が異なる以上、両者の値が最初から食い違っている状態で「復元後に bytesBefore を読み直した
        /// 値」を「valueJsonBefore」と比較しても必ず不一致になり、<see cref="AddressTellerSettings.ReloadFromDisk"/>
        /// 自体には何の問題もないのに「再読み込みに問題がある可能性」という的外れな失敗を報告してしまう
        /// ため。この場合は情報として <see cref="TestContext.Out"/> へ記録するだけに留める。
        /// ディスクの読み書き（<see cref="File.ReadAllBytes(string)"/>／<see cref="File.WriteAllBytes"/>／
        /// <see cref="File.Delete(string)"/>）は try/catch で囲み、例外（権限不足・排他ロック等）が発生した
        /// 場合はガード自身の未処理例外でフィクスチャ全体を落とすのではなく、原因を含むメッセージとともに
        /// 汚染として扱う（戻り値 true）。この場合、実際には復元（削除／書き戻し）が完了していない可能性が
        /// あるため <paramref name="restoreActuallyPerformed"/> に false を設定し、呼び出し側が「復元済み」と
        /// 断定するメッセージを出さないようにする。
        /// ベースライン未取得（ファイルが元々存在しなかった）の場合は何もせず false を返す。
        /// </summary>
        private static bool CheckAndRestoreAddressTellerSettings(
            string path, byte[] bytesBefore, string valueJsonBefore, bool hadPreExistingDrift,
            out bool restoreVerificationFailed, out bool restoreActuallyPerformed)
        {
            restoreVerificationFailed = false;
            restoreActuallyPerformed = true;

            if (bytesBefore == null)
            {
                // ベースライン取得時にはファイルが存在しなかった（初回保存前の新規プロジェクト等）。
                // テストの実行中にファイルが作成され、後始末（削除）し忘れたまま残っていないかを確認する。
                if (!File.Exists(path))
                    return false;

                try
                {
                    // 削除する前に、作成されたファイルの値がベースラインと一致するかを調べる。(1) の分岐
                    // と判定基準を揃えるため、「存在すること自体」ではなく「値」で汚染かどうかを判定する。
                    AddressTellerSettings.ReloadFromDisk();
                    var valueJsonCreated = EditorJsonUtility.ToJson(AddressTellerSettingsAsset.instance);
                    var valuePolluted = valueJsonCreated != valueJsonBefore;

                    File.Delete(path);

                    // メモリも実行開始前の値へ戻しておく（ディスクには書き戻さない。元々ファイルが
                    // 存在しない状態がベースラインのため）。
                    EditorJsonUtility.FromJsonOverwrite(valueJsonBefore, AddressTellerSettingsAsset.instance);

                    if (valuePolluted)
                    {
                        Debug.LogWarning("[AddressTeller] EditMode テストの実行により、元々存在しなかった " +
                            "ProjectSettings/AddressTellerSettings.asset が値の変わった状態で作成されたまま" +
                            "残っていたため削除します。");
                    }
                    else
                    {
                        TestContext.Out.WriteLine("[AddressTeller] 元々存在しなかった " +
                            "ProjectSettings/AddressTellerSettings.asset が作成されたまま残っていたが、" +
                            "値はベースラインと一致していたため削除した（後始末漏れの解消のみであり、" +
                            "汚染とは見なさない）。");
                    }

                    return valuePolluted;
                }
                catch (Exception ex)
                {
                    // File.Delete は IOException 以外にも UnauthorizedAccessException 等を投げうる
                    // （UnauthorizedAccessException は IOException のサブクラスではない）ため、
                    // Exception で広めに受ける。
                    restoreActuallyPerformed = false;
                    Debug.LogError($"[AddressTeller] 作成されたまま残っていた ProjectSettings/AddressTellerSettings.asset の削除に失敗した: {ex.Message}");
                    return true;
                }
            }

            try
            {
                var bytesAfter = File.Exists(path) ? File.ReadAllBytes(path) : null;
                if (BytesEqual(bytesAfter, bytesBefore))
                    return false;

                // まだ復元する前の（現在ディスクにある）内容をそのままメモリへ読み込み、値そのものが
                // ベースラインと違うのかを調べる。Assert.Fail まで繋げるべき「汚染」かどうかの判定であり、
                // この時点ではまだディスクを書き換えない。
                // bytesAfter == null（ファイルが削除された）の場合、AddressTellerSettings.ReloadFromDisk()
                // はファイル不在で早期 return し、メモリに一切触れない（instance は削除前の値のまま）。
                // これを素通りさせると「ファイルが消えた」という明確な汚染を、値の比較だけでは
                // 「メモリは元のまま＝汚染なし」と誤判定してしまう。reloaded の戻り値（false ならメモリに
                // 反映できていない）も同様に汚染として扱う。
                // なお、ファイルの内容が壊れている（パースできない）場合は ReloadFromDisk() 自体は true を
                // 返すが、Unity がフィールドの既定値へフォールバックするため、ベースラインが既定値のままの
                // プロジェクトでは値が偶然一致し検出できない。これは値ベース比較の残存する既知の限界であり、
                // このガード単体では解消しない（生バイト列比較に戻すと H2 の書式差問題が再発するため）。
                var reloaded = AddressTellerSettings.ReloadFromDisk();
                var valuePolluted = bytesAfter == null || !reloaded ||
                    EditorJsonUtility.ToJson(AddressTellerSettingsAsset.instance) != valueJsonBefore;

                if (valuePolluted)
                {
                    Debug.LogWarning("[AddressTeller] EditMode テストの実行により ProjectSettings/AddressTellerSettings.asset が変更（削除や読み込み失敗を含む）されたため、ベースラインに復元します。");
                }
                else
                {
                    // ここに到達するのは bytesAfter != null かつ reloaded == true かつ値が一致する場合のみ
                    // （valuePolluted の短絡評価より）。バイト列は変わっているが値は一致しているという
                    // ことであり、汚染とは見なさない。原因は書式のみの差異（例: 旧形式の .asset ファイル）
                    // のことも、OneTimeSetUp で検出済みの既存のずれ（DetectPreExistingDrift 参照）が正規の
                    // 経路で解消されたことのどちらもありうる。情報として残すに留め、Assert.Fail には
                    // 繋げない。
                    TestContext.Out.WriteLine("[AddressTeller] ProjectSettings/AddressTellerSettings.asset の" +
                        "バイト列がベースラインと異なるが、値は一致している。汚染とは見なさない。");
                }

                // 復元はバイト列が変わっていれば値の判定結果に関わらず無条件に行う。ワーキングツリーに
                // git 差分を残さない保証を、値の判定結果に関わらず落とさないため。
                File.WriteAllBytes(path, bytesBefore);
                AddressTellerSettings.ReloadFromDisk();

                // 復元後の値がベースラインと一致するかを検証する。ここが不一致なら、バイト列は正しく
                // 書き戻せているにもかかわらず値が再現できていないということであり、
                // AddressTellerSettings.ReloadFromDisk() の再読み込み自体に問題がある可能性を示す
                // （直前で判定した「値の汚染」の有無とは別の問題）。
                // ただし hadPreExistingDrift が true の場合はこの検証自体を行わない。bytesBefore（ディスク
                // 由来）と valueJsonBefore（メモリ由来）が実行開始前から既に食い違っているなら、この
                // 不一致は ReloadFromDisk() の問題ではなく、その既存のずれをそのまま反映しているだけ
                // だからである。
                var valueJsonAfterRestore = EditorJsonUtility.ToJson(AddressTellerSettingsAsset.instance);
                if (valueJsonAfterRestore != valueJsonBefore)
                {
                    if (hadPreExistingDrift)
                    {
                        TestContext.Out.WriteLine("[AddressTeller] 復元後の値がベースラインと一致しなかったが、" +
                            "これは OneTimeSetUp の時点で検出済みの、ディスクとメモリの既存のずれによるもので" +
                            "あり、AddressTellerSettings.ReloadFromDisk() の問題とは見なさない。");
                    }
                    else
                    {
                        restoreVerificationFailed = true;
                        Debug.LogError("[AddressTeller] ProjectSettings/AddressTellerSettings.asset を復元したが、" +
                            "値がベースラインと一致しない。AddressTellerSettings.ReloadFromDisk() による再読み込みに" +
                            "問題がある可能性がある。なおディスクはベースラインのバイト列へ書き戻し済みのため、" +
                            "ファイル自体が汚染されたまま残ることはない。");
                    }
                }

                return valuePolluted;
            }
            catch (Exception ex)
            {
                // ディスクの読み書きで例外（IOException・UnauthorizedAccessException 等。権限不足・
                // 排他ロック等）が起きた場合、未処理例外でフィクスチャ全体を落とすのではなく、汚染として
                // 扱い原因をメッセージに含める。File.WriteAllBytes（復元の書き戻し）で例外が起きた場合も
                // ここに到達するため、ディスクが実際にベースラインへ戻せているかは保証できない。
                // restoreVerificationFailed ではなく restoreActuallyPerformed を false にすることで、
                // 呼び出し側が「復元済み」と断定しないようにする。
                restoreActuallyPerformed = false;
                Debug.LogError($"[AddressTeller] ProjectSettings/AddressTellerSettings.asset の読み書き中に " +
                    $"例外が発生した: {ex.Message}");
                return true;
            }
        }

        /// <summary>2つのバイト列の内容が完全に一致するかどうかを返す。両方 null なら true。</summary>
        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null)
                return a == b;
            if (a.Length != b.Length)
                return false;
            for (var i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        /// <summary>
        /// OneTimeSetUp の時点で、ディスクの内容（<paramref name="bytesBefore"/>）が既にメモリの値
        /// （<paramref name="valueJsonBefore"/>）とずれていないかを確認する。Editor 起動中の VCS
        /// チェックアウト・手動編集・前回セッションでの汚染残留等が原因でありうる。
        /// 一時的に破棄→再取得（<see cref="AddressTellerSettings.ReloadFromDisk"/>）でディスクの内容を
        /// メモリへ読み込んで値を比較した後、<see cref="EditorJsonUtility.FromJsonOverwrite"/> で
        /// メモリを元の値（<paramref name="valueJsonBefore"/>）へ書き戻す。唯一のインスタンスを一時的に
        /// 差し替えて調べているだけであり、2個目のインスタンスを作ることはない。ディスクには一切触れない。
        /// あえてディスク優先で強制的に読み直す方式（開発者のエディタセッション中の未保存の変更を
        /// 捨てる）は採らない。このガードは「EditMode テストの実行中に汚染が起きたか」だけを判定する
        /// 責務に限定しており、実行開始前から存在していたずれの是非は判断しない。検出結果は
        /// <see cref="CheckAndRestoreAddressTellerSettings"/> で、復元後検証（<c>restoreVerificationFailed</c>）
        /// を無効化するためだけに使う。
        /// ファイルが存在しない場合は「ずれ」として扱わない（初回保存前の新規プロジェクトは正常な状態
        /// のため）。
        /// </summary>
        private static bool DetectPreExistingDrift(byte[] bytesBefore, string valueJsonBefore)
        {
            if (bytesBefore == null)
                return false;

            var reloaded = AddressTellerSettings.ReloadFromDisk();
            if (!reloaded)
            {
                // bytesBefore != null（ファイルは存在する）にもかかわらず ReloadFromDisk() が false を
                // 返すのは、この呼び出しの直前でファイルが消えた等、通常は起こらないはずのケース。
                // 「読めなかった」ことと「ずれが無かった」ことを区別できないため、安全側に倒して
                // 「ずれ無し」として扱う（誤って true にすると、本来有効なはずの復元後検証まで
                // 無効化してしまうため）。メモリにはまだ触れていないので書き戻しは不要。
                return false;
            }

            var valueJsonOnDisk = EditorJsonUtility.ToJson(AddressTellerSettingsAsset.instance);

            // メモリを元の値へ戻す（ディスクには触れない）。
            EditorJsonUtility.FromJsonOverwrite(valueJsonBefore, AddressTellerSettingsAsset.instance);

            if (valueJsonOnDisk == valueJsonBefore)
                return false;

            Debug.LogWarning("[AddressTeller] ProjectSettings/AddressTellerSettings.asset の値が、" +
                "EditMode テスト開始時点で既にメモリ上の値とずれている（ディスク上の値とエディタセッション" +
                "中の値が異なる）。このガードは EditMode テストの実行中に汚染が起きたかどうかだけを判定する" +
                "責務のため、この既存のずれ自体は復元しない（警告のみ）。原因として、VCS のチェックアウト・" +
                "手動編集・前回セッションでの汚染残留等が考えられる。");

            return true;
        }
    }
}
