using NUnit.Framework;
using System.IO;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettings.GetSnapshotFolderAbsolutePath の範囲チェック（パストラバーサル対策）の EditMode テスト。
    /// SnapshotFolder を一時的に書き換え、TearDown で元に戻す。
    /// </summary>
    public class AddressTellerSettingsSnapshotFolderTests
    {
        private string _originalSnapshotFolder;

        [SetUp]
        public void SetUp()
        {
            _originalSnapshotFolder = AddressTellerSettings.SnapshotFolder;
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.SnapshotFolder = _originalSnapshotFolder;
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        [Test]
        public void GetSnapshotFolderAbsolutePath_DefaultValue_ResolvesUnderProjectRoot()
        {
            AddressTellerSettings.SnapshotFolder = AddressTellerSettings.DefaultSnapshotFolder;

            var resolved = AddressTellerSettings.GetSnapshotFolderAbsolutePath();

            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(ProjectRoot, AddressTellerSettings.DefaultSnapshotFolder)),
                resolved);
        }

        [Test]
        public void GetSnapshotFolderAbsolutePath_PathTraversal_FallsBackToDefault_WithWarning()
        {
            // "../../shared" のような相対パスはプロジェクトルート外に解決されるため拒否されるべき。
            AddressTellerSettings.SnapshotFolder = "../../shared";

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("resolves outside the project root"));
            var resolved = AddressTellerSettings.GetSnapshotFolderAbsolutePath();

            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(ProjectRoot, AddressTellerSettings.DefaultSnapshotFolder)),
                resolved);
        }

        [Test]
        public void GetSnapshotFolderAbsolutePath_RelativePathInsideProjectRoot_IsAllowed()
        {
            AddressTellerSettings.SnapshotFolder = "Assets/CustomSnapshots";

            var resolved = AddressTellerSettings.GetSnapshotFolderAbsolutePath();

            Assert.AreEqual(Path.GetFullPath(Path.Combine(ProjectRoot, "Assets/CustomSnapshots")), resolved);
        }

        [Test]
        public void GetSnapshotFolderAbsolutePath_AbsolutePathOutsideProject_IsAllowed_AsIntentionalOverride()
        {
            // 絶対パスの指定はテスト用一時フォルダ等、意図的な外部指定として範囲チェックの対象外にする。
            var outsidePath = Path.Combine(Path.GetTempPath(), "AddressTellerSettingsSnapshotFolderTests");
            AddressTellerSettings.SnapshotFolder = outsidePath;

            var resolved = AddressTellerSettings.GetSnapshotFolderAbsolutePath();

            Assert.AreEqual(Path.GetFullPath(outsidePath), resolved);
        }
    }
}
