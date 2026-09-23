using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    public class AddressTellerExplainWindowTests
    {
        private static AssetContext Ctx() =>
            new AssetContext("guid1", "Assets/Foo.prefab", typeof(GameObject));

        private static AddressResolution Resolution(
            IReadOnlyList<AddressCandidate> candidates = null,
            IReadOnlyCollection<string> labels = null,
            IReadOnlyList<RuleEvaluationError> errors = null) =>
            new AddressResolution(
                candidates ?? Array.Empty<AddressCandidate>(),
                labels ?? Array.Empty<string>(),
                errors);

        [Test]
        public void RuleErrorAndSkipped_ConclusionMentionsRuleErrors_NotSkipped()
        {
            var validation = new ValidationResult(Ctx(), ValidationStatus.Skipped, null);
            var resolution = Resolution(errors: new[]
            {
                new RuleEvaluationError("SomeRule", "boom", order: 0, canProduceAddress: true),
            });

            var (text, cssClass) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution, default);

            StringAssert.Contains("1 rule error", text);
            StringAssert.Contains("SomeRule", text);
            Assert.AreEqual("at-conclusion--error", cssClass);
        }

        [Test]
        public void NoRuleErrors_Skipped_ConclusionIsUnchanged()
        {
            var validation = new ValidationResult(Ctx(), ValidationStatus.Skipped, null);
            var resolution = Resolution();

            var (text, cssClass) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution, default);

            StringAssert.Contains("No matching rule", text);
            Assert.AreEqual("at-conclusion--muted", cssClass);
        }

        [Test]
        public void LabelsOnly_ConclusionMentionsLabelsOnly()
        {
            var validation = new ValidationResult(Ctx(), ValidationStatus.LabelsOnly,
                "Only label rule(s) matched; no address assigned.");
            var resolution = Resolution(labels: new[] { "tag" });

            var (text, cssClass) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution, default);

            StringAssert.Contains("Labels only", text);
            Assert.AreEqual("at-conclusion--muted", cssClass);
        }

        [Test]
        public void RuleErrorAndLabelsOnly_ConclusionMentionsRuleErrors_NotLabelsOnly()
        {
            // アドレス産出ルールが例外を出し、勝者が確定しないまま別のラベル専用ルールがマッチしたケース。
            // LabelsOnly の文言だけでは、実は途中でルールが例外を出した事実（BlockedByRuleError にはならず、
            // ラベルは書かれる）が隠れてしまうため、Skipped と同様にルール例外を優先表示する。
            var validation = new ValidationResult(Ctx(), ValidationStatus.LabelsOnly,
                "Only label rule(s) matched; no address assigned.");
            var resolution = Resolution(labels: new[] { "tag" }, errors: new[]
            {
                new RuleEvaluationError("FailingAddressRule", "boom", order: 0, canProduceAddress: true),
            });

            var (text, cssClass) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution, default);

            StringAssert.Contains("1 rule error", text);
            StringAssert.Contains("FailingAddressRule", text);
            Assert.AreEqual("at-conclusion--error", cssClass);
        }

        [Test]
        public void RuleErrors_ButStatusOk_ConclusionShowsOkNotRuleError()
        {
            // 他のルールがマッチしてアドレスが確定している場合は、Ok 表示を優先する
            // （RuleError 優先表示は Skipped 時のみ、この観点はスコープ外）。
            var validation = new ValidationResult(Ctx(), ValidationStatus.Ok, null);
            var winner = new AddressCandidate("Group", "addr");
            var candidates = new[] { winner };
            var resolution = Resolution(candidates: candidates, errors: new[]
            {
                new RuleEvaluationError("SomeRule", "boom", order: 99, canProduceAddress: true),
            });

            var (text, cssClass) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution, winner);

            StringAssert.Contains("addr", text);
            Assert.AreEqual("at-conclusion--ok", cssClass);
        }

        [Test]
        public void BlockedByRuleError_ConclusionMentionsBlocked()
        {
            var validation = new ValidationResult(Ctx(), ValidationStatus.BlockedByRuleError,
                "Not writing an address for 'Assets/Foo.prefab': ...");
            var resolution = Resolution(candidates: new[] { new AddressCandidate("Group", "addr") });

            var (text, cssClass) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution, default);

            StringAssert.Contains("Blocked by rule error", text);
            Assert.AreEqual("at-conclusion--error", cssClass);
        }

        [Test]
        public void AdoptionSuffix_SupersededOrder_IsSuperseded()
        {
            var suffix = AddressTellerExplainWindow.DescribeAdoptionSuffix(
                detailOrder: 5, minOrder: 0, isConflict: false, status: ValidationStatus.Ok);

            StringAssert.Contains("superseded", suffix);
        }

        [Test]
        public void AdoptionSuffix_TiedAtMinOrder_IsTied()
        {
            var suffix = AddressTellerExplainWindow.DescribeAdoptionSuffix(
                detailOrder: 0, minOrder: 0, isConflict: true, status: ValidationStatus.ConflictingAddress);

            StringAssert.Contains("tied for priority", suffix);
        }

        [Test]
        public void AdoptionSuffix_MinOrderAndOk_IsAdopted()
        {
            var suffix = AddressTellerExplainWindow.DescribeAdoptionSuffix(
                detailOrder: 0, minOrder: 0, isConflict: false, status: ValidationStatus.Ok);

            StringAssert.Contains("adopted", suffix);
        }

        [Test]
        public void AdoptionSuffix_MinOrderAndGroupWillBeCreated_IsAdopted()
        {
            var suffix = AddressTellerExplainWindow.DescribeAdoptionSuffix(
                detailOrder: 0, minOrder: 0, isConflict: false, status: ValidationStatus.GroupWillBeCreated);

            StringAssert.Contains("adopted", suffix);
        }

        [Test]
        public void AdoptionSuffix_MinOrderAndBlockedByRuleError_MentionsBlockedByRuleError()
        {
            // BlockedByRuleError のとき: Order だけ見れば勝者に見えても、Validate は書き込みを見送っている。
            // 原因がルール例外だと特定できるため、その旨を明示する。
            var suffix = AddressTellerExplainWindow.DescribeAdoptionSuffix(
                detailOrder: 0, minOrder: 0, isConflict: false, status: ValidationStatus.BlockedByRuleError);

            StringAssert.DoesNotContain("adopted)", suffix);
            StringAssert.Contains("blocked by a rule error", suffix);
        }

        [Test]
        public void AdoptionSuffix_MinOrderAndGroupNotFound_IsNeutralNotBlockedByRuleError()
        {
            // GroupNotFound は BlockedByRuleError とは無関係な非採用理由。「ルール例外でブロックされた」と
            // 誤解させる表示を出してはならない（中立な文言にとどめる）。
            var suffix = AddressTellerExplainWindow.DescribeAdoptionSuffix(
                detailOrder: 0, minOrder: 0, isConflict: false, status: ValidationStatus.GroupNotFound);

            StringAssert.DoesNotContain("adopted)", suffix);
            StringAssert.DoesNotContain("blocked by a rule error", suffix);
            StringAssert.Contains("not written", suffix);
        }

        [Test]
        public void AdoptionSuffix_MinOrderAndInvalidAddress_IsNeutralNotBlockedByRuleError()
        {
            var suffix = AddressTellerExplainWindow.DescribeAdoptionSuffix(
                detailOrder: 0, minOrder: 0, isConflict: false, status: ValidationStatus.InvalidAddress);

            StringAssert.DoesNotContain("blocked by a rule error", suffix);
            StringAssert.Contains("not written", suffix);
        }

        [Test]
        public void AdoptionSuffix_MinOrderAndDefaultGroupUnavailable_IsNeutralNotBlockedByRuleError()
        {
            var suffix = AddressTellerExplainWindow.DescribeAdoptionSuffix(
                detailOrder: 0, minOrder: 0, isConflict: false, status: ValidationStatus.DefaultGroupUnavailable);

            StringAssert.DoesNotContain("blocked by a rule error", suffix);
            StringAssert.Contains("not written", suffix);
        }
    }
}
