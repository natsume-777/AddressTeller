using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace Natsume777.AddressTeller.Editor.Tests
{
    /// <summary>
    /// SnapshotFileCatalog の Collect/SortByCapturedDesc/Filter を、OS の一時ディレクトリに
    /// 配置したスナップショット JSON ファイルを使って検証する。
    /// </summary>
    public class SnapshotFileCatalogTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "AddressTellerSnapshotFileCatalogTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }

        private static string ValidJson(string capturedAtIso, string comment, int schemaVersion = AddressTellerSnapshotService.CurrentSchemaVersion)
        {
            var snapshot = new AddressTellerSnapshot
            {
                CapturedAtIso = capturedAtIso,
                Comment = comment,
                SchemaVersion = schemaVersion,
            };
            snapshot.Entries.Add(new SnapshotEntry { Guid = "guid1", Address = "Foo", GroupName = "GroupA" });
            return snapshot.ToJson();
        }

        private void WriteFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(_tempRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content);
        }

        [Test]
        public void Collect_FolderNotFound_ReturnsEmpty()
        {
            var result = SnapshotFileCatalog.Collect(Path.Combine(_tempRoot, "DoesNotExist"), includeAuto: true);

            CollectionAssert.IsEmpty(result);
        }

        [Test]
        public void Collect_ValidAndBrokenFiles_BrokenFileIncludedWithLoadError()
        {
            WriteFile("valid.json", ValidJson("2026-06-13T00:00:00Z", "manual"));
            WriteFile("broken.json", "{not valid json");

            var result = SnapshotFileCatalog.Collect(_tempRoot, includeAuto: true);

            Assert.AreEqual(2, result.Count);

            var valid = result.Single(i => i.FileName == "valid.json");
            Assert.IsNull(valid.LoadError);
            Assert.AreEqual("2026-06-13T00:00:00Z", valid.CapturedAtIso);
            Assert.AreEqual("manual", valid.Comment);
            Assert.AreEqual(1, valid.EntryCount);

            var broken = result.Single(i => i.FileName == "broken.json");
            Assert.IsNotNull(broken.LoadError);
        }

        [Test]
        public void Collect_OldFormatWithoutCapturedAt_LoadedWithEmptyCapturedAtIso()
        {
            WriteFile("old.json", ValidJson("", "old format"));

            var result = SnapshotFileCatalog.Collect(_tempRoot, includeAuto: true);

            var old = result.Single();
            Assert.IsNull(old.LoadError);
            Assert.AreEqual("", old.CapturedAtIso);
        }

        [Test]
        public void Collect_IncludeAutoFalse_ExcludesAutoFolder()
        {
            WriteFile("manual.json", ValidJson("2026-06-13T00:00:00Z", "manual"));
            WriteFile("Auto/auto.json", ValidJson("2026-06-12T00:00:00Z", "auto"));

            var withAuto = SnapshotFileCatalog.Collect(_tempRoot, includeAuto: true);
            var withoutAuto = SnapshotFileCatalog.Collect(_tempRoot, includeAuto: false);

            Assert.AreEqual(2, withAuto.Count);
            Assert.AreEqual(1, withoutAuto.Count);
            Assert.AreEqual("manual.json", withoutAuto.Single().FileName);
            Assert.IsFalse(withoutAuto.Single().IsAuto);
            Assert.IsTrue(withAuto.Single(i => i.FileName == "auto.json").IsAuto);
        }

        [Test]
        public void SortByCapturedDesc_OrdersByCapturedAtDescending_EmptyLast_TieBrokenByFileName()
        {
            var items = new[]
            {
                new SnapshotFileInfo { FileName = "b.json", CapturedAtIso = "2026-06-12T00:00:00Z" },
                new SnapshotFileInfo { FileName = "a.json", CapturedAtIso = "2026-06-13T00:00:00Z" },
                new SnapshotFileInfo { FileName = "old.json", CapturedAtIso = "" },
                new SnapshotFileInfo { FileName = "a2.json", CapturedAtIso = "2026-06-13T00:00:00Z" },
            };

            var sorted = SnapshotFileCatalog.SortByCapturedDesc(items);

            Assert.AreEqual(new[] { "a.json", "a2.json", "b.json", "old.json" }, sorted.Select(i => i.FileName).ToArray());
        }

        [Test]
        public void Filter_NullOrEmptyKeyword_ReturnsAllItems()
        {
            var items = new[]
            {
                new SnapshotFileInfo { FileName = "a.json", Comment = "foo" },
                new SnapshotFileInfo { FileName = "b.json", Comment = "bar" },
            };

            Assert.AreEqual(2, SnapshotFileCatalog.Filter(items, null).Count);
            Assert.AreEqual(2, SnapshotFileCatalog.Filter(items, "").Count);
        }

        [Test]
        public void Filter_MatchesFileNameOrCommentCaseInsensitive()
        {
            var items = new[]
            {
                new SnapshotFileInfo { FileName = "Release_v1.json", Comment = "before release" },
                new SnapshotFileInfo { FileName = "snapshot2.json", Comment = "hotfix" },
            };

            var byFileName = SnapshotFileCatalog.Filter(items, "release");
            Assert.AreEqual(1, byFileName.Count);
            Assert.AreEqual("Release_v1.json", byFileName[0].FileName);

            var byComment = SnapshotFileCatalog.Filter(items, "HOTFIX");
            Assert.AreEqual(1, byComment.Count);
            Assert.AreEqual("snapshot2.json", byComment[0].FileName);

            var noMatch = SnapshotFileCatalog.Filter(items, "nope");
            CollectionAssert.IsEmpty(noMatch);
        }
    }
}
