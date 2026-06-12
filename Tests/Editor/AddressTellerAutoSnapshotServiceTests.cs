using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace Natsume777.AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerAutoSnapshotService の CaptureAndSave/Rotate/FindLatestAuto/LoadAuto を、
    /// OS の一時ディレクトリ配下で検証する。AddressTellerSettings.SnapshotFolder を
    /// テスト用の一時フォルダ（絶対パス）に差し替え、TearDown で元に戻す。
    /// </summary>
    public class AddressTellerAutoSnapshotServiceTests
    {
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _group;
        private string _originalSnapshotFolder;
        private int _originalRetention;
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _originalSnapshotFolder = AddressTellerSettings.SnapshotFolder;
            _originalRetention = AddressTellerSettings.AutoSnapshotRetention;

            _tempRoot = Path.Combine(Path.GetTempPath(), "AddressTellerAutoSnapshotTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            // SnapshotFolder には絶対パスを直接渡せる
            // (GetSnapshotFolderAbsolutePath 内の Path.Combine は第二引数が絶対パスなら第一引数を無視する)。
            AddressTellerSettings.SnapshotFolder = _tempRoot;

            _settings = AddressableAssetSettings.Create("Assets/_AddressTellerTestTemp", "AddressTellerAutoSnapshotTestSettings", false, false);
            _group = _settings.CreateGroup("GroupA", false, false, false, null);
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.SnapshotFolder = _originalSnapshotFolder;
            AddressTellerSettings.AutoSnapshotRetention = _originalRetention;

            UnityEngine.Object.DestroyImmediate(_group, true);
            UnityEngine.Object.DestroyImmediate(_settings, true);

            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }

        private string AutoFolder => Path.Combine(_tempRoot, "Auto");

        [Test]
        public void GetAutoSnapshotFolder_CreatesAutoSubfolder()
        {
            var folder = AddressTellerAutoSnapshotService.GetAutoSnapshotFolder();

            Assert.AreEqual(Path.GetFullPath(AutoFolder), Path.GetFullPath(folder));
            Assert.IsTrue(Directory.Exists(folder));
        }

        [Test]
        public void CaptureAndSave_WritesSnapshotFile()
        {
            _settings.CreateOrMoveEntry("guid1", _group).SetAddress("Foo");

            var path = AddressTellerAutoSnapshotService.CaptureAndSave(_settings);

            Assert.IsNotNull(path);
            Assert.IsTrue(File.Exists(path));
            StringAssert.StartsWith("AddressTellerSnapshot_", Path.GetFileName(path));

            var snapshot = AddressTellerSnapshot.FromJson(File.ReadAllText(path));
            Assert.AreEqual(1, snapshot.Entries.Count);
            Assert.AreEqual("Foo", snapshot.Entries[0].Address);
        }

        [Test]
        public void CaptureAndSave_SameTimestamp_GetsUniqueSuffix()
        {
            // 同一秒内に複数回保存しても、ファイル名が衝突せず一意化される。
            var path1 = AddressTellerAutoSnapshotService.CaptureAndSave(_settings);
            var path2 = AddressTellerAutoSnapshotService.CaptureAndSave(_settings);

            Assert.IsNotNull(path1);
            Assert.IsNotNull(path2);
            Assert.AreNotEqual(path1, path2);
            Assert.IsTrue(File.Exists(path1));
            Assert.IsTrue(File.Exists(path2));
        }

        [Test]
        public void Rotate_RemovesOldestFilesBeyondRetention()
        {
            Directory.CreateDirectory(AutoFolder);

            // 最終更新日時を意図的にずらした5ファイルを作成する。
            for (var i = 0; i < 5; i++)
            {
                var path = Path.Combine(AutoFolder, $"AddressTellerSnapshot_2026010{i + 1}_000000.json");
                File.WriteAllText(path, "{}");
                File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, i + 1, 0, 0, 0, DateTimeKind.Utc));
            }

            AddressTellerAutoSnapshotService.Rotate(3);

            var remaining = Directory.GetFiles(AutoFolder, "AddressTellerSnapshot_*.json")
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            CollectionAssert.AreEqual(
                new[] { "AddressTellerSnapshot_20260103_000000.json", "AddressTellerSnapshot_20260104_000000.json", "AddressTellerSnapshot_20260105_000000.json" },
                remaining);
        }

        [Test]
        public void Rotate_IgnoresFilesNotMatchingNamingPattern()
        {
            Directory.CreateDirectory(AutoFolder);

            var unrelated = Path.Combine(AutoFolder, "NotASnapshot.json");
            File.WriteAllText(unrelated, "{}");

            for (var i = 0; i < 3; i++)
            {
                var path = Path.Combine(AutoFolder, $"AddressTellerSnapshot_2026010{i + 1}_000000.json");
                File.WriteAllText(path, "{}");
                File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, i + 1, 0, 0, 0, DateTimeKind.Utc));
            }

            AddressTellerAutoSnapshotService.Rotate(1);

            Assert.IsTrue(File.Exists(unrelated), "命名パターンに一致しないファイルは Rotate の対象外であるべき。");

            var remaining = Directory.GetFiles(AutoFolder, "AddressTellerSnapshot_*.json");
            Assert.AreEqual(1, remaining.Length);
        }

        [Test]
        public void FindLatestAuto_ReturnsMostRecentlyWrittenFile()
        {
            Directory.CreateDirectory(AutoFolder);

            var older = Path.Combine(AutoFolder, "AddressTellerSnapshot_20260101_000000.json");
            var newer = Path.Combine(AutoFolder, "AddressTellerSnapshot_20260102_000000.json");

            File.WriteAllText(older, "{}");
            File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            File.WriteAllText(newer, "{}");
            File.SetLastWriteTimeUtc(newer, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

            var latest = AddressTellerAutoSnapshotService.FindLatestAuto();

            Assert.AreEqual(Path.GetFullPath(newer), Path.GetFullPath(latest));
        }

        [Test]
        public void FindLatestAuto_NoAutoFolder_ReturnsNull()
        {
            // SetUp で Auto フォルダはまだ作成されていない（GetAutoSnapshotFolder 等を呼んでいないため）。
            Assert.IsFalse(Directory.Exists(AutoFolder));

            var latest = AddressTellerAutoSnapshotService.FindLatestAuto();

            Assert.IsNull(latest);
        }

        [Test]
        public void LoadAuto_ValidFile_ReturnsSnapshot()
        {
            _settings.CreateOrMoveEntry("guid1", _group).SetAddress("Foo");
            var path = AddressTellerAutoSnapshotService.CaptureAndSave(_settings);

            var snapshot = AddressTellerAutoSnapshotService.LoadAuto(path);

            Assert.IsNotNull(snapshot);
            Assert.AreEqual(1, snapshot.Entries.Count);
            Assert.AreEqual("Foo", snapshot.Entries[0].Address);
        }

        [Test]
        public void LoadAuto_MissingFile_ReturnsNullAndLogsWarning()
        {
            var missing = Path.Combine(AutoFolder, "AddressTellerSnapshot_does_not_exist.json");

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("読み込みに失敗"));
            var snapshot = AddressTellerAutoSnapshotService.LoadAuto(missing);

            Assert.IsNull(snapshot);
        }

        [Test]
        public void Rotate_WithinRetention_DeletesNothing()
        {
            Directory.CreateDirectory(AutoFolder);

            for (var i = 0; i < 2; i++)
            {
                var path = Path.Combine(AutoFolder, $"AddressTellerSnapshot_2026010{i + 1}_000000.json");
                File.WriteAllText(path, "{}");
                File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, i + 1, 0, 0, 0, DateTimeKind.Utc));
            }

            AddressTellerAutoSnapshotService.Rotate(10);

            var remaining = Directory.GetFiles(AutoFolder, "AddressTellerSnapshot_*.json");
            Assert.AreEqual(2, remaining.Length);
        }

        [Test]
        public void FindLatestAuto_SameTimestamp_ReturnsSuffixedFile()
        {
            Directory.CreateDirectory(AutoFolder);

            // 同一秒内に2回保存した場合、ベース名（先勝ち）とサフィックス付き（後勝ち）が同じ
            // LastWriteTimeUtc を持つことがある。ファイル名の Ordinal 降順では "_1" 付きの方が
            // 大きい（'_' > '.'）ため、後から保存された方が最新として選ばれるべき。
            var first = Path.Combine(AutoFolder, "AddressTellerSnapshot_20260101_000000.json");
            var second = Path.Combine(AutoFolder, "AddressTellerSnapshot_20260101_000000_1.json");

            File.WriteAllText(first, "{}");
            File.WriteAllText(second, "{}");

            var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(first, timestamp);
            File.SetLastWriteTimeUtc(second, timestamp);

            var latest = AddressTellerAutoSnapshotService.FindLatestAuto();

            Assert.AreEqual(Path.GetFullPath(second), Path.GetFullPath(latest));
        }

        [Test]
        public void CaptureAndSave_RotatesOldestAutoSnapshotBeyondRetention()
        {
            AddressTellerSettings.AutoSnapshotRetention = 2;

            var path1 = AddressTellerAutoSnapshotService.CaptureAndSave(_settings);
            var path2 = AddressTellerAutoSnapshotService.CaptureAndSave(_settings);
            var path3 = AddressTellerAutoSnapshotService.CaptureAndSave(_settings);

            Assert.IsFalse(File.Exists(path1), "保持件数を超えた最も古いスナップショットは CaptureAndSave 内の Rotate で削除されるべき。");
            Assert.IsTrue(File.Exists(path2));
            Assert.IsTrue(File.Exists(path3));

            var remaining = Directory.GetFiles(AutoFolder, "AddressTellerSnapshot_*.json");
            Assert.AreEqual(2, remaining.Length);
        }
    }
}
