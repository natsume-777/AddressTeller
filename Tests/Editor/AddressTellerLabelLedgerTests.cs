using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// ラベル台帳（ProjectSettings/AddressTellerLabelLedger.json）の読み書きと、壊れた台帳でゲートが止まることを検証する。
    /// 台帳の読み書き先は AddressTellerAddressablesPollutionGuard が一時ファイルへ切り替え済みで、
    /// 各テストの後に AddressTellerLabelLedgerIsolation が削除して空へ戻す。
    /// </summary>
    public class AddressTellerLabelLedgerTests
    {
        private static string LedgerPath => AddressTellerLabelLedger.AbsoluteFilePath;

        [SetUp]
        public void SetUp() => LabelLedgerTestUtil.Reset();

        [TearDown]
        public void TearDown() => LabelLedgerTestUtil.Reset();

        private static void WriteBrokenLedger(string text = "{\"_marker\":\"not-a-ledger\",\"labels\":[\"a\"]}")
        {
            File.WriteAllText(LedgerPath, text);
            AddressTellerLabelLedger.ResetInMemoryState();
        }

        // --- 読み込み ---

        [Test]
        public void EnsureLoaded_NoFile_IsEmptyLedgerAndSuccess()
        {
            Assert.IsFalse(File.Exists(LedgerPath), "前提: 台帳ファイルは存在しない。");

            var gate = AddressTellerLabelLedger.EnsureLoaded();

            Assert.IsTrue(gate.Success);
            Assert.IsFalse(gate.FileExists);
            Assert.IsFalse(AddressTellerLabelLedger.IsManaged("anything"));
            CollectionAssert.IsEmpty(AddressTellerLabelLedger.GetAll());
        }

        [Test]
        public void Record_WritesMarkerAndOrdinalSortedLabels_AndRoundTrips()
        {
            AddressTellerLabelLedger.Record(new[] { "b", "B", "a" });

            var text = File.ReadAllText(LedgerPath);
            StringAssert.Contains(AddressTellerLabelLedger.MarkerValue, text);
            // Ordinal 順（大文字が先）で書く。
            CollectionAssert.AreEqual(new[] { "B", "a", "b" }, JsonUtility.FromJson<AddressTellerLabelLedger.Data>(text).labels);

            AddressTellerLabelLedger.ResetInMemoryState();

            Assert.IsTrue(AddressTellerLabelLedger.EnsureLoaded().Success);
            CollectionAssert.AreEqual(new[] { "B", "a", "b" }, AddressTellerLabelLedger.GetAll());
            Assert.IsTrue(AddressTellerLabelLedger.IsManaged("a"));
            Assert.IsFalse(AddressTellerLabelLedger.IsManaged("A"), "台帳の照合は大文字小文字を区別する（Ordinal）。");
        }

        [Test]
        public void Record_AccumulatesAcrossCalls()
        {
            AddressTellerLabelLedger.Record(new[] { "a" });
            AddressTellerLabelLedger.Record(new[] { "b", "a" });

            CollectionAssert.AreEqual(new[] { "a", "b" }, AddressTellerLabelLedger.GetAll());
        }

        [Test]
        public void Record_NothingNew_DoesNotCreateOrTouchFile()
        {
            AddressTellerLabelLedger.Record(Array.Empty<string>());
            Assert.IsFalse(File.Exists(LedgerPath), "空の記録ではファイルを作らない。");

            AddressTellerLabelLedger.Record(new[] { "a" });
            var oldTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(LedgerPath, oldTime);

            AddressTellerLabelLedger.Record(new[] { "a" });

            Assert.AreEqual(oldTime, File.GetLastWriteTimeUtc(LedgerPath));
        }

        [Test]
        public void Forget_RemovesNamesFromLedgerAndFile()
        {
            AddressTellerLabelLedger.Record(new[] { "a", "b" });

            AddressTellerLabelLedger.Forget(new[] { "a", "unknown" });

            CollectionAssert.AreEqual(new[] { "b" }, AddressTellerLabelLedger.GetAll());
            AddressTellerLabelLedger.ResetInMemoryState();
            CollectionAssert.AreEqual(new[] { "b" }, AddressTellerLabelLedger.GetAll());
        }

        // --- ラン単位の記録 ---

        [Test]
        public void Run_RecordIsDeferredToEndRun_AndDoesNotChangeTheManagedSetDuringTheRun()
        {
            AddressTellerLabelLedger.Record(new[] { "old" });

            AddressTellerLabelLedger.BeginRun();
            AddressTellerLabelLedger.Record(new[] { "new", "old" });

            Assert.IsTrue(AddressTellerLabelLedger.IsManaged("old"));
            Assert.IsFalse(AddressTellerLabelLedger.IsManaged("new"), "ラン中に記録した名前は、そのランの管理済み集合に入らない。");
            CollectionAssert.AreEqual(new[] { "old" }, JsonUtility.FromJson<AddressTellerLabelLedger.Data>(File.ReadAllText(LedgerPath)).labels,
                "ファイルへはラン末尾まで書かない。");

            Assert.IsNull(AddressTellerLabelLedger.EndRun());

            Assert.IsTrue(AddressTellerLabelLedger.IsManaged("new"));
            CollectionAssert.AreEqual(new[] { "new", "old" }, JsonUtility.FromJson<AddressTellerLabelLedger.Data>(File.ReadAllText(LedgerPath)).labels);
        }

        [Test]
        public void EndRun_MergesWithWhatIsOnDiskNow()
        {
            AddressTellerLabelLedger.BeginRun();
            AddressTellerLabelLedger.Record(new[] { "mine" });
            // ランの途中で、別の経路（git のマージなど）が台帳に名前を足した。
            File.WriteAllText(LedgerPath, "{\"_marker\":\"" + AddressTellerLabelLedger.MarkerValue + "\",\"labels\":[\"theirs\"]}");

            Assert.IsNull(AddressTellerLabelLedger.EndRun());

            CollectionAssert.AreEqual(new[] { "mine", "theirs" }, AddressTellerLabelLedger.GetAll());
        }

        [Test]
        public void EndRun_WriteFails_ReturnsReasonInsteadOfThrowing()
        {
            var original = AddressTellerLabelLedger.WriteFile;
            AddressTellerLabelLedger.WriteFile = (path, text) => throw new IOException("disk full");
            string error;
            try
            {
                AddressTellerLabelLedger.BeginRun();
                AddressTellerLabelLedger.Record(new[] { "a" });
                error = AddressTellerLabelLedger.EndRun();
            }
            finally
            {
                AddressTellerLabelLedger.WriteFile = original;
            }

            StringAssert.Contains("disk full", error);
            StringAssert.Contains("a", error);
            Assert.IsFalse(AddressTellerLabelLedger.IsManaged("a"));
        }

        [Test]
        public void EndRun_WithoutBeginRun_IsNoOp()
        {
            Assert.IsNull(AddressTellerLabelLedger.EndRun());
        }

        // --- 壊れた台帳 ---

        [TestCase("{\"_marker\":\"not-a-ledger\",\"labels\":[\"a\"]}")]
        [TestCase("{\"labels\":[\"a\"]}")]
        [TestCase("this is not json")]
        [TestCase("{\"_marker\":\"addressteller-label-ledger-v1\"}")]
        public void EnsureLoaded_BrokenFile_FailsAndMentionsLedger(string content)
        {
            WriteBrokenLedger(content);

            var gate = AddressTellerLabelLedger.EnsureLoaded();

            Assert.IsFalse(gate.Success);
            Assert.IsTrue(gate.FileExists);
            StringAssert.Contains("label ledger", gate.Error);
            Assert.IsFalse(AddressTellerLabelLedger.IsManaged("a"), "壊れた台帳は空として扱う（何も除去しない側に倒れる）。");
        }

        [Test]
        public void Record_BrokenFile_ThrowsAndLeavesFileUntouched()
        {
            WriteBrokenLedger();
            var before = File.ReadAllText(LedgerPath);

            Assert.Throws<InvalidOperationException>(() => AddressTellerLabelLedger.Record(new[] { "x" }));

            Assert.AreEqual(before, File.ReadAllText(LedgerPath), "壊れたファイルの上に書き足さない。");
        }

        // --- ゲートで止まる ---

        [Test]
        public void SettingsGate_BrokenLedger_Fails_WithLedgerError()
        {
            WriteBrokenLedger();

            var gate = AddressTellerSettingsAsset.EnsureLoaded();

            Assert.IsFalse(gate.Success);
            StringAssert.Contains("label ledger", gate.Error);
        }

        [Test]
        public void SettingsGate_LoggingGate_LogsLedgerError()
        {
            WriteBrokenLedger();
            LogAssert.Expect(LogType.Error, new Regex("label ledger"));

            Assert.IsFalse(AddressTellerSettings.EnsureLoaded().Success);
        }

        [Test]
        public void SettingsGate_LedgerFixed_PassesAgain()
        {
            WriteBrokenLedger();
            Assert.IsFalse(AddressTellerSettingsAsset.EnsureLoaded().Success);

            File.Delete(LedgerPath);

            Assert.IsTrue(AddressTellerSettingsAsset.EnsureLoaded().Success);
        }

        [Test]
        public void ApplyAllAndValidateAll_BrokenLedger_ReturnSettingsUnavailable()
        {
            var settings = AddressTellerTestSettingsFactory.CreateInMemory("Assets/_AddressTellerLedgerGateConfig", "AddressTellerLedgerGateTestSettings");
            try
            {
                WriteBrokenLedger();

                var applyIssues = AddressTellerService.ApplyAll(Array.Empty<string>(), settings, NullProgressReporter.Instance, Array.Empty<AddressRuleBase>());
                var validateIssues = AddressTellerService.ValidateAll(settings, NullProgressReporter.Instance, Array.Empty<AddressRuleBase>());
                var snapshotIssues = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, Array.Empty<string>(), Array.Empty<AddressRuleBase>()).Issues;

                foreach (var issues in new[] { applyIssues, validateIssues, snapshotIssues })
                {
                    Assert.AreEqual(1, issues.Count);
                    Assert.AreEqual(ValidationStatus.SettingsUnavailable, issues[0].Status);
                    StringAssert.Contains("label ledger", issues[0].Message);
                }
            }
            finally
            {
                foreach (var group in settings.groups.Where(g => g != null).ToList())
                    UnityEngine.Object.DestroyImmediate(group, true);
                UnityEngine.Object.DestroyImmediate(settings, true);
            }
        }

        [Test]
        public void TryRecoverFromBrokenFile_OnlyLedgerBroken_DoesNotResetSettingsFile()
        {
            // 台帳だけが壊れているとき、設定ファイルの復旧処理が正常な設定ファイルを退避して既定値に戻してはならない。
            var originalOrder = AddressTellerSettings.PostprocessOrder;
            try
            {
                AddressTellerSettings.PostprocessOrder = 4321;
                var settingsPath = AddressTellerSettingsAsset.AbsoluteFilePath;
                var settingsBefore = File.ReadAllText(settingsPath);
                WriteBrokenLedger();

                var recovered = AddressTellerSettingsAsset.TryRecoverFromBrokenFile(out var error);

                Assert.IsTrue(recovered, error);
                Assert.AreEqual(settingsBefore, File.ReadAllText(settingsPath));
                Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(settingsPath), Path.GetFileName(settingsPath) + ".*.bak").Length);
                Assert.IsFalse(AddressTellerSettingsAsset.EnsureLoaded().Success, "台帳は壊れたまま（この処理の対象外）。");
            }
            finally
            {
                File.Delete(LedgerPath);
                AddressTellerLabelLedger.ResetInMemoryState();
                AddressTellerSettings.PostprocessOrder = originalOrder;
            }
        }

        // --- 設定フラグ ---

        [Test]
        public void RemoveStaleManagedLabels_DefaultIsOff_AndRoundTrips()
        {
            Assert.IsFalse(new AddressTellerSettingsAsset.Data()._removeStaleManagedLabels, "既定は OFF。");

            var original = AddressTellerSettings.RemoveStaleManagedLabels;
            try
            {
                AddressTellerSettings.RemoveStaleManagedLabels = true;
                AddressTellerSettingsAsset.ResetInMemoryState();

                Assert.IsTrue(AddressTellerSettings.EnsureLoaded());
                Assert.IsTrue(AddressTellerSettings.RemoveStaleManagedLabels);
            }
            finally
            {
                AddressTellerSettings.RemoveStaleManagedLabels = original;
            }
        }
    }
}
