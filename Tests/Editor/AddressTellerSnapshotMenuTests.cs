using NUnit.Framework;
using System;
using System.IO;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSnapshotMenu.TryWriteSnapshotFile（Save Snapshot のフォルダ作成・ファイル書き込み部分）を、
    /// EditorUtility.DisplayDialog を経由するメニュー本体（SaveSnapshot()）を呼ばずに検証する。
    /// SaveSnapshot() 自体はダイアログ操作を挟むため EditMode テストの対象外
    /// （AddressTellerApplyFlowExecuteApplyTests のクラスコメントと同じ考え方）。
    /// </summary>
    public class AddressTellerSnapshotMenuTests
    {
        private string _originalSnapshotFolder;
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _originalSnapshotFolder = AddressTellerSettings.SnapshotFolder;

            _tempRoot = Path.Combine(Path.GetTempPath(), "AddressTellerSnapshotMenuTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            // SnapshotFolder には絶対パスを直接渡せる
            // (GetSnapshotFolderAbsolutePath 内の Path.Combine は第二引数が絶対パスなら第一引数を無視する)。
            AddressTellerSettings.SnapshotFolder = _tempRoot;
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.SnapshotFolder = _originalSnapshotFolder;

            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
            if (File.Exists(_tempRoot))
                File.Delete(_tempRoot);
        }

        [Test]
        public void TryWriteSnapshotFile_ValidFolder_WritesFileAndReturnsTrue()
        {
            var snapshot = new AddressTellerSnapshot();

            var succeeded = AddressTellerSnapshotMenu.TryWriteSnapshotFile(snapshot, out var path, out var error, out var exception);

            Assert.IsTrue(succeeded);
            Assert.IsNull(error);
            Assert.IsNull(exception);
            Assert.IsTrue(File.Exists(path));
        }

        [Test]
        public void TryWriteSnapshotFile_InvalidSnapshotFolder_ReturnsFalseWithoutThrowing()
        {
            // SnapshotFolder 自体を「ディレクトリではなくファイル」にすることで、GetSnapshotFolder() 内の
            // Directory.CreateDirectory を確実かつポータブルに失敗させる
            // （SnapshotFolder は Project Settings の自由入力テキストであり、無効なパスを指定されうる）。
            Directory.Delete(_tempRoot, true);
            File.WriteAllText(_tempRoot, "not a directory");

            var snapshot = new AddressTellerSnapshot();

            bool succeeded = true;
            string error = null;
            Exception exception = null;
            Assert.DoesNotThrow(() =>
                succeeded = AddressTellerSnapshotMenu.TryWriteSnapshotFile(snapshot, out _, out error, out exception));

            Assert.IsFalse(succeeded);
            Assert.IsNotEmpty(error);
            StringAssert.Contains(_tempRoot, error, "エラーメッセージには対象フォルダを含めるべき。");
            Assert.IsNotNull(exception, "呼び出し側がログにスタックトレース込みで出せるよう、元の例外を返すべき。");
        }
    }
}
