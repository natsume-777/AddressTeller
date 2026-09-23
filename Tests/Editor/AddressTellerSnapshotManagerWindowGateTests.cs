using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSnapshotManagerWindow.Refresh() の設定読み込みゲートを検証する。
    /// 実ウィンドウ表示（Show/Focus）は行わず、ScriptableObject.CreateInstance で直接インスタンス化する
    /// （AddressTellerResultWindowExportTests と同じ考え方）。Refresh/_items/_settingsErrorHelpBox は
    /// private のため、リフレクション経由でアクセスする。
    /// </summary>
    public class AddressTellerSnapshotManagerWindowGateTests
    {
        private string _originalOverride;
        private string _tempPath;
        private string _tempSnapshotFolder;

        [SetUp]
        public void SetUp()
        {
            // AddressTellerAddressablesPollutionGuard（[SetUpFixture]）がテストアセンブリ全体で
            // FilePathOverride を一時パスへ切り替え済みのはずであり、これを退避せず null に戻すと
            // 本番の ProjectSettings/AddressTellerSettings.json へ他のテストが触れてしまう。
            _originalOverride = AddressTellerSettingsAsset.FilePathOverride;
            _tempPath = Path.Combine(Path.GetTempPath(), $"AddressTellerSnapshotManagerWindowGateTests_{Guid.NewGuid():N}.json");
            AddressTellerSettingsAsset.FilePathOverride = _tempPath;
            AddressTellerSettingsAsset.ResetInMemoryState();

            // SnapshotFolder を一時フォルダに固定する（安全網）。設定していないと、CreateInstance の
            // OnEnable が走らせる最初の Refresh（この時点では設定ファイルがまだ無く成功するため実際に
            // SnapshotFolder を読みに行く）が既定値 "AddressTellerSnapshots" を解決してしまい、
            // 実プロジェクト直下の同名フォルダを読み取り対象にしてしまう。
            _tempSnapshotFolder = Path.Combine(Path.GetTempPath(), $"AddressTellerSnapshotManagerWindowGateTests_Snapshot_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempSnapshotFolder);
            AddressTellerSettings.SnapshotFolder = _tempSnapshotFolder;

            // Refresh() の既定ログポリシーは OncePerDistinctFailure。重複排除状態はテスト間で
            // 共有される static のため、他のテストが残した状態でこのテストの LogAssert.Expect が
            // 誤って抑制されない/逆に誤って漏れないよう、テストごとにリセットする。
            AddressTellerSettings.ResetOncePerDistinctFailureLogForTests();
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.ResetOncePerDistinctFailureLogForTests();

            AddressTellerSettingsAsset.FilePathOverride = _originalOverride;
            AddressTellerSettingsAsset.ResetInMemoryState();

            if (_tempPath != null && File.Exists(_tempPath))
                File.Delete(_tempPath);

            if (_tempSnapshotFolder != null && Directory.Exists(_tempSnapshotFolder))
                Directory.Delete(_tempSnapshotFolder, true);
        }

        private static void InvokeRefresh(AddressTellerSnapshotManagerWindow window)
        {
            var method = typeof(AddressTellerSnapshotManagerWindow).GetMethod(
                "Refresh", BindingFlags.NonPublic | BindingFlags.Instance);
            // Refresh は省略可能引数 logPolicy を持つ（既定 OncePerDistinctFailure）。Reflection 経由の
            // Invoke は既定値を補ってくれないため、明示的に渡す必要がある。
            method.Invoke(window, new object[] { SettingsGateLogPolicy.Always });
        }

        private static List<SnapshotFileInfo> GetItems(AddressTellerSnapshotManagerWindow window)
        {
            var field = typeof(AddressTellerSnapshotManagerWindow).GetField(
                "_items", BindingFlags.NonPublic | BindingFlags.Instance);
            return (List<SnapshotFileInfo>)field.GetValue(window);
        }

        private static HelpBox GetSettingsErrorHelpBox(AddressTellerSnapshotManagerWindow window)
        {
            var field = typeof(AddressTellerSnapshotManagerWindow).GetField(
                "_settingsErrorHelpBox", BindingFlags.NonPublic | BindingFlags.Instance);
            return (HelpBox)field.GetValue(window);
        }

        [Test]
        public void Refresh_SettingsFileUnreadable_ClearsItemsAndShowsHelpBox()
        {
            // OnEnable 経由の初回 Refresh は SetUp が書いた正常な設定ファイルで走らせ、正常系として
            // 素通りさせる（CreateGUI 前は _settingsErrorHelpBox が null のため、ここで壊すと早期 Refresh の
            // ログ検証が余分に必要になる）。
            var window = ScriptableObject.CreateInstance<AddressTellerSnapshotManagerWindow>();
            try
            {
                window.CreateGUI();

                File.WriteAllText(_tempPath, "{}"); // マーカーなし

                LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
                InvokeRefresh(window);

                Assert.AreEqual(0, GetItems(window).Count,
                    "設定ファイルが読めない場合、一覧は空に揃えるべき（古い/既定値の SnapshotFolder で誤った一覧を見せないため）。");

                var helpBox = GetSettingsErrorHelpBox(window);
                Assert.AreEqual(DisplayStyle.Flex, helpBox.style.display.value,
                    "設定ファイルが読めない場合、HelpBox でエラーを表示するべき。");
                StringAssert.Contains("does not look like an AddressTeller settings file", helpBox.text);

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void OnEnableRefreshFailsBeforeCreateGUI_CreateGUI_ShowsHelpBoxWithLastError()
        {
            // 設定ファイルを CreateInstance の前に壊しておく。OnEnable→Refresh はこの時点で走り、
            // _settingsErrorHelpBox がまだ null な状態で失敗を記録するだけになる（_lastSettingsGateError
            // のコメント参照）。CreateGUI がその記録を読んで HelpBox に反映できるかを検証する
            // （以前は CreateGUI が空の HelpBox を作るだけで、この失敗を一切反映できなかった）。
            File.WriteAllText(_tempPath, "{}"); // マーカーなし

            LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
            var window = ScriptableObject.CreateInstance<AddressTellerSnapshotManagerWindow>();
            try
            {
                // CreateGUI 自体は Refresh を呼ばない（RebuildList/UpdateActionButtons のみ）ため、
                // ここで追加のログは発生しないはず。
                window.CreateGUI();

                var helpBox = GetSettingsErrorHelpBox(window);
                Assert.AreEqual(DisplayStyle.Flex, helpBox.style.display.value,
                    "OnEnable 時点の失敗が、CreateGUI 後の HelpBox に反映されているべき。");
                StringAssert.Contains("does not look like an AddressTeller settings file", helpBox.text);

                Assert.AreEqual(0, GetItems(window).Count);

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Refresh_SettingsFileReadableAgain_HidesHelpBox()
        {
            var window = ScriptableObject.CreateInstance<AddressTellerSnapshotManagerWindow>();
            try
            {
                window.CreateGUI();

                File.WriteAllText(_tempPath, "{}"); // マーカーなし
                LogAssert.Expect(LogType.Error, new Regex("does not look like an AddressTeller settings file"));
                InvokeRefresh(window);

                var helpBox = GetSettingsErrorHelpBox(window);
                Assert.AreEqual(DisplayStyle.Flex, helpBox.style.display.value, "前提: まずエラー表示が出ていること。");

                // ファイルを直す。
                File.WriteAllText(_tempPath, $"{{\"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\"}}");
                InvokeRefresh(window);

                Assert.AreEqual(DisplayStyle.None, helpBox.style.display.value,
                    "設定ファイルが読めるようになったら、HelpBox は隠すべき。");

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }
    }
}
