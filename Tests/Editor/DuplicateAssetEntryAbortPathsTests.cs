using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 同一 guid が2つ以上のグループにまたがって存在する状態で、書き込み系の入口
    /// （Undo Last Apply / Save Snapshot / Clear All、いずれも settings 注入可能な internal コア）が
    /// 何も書き込まずに中止することを検証する。<see cref="DuplicateAssetEntryDetector.LogAndReturnTrueIfDuplicates"/>
    /// 自体の単体テストも合わせて持つ。本物のプロジェクト設定（AddressableAssetSettingsDefaultObject.Settings）
    /// には一切触れない——すべて非永続の in-memory settings を使う。
    /// </summary>
    public class DuplicateAssetEntryAbortPathsTests
    {
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _groupA;
        private AddressableAssetGroup _groupB;

        [SetUp]
        public void SetUp()
        {
            _settings = AddressTellerTestSettingsFactory.CreateInMemory(
                "Assets/_AddressTellerTestTempConfig", "DuplicateAssetEntryAbortPathsTestSettings");
            _groupA = _settings.CreateGroup("GroupA", false, false, false, null);
            _groupB = _settings.CreateGroup("GroupB", false, false, false, null);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_groupA, true);
            UnityEngine.Object.DestroyImmediate(_groupB, true);
            UnityEngine.Object.DestroyImmediate(_settings, true);
        }

        private void InjectDuplicate()
        {
            _settings.CreateOrMoveEntry("guid-dup", _groupA).SetAddress("AddressA");
            DuplicateAssetEntryTestInjector.InjectDuplicateEntry(_groupB, "guid-dup", "AddressB");
        }

        // --- DuplicateAssetEntryDetector.LogAndReturnTrueIfDuplicates ---

        [Test]
        public void LogAndReturnTrueIfDuplicates_NoDuplicates_ReturnsFalse_NoLog()
        {
            _settings.CreateOrMoveEntry("guid-a", _groupA).SetAddress("A");

            var aborted = DuplicateAssetEntryDetector.LogAndReturnTrueIfDuplicates(_settings, "Test");

            Assert.IsFalse(aborted);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogAndReturnTrueIfDuplicates_HasDuplicates_ReturnsTrue_LogsEachAndSummary()
        {
            InjectDuplicate();

            LogAssert.Expect(LogType.Error, new Regex("guid-dup"));
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[AddressTeller] Test aborted:")));

            var aborted = DuplicateAssetEntryDetector.LogAndReturnTrueIfDuplicates(_settings, "Test");

            Assert.IsTrue(aborted);
            LogAssert.NoUnexpectedReceived();
        }

        // --- AddressTellerMenu.ClearAll(settings, rules) ---

        [Test]
        public void ClearAll_DuplicateAssetEntry_AbortsWithoutRemovingEntries()
        {
            InjectDuplicate();
            _settings.CreateOrMoveEntry("guid-other", _groupA).SetAddress("Other");

            string notifiedMessage = null;
            var originalNotify = AddressTellerMenu.s_notifyClearAborted;
            AddressTellerMenu.s_notifyClearAborted = message => notifiedMessage = message;
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("guid-dup"));
                LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[AddressTeller] Clear All aborted:")));

                AddressTellerMenu.ClearAll(_settings, Array.Empty<AddressRuleBase>());

                Assert.IsNotNull(notifiedMessage, "重複検出時も s_notifyClearAborted 経由でユーザーに通知されるべき。");
                Assert.IsNotNull(_settings.FindAssetEntry("guid-other"),
                    "重複が検出された実行では、重複と無関係なエントリも一切削除されてはいけない。");
            }
            finally
            {
                AddressTellerMenu.s_notifyClearAborted = originalNotify;
            }
        }

        // --- AddressTellerSnapshotMenu.UndoLastApply(settings) / SaveSnapshot(settings) ---

        [Test]
        public void UndoLastApply_DuplicateAssetEntry_AbortsBeforeLookingUpAutoSnapshot()
        {
            InjectDuplicate();

            LogAssert.Expect(LogType.Error, new Regex("guid-dup"));
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[AddressTeller] Undo Last Apply aborted:")));

            // 重複検出で中止するなら、この後の AddressTellerAutoSnapshotService.FindLatestAuto() 呼び出しには
            // 到達しないはず——到達していれば「見つからない」旨の Warning ログが追加で出るため、
            // LogAssert.NoUnexpectedReceived() がそれを検出する。
            Assert.DoesNotThrow(() => AddressTellerSnapshotMenu.UndoLastApply(_settings));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSnapshot_DuplicateAssetEntry_AbortsWithoutWritingFile()
        {
            var tempSnapshotRoot = Path.Combine(Path.GetTempPath(), "AddressTellerDuplicateSaveSnapshotTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempSnapshotRoot);
            var originalSnapshotFolder = AddressTellerSettings.SnapshotFolder;
            AddressTellerSettings.SnapshotFolder = tempSnapshotRoot;
            try
            {
                InjectDuplicate();

                LogAssert.Expect(LogType.Error, new Regex("guid-dup"));
                LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[AddressTeller] Save Snapshot aborted:")));

                AddressTellerSnapshotMenu.SaveSnapshot(_settings);

                Assert.IsEmpty(Directory.GetFiles(tempSnapshotRoot),
                    "重複検出時は、読み込み不能な（重複 guid 入りの）スナップショットファイルを作ってはいけない。");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                AddressTellerSettings.SnapshotFolder = originalSnapshotFolder;
                Directory.Delete(tempSnapshotRoot, true);
            }
        }
    }
}
