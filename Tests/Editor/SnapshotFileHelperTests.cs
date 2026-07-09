using NUnit.Framework;
using System;
using System.IO;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerAutoSnapshotService/AddressTellerClearSnapshotService/AddressTellerSnapshotMenu が
    /// 共有する SnapshotFileHelper（ResolveUniquePath・IsInsideAssets）の EditMode テスト。
    /// </summary>
    public class SnapshotFileHelperTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "AddressTellerSnapshotFileHelperTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }

        [Test]
        public void ResolveUniquePath_NoExistingFile_ReturnsBaseName()
        {
            var timestamp = new DateTime(2026, 1, 2, 3, 4, 5);

            var path = SnapshotFileHelper.ResolveUniquePath(_tempRoot, timestamp);

            Assert.AreEqual(Path.Combine(_tempRoot, "AddressTellerSnapshot_20260102_030405.json"), path);
        }

        [Test]
        public void ResolveUniquePath_SameTimestampTwice_GetsSuffixedName()
        {
            var timestamp = new DateTime(2026, 1, 2, 3, 4, 5);
            var basePath = SnapshotFileHelper.ResolveUniquePath(_tempRoot, timestamp);
            File.WriteAllText(basePath, "{}");

            var second = SnapshotFileHelper.ResolveUniquePath(_tempRoot, timestamp);

            Assert.AreEqual(Path.Combine(_tempRoot, "AddressTellerSnapshot_20260102_030405_1.json"), second);
        }

        [Test]
        public void ResolveUniquePath_MultipleCollisions_IncrementsSuffix()
        {
            var timestamp = new DateTime(2026, 1, 2, 3, 4, 5);
            File.WriteAllText(Path.Combine(_tempRoot, "AddressTellerSnapshot_20260102_030405.json"), "{}");
            File.WriteAllText(Path.Combine(_tempRoot, "AddressTellerSnapshot_20260102_030405_1.json"), "{}");

            var third = SnapshotFileHelper.ResolveUniquePath(_tempRoot, timestamp);

            Assert.AreEqual(Path.Combine(_tempRoot, "AddressTellerSnapshot_20260102_030405_2.json"), third);
        }

        [Test]
        public void IsInsideAssets_PathUnderDataPath_ReturnsTrue()
        {
            var path = Path.Combine(Application.dataPath, "Foo", "bar.json");

            Assert.IsTrue(SnapshotFileHelper.IsInsideAssets(path));
        }

        [Test]
        public void IsInsideAssets_SiblingFolderWithDataPathPrefix_ReturnsFalse()
        {
            // "<project>/AssetsSnapshots/..." は Path.GetFullPath(Application.dataPath) の文字列プレフィックスに
            // 一致するが、実際には Assets フォルダの外（境界のない StartsWith 判定だと誤検知していた）。
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var siblingPath = Path.Combine(projectRoot, "AssetsSnapshots", "foo.json");

            Assert.IsFalse(SnapshotFileHelper.IsInsideAssets(siblingPath));
        }

        [Test]
        public void IsInsideAssets_PathOutsideProject_ReturnsFalse()
        {
            var outsidePath = Path.Combine(_tempRoot, "foo.json");

            Assert.IsFalse(SnapshotFileHelper.IsInsideAssets(outsidePath));
        }
    }
}
