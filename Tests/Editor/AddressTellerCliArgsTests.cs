using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerCliArgs.TryParse の単体テスト。
    /// </summary>
    public class AddressTellerCliArgsTests
    {
        [Test]
        public void NullArgs_ReturnsFalseWithError()
        {
            // TryParse は Try プレフィックスのメソッドであるため、null 入力でも例外を投げず false を返す契約。
            var ok = AddressTellerCliArgs.TryParse(null, out var result, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(result);
            Assert.IsNotEmpty(error);
        }

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
            Assert.AreEqual(ReportFormat.Json, result.ReportFormat);
        }

        [Test]
        public void ReportPathWithXmlExtension_FormatInferredAsJUnit()
        {
            var args = new[] { "-addressTellerReport", "report.xml" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.AreEqual("report.xml", result.ReportPath);
            Assert.AreEqual(ReportFormat.Junit, result.ReportFormat);
        }

        [Test]
        public void ReportPathAndFormatBothSpecified_UsesExplicitFormat()
        {
            var args = new[] { "-addressTellerReport", "report.txt", "-addressTellerReportFormat", "junit" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.AreEqual("report.txt", result.ReportPath);
            Assert.AreEqual(ReportFormat.Junit, result.ReportFormat);
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
            Assert.AreEqual(ReportFormat.Junit, result.ReportFormat);
        }

        [Test]
        public void NoDisableRulesFlag_DisableRuleFullNamesIsEmpty()
        {
            var args = new[] { "-batchmode", "-quit" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.IsEmpty(result.DisableRuleFullNames);
        }

        [Test]
        public void DisableRules_CommaSeparated_TrimmedAndEmptyEntriesRemoved()
        {
            var args = new[] { "-addressTellerDisableRules", " MyNamespace.RuleA ,, MyNamespace.RuleB" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            CollectionAssert.AreEqual(new[] { "MyNamespace.RuleA", "MyNamespace.RuleB" }, result.DisableRuleFullNames);
        }

        [Test]
        public void DisableRules_DuplicateNames_KeptAsIs()
        {
            // 重複は RuleCollector.TryCollectEnabledRules 側で Distinct されるため、パーサでは除去しない。
            var args = new[] { "-addressTellerDisableRules", "MyNamespace.RuleA,MyNamespace.RuleA" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            CollectionAssert.AreEqual(new[] { "MyNamespace.RuleA", "MyNamespace.RuleA" }, result.DisableRuleFullNames);
        }

        [Test]
        public void DisableRulesFlagWithoutValue_ReturnsError()
        {
            var args = new[] { "-addressTellerDisableRules" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(result);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void NoConfirmClearFlag_ConfirmClearIsFalse()
        {
            var args = new[] { "-batchmode", "-quit" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.IsFalse(result.ConfirmClear);
        }

        [Test]
        public void ConfirmClearFlag_SetsConfirmClearTrue()
        {
            var args = new[] { "-addressTellerConfirmClear" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.IsTrue(result.ConfirmClear);
        }

        [Test]
        public void NoClearScopeFlag_DefaultsToManaged()
        {
            var args = new[] { "-batchmode", "-quit" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.AreEqual(ClearScope.Managed, result.ClearScope);
        }

        [Test]
        public void ClearScopeManaged_SetsClearScopeManaged()
        {
            var args = new[] { "-addressTellerClearScope", "managed" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.AreEqual(ClearScope.Managed, result.ClearScope);
        }

        [Test]
        public void ClearScopeAll_SetsClearScopeAll()
        {
            var args = new[] { "-addressTellerClearScope", "all" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.AreEqual(ClearScope.All, result.ClearScope);
        }

        [Test]
        public void ClearScopeFlagWithoutValue_ReturnsError()
        {
            var args = new[] { "-addressTellerClearScope" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(result);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void UnknownClearScope_ReturnsError()
        {
            var args = new[] { "-addressTellerClearScope", "unknown" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(result);
            Assert.IsNotEmpty(error);
        }
    }
}
