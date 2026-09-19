using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettingsLoadDiagnostics の純粋ロジック（例外分類、Classify の分類順序、
    /// SettingsLoadDiagnosis の不変条件、ドメインスコープキャッシュの出し入れ）の単体テスト。
    /// ProjectSettings 配下の実ファイルや実際の権限操作（ACL 変更等）を一切経由しない。
    /// Diagnose() 自体は ScriptableSingleton（AddressTellerSettingsAsset.instance）の再シリアライズを
    /// 内部で行うため、ここではファイル I/O を経由しない範囲——ClassifyReadException、Classify、
    /// SettingsLoadDiagnosis の各ファクトリメソッドの不変条件、GetOrDiagnoseForThisDomain/
    /// InvalidateDomainCache のキャッシュ出し入れ（内部の private static フィールドをリフレクション経由で
    /// 直接差し替えることで Diagnose() 自体の呼び出しを避ける）——のみを対象にする。
    /// SaveToDisk()/ReloadFromDisk() がこのキャッシュを実際に無効化することの確認は、実ファイルを扱う
    /// AddressTellerSettingsPersistenceTests.cs 側に置く（このファイルの「実ファイルを経由しない」という
    /// 制約と両立しないため）。
    /// </summary>
    public class AddressTellerSettingsLoadDiagnosticsTests
    {
        // AddressTellerSettingsLoadDiagnostics.s_diagnosisThisDomain（private static）へ、Diagnose() を
        // 一切呼ばずに直接値を出し入れするためのリフレクションハンドル。
        private static readonly FieldInfo DomainCacheField =
            typeof(AddressTellerSettingsLoadDiagnostics).GetField(
                "s_diagnosisThisDomain", BindingFlags.NonPublic | BindingFlags.Static);

        [TearDown]
        public void TearDown()
        {
            // このクラスの一部のテストがドメインスコープキャッシュへ直接値を書き込むため、後続のテスト
            // （このドメイン内で GetOrDiagnoseForThisDomain を呼ぶ可能性がある他のテスト）へ汚染が
            // 漏れないよう、テストごとに必ず無効化しておく。
            AddressTellerSettingsLoadDiagnostics.InvalidateDomainCache();
        }

        // 素の Exception を継承しただけの、AddressTeller が一切知らない例外型。ClassifyReadException の
        // 既定アーム（未知の型は FileUnreadable に倒す）を固定するために使う。
        private sealed class UnknownTestException : Exception
        {
        }

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
            // 一律 FileUnreadable に分類されることを固定する。PathTooLongException/FileNotFoundException/
            // DirectoryNotFoundException はいずれも IOException の派生だが、それらは別テストで個別に
            // 固定しているため、ここでは素の IOException のみを対象にする。
            Assert.AreEqual(
                SettingsLoadDiagnosisKind.FileUnreadable,
                AddressTellerSettingsLoadDiagnostics.ClassifyReadException(new IOException("locked by another process")));
        }

        [Test]
        public void ClassifyReadException_SecurityException_IsFileUnreadable()
        {
            Assert.AreEqual(
                SettingsLoadDiagnosisKind.FileUnreadable,
                AddressTellerSettingsLoadDiagnostics.ClassifyReadException(new System.Security.SecurityException()));
        }

        [Test]
        public void ClassifyReadException_PathTooLongException_IsDiagnosticUnavailable()
        {
            // PathTooLongException は IOException の派生だが、パスの形状自体が理由の失敗であり
            // ファイル側の問題ではない。FileUnreadable に落ちると -addressTellerFailOnSettingsMismatch
            // 指定時に本来ファイルの問題ではない失敗で CI を落としてしまう。
            Assert.AreEqual(
                SettingsLoadDiagnosisKind.DiagnosticUnavailable,
                AddressTellerSettingsLoadDiagnostics.ClassifyReadException(new PathTooLongException()));
        }

        [Test]
        public void ClassifyReadException_ArgumentException_IsDiagnosticUnavailable()
        {
            Assert.AreEqual(
                SettingsLoadDiagnosisKind.DiagnosticUnavailable,
                AddressTellerSettingsLoadDiagnostics.ClassifyReadException(new ArgumentException("bad path")));
        }

        [Test]
        public void ClassifyReadException_ArgumentNullException_IsDiagnosticUnavailable()
        {
            // ArgumentNullException / ArgumentOutOfRangeException は ArgumentException の派生であり、
            // 個別にホワイトリストせずとも同じ分岐へ落ちることを固定する。
            Assert.AreEqual(
                SettingsLoadDiagnosisKind.DiagnosticUnavailable,
                AddressTellerSettingsLoadDiagnostics.ClassifyReadException(new ArgumentNullException("path")));
        }

        [Test]
        public void ClassifyReadException_NotSupportedException_IsDiagnosticUnavailable()
        {
            Assert.AreEqual(
                SettingsLoadDiagnosisKind.DiagnosticUnavailable,
                AddressTellerSettingsLoadDiagnostics.ClassifyReadException(new NotSupportedException()));
        }

        [Test]
        public void ClassifyReadException_UnknownExceptionType_FallsBackToFileUnreadable()
        {
            // 既定アーム（3分類のどれにも明示的に該当しない例外）は FileUnreadable に倒す。
            // DiagnosticUnavailable に倒すと「読めないファイルがあるのに CI を落とさない」抜け穴になるため、
            // この既定の向きそのものを固定する。
            Assert.AreEqual(
                SettingsLoadDiagnosisKind.FileUnreadable,
                AddressTellerSettingsLoadDiagnostics.ClassifyReadException(new UnknownTestException()));
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
        public void SettingsLoadDiagnosis_NonNullMessageFactories_RejectNullMessage()
        {
            // Match()/FileAbsent() 以外は Message が非null という不変条件をファクトリ自身が強制する。
            Assert.Throws<ArgumentNullException>(() => SettingsLoadDiagnosis.Mismatch(null));
            Assert.Throws<ArgumentNullException>(() => SettingsLoadDiagnosis.FileUnreadable(null));
            Assert.Throws<ArgumentNullException>(() => SettingsLoadDiagnosis.FileUnparsable(null));
            Assert.Throws<ArgumentNullException>(() => SettingsLoadDiagnosis.DiagnosticUnavailable(null));
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

        // --- Classify(SettingsTextComparison, int) ---
        // Diagnose() から切り出した「分類だけを行う純粋関数」の分類順序を、ファイル I/O なしで固定する。

        [Test]
        public void Classify_MemoryTextEmptyAndDiskTextEmpty_IsDiagnosticUnavailable()
        {
            // 核心: メモリ側不認識かつディスク側解釈不能が同時に成立するとき、
            // MemoryTextRecognized 側の判定が ComparableFieldCount 側より必ず先に評価され、
            // DiagnosticUnavailable が勝つ。AddressTellerSettingsLoadDiagnostics.Classify 内の2つの if の
            // 順序を入れ替えると、この入力は FileUnparsable になり本テストが落ちる。
            var comparison = AddressTellerSettingsTextDiff.Compare(string.Empty, string.Empty);

            var diagnosis = AddressTellerSettingsLoadDiagnostics.Classify(comparison, diskTextLength: 0);

            Assert.AreEqual(SettingsLoadDiagnosisKind.DiagnosticUnavailable, diagnosis.Kind);
        }

        [Test]
        public void Classify_MemoryTextNotRecognizedEvenWithComparableFields_IsDiagnosticUnavailable()
        {
            // Compare() の実装からは作れない組み合わせ（MemoryTextRecognized=false かつ
            // ComparableFieldCount>0）を SettingsTextComparison の内部コンストラクタで直接構築し、
            // それでも MemoryTextRecognized 側が勝つことを固定する。
            var comparison = new SettingsTextComparison(
                diffs: Array.Empty<SettingsFieldDiff>(),
                typeIdentifierMatches: false,
                memoryTextRecognized: false,
                comparableFieldCount: 5);

            var diagnosis = AddressTellerSettingsLoadDiagnostics.Classify(comparison, diskTextLength: 100);

            Assert.AreEqual(SettingsLoadDiagnosisKind.DiagnosticUnavailable, diagnosis.Kind);
        }

        [Test]
        public void Classify_MemoryRecognizedNoComparableFields_IsFileUnparsableAndMessageIncludesDiskTextLength()
        {
            var comparison = new SettingsTextComparison(
                diffs: Array.Empty<SettingsFieldDiff>(),
                typeIdentifierMatches: false,
                memoryTextRecognized: true,
                comparableFieldCount: 0);

            var diagnosis = AddressTellerSettingsLoadDiagnostics.Classify(comparison, diskTextLength: 42);

            Assert.AreEqual(SettingsLoadDiagnosisKind.FileUnparsable, diagnosis.Kind);
            StringAssert.Contains("42 characters", diagnosis.Message);
        }

        [Test]
        public void Classify_ComparableFieldsWithNoDiffs_IsMatch()
        {
            var comparison = new SettingsTextComparison(
                diffs: Array.Empty<SettingsFieldDiff>(),
                typeIdentifierMatches: true,
                memoryTextRecognized: true,
                comparableFieldCount: 3);

            var diagnosis = AddressTellerSettingsLoadDiagnostics.Classify(comparison, diskTextLength: 100);

            Assert.AreEqual(SettingsLoadDiagnosisKind.Match, diagnosis.Kind);
            Assert.IsNull(diagnosis.Message);
        }

        [Test]
        public void Classify_ComparableFieldsWithDiffs_IsMismatch()
        {
            var diffs = new List<SettingsFieldDiff> { new("_postprocessOrder", "2000", "1000") };
            var comparison = new SettingsTextComparison(
                diffs: diffs,
                typeIdentifierMatches: true,
                memoryTextRecognized: true,
                comparableFieldCount: 3);

            var diagnosis = AddressTellerSettingsLoadDiagnostics.Classify(comparison, diskTextLength: 100);

            Assert.AreEqual(SettingsLoadDiagnosisKind.Mismatch, diagnosis.Kind);
            Assert.IsNotNull(diagnosis.Message);
        }

        [Test]
        public void Classify_NeverReturnsFileAbsentOrFileUnreadable()
        {
            // Classify は「ファイルを読めたかどうか」という I/O 層の判断をしない純粋関数であり、
            // FileAbsent/FileUnreadable を返さない契約を、代表的な入力の組み合わせで固定する。
            var inputs = new[]
            {
                new SettingsTextComparison(Array.Empty<SettingsFieldDiff>(), false, false, 0),
                new SettingsTextComparison(Array.Empty<SettingsFieldDiff>(), false, true, 0),
                new SettingsTextComparison(Array.Empty<SettingsFieldDiff>(), true, true, 2),
                new SettingsTextComparison(
                    new List<SettingsFieldDiff> { new("_postprocessOrder", "2000", "1000") }, true, true, 2),
            };

            foreach (var comparison in inputs)
            {
                var diagnosis = AddressTellerSettingsLoadDiagnostics.Classify(comparison, diskTextLength: 0);
                Assert.AreNotEqual(SettingsLoadDiagnosisKind.FileAbsent, diagnosis.Kind);
                Assert.AreNotEqual(SettingsLoadDiagnosisKind.FileUnreadable, diagnosis.Kind);
            }
        }

        // --- GetOrDiagnoseForThisDomain() / InvalidateDomainCache() ---
        // Diagnose() 自体は呼ばず、private static のキャッシュフィールドをリフレクションで直接
        // 出し入れすることで、実ファイルに一切触れずにキャッシュの出し入れそのものを固定する。

        [Test]
        public void GetOrDiagnoseForThisDomain_CacheAlreadyPopulated_ReturnsCachedValueRatherThanRecomputing()
        {
            // 実際の Diagnose() が偶然この文面を生成する確率は無視できるため、戻り値がこのマーカーと
            // 一致すること自体が「キャッシュ済みの値をそのまま返した（再計算していない）」ことの証拠になる。
            var marker = SettingsLoadDiagnosis.Mismatch("cache-probe-marker (GetOrDiagnoseForThisDomain test)");
            DomainCacheField.SetValue(null, (SettingsLoadDiagnosis?)marker);

            var first = AddressTellerSettingsLoadDiagnostics.GetOrDiagnoseForThisDomain();
            var second = AddressTellerSettingsLoadDiagnostics.GetOrDiagnoseForThisDomain();

            Assert.AreEqual(SettingsLoadDiagnosisKind.Mismatch, first.Kind);
            Assert.AreEqual(marker.Message, first.Message);
            Assert.AreEqual(SettingsLoadDiagnosisKind.Mismatch, second.Kind);
            Assert.AreEqual(marker.Message, second.Message);
        }

        [Test]
        public void InvalidateDomainCache_CacheWasPopulated_ClearsItToNull()
        {
            var marker = SettingsLoadDiagnosis.Mismatch("cache-probe-marker (InvalidateDomainCache test)");
            DomainCacheField.SetValue(null, (SettingsLoadDiagnosis?)marker);
            Assert.IsNotNull(DomainCacheField.GetValue(null), "前提: キャッシュに値を書き込めていること。");

            AddressTellerSettingsLoadDiagnostics.InvalidateDomainCache();

            Assert.IsNull(DomainCacheField.GetValue(null),
                "InvalidateDomainCache() の後はキャッシュフィールドが null（Nullable<T> の HasValue=false）であるべき。");
        }
    }
}
