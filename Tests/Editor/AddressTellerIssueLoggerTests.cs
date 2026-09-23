using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerIssueLogger.LogAll の振り分けを検証する。特に
    /// <see cref="ValidationStatus.UnmatchedEntryKept"/> が個別にログされず、件数のみの1行サマリに
    /// まとめられることを確認する（CleanupStaleEntries が OFF の間、所有グループの無マッチ・無効パス
    /// エントリ全件がこの通知になり得るため、個別ログではコンソールを埋め尽くしてしまう）。
    /// </summary>
    public class AddressTellerIssueLoggerTests
    {
        private static ValidationResult Make(ValidationStatus status, string message) =>
            new ValidationResult(null, status, message);

        [Test]
        public void LogAll_UnmatchedEntryKeptOnly_LogsSingleSummaryInfo()
        {
            var issues = new List<ValidationResult>
            {
                Make(ValidationStatus.UnmatchedEntryKept, "entry 1"),
                Make(ValidationStatus.UnmatchedEntryKept, "entry 2"),
                Make(ValidationStatus.UnmatchedEntryKept, "entry 3"),
            };

            // 意図して CleanupStaleEntries を OFF のまま運用しているプロジェクトでは毎回出続けるため、
            // Warning ではなく Info（Debug.Log）にする。
            LogAssert.Expect(LogType.Log, new Regex("UnmatchedEntryKept: 3 entries.*are being kept"));

            AddressTellerIssueLogger.LogAll(issues);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogAll_SingleUnmatchedEntryKept_UsesSingularWordsInSummary()
        {
            var issues = new List<ValidationResult> { Make(ValidationStatus.UnmatchedEntryKept, "entry 1") };

            LogAssert.Expect(LogType.Log, new Regex("UnmatchedEntryKept: 1 entry would.*is being kept"));

            AddressTellerIssueLogger.LogAll(issues);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogAll_MixedIssues_LogsOthersIndividuallyAndUnmatchedEntryKeptAsOneSummary()
        {
            var issues = new List<ValidationResult>
            {
                Make(ValidationStatus.GroupNotFound, "missing group"),
                Make(ValidationStatus.UnmatchedEntryKept, "entry 1"),
                Make(ValidationStatus.UnmatchedEntryKept, "entry 2"),
            };

            LogAssert.Expect(LogType.Error, new Regex("GroupNotFound: missing group"));
            LogAssert.Expect(LogType.Log, new Regex("UnmatchedEntryKept: 2 entries"));

            AddressTellerIssueLogger.LogAll(issues);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogAll_NoUnmatchedEntryKept_DoesNotLogSummary()
        {
            var issues = new List<ValidationResult> { Make(ValidationStatus.GroupNotFound, "missing group") };

            LogAssert.Expect(LogType.Error, new Regex("GroupNotFound: missing group"));

            AddressTellerIssueLogger.LogAll(issues);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogAll_EmptyList_LogsNothing()
        {
            AddressTellerIssueLogger.LogAll(new List<ValidationResult>());

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogAll_BlockedByRuleErrorOnly_LogsSingleSummaryError()
        {
            var issues = new List<ValidationResult>
            {
                Make(ValidationStatus.BlockedByRuleError, "blocked by: RuleA (Order=0)"),
                Make(ValidationStatus.BlockedByRuleError, "blocked by: RuleA (Order=0)"),
                Make(ValidationStatus.BlockedByRuleError, "blocked by: RuleA (Order=0)"),
            };

            // 1つのルール例外が多数のアセットへ波及しうるため、UnmatchedEntryKept と同様に個別ログはせず
            // 件数だけの1行サマリにまとめる。ただしこちらは実際の問題（IsOk=false）なので Error にする。
            LogAssert.Expect(LogType.Error, new Regex("BlockedByRuleError: 3 assets were not written"));

            AddressTellerIssueLogger.LogAll(issues);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogAll_BlockedByRuleErrorOnly_SummaryMentionsSamePriorityAndPointsToValidate()
        {
            var issues = new List<ValidationResult> { Make(ValidationStatus.BlockedByRuleError, "blocked by: RuleA (Order=0)") };

            // 「より優先度の高いルール」だけでなく、同点(Order 一致)や勝者自身のケースも含むため、
            // "same or higher priority" (winning rule 自身を含みうる) と表現する。Postprocessor 経由では
            // Result Window が開かないため、UnmatchedEntryKept と同じく "Run Validate to see..." の
            // 導線を添える。
            LogAssert.Expect(LogType.Error, new Regex(
                "because a rule at the same or higher priority threw.*possibly the winning rule itself" +
                ".*Run Validate to see the individual entries in the Result Window"));

            AddressTellerIssueLogger.LogAll(issues);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogAll_SingleBlockedByRuleError_UsesSingularWordsInSummary()
        {
            var issues = new List<ValidationResult> { Make(ValidationStatus.BlockedByRuleError, "blocked by: RuleA (Order=0)") };

            LogAssert.Expect(LogType.Error, new Regex("BlockedByRuleError: 1 asset was not written"));

            AddressTellerIssueLogger.LogAll(issues);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogAll_MixedIssues_LogsOthersIndividuallyAndBlockedByRuleErrorAsOneSummary()
        {
            var issues = new List<ValidationResult>
            {
                Make(ValidationStatus.GroupNotFound, "missing group"),
                Make(ValidationStatus.BlockedByRuleError, "blocked by: RuleA (Order=0)"),
                Make(ValidationStatus.BlockedByRuleError, "blocked by: RuleA (Order=0)"),
            };

            LogAssert.Expect(LogType.Error, new Regex("GroupNotFound: missing group"));
            LogAssert.Expect(LogType.Error, new Regex("BlockedByRuleError: 2 assets"));

            AddressTellerIssueLogger.LogAll(issues);

            LogAssert.NoUnexpectedReceived();
        }
    }
}
