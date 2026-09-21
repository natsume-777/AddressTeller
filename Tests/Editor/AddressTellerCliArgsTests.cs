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

        [Test]
        public void UnknownAddressTellerFlag_ReturnsErrorContainingFlagName()
        {
            // フラグ名の typo（例: -addressTellerRepot）を、値の typo と対称にエラー扱いする。
            var args = new[] { "-addressTellerFoo" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(result);
            StringAssert.Contains("-addressTellerFoo", error);
        }

        [Test]
        public void NonAddressTellerFlag_IsIgnored()
        {
            // "-addressTeller" で始まらない引数は Unity 自身が多数渡すため、無視されなければならない。
            var args = new[] { "-someUnityFlag" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
        }

        [Test]
        public void UnknownAddressTellerFlag_ErrorListsKnownFlagNames()
        {
            // 値の typo 側のエラー（例: "(must be 'json' or 'junit')"）と対称に、
            // フラグ名の typo でも既知の語彙を提示する。
            var args = new[] { "-addressTellerFoo" };

            AddressTellerCliArgs.TryParse(args, out _, out var error);

            StringAssert.Contains("-addressTellerReport", error);
            StringAssert.Contains("-addressTellerClearScope", error);
        }

        [Test]
        public void UppercasedAddressTellerPrefix_TypoIsStillDetectedAsError()
        {
            // フラグ名の先頭を誤って大文字にする（クラス名 "AddressTeller" の見た目に引きずられがちな typo）
            // ケース。プレフィックス判定だけ大文字小文字を無視するため、これも黙って無視されず検出される。
            var args = new[] { "-AddressTellerReport", "report.json" };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsFalse(ok, "「成功したように見えて何もしない」が最悪の失敗の仕方であり、これを検出できることが本テストの主眼。");
            Assert.IsNull(result);
            StringAssert.Contains("-AddressTellerReport", error);
        }

        [Test]
        public void RealisticUnityCommandLine_KnownFlagsParsedAndUnrelatedArgsIgnored()
        {
            // 実際の CI 起動に近い引数配列（Unity 自身が渡す引数の間に既知フラグが混ざる想定）で、
            // 値を取るフラグの「値」自体が誤って未知フラグと判定されないこと、かつ Unity 側の引数が
            // 素通りすることを合わせて固定する回帰テスト。
            var args = new[]
            {
                "Unity.exe", "-batchmode", "-projectPath", "C:/p", "-executeMethod",
                "AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI", "-logFile", "-", "-quit",
                "-hubSessionId", "x", "-accessToken", "y", "-addressTellerReport", "report.json",
            };

            var ok = AddressTellerCliArgs.TryParse(args, out var result, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsNotNull(result);
            Assert.AreEqual("report.json", result.ReportPath);
        }
    }
}
