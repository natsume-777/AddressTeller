using System;
using System.IO;
using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettingsLoadDiagnostics の純粋ロジック（例外分類、SettingsLoadDiagnosis の不変条件）の
    /// 単体テスト。ProjectSettings 配下の実ファイルや実際の権限操作（ACL 変更等）を一切経由しない。
    /// Diagnose() 自体は ScriptableSingleton（AddressTellerSettingsAsset.instance）の再シリアライズを
    /// 内部で行うため、ここではファイル I/O を経由しない範囲——ClassifyReadException と
    /// SettingsLoadDiagnosis の各ファクトリメソッドの不変条件——のみを対象にする。
    /// </summary>
    public class AddressTellerSettingsLoadDiagnosticsTests
    {
        [Test]
        public void ClassifyReadException_FileNotFoundException_IsFileAbsent()
        {
            Assert.AreEqual(
                SettingsLoadDiagnosisKind.FileAbsent,
                AddressTellerSettingsLoadDiagnostics.ClassifyReadException(new FileNotFoundException()));
        }

        [Test]
        public void ClassifyReadException_DirectoryNotFoundException_IsFileAbsent()
        {
            Assert.AreEqual(
                SettingsLoadDiagnosisKind.FileAbsent,
                AddressTellerSettingsLoadDiagnostics.ClassifyReadException(new DirectoryNotFoundException()));
        }

        [Test]
        public void ClassifyReadException_UnauthorizedAccessException_IsFileUnreadable()
        {
            Assert.AreEqual(
                SettingsLoadDiagnosisKind.FileUnreadable,
                AddressTellerSettingsLoadDiagnostics.ClassifyReadException(new UnauthorizedAccessException()));
        }

        [Test]
        public void ClassifyReadException_IOException_IsFileUnreadable()
        {
            // UnauthorizedAccessException 以外の IOException 系（例: 別プロセスによるロック）も
            // 一律 FileUnreadable に分類されることを固定する。
            Assert.AreEqual(
                SettingsLoadDiagnosisKind.FileUnreadable,
                AddressTellerSettingsLoadDiagnostics.ClassifyReadException(new IOException("locked by another process")));
        }

        [Test]
        public void SettingsLoadDiagnosis_MatchAndFileAbsent_HaveNullMessage()
        {
            Assert.IsNull(SettingsLoadDiagnosis.Match().Message);
            Assert.IsNull(SettingsLoadDiagnosis.FileAbsent().Message);
        }

        [Test]
        public void SettingsLoadDiagnosis_EveryOtherKind_HasNonNullMessage()
        {
            Assert.IsNotNull(SettingsLoadDiagnosis.Mismatch("mismatch detail").Message);
            Assert.IsNotNull(SettingsLoadDiagnosis.FileUnreadable("unreadable detail").Message);
            Assert.IsNotNull(SettingsLoadDiagnosis.FileUnparsable("unparsable detail").Message);
            Assert.IsNotNull(SettingsLoadDiagnosis.DiagnosticUnavailable("unavailable detail").Message);
        }

        [Test]
        public void SettingsLoadDiagnosis_IsSettingsFileProblem_TrueOnlyForMismatchFileUnreadableFileUnparsable()
        {
            Assert.IsTrue(SettingsLoadDiagnosis.Mismatch("x").IsSettingsFileProblem);
            Assert.IsTrue(SettingsLoadDiagnosis.FileUnreadable("x").IsSettingsFileProblem);
            Assert.IsTrue(SettingsLoadDiagnosis.FileUnparsable("x").IsSettingsFileProblem);

            Assert.IsFalse(SettingsLoadDiagnosis.Match().IsSettingsFileProblem);
            Assert.IsFalse(SettingsLoadDiagnosis.FileAbsent().IsSettingsFileProblem);
            Assert.IsFalse(SettingsLoadDiagnosis.DiagnosticUnavailable("x").IsSettingsFileProblem);
        }
    }
}
