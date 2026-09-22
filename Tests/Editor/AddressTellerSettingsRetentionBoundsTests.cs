using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 設定ファイル（ProjectSettings/AddressTellerSettings.json）を手編集等で下限（1）未満の
    /// AutoSnapshotRetention のまま読み込んだ場合の挙動を検証する。setter のクランプ
    /// （<see cref="AddressTellerSettingsAutoSnapshotRetentionTests"/>）とは別に、読み込み時の正規化
    /// （メモリ上のみ、ファイルへは書き戻さない）を対象にする。
    /// </summary>
    public class AddressTellerSettingsRetentionBoundsTests
    {
        private string _originalOverride;
        private string _tempPath;

        [SetUp]
        public void SetUp()
        {
            _originalOverride = AddressTellerSettingsAsset.FilePathOverride;
            _tempPath = Path.Combine(Path.GetTempPath(), $"AddressTellerSettingsRetentionBoundsTests_{Guid.NewGuid():N}.json");
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

        private void WriteRetentionJson(int retention) =>
            File.WriteAllText(_tempPath,
                $"{{\"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\", \"_autoSnapshotRetention\": {retention}}}");

        // 下限未満はメモリ上で1へ正規化される。

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void EnsureLoaded_RetentionBelowMinimum_NormalizesToOne(int retention)
        {
            WriteRetentionJson(retention);

            LogAssert.Expect(LogType.Warning, new Regex("AutoSnapshotRetention"));
            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());

            Assert.AreEqual(1, AddressTellerSettings.AutoSnapshotRetention);
        }

        // 読み込みだけではファイルを書き換えない。

        [Test]
        public void EnsureLoaded_RetentionBelowMinimum_DoesNotRewriteFile()
        {
            WriteRetentionJson(0);
            var beforeContent = File.ReadAllText(_tempPath);
            var beforeWriteTime = File.GetLastWriteTimeUtc(_tempPath);

            LogAssert.Expect(LogType.Warning, new Regex("AutoSnapshotRetention"));
            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());

            Assert.AreEqual(beforeContent, File.ReadAllText(_tempPath),
                "正規化はメモリ上だけで行われ、読み込みの副作用としてファイルへ書き戻してはならない。");
            Assert.AreEqual(beforeWriteTime, File.GetLastWriteTimeUtc(_tempPath));
        }

        // 下限以上の値はそのまま読み戻される。

        [TestCase(1)]
        [TestCase(10)]
        [TestCase(1000)]
        public void EnsureLoaded_RetentionAtOrAboveMinimum_IsUnchanged(int retention)
        {
            WriteRetentionJson(retention);

            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());

            Assert.AreEqual(retention, AddressTellerSettings.AutoSnapshotRetention);
            LogAssert.NoUnexpectedReceived();
        }

        // 正規化が働いたときの Warning。

        [Test]
        public void EnsureLoaded_RetentionBelowMinimum_LogsWarning()
        {
            WriteRetentionJson(-5);

            LogAssert.Expect(LogType.Warning, new Regex("AutoSnapshotRetention"));
            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());

            LogAssert.NoUnexpectedReceived();
        }
    }
}
