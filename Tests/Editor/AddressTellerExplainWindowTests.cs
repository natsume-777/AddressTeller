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
                new RuleEvaluationError("SomeRule", "boom"),
            });

            var (text, cssClass) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution);

            StringAssert.Contains("1 rule error", text);
            StringAssert.Contains("SomeRule", text);
            Assert.AreEqual("at-conclusion--error", cssClass);
        }

        [Test]
        public void NoRuleErrors_Skipped_ConclusionIsUnchanged()
        {
            var validation = new ValidationResult(Ctx(), ValidationStatus.Skipped, null);
            var resolution = Resolution();

            var (text, cssClass) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution);

            StringAssert.Contains("No matching rule", text);
            Assert.AreEqual("at-conclusion--muted", cssClass);
        }

        [Test]
        public void LabelsOnly_ConclusionMentionsLabelsOnly()
        {
            var validation = new ValidationResult(Ctx(), ValidationStatus.LabelsOnly,
                "Only label rule(s) matched; no address assigned.");
            var resolution = Resolution(labels: new[] { "tag" });

            var (text, cssClass) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution);

            StringAssert.Contains("Labels only", text);
            Assert.AreEqual("at-conclusion--muted", cssClass);
        }

        [Test]
        public void RuleErrors_ButStatusOk_ConclusionShowsOkNotRuleError()
        {
            // 他のルールがマッチしてアドレスが確定している場合は、Ok 表示を優先する
            // （RuleError 優先表示は Skipped 時のみ、この観点はスコープ外）。
            var validation = new ValidationResult(Ctx(), ValidationStatus.Ok, null);
            var candidates = new[] { new AddressCandidate("Group", "addr") };
            var resolution = Resolution(candidates: candidates, errors: new[]
            {
                new RuleEvaluationError("SomeRule", "boom"),
            });

            var (text, cssClass) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution);

            StringAssert.Contains("addr", text);
            Assert.AreEqual("at-conclusion--ok", cssClass);
        }
    }
}
