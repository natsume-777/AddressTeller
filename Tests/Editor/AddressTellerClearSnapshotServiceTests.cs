using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerClearSnapshotService.CaptureAndSave、および
    /// Clear → Restore(Exact) によるフルクリアの完全復元を検証する。
    /// AddressTellerSettings.SnapshotFolder をテスト用の一時フォルダ（絶対パス）に差し替え、
    /// TearDown で元に戻す。
    /// </summary>
    public class AddressTellerClearSnapshotServiceTests
    {
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _groupA;
        private AddressableAssetGroup _groupB;
        private string _originalSnapshotFolder;
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _originalSnapshotFolder = AddressTellerSettings.SnapshotFolder;

            _tempRoot = Path.Combine(Path.GetTempPath(), "AddressTellerClearSnapshotTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            // SnapshotFolder には絶対パスを直接渡せる
            // (GetSnapshotFolderAbsolutePath 内の Path.Combine は第二引数が絶対パスなら第一引数を無視する)。
            AddressTellerSettings.SnapshotFolder = _tempRoot;

            _settings = AddressableAssetSettings.Create("Assets/_AddressTellerTestTemp", "AddressTellerClearSnapshotTestSettings", false, false);
            _groupA = _settings.CreateGroup("GroupA", false, false, false, null);
            _groupB = _settings.CreateGroup("GroupB", false, false, false, null);
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.SnapshotFolder = _originalSnapshotFolder;

            UnityEngine.Object.DestroyImmediate(_groupA, true);
            UnityEngine.Object.DestroyImmediate(_groupB, true);
            UnityEngine.Object.DestroyImmediate(_settings, true);

            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }

        private string ClearFolder => Path.Combine(_tempRoot, "Clear");

        [Test]
        public void GetClearSnapshotFolder_CreatesClearSubfolder()
        {
            var folder = AddressTellerClearSnapshotService.GetClearSnapshotFolder();

            Assert.AreEqual(Path.GetFullPath(ClearFolder), Path.GetFullPath(folder));
            Assert.IsTrue(Directory.Exists(folder));
        }

        [Test]
        public void CaptureAndSave_WritesSnapshotFile()
        {
            _settings.CreateOrMoveEntry("guid1", _groupA).SetAddress("Foo");

            var path = AddressTellerClearSnapshotService.CaptureAndSave(_settings, out var error);

            Assert.IsNull(error);
            Assert.IsNotNull(path);
            Assert.IsTrue(File.Exists(path));
            StringAssert.StartsWith("AddressTellerSnapshot_", Path.GetFileName(path));

            var snapshot = AddressTellerSnapshot.FromJson(File.ReadAllText(path));
            Assert.AreEqual(1, snapshot.Entries.Count);
            Assert.AreEqual("Foo", snapshot.Entries[0].Address);
        }

        [Test]
        public void CaptureAndSave_NullSettings_ReturnsErrorAndDoesNotThrow()
        {
            var path = AddressTellerClearSnapshotService.CaptureAndSave(null, out var error);

            Assert.IsNull(path);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void ClearFolder_IsExcludedFromAutoRotation_AndListedAsNormalSnapshot()
        {
            _settings.CreateOrMoveEntry("guid1", _groupA).SetAddress("Foo");
            AddressTellerClearSnapshotService.CaptureAndSave(_settings, out _);

            var catalog = SnapshotFileCatalog.Collect(AddressTellerSettings.GetSnapshotFolderAbsolutePath(), includeAuto: false);

            // Auto を含めない一覧でも Clear フォルダのファイルは通常スナップショットとして表示される。
            Assert.AreEqual(1, catalog.Count);
            Assert.IsFalse(catalog[0].IsAuto);
        }

        [Test]
        public void Clear_Then_RestoreExact_FullyReconstructsOriginalState()
        {
            var entryA = _settings.CreateOrMoveEntry("guid1", _groupA);
            entryA.SetAddress("Foo");
            entryA.SetLabel("preload", true);
            entryA.SetLabel("ui", true);

            var entryB = _settings.CreateOrMoveEntry("guid2", _groupB);
            entryB.SetAddress("Bar");

            var before = AddressTellerSnapshotService.Capture(_settings);

            var savedPath = AddressTellerClearSnapshotService.CaptureAndSave(_settings, out var saveError);
            Assert.IsNull(saveError);

            AddressTellerClearService.Clear(_settings, ClearScope.All);
            Assert.IsNull(_settings.FindAssetEntry("guid1"));
            Assert.IsNull(_settings.FindAssetEntry("guid2"));

            Assert.IsTrue(AddressTellerSnapshotService.LoadFromFile(savedPath, out var loaded, out var loadError));
            Assert.IsNull(loadError);

            var issues = AddressTellerSnapshotService.Restore(loaded, _settings, SnapshotRestoreMode.Exact);
            Assert.IsEmpty(issues);

            var after = AddressTellerSnapshotService.Capture(_settings);
            var diff = AddressTellerSnapshotService.Diff(before, after);

            Assert.IsTrue(diff.IsEmpty, "Clear → Restore(Exact) 後は元の状態と完全に一致するべき。");
        }
    }
}
