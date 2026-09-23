using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettings の JSON 永続化（ProjectSettings/AddressTellerSettings.json）を検証する。
    /// AddressTellerSettingsAsset.FilePathOverride（テスト用シーム）で毎テスト専用の一時ファイルへ
    /// 差し替えるため、本番の設定ファイルには一切触れない
    /// （AddressTellerAddressablesPollutionGuard がテストアセンブリ全体で既に本番パスから退避させているが、
    /// このクラスはさらにテストごとに独立した一時ファイルを使うことで他のテストとの干渉も避ける）。
    /// </summary>
    public class AddressTellerSettingsPersistenceTests
    {
        private string _originalOverride;
        private string _tempPath;

        [SetUp]
        public void SetUp()
        {
            // AddressTellerAddressablesPollutionGuard（[SetUpFixture]）がテストアセンブリ全体で
            // FilePathOverride を一時パスへ切り替え済みのはずであり、これを退避せず null に戻すと
            // 本番の ProjectSettings/AddressTellerSettings.json へ他のテストが触れてしまう。
            // 必ず元の値へ戻す（null と決め打ちしない）。
            _originalOverride = AddressTellerSettingsAsset.FilePathOverride;
            _tempPath = Path.Combine(Path.GetTempPath(), $"AddressTellerSettingsPersistenceTests_{Guid.NewGuid():N}.json");
            AddressTellerSettingsAsset.FilePathOverride = _tempPath;
            AddressTellerSettingsAsset.ResetInMemoryState();
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettingsAsset.FilePathOverride = _originalOverride;
            AddressTellerSettingsAsset.ResetInMemoryState();

            if (_tempPath != null && File.Exists(_tempPath))
                File.Delete(_tempPath);
        }

        // --- 1. 書いた値が読み戻せる ---

        [Test]
        public void EnsureLoaded_AfterSavingValue_RoundTripsThroughFile()
        {
            AddressTellerSettings.PostprocessOrder = 777;
            AddressTellerSettings.CleanupStaleEntries = false;
            AddressTellerSettings.SetRuleEnabled("Some.Rule.ClassName", false);

            // メモリを空にして、次の EnsureLoaded にファイルから実際に読み直させる。
            AddressTellerSettingsAsset.ResetInMemoryState();

            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());
            Assert.AreEqual(777, AddressTellerSettings.PostprocessOrder);
            Assert.IsFalse(AddressTellerSettings.CleanupStaleEntries);
            Assert.IsFalse(AddressTellerSettings.IsRuleEnabled("Some.Rule.ClassName"));

            LogAssert.NoUnexpectedReceived();
        }

        // --- 2. 型を定義しているファイルを別ファイルへ切り出しても値が保持される（実測事実の固定） ---
        // ファイル切り出し自体はテストから再現できないため、JsonUtility.FromJson<Data> が
        // 「型がどのファイル・GUIDで定義されているか」ではなくフィールド名だけを見て値を復元するという
        // 性質を、JSON テキストを直接組み立てて読ませる形で等価に固定する。

        [Test]
        public void EnsureLoaded_DirectlyBuiltJson_LoadsEveryFieldRegardlessOfDefiningFile()
        {
            var json = "{\n" +
                $"  \"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\",\n" +
                "  \"_cleanupStaleEntries\": false,\n" +
                "  \"_postprocessEnabled\": false,\n" +
                "  \"_snapshotFolder\": \"CustomSnapshotFolder\",\n" +
                "  \"_autoSnapshotBeforeApplyAll\": false,\n" +
                "  \"_autoSnapshotRetention\": 42,\n" +
                "  \"_autoCreateMissingGroups\": true,\n" +
                "  \"_postprocessOrder\": 5,\n" +
                "  \"_disabledRuleClassNames\": [\"Foo.Bar\"]\n" +
                "}";
            File.WriteAllText(_tempPath, json);

            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());

            Assert.IsFalse(AddressTellerSettings.CleanupStaleEntries);
            Assert.IsFalse(AddressTellerSettings.PostprocessEnabled);
            Assert.AreEqual("CustomSnapshotFolder", AddressTellerSettings.SnapshotFolder);
            Assert.IsFalse(AddressTellerSettings.AutoSnapshotBeforeApplyAll);
            Assert.AreEqual(42, AddressTellerSettings.AutoSnapshotRetention);
            Assert.IsTrue(AddressTellerSettings.AutoCreateMissingGroups);
            Assert.AreEqual(5, AddressTellerSettings.PostprocessOrder);
            CollectionAssert.Contains(AddressTellerSettings.DisabledRuleClassNames, "Foo.Bar");

            LogAssert.NoUnexpectedReceived();
        }

        // --- 3. マーカーが無い JSON は拒否される ---

        [Test]
        public void EnsureLoaded_JsonWithoutMarker_IsRejected()
        {
            File.WriteAllText(_tempPath, "{\"_postprocessOrder\": 99}");

            LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
            var result = AddressTellerSettings.EnsureLoaded();

            Assert.IsFalse(result);
        }

        [Test]
        public void EnsureLoaded_EmptyJsonObject_IsRejectedForMissingMarker()
        {
            File.WriteAllText(_tempPath, "{}");

            LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
            var result = AddressTellerSettings.EnsureLoaded();

            Assert.IsFalse(result);
        }

        // --- 4. ファイルが無い場合は既定値・無言 ---

        [Test]
        public void EnsureLoaded_FileDoesNotExist_ReturnsTrueSilentlyWithDefaults()
        {
            Assert.IsFalse(File.Exists(_tempPath));

            var result = AddressTellerSettings.EnsureLoaded();

            Assert.IsTrue(result);
            Assert.AreEqual(AddressTellerSettings.DefaultPostprocessOrder, AddressTellerSettings.PostprocessOrder);
            Assert.IsFalse(AddressTellerSettings.CleanupStaleEntries);
            Assert.IsFalse(AddressTellerSettings.PostprocessEnabled);

            LogAssert.NoUnexpectedReceived();
        }

        // --- ファイルが読めない／マーカーが無い場合、Apply/Validate は実行されない ---

        [Test]
        public void Validate_SettingsFileUnreadable_AbortsBeforeCheckingAddressables()
        {
            File.WriteAllText(_tempPath, "{}"); // マーカーなし

            LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
            AddressTellerMenu.Validate();

            // ゲートで中止していれば、後続の「AddressableAssetSettings not found」等のログは一切出ない。
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ClearAll_SettingsFileUnreadable_AbortsBeforeCheckingAddressables()
        {
            File.WriteAllText(_tempPath, "{}"); // マーカーなし

            LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
            AddressTellerMenu.ClearAll();

            // ゲートで中止していれば、後続の「AddressableAssetSettings not found」等のログは一切出ない。
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ApplyFlow_Run_SettingsFileUnreadable_DoesNotEvaluateOrWrite()
        {
            File.WriteAllText(_tempPath, "{}"); // マーカーなし

            var settings = AddressTellerTestSettingsFactory.CreateInMemory(
                "Assets/_AddressTellerSettingsPersistenceTestsConfig", "AddressTellerSettingsPersistenceApplyFlowTestSettings");
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
                AddressTellerApplyFlow.Run(settings, validateFirst: false);

                // ゲートで中止していれば、dry-run の完了ログ等は一切出ない。
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                foreach (var group in settings.groups.Where(g => g != null).ToList())
                    UnityEngine.Object.DestroyImmediate(group, true);
                UnityEngine.Object.DestroyImmediate(settings, true);
            }
        }

        // --- _postprocessOrder のセンチネル廃止の回帰検出 ---

        [Test]
        public void EnsureLoaded_JsonWithoutPostprocessOrderKey_FallsBackToFieldInitializerDefault()
        {
            File.WriteAllText(_tempPath, $"{{\"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\"}}");

            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());

            Assert.AreEqual(1000, AddressTellerSettings.PostprocessOrder);
        }

        [Test]
        public void EnsureLoaded_JsonWithExplicitZeroPostprocessOrder_ReadsBackAsZero()
        {
            File.WriteAllText(_tempPath,
                $"{{\"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\", \"_postprocessOrder\": 0}}");

            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());

            Assert.AreEqual(0, AddressTellerSettings.PostprocessOrder,
                "センチネル(0)は廃止されたため、明示的な0はそのまま0として読み戻されるべき。");
        }

        // --- 手編集 JSON で参照型フィールドが null になった場合の挙動を固定する ---
        // JsonUtility.FromJson<Data> が JSON の null を空文字列／空リストへ正規化するのか、
        // null のまま Data のフィールドに残すのかは未確認（Unity のバージョンに依存しうる）。
        // null のまま残る場合、GetSnapshotFolderAbsolutePath() の Path.Combine や
        // DisabledRuleClassNames の .ToArray() / IsRuleEnabled() の .Contains() が
        // NullReferenceException を投げうる。防御コードを先に足すのではなく、まずこのテストで
        // 実際の挙動を固定する。

        [Test]
        public void EnsureLoaded_JsonWithNullSnapshotFolderAndDisabledRuleClassNames_DoesNotThrow()
        {
            var json = "{\n" +
                $"  \"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\",\n" +
                "  \"_snapshotFolder\": null,\n" +
                "  \"_disabledRuleClassNames\": null\n" +
                "}";
            File.WriteAllText(_tempPath, json);

            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());

            Assert.DoesNotThrow(() => AddressTellerSettings.GetSnapshotFolderAbsolutePath());
            Assert.DoesNotThrow(() => _ = AddressTellerSettings.DisabledRuleClassNames);
            Assert.DoesNotThrow(() => AddressTellerSettings.IsRuleEnabled("Some.Rule"));
        }

        // --- _snapshotFolder の空文字・空白のみの正規化（読み込み時） ---

        [Test]
        public void EnsureLoaded_JsonWithEmptySnapshotFolder_NormalizesToDefault_WithWarning_AndDoesNotRewriteFile()
        {
            var json = $"{{\"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\", \"_snapshotFolder\": \"\"}}";
            File.WriteAllText(_tempPath, json);
            var beforeText = File.ReadAllText(_tempPath);

            LogAssert.Expect(LogType.Warning, new Regex("SnapshotFolder"));
            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());

            Assert.AreEqual(AddressTellerSettings.DefaultSnapshotFolder, AddressTellerSettings.SnapshotFolder);
            Assert.AreEqual(beforeText, File.ReadAllText(_tempPath),
                "読み込み時の正規化はメモリ上だけに適用され、ファイルへは書き戻さないはず。");

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void EnsureLoaded_JsonWithWhitespaceOnlySnapshotFolder_NormalizesToDefault()
        {
            var json = $"{{\"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\", \"_snapshotFolder\": \"   \"}}";
            File.WriteAllText(_tempPath, json);

            LogAssert.Expect(LogType.Warning, new Regex("SnapshotFolder"));
            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());

            Assert.AreEqual(AddressTellerSettings.DefaultSnapshotFolder, AddressTellerSettings.SnapshotFolder);

            LogAssert.NoUnexpectedReceived();
        }

        // --- POCO のフィールド数のゴールデン ---

        [Test]
        public void DataType_FieldCount_IsNine()
        {
            var fieldCount = typeof(AddressTellerSettingsAsset.Data)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Length;

            Assert.AreEqual(9, fieldCount,
                "設定フィールド数(8個 + マーカー1個)を増減したら、Documentation~/operations.md の設定一覧も同時に更新すること。");
        }

        // --- ログ段: SettingsGateLogPolicy.OncePerDistinctFailure ---
        // Postprocessor（1 import ごとに毎回呼ばれる）向けの抑制ポリシー。読み込み段（EnsureLoaded の
        // 戻り値）自体は Always と同じであることは既存テストで担保済みのため、ここではログ抑制のみを検証する。

        [Test]
        public void EnsureLoaded_OncePerDistinctFailure_SameFailure_LogsOnlyOnce()
        {
            AddressTellerSettings.ResetOncePerDistinctFailureLogForTests();
            try
            {
                File.WriteAllText(_tempPath, "{}"); // マーカーなし

                LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
                Assert.IsFalse(AddressTellerSettings.EnsureLoaded(SettingsGateLogPolicy.OncePerDistinctFailure));

                // ファイル（更新日時・サイズ・エラー文）は変わっていないため、2回目は抑制されるべき。
                Assert.IsFalse(AddressTellerSettings.EnsureLoaded(SettingsGateLogPolicy.OncePerDistinctFailure));

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                AddressTellerSettings.ResetOncePerDistinctFailureLogForTests();
            }
        }

        [Test]
        public void EnsureLoaded_OncePerDistinctFailure_FileChangesButStillFails_LogsAgain()
        {
            AddressTellerSettings.ResetOncePerDistinctFailureLogForTests();
            try
            {
                File.WriteAllText(_tempPath, "{}"); // マーカーなし

                LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
                Assert.IsFalse(AddressTellerSettings.EnsureLoaded(SettingsGateLogPolicy.OncePerDistinctFailure));

                // 依然マーカーなしで失敗するが、サイズが変わる＝別の失敗として扱われ再度ログされるべき。
                File.WriteAllText(_tempPath, "{ }");

                LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
                Assert.IsFalse(AddressTellerSettings.EnsureLoaded(SettingsGateLogPolicy.OncePerDistinctFailure));

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                AddressTellerSettings.ResetOncePerDistinctFailureLogForTests();
            }
        }

        [Test]
        public void EnsureLoaded_OncePerDistinctFailure_AfterSuccessThenFailsAgain_LogsAgain()
        {
            AddressTellerSettings.ResetOncePerDistinctFailureLogForTests();
            try
            {
                File.WriteAllText(_tempPath, "{}"); // マーカーなし

                LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
                Assert.IsFalse(AddressTellerSettings.EnsureLoaded(SettingsGateLogPolicy.OncePerDistinctFailure));

                // ファイルを正しい内容へ直す（成功）。成功のたびに重複排除キーが捨てられるべき。
                File.WriteAllText(_tempPath, $"{{\"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\"}}");
                Assert.IsTrue(AddressTellerSettings.EnsureLoaded(SettingsGateLogPolicy.OncePerDistinctFailure));

                // 再び同じ内容（"{}"）で壊す。直前に成功しているため、たとえ失敗の内容が最初と同じでも
                // 再度ログされるべき（「直っていない間だけ抑制する」ためのポリシーであり、直った後に
                // 再発した失敗まで永久に抑制してはならない）。
                File.WriteAllText(_tempPath, "{}");
                LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
                Assert.IsFalse(AddressTellerSettings.EnsureLoaded(SettingsGateLogPolicy.OncePerDistinctFailure));

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                AddressTellerSettings.ResetOncePerDistinctFailureLogForTests();
            }
        }

        // --- 新規ゲート箇所: Save Snapshot / ExecuteApply ---

        [Test]
        public void SnapshotMenu_SaveSnapshot_SettingsFileUnreadable_DoesNotWriteSnapshotFile()
        {
            var tempSnapshotFolder = Path.Combine(Path.GetTempPath(), $"AddressTellerSettingsPersistenceTests_Snapshot_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempSnapshotFolder);
            // 安全網: 万一ゲートが効かなくても、実プロジェクトの SnapshotFolder には書き込ませない
            // （AddressTellerSnapshotMenuTests と同じ考え方）。この呼び出し自体は正しい設定ファイルを書くため、
            // 直後にファイルを壊しても "SnapshotFolder が読めない" 経路の検証には影響しない。
            AddressTellerSettings.SnapshotFolder = tempSnapshotFolder;

            try
            {
                File.WriteAllText(_tempPath, "{}"); // マーカーなし

                LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
                AddressTellerSnapshotMenu.SaveSnapshot();

                // ゲートで中止していれば、AddressableAssetSettings の探索・スナップショット書き込みは一切走らない。
                LogAssert.NoUnexpectedReceived();
                Assert.AreEqual(0, Directory.GetFiles(tempSnapshotFolder, "*", SearchOption.AllDirectories).Length,
                    "ゲートで中止していれば、SnapshotFolder には何も書き込まれないはず。");
            }
            finally
            {
                // AddressTellerSettings.SnapshotFolder をここで元へ戻す試みはしない——setter は
                // ゲート失敗中に例外を投げるため、このファイルが "{}" のまま（マーカーなし）残っている間に
                // この行を実行すると例外が飛び、Directory.Delete 以降の後始末が飛んでしまう。設定ファイル
                // 自体は TearDown が _tempPath ごと削除するため、ここで明示的に戻す必要はない。
                Directory.Delete(tempSnapshotFolder, true);
            }
        }

        [Test]
        public void ApplyFlow_ExecuteApply_SettingsFileUnreadable_DoesNotEvaluateOrWrite()
        {
            var originalNotifyApplyAborted = AddressTellerApplyFlow.s_notifyApplyAborted;
            var tempSnapshotFolder = Path.Combine(Path.GetTempPath(), $"AddressTellerSettingsPersistenceTests_ExecuteApplySnapshot_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempSnapshotFolder);
            // 安全網: このテストはゲートで中止される経路を検証するものだが、万一ゲートが将来効かなくなっても
            // 実プロジェクトの SnapshotFolder には書き込ませない（AddressTellerSnapshotMenuTests と同じ考え方）。
            AddressTellerSettings.SnapshotFolder = tempSnapshotFolder;

            var settings = AddressTellerTestSettingsFactory.CreateInMemory(
                "Assets/_AddressTellerSettingsPersistenceTestsExecuteApplyConfig",
                "AddressTellerSettingsPersistenceExecuteApplyTestSettings");
            try
            {
                File.WriteAllText(_tempPath, "{}"); // マーカーなし

                string notifiedTitle = null;
                string notifiedMessage = null;
                AddressTellerApplyFlow.s_notifyApplyAborted = (title, message) =>
                {
                    notifiedTitle = title;
                    notifiedMessage = message;
                };

                LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));

                // Run() のゲートを経由しない経路（結果ウィンドウの「Apply with this content」ボタンから
                // 遅延実行されるコールバックに相当）を直接検証する。rules は空リストを明示的に渡し、
                // 万一ゲートが効かなくてもリフレクションによるルール収集（プロジェクト内の実ルール）が
                // 走らないようにする。
                AddressTellerApplyFlow.ExecuteApply(settings, Array.Empty<string>(), Array.Empty<AddressRuleBase>());

                // ゲートで中止していれば、ApplyAll 完了ログ等は一切出ない。
                LogAssert.NoUnexpectedReceived();

                Assert.AreEqual("AddressTeller - Apply All", notifiedTitle,
                    "s_notifyApplyAborted 経由でも中止をユーザーに通知するべき（Console の Error だけでは、" +
                    "結果ウィンドウの Apply ボタン経由の場合にウィンドウが閉じたことと区別できない）。");
                Assert.IsNotNull(notifiedMessage);
                StringAssert.Contains("settings file could not be loaded", notifiedMessage);
            }
            finally
            {
                AddressTellerApplyFlow.s_notifyApplyAborted = originalNotifyApplyAborted;
                // AddressTellerSettings.SnapshotFolder をここで元へ戻す試みはしない（上のテストの finally
                // コメント参照——setter はゲート失敗中に例外を投げるため、ここで呼ぶと後始末を飛ばす）。
                Directory.Delete(tempSnapshotFolder, true);

                foreach (var group in settings.groups.Where(g => g != null).ToList())
                    UnityEngine.Object.DestroyImmediate(group, true);
                UnityEngine.Object.DestroyImmediate(settings, true);
            }
        }
    }
}
