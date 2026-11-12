using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerMenu.RunCliEntryPoint（ClearCLI/ApplyAllCLI/ApplyWithValidateCLI/CheckCLI が共有する
    /// 予期しない例外の catch）が、既知の分岐が明示的に呼ぶ exit code を通り抜けた例外を catch し、
    /// 環境エラーを表す exit code 3 に統一することを検証する。実際に Editor プロセスを終了させないよう、
    /// <see cref="AddressTellerMenu.s_exitCli"/>（テスト用シーム）を差し替える。
    /// </summary>
    public class AddressTellerMenuCliExitTests
    {
        private Action<int> _originalExitCli;

        [SetUp]
        public void SetUp()
        {
            _originalExitCli = AddressTellerMenu.s_exitCli;
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerMenu.s_exitCli = _originalExitCli;
        }

        [Test]
        public void UnexpectedException_ExitsWithCode3_AndLogsError()
        {
            int? exitCode = null;
            AddressTellerMenu.s_exitCli = code => exitCode = code;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("aborted due to an unexpected exception"));

            AddressTellerMenu.RunCliEntryPoint("TestEntryPoint", () => throw new InvalidOperationException("boom"));

            Assert.AreEqual(3, exitCode);
        }

        private static DryRunResult DryRunWithUnmatched()
            => new DryRunResult(new SnapshotDiff(), new List<ValidationResult>(), unmatchedInDeclaredFolders: new[] { "Assets/A/Orphan.prefab" });

        private static AddressTellerCliArgs ParseArgs(params string[] args)
        {
            Assert.IsTrue(AddressTellerCliArgs.TryParse(args, out var parsed, out var error), error);
            return parsed;
        }

        [Test]
        public void ReportCheckResult_FailOnUnmatched_ExitsTwoAndLogsError()
        {
            int? exitCode = null;
            AddressTellerMenu.s_exitCli = code => exitCode = code;
            LogAssert.Expect(LogType.Log, new Regex("Check completed"));
            LogAssert.Expect(LogType.Error, new Regex("Unmatched: 1 asset"));

            AddressTellerMenu.ReportCheckResult(DryRunWithUnmatched(), ParseArgs("-addressTellerFailOnUnmatched"), null);

            Assert.AreEqual(2, exitCode);
        }

        [Test]
        public void ReportCheckResult_WithoutFailOnUnmatched_ExitsZeroAndLogsWarning()
        {
            int? exitCode = null;
            AddressTellerMenu.s_exitCli = code => exitCode = code;
            LogAssert.Expect(LogType.Log, new Regex("Check completed"));
            LogAssert.Expect(LogType.Warning, new Regex("Unmatched: 1 asset"));

            AddressTellerMenu.ReportCheckResult(DryRunWithUnmatched(), ParseArgs(), null);

            Assert.AreEqual(0, exitCode);
        }

        [Test]
        public void ReportCheckResult_FailOnUnmatched_WritesExitCodeIntoJsonReport()
        {
            var path = Path.Combine(Path.GetTempPath(), "AddressTellerCheck_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                int? exitCode = null;
                AddressTellerMenu.s_exitCli = code => exitCode = code;
                LogAssert.Expect(LogType.Log, new Regex("Check completed"));
                LogAssert.Expect(LogType.Error, new Regex("Unmatched: 1 asset"));

                AddressTellerMenu.ReportCheckResult(DryRunWithUnmatched(),
                    ParseArgs("-addressTellerFailOnUnmatched", "-addressTellerReport", path), null);

                Assert.AreEqual(2, exitCode);
                var report = AddressTellerReport.FromJson(File.ReadAllText(path));
                Assert.AreEqual(2, report.Summary.ExitCode, "レポートの Summary.ExitCode は、実際に返す exit code と一致させる。");
                CollectionAssert.AreEqual(new[] { "Assets/A/Orphan.prefab" }, report.Unmatched);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReportCheckResult_JunitReport_UnmatchedTestCaseFailsOnlyWithFailOnUnmatched()
        {
            var path = Path.Combine(Path.GetTempPath(), "AddressTellerCheck_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                AddressTellerMenu.s_exitCli = _ => { };

                LogAssert.Expect(LogType.Log, new Regex("Check completed"));
                LogAssert.Expect(LogType.Warning, new Regex("Unmatched: 1 asset"));
                AddressTellerMenu.ReportCheckResult(DryRunWithUnmatched(), ParseArgs("-addressTellerReport", path), null);
                var withoutFlag = File.ReadAllText(path);

                LogAssert.Expect(LogType.Log, new Regex("Check completed"));
                LogAssert.Expect(LogType.Error, new Regex("Unmatched: 1 asset"));
                AddressTellerMenu.ReportCheckResult(DryRunWithUnmatched(), ParseArgs("-addressTellerFailOnUnmatched", "-addressTellerReport", path), null);
                var withFlag = File.ReadAllText(path);

                StringAssert.Contains("AddressTeller.Unmatched", withoutFlag);
                StringAssert.DoesNotContain("<failure", withoutFlag);
                StringAssert.Contains("Assets/A/Orphan.prefab", withFlag);
                StringAssert.Contains("<failure", withFlag);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void NoException_DoesNotExit()
        {
            var exitCalled = false;
            AddressTellerMenu.s_exitCli = _ => exitCalled = true;

            AddressTellerMenu.RunCliEntryPoint("TestEntryPoint", () => { });

            Assert.IsFalse(exitCalled, "body が例外を投げなければ RunCliEntryPoint 自身は exit を呼ばない(body 内の明示的な exit 呼び出しに任せる)。");
        }

        [Test]
        public void BodyCallsExitExplicitly_RecordsOnlyThatCode_NoAdditionalLogOrExit()
        {
            // body が例外を投げずに自ら s_exitCli(...) を呼ぶ経路（重複 guid 検出時に exit 2 を呼ぶ
            // ClearCLICore 等）で、RunCliEntryPoint がそれを妨げたり、二重に exit を記録したりしないことを確認する。
            var exitCodes = new List<int>();
            AddressTellerMenu.s_exitCli = exitCodes.Add;

            AddressTellerMenu.RunCliEntryPoint("TestEntryPoint", () => AddressTellerMenu.s_exitCli(2));

            CollectionAssert.AreEqual(new[] { 2 }, exitCodes);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
