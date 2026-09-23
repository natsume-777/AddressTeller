using System;
using System.Collections.Generic;
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
