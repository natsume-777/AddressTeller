using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerCliArgs.TryParse の単体テスト。
    /// </summary>
    public class AddressTellerCliArgsTests
    {
        [Test]
        public void NoReportFlags_ReportPathAndFormatAreNull()
        {
            var args = new[] { "-batchmode", "-quit" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.IsNull(result.ReportPath);
            Assert.IsNull(result.ReportFormat);
        }

        [Test]
        public void ReportPathOnly_FormatInferredAsJsonByDefault()
        {
            var args = new[] { "-addressTellerReport", "report.json" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.AreEqual("report.json", result.ReportPath);
            Assert.AreEqual("json", result.ReportFormat);
        }

        [Test]
        public void ReportPathWithXmlExtension_FormatInferredAsJUnit()
        {
            var args = new[] { "-addressTellerReport", "report.xml" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.AreEqual("report.xml", result.ReportPath);
            Assert.AreEqual("junit", result.ReportFormat);
        }

        [Test]
        public void ReportPathAndFormatBothSpecified_UsesExplicitFormat()
        {
            var args = new[] { "-addressTellerReport", "report.txt", "-addressTellerReportFormat", "junit" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.AreEqual("report.txt", result.ReportPath);
            Assert.AreEqual("junit", result.ReportFormat);
        }

        [Test]
        public void ReportPathFlagWithoutValue_ReturnsError()
        {
            var args = new[] { "-addressTellerReport" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(result);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void ReportFormatFlagWithoutValue_ReturnsError()
        {
            var args = new[] { "-addressTellerReport", "report.json", "-addressTellerReportFormat" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(result);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void UnknownReportFormat_ReturnsError()
        {
            var args = new[] { "-addressTellerReport", "report.json", "-addressTellerReportFormat", "yaml" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(result);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void ReportFormatOnly_WithoutReportPath_FormatIsStillSetFromExplicitFlag()
        {
            // ReportPath が未指定でも -addressTellerReportFormat が明示されていればその値を保持する。
            // CheckCLI 側ではReportPathが無ければ未使用のため実害はないが、パーサとしては値を反映する。
            var args = new[] { "-addressTellerReportFormat", "junit" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.IsNull(result.ReportPath);
            Assert.AreEqual("junit", result.ReportFormat);
        }
    }
}
