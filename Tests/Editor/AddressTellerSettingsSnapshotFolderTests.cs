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

        [TestCase("")]
        [TestCase("   ")]
        public void SnapshotFolder_SetToEmptyOrWhitespace_NormalizesToDefault(string input)
        {
            AddressTellerSettings.SnapshotFolder = input;

            Assert.AreEqual(AddressTellerSettings.DefaultSnapshotFolder, AddressTellerSettings.SnapshotFolder);
        }

        [Test]
        public void SnapshotFolder_SetterNormalizeEmpty_DoesNotLogWarning()
        {
            // 正規化ロジックは setter と設定ファイル読み込み（EnsureLoaded）で共有している
            // （AddressTellerSettingsAsset.NormalizeSnapshotFolder）が、Warning を出すのは読み込み時のみ
            // （warnIfChanged: true）。setter は利用者がその場で指定した値を正規化する通常の挙動であり、
            // EnsureLoaded 側の Warning テストと違い、setter 側では出てはならない。
            AddressTellerSettings.SnapshotFolder = "";

            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SnapshotFolder_SetToEmpty_WritesNormalizedValueToDisk()
        {
            // メモリ上の値だけでなく、実際に書き込まれるファイルにも正規化後の値が保存されることを確認する。
            // 先に既定値と異なる値へ変更して書き込みを発生させてから空文字を設定する——直前の値が既に
            // 既定値と一致していると、Mutate の no-op 判定（変更前後で値が同じなら書き込みをスキップする）
            // により、そもそもファイルへの書き込み自体が起きず、この確認ができないため。
            AddressTellerSettings.SnapshotFolder = "SomeCustomSnapshotFolder";

            AddressTellerSettings.SnapshotFolder = "";

            var json = File.ReadAllText(AddressTellerSettingsAsset.AbsoluteFilePath);
            var parsed = JsonUtility.FromJson<AddressTellerSettingsAsset.Data>(json);
            Assert.AreEqual(AddressTellerSettings.DefaultSnapshotFolder, parsed._snapshotFolder);
        }
    }
}
