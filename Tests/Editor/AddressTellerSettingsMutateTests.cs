using System;
using System.IO;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettingsAsset.Mutate（各設定プロパティ setter の唯一の書き込み口）と
    /// TryRecoverFromBrokenFile（Project Settings の「壊れたファイルを .bak 退避して既定値で作り直す」
    /// ボタンが呼ぶ処理関数）の EditMode テスト。AddressTellerSettingsPersistenceTests.cs と同じく、
    /// FilePathOverride（テスト用シーム）で毎テスト専用の一時ファイルへ差し替えるため、本番の設定
    /// ファイルには一切触れない。
    /// </summary>
    public class AddressTellerSettingsMutateTests
    {
        private string _originalOverride;
        private string _tempPath;

        [SetUp]
        public void SetUp()
        {
            _originalOverride = AddressTellerSettingsAsset.FilePathOverride;
            _tempPath = Path.Combine(Path.GetTempPath(), $"AddressTellerSettingsMutateTests_{Guid.NewGuid():N}.json");
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

            // .bak はタイムスタンプ付きの名前で作られるため、拡張子だけで前方一致検索して掃除する。
            foreach (var backup in FindBackupFiles())
                File.Delete(backup);
        }

        private string[] FindBackupFiles()
        {
            var directory = Path.GetDirectoryName(_tempPath);
            var fileName = Path.GetFileName(_tempPath);
            if (directory == null || !Directory.Exists(directory)) return Array.Empty<string>();
            return Directory.GetFiles(directory, $"{fileName}.*.bak");
        }

        // --- 1. ファイルが外部で更新されていれば、書き込み前に再読み込みしてから変更を重ねる ---

        [Test]
        public void Mutate_FileChangedOnDiskSinceLastRead_ReloadsBeforeApplyingChange()
        {
            // 最初の書き込みで正しいファイルを作る。
            AddressTellerSettings.PostprocessOrder = 111;

            AddressTellerSettingsAsset.ResetInMemoryState();
            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());
            Assert.AreEqual(111, AddressTellerSettings.PostprocessOrder);

            // git pull 等、AddressTeller を経由しない外部変更でファイルが書き換わった状態を再現する。
            // メモリはまだ書き換え前（PostprocessOrder=111）のまま。
            var externallyChangedJson =
                $"{{\"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\", \"_postprocessOrder\": 222}}";
            File.WriteAllText(_tempPath, externallyChangedJson);

            // 別プロパティの setter を呼ぶ。Mutate が書き込み前に再読み込みするため、
            // 外部変更（_postprocessOrder=222）の上にこの変更が重なるはず
            // （メモリに残っていた古い値 111 で外部変更を踏み潰してはならない）。
            AddressTellerSettings.CleanupStaleEntries = false;

            AddressTellerSettingsAsset.ResetInMemoryState();
            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());
            Assert.AreEqual(222, AddressTellerSettings.PostprocessOrder,
                "外部から書き換えられた値がそのまま保持されているべき。");
            Assert.IsFalse(AddressTellerSettings.CleanupStaleEntries,
                "Mutate 経由で行った変更も、外部変更の上に正しく反映されているべき。");
        }

        // --- 2. 書き込みに失敗した場合、メモリは変更前の値のまま残る ---

        [Test]
        public void Mutate_WriteFails_MemoryKeepsPreviousValue()
        {
            // ファイルパスを、実際にはフォルダである場所へ向ける。EnsureLoaded の FileInfo.Exists は
            // フォルダに対して false を返す（＝ファイル不在として既定値ゲートが成功する）ため、
            // Mutate 内部のゲート自体は通るが、直後の File.WriteAllText がフォルダへの書き込みとして
            // 必ず失敗する——壊れたファイル（マーカー不一致）を用意する経路とは別に、書き込み失敗
            // だけを単独で再現するための手段。
            var directoryAsFilePath = Path.Combine(Path.GetTempPath(), $"AddressTellerSettingsMutateTests_Dir_{Guid.NewGuid():N}");
            Directory.CreateDirectory(directoryAsFilePath);
            AddressTellerSettingsAsset.FilePathOverride = directoryAsFilePath;
            AddressTellerSettingsAsset.ResetInMemoryState();

            try
            {
                var before = AddressTellerSettings.CleanupStaleEntries;

                // 書き込み失敗時の具体的な例外型（IOException / UnauthorizedAccessException 等）はOS・
                // .NETランタイムに依存しうるため固定せず、何らかの例外が伝播すること自体だけを確認する。
                Assert.Catch<Exception>(() => AddressTellerSettings.CleanupStaleEntries = !before);

                Assert.AreEqual(before, AddressTellerSettings.CleanupStaleEntries,
                    "書き込みに失敗した場合、メモリ上の値は変更前のまま残るべき。");
            }
            finally
            {
                Directory.Delete(directoryAsFilePath, true);
            }
        }

        // --- 3. ゲートが失敗している間、setter は InvalidOperationException を投げる ---

        [Test]
        public void Mutate_SettingsFileUnreadable_SetterThrowsInvalidOperationException_AndDoesNotChangeMemory()
        {
            File.WriteAllText(_tempPath, "{}"); // マーカーなし（壊れたファイル扱い）

            var before = AddressTellerSettings.CleanupStaleEntries;

            var ex = Assert.Throws<InvalidOperationException>(() => AddressTellerSettings.CleanupStaleEntries = !before);
            StringAssert.Contains("does not look like an AddressTeller settings file", ex.Message);

            Assert.AreEqual(before, AddressTellerSettings.CleanupStaleEntries,
                "ゲート失敗中は変更が一切適用されないべき。");
        }

        [Test]
        public void Mutate_SettingsFileUnreadable_SetRuleEnabledThrowsInvalidOperationException()
        {
            File.WriteAllText(_tempPath, "{}"); // マーカーなし

            Assert.Throws<InvalidOperationException>(
                () => AddressTellerSettings.SetRuleEnabled("Some.Rule.ClassName", false));
        }

        // --- 4. 同じ値を再代入した場合は書き込みが発生しない（Mutate に集約された no-op 契約） ---

        [Test]
        public void Mutate_AssigningSameValue_DoesNotWriteFile()
        {
            AddressTellerSettings.PostprocessOrder = 500;
            Assert.IsTrue(File.Exists(_tempPath));

            var stampBefore = new FileInfo(_tempPath).LastWriteTimeUtc;

            // 十分な解像度差を確保するため、書き込みが起きていないことを更新日時の比較で確認する。
            AddressTellerSettings.PostprocessOrder = 500;

            var stampAfter = new FileInfo(_tempPath).LastWriteTimeUtc;
            Assert.AreEqual(stampBefore, stampAfter, "同じ値の再代入ではファイルへ書き込まれないべき。");
        }

        // --- 5. ファイルがまだ存在しない状態で既定値と同じ値を代入しても、ファイルは作られない ---
        // マーカーの確定（Data._marker = MarkerValue）を no-op 判定の後に行うことを検証する回帰テスト。
        // 先に確定させてしまうと、他の値が全て既定値のままでもマーカーの有無（null → 値あり）だけで
        // 「変更あり」と誤検出し、実際には何も変えていないのにファイルを作ってしまう。

        [Test]
        public void Mutate_AssigningDefaultValueWhileFileDoesNotExist_DoesNotCreateFile()
        {
            Assert.IsFalse(File.Exists(_tempPath));

            // CleanupStaleEntries の既定値は false。既定値のまま再代入する。
            AddressTellerSettings.CleanupStaleEntries = false;

            Assert.IsFalse(File.Exists(_tempPath),
                "既定値と同じ値を代入しただけでは、ファイルが存在しない状態を維持するべき（ファイル不在＝既定値の契約）。");
        }

        // --- 6. TryRecoverFromBrokenFile: 壊れたファイルを .bak へ退避し、既定値で作り直す ---

        [Test]
        public void TryRecoverFromBrokenFile_MarkerMismatch_BacksUpAndRecreatesWithDefaults()
        {
            const string brokenContent = "{\"not\": \"an addressteller file\"}";
            File.WriteAllText(_tempPath, brokenContent);

            var recovered = AddressTellerSettingsAsset.TryRecoverFromBrokenFile(out var error);

            Assert.IsTrue(recovered);
            Assert.IsNull(error);

            var backups = FindBackupFiles();
            Assert.AreEqual(1, backups.Length, ".bak として元のファイルが1件残っているべき。");
            Assert.AreEqual(brokenContent, File.ReadAllText(backups[0]));

            // 復旧後は同じドメイン内でも EnsureLoaded がすぐ成功し、既定値になっているべき
            // （TryRecoverFromBrokenFile 自体がメモリ・スタンプを更新済みのため、再読み込みは不要）。
            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());
            Assert.AreEqual(AddressTellerSettings.DefaultPostprocessOrder, AddressTellerSettings.PostprocessOrder);
            Assert.IsFalse(AddressTellerSettings.CleanupStaleEntries);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TryRecoverFromBrokenFile_EmptyJsonWithoutMarker_BacksUpAndRecreatesWithDefaults()
        {
            File.WriteAllText(_tempPath, "{}"); // マーカーなし（読めるが認識できないファイル）

            var recovered = AddressTellerSettingsAsset.TryRecoverFromBrokenFile(out var error);

            Assert.IsTrue(recovered);
            Assert.IsNull(error);
            Assert.AreEqual(1, FindBackupFiles().Length);

            // 復旧後にファイルから素直に読み直しても、既定値のまま正しく読み込めるべき。
            AddressTellerSettingsAsset.ResetInMemoryState();
            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());
            Assert.IsFalse(AddressTellerSettings.CleanupStaleEntries);
        }

        [Test]
        public void TryRecoverFromBrokenFile_NoFileExists_ReturnsFalseWithError()
        {
            Assert.IsFalse(File.Exists(_tempPath));

            var recovered = AddressTellerSettingsAsset.TryRecoverFromBrokenFile(out var error);

            Assert.IsFalse(recovered);
            Assert.IsNotNull(error);
        }

        // --- 7. ゲートが既に成功する状態（ボタン表示後に外部でファイルが直った等）では、
        //        ファイルに一切触れずそのまま成功を返す ---

        [Test]
        public void TryRecoverFromBrokenFile_FileAlreadyReadable_ReturnsTrueWithoutTouchingFile()
        {
            var validContent = $"{{\"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\", \"_postprocessOrder\": 321}}";
            File.WriteAllText(_tempPath, validContent);

            var writeTimeBefore = File.GetLastWriteTimeUtc(_tempPath);

            var recovered = AddressTellerSettingsAsset.TryRecoverFromBrokenFile(out var error);

            Assert.IsTrue(recovered);
            Assert.IsNull(error);
            Assert.AreEqual(0, FindBackupFiles().Length, "既に読めるファイルに対しては .bak を作らないべき。");
            Assert.AreEqual(validContent, File.ReadAllText(_tempPath), "既に読めるファイルの中身を書き換えないべき。");
            Assert.AreEqual(writeTimeBefore, File.GetLastWriteTimeUtc(_tempPath), "既に読めるファイルへは一切書き込まないべき。");

            // 元の内容がそのまま読み込まれているべき（既定値へリセットされていない）。
            Assert.AreEqual(321, AddressTellerSettings.PostprocessOrder);
        }

        // --- 8. 復旧処理自体が失敗する場合（バックアップの作成に失敗する等）は false とエラーを返す ---
        // 「ファイルが存在するのに読めない」を、マーカー不一致とは別の経路（排他ロック）で再現する。

        [Test]
        public void TryRecoverFromBrokenFile_FileLockedForExclusiveAccess_FailsWithoutModifyingTheFile()
        {
            var validContent = $"{{\"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\"}}";
            File.WriteAllText(_tempPath, validContent);

            bool recovered;
            string error;
            using (new FileStream(_tempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // ファイル自体は正しい内容だが排他ロック中のため、EnsureLoaded の読み込みも、
                // バックアップのための File.Copy も失敗するはず（Windows の排他ロックは同一プロセス内の
                // 別ハンドルからのアクセスも拒否する）。マーカー不一致とは異なる「ファイルは存在するが
                // 読めない」経路を再現する。
                recovered = AddressTellerSettingsAsset.TryRecoverFromBrokenFile(out error);
            }

            Assert.IsFalse(recovered);
            Assert.IsNotNull(error);
            Assert.AreEqual(0, FindBackupFiles().Length);

            // ロック解除後は元の正しい内容のまま残っているべき（バックアップ・再作成のいずれも
            // 発生していない）。
            Assert.AreEqual(validContent, File.ReadAllText(_tempPath));
            AddressTellerSettingsAsset.ResetInMemoryState();
            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());
        }

        // --- 9. SettingsGateResult.FileExists ---

        [Test]
        public void EnsureLoaded_FileDoesNotExist_FileExistsIsFalse()
        {
            Assert.IsFalse(File.Exists(_tempPath));

            var result = AddressTellerSettingsAsset.EnsureLoaded();

            Assert.IsTrue(result.Success);
            Assert.IsFalse(result.FileExists);
        }

        [Test]
        public void EnsureLoaded_BrokenFile_FileExistsIsTrue()
        {
            File.WriteAllText(_tempPath, "{}"); // マーカーなし

            var result = AddressTellerSettingsAsset.EnsureLoaded();

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.FileExists, "壊れていてもファイル自体は存在するため FileExists は true であるべき。");
        }

        [Test]
        public void EnsureLoaded_ValidFile_FileExistsIsTrue()
        {
            File.WriteAllText(_tempPath, $"{{\"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\"}}");

            var result = AddressTellerSettingsAsset.EnsureLoaded();

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.FileExists);
        }
    }
}
