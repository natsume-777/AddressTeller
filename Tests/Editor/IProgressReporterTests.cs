using NUnit.Framework;

namespace Natsume777.AddressTeller.Editor.Tests
{
    /// <summary>
    /// NullProgressReporter/EditorProgressReporter の基本動作を検証する。
    /// </summary>
    public class IProgressReporterTests
    {
        [Test]
        public void NullProgressReporter_AlwaysReturnsTrue()
        {
            var reporter = NullProgressReporter.Instance;

            Assert.IsTrue(reporter.Report(0, 10, "first"));
            Assert.IsTrue(reporter.Report(5, 10, "middle"));
            Assert.IsTrue(reporter.Report(10, 10, "last"));
        }

        [Test]
        public void EditorProgressReporter_LastItem_AlwaysReports()
        {
            using var reporter = new EditorProgressReporter("Test");

            // 連続呼び出しでも最後の1件（current == total）は間引かれず、
            // キャンセルされていなければ true を返す。
            var result = reporter.Report(10, 10, "last");

            Assert.IsTrue(result);
            Assert.IsFalse(reporter.WasCancelled);
        }

        [Test]
        public void EditorProgressReporter_Dispose_DoesNotThrow()
        {
            var reporter = new EditorProgressReporter("Test");
            reporter.Report(0, 1, "first");

            Assert.DoesNotThrow(() => reporter.Dispose());
        }
    }
}
